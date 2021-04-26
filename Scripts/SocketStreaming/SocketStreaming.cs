using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using System;
using CircularBuffer;
using System.Runtime.InteropServices;
using System.Net;
using System.Net.Sockets;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Linq;

public class SocketStreaming : MonoBehaviour
{
    private Material mat;

    [HideInInspector]
    public Texture2D tex, m_YImageTex, m_UImageTex, m_VImageTex;

    [StructLayout(LayoutKind.Sequential, Pack = 1, CharSet = CharSet.Ansi)]
    private struct s_dimension
    {
        public int width;
        public int height;
    }
    [StructLayout(LayoutKind.Sequential, Pack = 1, CharSet = CharSet.Ansi)]
    private struct s_decoded_frame
    {
        public IntPtr data;
        public int linesize;
        public int width;
        public int height;
        public int time_avcodec_send_packet;
        public int time_avcodec_receive_frame;
        public int time_avcodec_sws_scale;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1, CharSet = CharSet.Ansi)]
    private struct sYUVplanes
    {
        public IntPtr avframe;
        public IntPtr Y;
        public IntPtr U;
        public IntPtr V;
    }

    private byte[] buffer;
    private bool hasInit = false;

    private s_dimension dimension;

    private Socket receiver = null;
    private static Mutex mutex = new Mutex();
    private CircularBuffer<IntPtr> circularBuffer = new CircularBuffer<IntPtr>(100);

    private static bool HARDWARE_ACCELERATION;

    private static string host = "2.85.228.157";
    private static int port = 40000;
    private byte[] iFrameHeaderTemplate = new byte[] { 0x00, 0x00, 0x00, 0x01, 0x67, 0x42 };
    int pImageSize;
    int i = 90;
    private IntPtr pImage;

    [DllImport("Decoder", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr init(IntPtr data, int length);
    [DllImport("Decoder", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr decode(IntPtr data, int length);    
    [DllImport("Decoder", CallingConvention = CallingConvention.Cdecl)]
    public static extern int check_hardware_device_support(int i);
    [DllImport("Decoder", CallingConvention = CallingConvention.Cdecl)]
    public static extern void clear();
    [DllImport("Decoder", CallingConvention = CallingConvention.Cdecl)]
    public static extern int test(int a, int b);
    [DllImport("Decoder", CallingConvention = CallingConvention.Cdecl)]
    public static extern void unref_yuvPlanes_structure(IntPtr yuvPlane);

    private bool decoder_init(byte[] data)
    {
        var handle = GCHandle.Alloc(data, GCHandleType.Pinned);
        IntPtr p = handle.AddrOfPinnedObject();
        IntPtr pointer;
        if (HARDWARE_ACCELERATION)
        {
            pointer = init(p, data.Length);
        }
        else
        {
            pointer = init(p, data.Length);
        }
        if (pointer == IntPtr.Zero)
        {
            print("init pointer == zero");
            return false;
        }
        dimension = (s_dimension)Marshal.PtrToStructure(pointer, typeof(s_dimension));
        handle.Free();
        return true;
    }
    public IntPtr decode_frame(byte[] data)
    {
        IntPtr pointer;

        if (!hasInit)
        {
            Debug.Log("start initialization");
            hasInit = decoder_init(data);
            if (!hasInit)
            {
                Debug.Log("I need more frames. Initialize again.");
                return IntPtr.Zero;
            }

        }
        var handle = GCHandle.Alloc(data, GCHandleType.Pinned);
        IntPtr pData = handle.AddrOfPinnedObject();
        
        if (HARDWARE_ACCELERATION)
        {
            //TODO set the hware acceleration function if is need it.
            pointer = decode(pData, data.Length);
        }
        else
        {
            pointer = decode(pData, data.Length);
        }
        if (pointer == IntPtr.Zero)
        {
            Debug.Log("returned null pointer from decoding (check frame and feed again dll function)");
            return pointer;
        }
        return pointer;
    }
    public async void OpenAndShowSavedFrames()
    {
        if (i == 100)
        {
            i = 90;
        }
        byte[] frame = File.ReadAllBytes("D:\\" + i.ToString());

        if (iFrameHeaderTemplate.SequenceEqual(frame.Take(6)))
        {
            string hex = BitConverter.ToString(frame.Take(10).ToArray());
            Debug.Log("First 10 bytes in hex format [before] preprocessing is :" + hex);
            //0x80 -> 0xC0 header byte (constraint_set1_flag must be 1)
            Buffer.SetByte(frame, 6, 0xC0);
            hex = BitConverter.ToString(frame.Take(10).ToArray());
            Debug.Log("First 10 bytes in hex format [after] preprocessing is :" + hex);
        }

        var watch = System.Diagnostics.Stopwatch.StartNew();
        IntPtr image = await Task.Run(() => decode_frame(frame));
        watch.Stop();
        float total_time = watch.ElapsedMilliseconds;
        Debug.Log("Decoding time over dll is : " + total_time);

        i++;
        if(image != IntPtr.Zero)
        {
            mutex.WaitOne();//mutex blocks
            circularBuffer.PushBack(image);
            mutex.ReleaseMutex();//mutex released
        }
    }
    public async void StartClientAsync()
    {
        try
        {
            IPHostEntry ipHostInfo = Dns.GetHostEntry(host);
            IPAddress ipAddress = ipHostInfo.AddressList[0];
            IPEndPoint remoteEP = new IPEndPoint(ipAddress, port);

            receiver = new Socket(ipAddress.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            try
            {
                receiver.Connect(remoteEP);
                Debug.Log("Socket connected to " + receiver.RemoteEndPoint.ToString());
                Stream myNetworkStream = new NetworkStream(receiver);
                using (BinaryReader br = new BinaryReader(myNetworkStream))
                {
                    int size = br.ReadInt32();
                    Debug.Log(size);
                    while (size > 0)
                    {
                        byte[] frame = br.ReadBytes(size);
                        if (frame.Length != size)
                        {
                            Debug.LogError("Error :: frame length isn't equal to size");
                        }
                        //check if current frame is iFrame. If true, change the 7th byte.
                        if (iFrameHeaderTemplate.SequenceEqual(frame.Take(6)))
                        {
                            //0x80 -> 0xC0 header byte (constraint_set1_flag must be 1)
                            Buffer.SetByte(frame, 6, 0xC0);
                        }

                        //File.WriteAllBytes("./Assets/FramesPieces/3" + frame_index, frame);
                        size = br.ReadInt32();

                        var watch = System.Diagnostics.Stopwatch.StartNew();
                        IntPtr pImage = await Task.Run(() => decode_frame(frame));
                        watch.Stop();
                        float total_time = watch.ElapsedMilliseconds;
                        Debug.Log("Decoding time over dll is : " + total_time);

                        mutex.WaitOne();//mutex blocks
                        circularBuffer.PushBack(pImage);
                        mutex.ReleaseMutex();//mutex released
                    }
                }
                receiver.Shutdown(SocketShutdown.Both);
                receiver.Close();
            }
            catch (ArgumentNullException ane)
            {
                Debug.Log("ArgumentNullException : " + ane.ToString());
            }
            catch (SocketException se)
            {
                Debug.Log("SocketException : " + se.ToString());
            }
            catch (Exception e)
            {
                Debug.Log("Unexpected exception : " + e.ToString());
            }
        }
        catch (Exception e)
        {
            Console.WriteLine(e.ToString());
        }
    }
    private void StartClientThread()
    {
        var th1 = new Thread(StartClientAsync);
        th1.Start();
    }
    void OnApplicationQuit()
    {
        if(receiver != null)
        {
            receiver.Shutdown(SocketShutdown.Both);
            receiver.Close();
        }
        CancelInvoke();
    }
    void UnloadDirties()
    {
        Resources.UnloadUnusedAssets();
    }
    void Start()
    {
        HARDWARE_ACCELERATION = false;
        Invoke("StartClientThread", 0f);
        //Invoke("OpenAndShowSavedFrames", 0f);
        //Invoke("RenderFrame", 0.033f);
        InvokeRepeating("OpenAndShowSavedFrames", 2f, 0.035f);
        InvokeRepeating("RenderFrame", 0f, 0.033f);
        //InvokeRepeating("UnloadDirties", 0f, 5f);
        //print_hardware_device_support();
    }
    void Update()
    {
        /*
        if(frames_number == 300)
        {
            Debug.Log("Mean time is : " + (int)(total_time_sum / frames_number));
        }

        var watch = System.Diagnostics.Stopwatch.StartNew();

        var w = System.Diagnostics.Stopwatch.StartNew();
        int k = test(1, 2);
        //Debug.Log("k = " + k);
        w.Stop();
        float total_time1 = w.ElapsedMilliseconds;
        Debug.Log("dump debug.log : " + total_time1 + "" + k);
        watch.Stop();
        float total_time = watch.ElapsedMilliseconds;
        Debug.Log("Call function from dll [time] : " + total_time +" with k = " + k);
        */
    }
    private void RenderFrame()
    {
        if (hasInit)
        {
            pImageSize = (int)(dimension.width * dimension.height * 1.5f);//YUV420
            tex = new Texture2D(dimension.width, dimension.height, TextureFormat.R8, false);

            m_YImageTex = new Texture2D(dimension.width, dimension.height, TextureFormat.Alpha8, false);
            m_UImageTex = new Texture2D(dimension.width >> 1, dimension.height >> 1, TextureFormat.Alpha8, false);
            m_VImageTex = new Texture2D(dimension.width >> 1, dimension.height >> 1, TextureFormat.Alpha8, false);
        }
        if (!circularBuffer.IsEmpty)
        {
            mutex.WaitOne();//mutex blocks
            pImage = circularBuffer.Front();
            circularBuffer.PopFront();
            mutex.ReleaseMutex(); //mutex released

            var watch = System.Diagnostics.Stopwatch.StartNew();

            sYUVplanes yuvPlanes = new sYUVplanes();
            yuvPlanes = (sYUVplanes)Marshal.PtrToStructure(pImage, typeof(sYUVplanes));

            //pY, pU, pV :: pointers
            //pY[........Y........]pU[....U....]pV[....V....]
            //  |<-------1------->|  |<--1/4-->|  |<--1/4-->|

            GetComponent<Renderer>().material.mainTexture = m_YImageTex;
            mat = GetComponent<MeshRenderer>().material;

            int s = dimension.width * dimension.height;

            m_YImageTex.LoadRawTextureData(yuvPlanes.Y, s);
            m_UImageTex.LoadRawTextureData(yuvPlanes.U, s / 4);
            m_VImageTex.LoadRawTextureData(yuvPlanes.V, (s / 4));
            m_YImageTex.Apply();
            m_UImageTex.Apply();
            m_VImageTex.Apply();

            mat.SetTexture("_MainTex", m_YImageTex);
            mat.SetTexture("_UTex", m_UImageTex);
            mat.SetTexture("_VTex", m_VImageTex);

            unref_yuvPlanes_structure(pImage);//unref sYUVPlanes structure, as well as AVFrame* memory

            watch.Stop();
            float total_time = watch.ElapsedMilliseconds;
            Debug.Log("Rendering time in current frame is : " + total_time);
            //#endregion
        }
    }
    private void print_hardware_device_support()
    {
        for (int i = 0; i < 10; i++)
        {
            Debug.Log(i);
            Debug.Log(i + " = " + check_hardware_device_support(i));
        }
    }
}

   