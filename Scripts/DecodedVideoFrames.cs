using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Runtime.InteropServices;
using System;
using System.IO;
using System.Diagnostics;
using System.ComponentModel;

public class DecodedVideoFrames : MonoBehaviour
{
    private Material mat;
    private Texture2D tex;

    [StructLayout(LayoutKind.Sequential, Pack = 1, CharSet = CharSet.Ansi)]
    private struct s_decoded_frame
    {
        public IntPtr data;
        public int linesize;
        public int width;
        public int height;
    }

    private int index = 0;
    private byte[] buffer;
    private int buffer_size;


    [DllImport("Decoder")]
    public static extern bool init(IntPtr data, int length);

    [DllImport("Decoder")]
    public static extern IntPtr decode(IntPtr data, int length);

    [DllImport("Decoder")]
    public static extern void clear();

    void Awake()
    {
        
    }

    void Start()
    {
        byte[] data = File.ReadAllBytes(@"D:\1.h264");
        var handle = GCHandle.Alloc(data, GCHandleType.Pinned);
        IntPtr p = handle.AddrOfPinnedObject();
       
        init(p, data.Length);
        handle.Free();


        tex = new Texture2D(960, 720, TextureFormat.RGB24, false);

        InvokeRepeating("decode_frame", 1.0f, 1.0f);
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    void decode_frame()
    {
        index++;
        byte[] data = File.ReadAllBytes(@"D:\" + index + ".h264");
        var handle = GCHandle.Alloc(data, GCHandleType.Pinned);
        IntPtr pData = handle.AddrOfPinnedObject();

        s_decoded_frame structDecodedImage = new s_decoded_frame();

        IntPtr pointer = decode(pData, data.Length);
        structDecodedImage = (s_decoded_frame)Marshal.PtrToStructure(pointer, typeof(s_decoded_frame));

        print("file " + index);

        int len = structDecodedImage.height * structDecodedImage.linesize;
        buffer = new byte[len];
        Marshal.Copy(structDecodedImage.data, buffer, 0, len);
        handle.Free();

        tex.SetPixelData(buffer, 0);
        tex.Apply();
        GetComponent<Renderer>().material.mainTexture = tex;
        mat = GetComponent<MeshRenderer>().material;
        mat.SetTexture("_MainTex", tex);
        
        /*
        
        */
        //tex.LoadImage(buffer,);//memory leaking - clean new Textures
        //mat.SetTexture("_MainTex", tex);// = tex.LoadImage(msg.value.image);
        //Resources.UnloadUnusedAssets();
    }

}
