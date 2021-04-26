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

public class SocketStreaming2 : MonoBehaviour
{
    private Material mat;

    public Texture2D m_YImageTex;
    public Texture2D m_UImageTex = null;
    public Texture2D m_VImageTex = null;
   
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
    public static extern IntPtr init_hw_acceleration(IntPtr data, int length);
    [DllImport("Decoder", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr decode_hw_acceleration(IntPtr data, int length);
    [DllImport("Decoder", CallingConvention = CallingConvention.Cdecl)]
    public static extern int check_hardware_device_support(int i);
    [DllImport("Decoder", CallingConvention = CallingConvention.Cdecl)]
    public static extern void clear();
    [DllImport("Decoder", CallingConvention = CallingConvention.Cdecl)]
    public static extern int test(int a, int b);

    private bool decoder_init(byte[] data)
    {
        var handle = GCHandle.Alloc(data, GCHandleType.Pinned);
        IntPtr p = handle.AddrOfPinnedObject();
        IntPtr pointer;
        if (HARDWARE_ACCELERATION)
        {
            pointer = init_hw_acceleration(p, data.Length);
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
            pointer = decode_hw_acceleration(pData, data.Length);
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
    public void clear_frame()
    {
        clear();//ffmpeg clear stream.
    }
    public async void OpenAndShowSavedFrames()
    {
        if(i == 100)
        {
            i = 90;
        }
        byte[] frame = File.ReadAllBytes("D:\\" + i.ToString());

        if (iFrameHeaderTemplate.SequenceEqual(frame.Take(6)))
        {
            string hex = BitConverter.ToString(frame.Take(6).ToArray());
            Debug.Log("First 10 bytes in hex format [before] preprocessing is :" + hex);
            //0x80 -> 0xC0 header byte (constraint_set1_flag must be 1)
            Buffer.SetByte(frame, 6, 0xC0);
            hex = BitConverter.ToString(frame.Take(6).ToArray());
            Debug.Log("First 10 bytes in hex format [after] preprocessing is :" + hex);
        }

        var watch = System.Diagnostics.Stopwatch.StartNew();
        IntPtr image = IntPtr.Zero;
        try
        {
            image = await Task.Run(() => decode_frame(frame));
        }
        catch(Exception ex)
        {
            Debug.Log(ex.Message);
        }
        
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
                        //Debug.Log("Decoding time over dll is : " + total_time);

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
        clear_frame();
    }
    void UnloadDirties()
    {
        Resources.UnloadUnusedAssets();
    }
    void Start()
    {
        HARDWARE_ACCELERATION = false;
        //Invoke("StartClientThread", 0f);
        //Invoke("OpenAndShowSavedFrames", 0f);
        //Invoke("RenderFrame", 1.5f);
        InvokeRepeating("OpenAndShowSavedFrames", 0f, 2f);
        InvokeRepeating("RenderFrame", 0f, 0.033f);
        InvokeRepeating("UnloadDirties", 0f, 5f);
        //print_hardware_device_support();
    }
    void Update()
    {
        /*
        if(frames_number == 300)
        {
            Debug.Log("Mean time is : " + (int)(total_time_sum / frames_number));
        }
        */
        /*
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
            //tex = new Texture2D(dimension.width, dimension.height, TextureFormat.R8, false);

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

            

            byte[] managedArray = new byte[pImageSize];
            Marshal.Copy(pImage, managedArray, 0, pImageSize);
            
            GetComponent<Renderer>().material.mainTexture = m_YImageTex; ;
            mat = GetComponent<MeshRenderer>().material;

            #region Kiki 2
            // solution inspired by https://github.com/BennyZong/YUV_NV21ToRGB_Unity/tree/master/Assets/YUV
            byte[] bufU = new byte[dimension.width * dimension.height >> 2];
            byte[] bufV = new byte[dimension.width * dimension.height >> 2];
            //byte[] bufUV = new byte[dimension.width * dimension.height >> 1];

            int yIndex = pImageSize * 4 / 6;
            int uIndex = pImageSize * 5 / 6;

            Buffer.BlockCopy(managedArray, yIndex, bufU, 0, dimension.width * dimension.height >> 2);
            Buffer.BlockCopy(managedArray, uIndex, bufV, 0, dimension.width * dimension.height >> 2);

            //for (int i = 0; i < bufUV.Length; i += 2)
            //{
            //    bufUV[i] = bufU[i >> 1];
            //    bufUV[i + 1] = bufV[i >> 1];
            //}
            var watch = System.Diagnostics.Stopwatch.StartNew();

            m_YImageTex.LoadRawTextureData(managedArray);
            m_UImageTex.LoadRawTextureData(bufU);
            m_VImageTex.LoadRawTextureData(bufV);
            m_YImageTex.Apply();
            m_UImageTex.Apply();
            m_VImageTex.Apply();

            mat.SetTexture("_MainTex", m_YImageTex);
            mat.SetTexture("_UTex", m_UImageTex);
            mat.SetTexture("_VTex", m_VImageTex);

            watch.Stop();
            float total_time = watch.ElapsedMilliseconds;
            Debug.Log("Rendering time in current frame is : " + total_time);
            #endregion

            
        }
    }


    private static unsafe void YUV2RGBManaged(byte[] YUVData, byte[] RGBData, int width, int height)
    {

        //returned pixel format is 2yuv - i.e. luminance, y, is represented for every pixel and the u and v are alternated
        //like this (where Cb = u , Cr = y)
        //Y0 Cb Y1 Cr Y2 Cb Y3 

        /*http://msdn.microsoft.com/en-us/library/ms893078.aspx
         * 
         * C = Y - 16
         D = U - 128
         E = V - 128
         R = clip(( 298 * C           + 409 * E + 128) >> 8)
         G = clip(( 298 * C - 100 * D - 208 * E + 128) >> 8)
         B = clip(( 298 * C + 516 * D           + 128) >> 8)

         * here are a whole bunch more formats for doing this...
         * http://stackoverflow.com/questions/3943779/converting-to-yuv-ycbcr-colour-space-many-versions
         */


        fixed (byte* pRGBs = RGBData, pYUVs = YUVData)
        {
            for (int r = 0; r < height; r++)
            {
                byte* pRGB = pRGBs + r * width * 3;
                byte* pYUV = pYUVs + r * width * 2;

                //process two pixels at a time
                for (int c = 0; c < width; c += 2)
                {
                    int C1 = pYUV[1] - 16;
                    int C2 = pYUV[3] - 16;
                    int D = pYUV[2] - 128;
                    int E = pYUV[0] - 128;

                    int R1 = (298 * C1 + 409 * E + 128) >> 8;
                    int G1 = (298 * C1 - 100 * D - 208 * E + 128) >> 8;
                    int B1 = (298 * C1 + 516 * D + 128) >> 8;

                    int R2 = (298 * C2 + 409 * E + 128) >> 8;
                    int G2 = (298 * C2 - 100 * D - 208 * E + 128) >> 8;
                    int B2 = (298 * C2 + 516 * D + 128) >> 8;
#if true
                    //check for overflow
                    //unsurprisingly this takes the bulk of the time.
                    pRGB[0] = (byte)(R1 < 0 ? 0 : R1 > 255 ? 255 : R1);
                    pRGB[1] = (byte)(G1 < 0 ? 0 : G1 > 255 ? 255 : G1);
                    pRGB[2] = (byte)(B1 < 0 ? 0 : B1 > 255 ? 255 : B1);

                    pRGB[3] = (byte)(R2 < 0 ? 0 : R2 > 255 ? 255 : R2);
                    pRGB[4] = (byte)(G2 < 0 ? 0 : G2 > 255 ? 255 : G2);
                    pRGB[5] = (byte)(B2 < 0 ? 0 : B2 > 255 ? 255 : B2);
#else
                    pRGB[0] = (byte)(R1);
                    pRGB[1] = (byte)(G1);
                    pRGB[2] = (byte)(B1);

                    pRGB[3] = (byte)(R2);
                    pRGB[4] = (byte)(G2);
                    pRGB[5] = (byte)(B2);
#endif

                    pRGB += 6;
                    pYUV += 4;
                }
            }
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

   