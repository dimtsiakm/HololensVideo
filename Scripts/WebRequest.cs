using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using System;
using CircularBuffer;
using System.Runtime.InteropServices;
using UnityEngine.UI;

public class WebRequest : MonoBehaviour
{

    [Serializable]
    public class Offset
    {
        public int beginning_offset;
        public int end_offset;
    }

    //private int counter = 0;
    private int number_5 = 0;
    //private int FRAME_RATE_DIV = 1;

    private string topic_name;
    private string instance_name;
    private string consumer_name;
    private string base_url; 

    private Text FPSText;
    private Text ReceivedText;

    private Material mat;
    private Texture2D tex;

    private UnityWebRequest req;
    private UnityWebRequest webRequest;

    private long total_time = 0;
    private long total_time_getrequest = 0;
    private long total_time_producemessage = 0;
    private long total_time_loadimage = 0;
    private int total_photos = 0;


    
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
    }
    private int index = 0;
    private byte[] buffer;
    private int buffer_size;
    private bool hasInit = false;
    

    private bool isVideoPlay = false;

    private s_dimension dimension;

    
    [DllImport("Decoder", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr init(IntPtr data, int length);

    [DllImport("Decoder", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr decode(IntPtr data, int length);

    [DllImport("Decoder", CallingConvention = CallingConvention.Cdecl)]
    public static extern void clear();
    

    private int frames = 0; 

    private const int CIRCULAR_BUFFER_CAPACITY = 300;
    
    private CircularBuffer<kafkaMessage> cb = new CircularBuffer<kafkaMessage>(CIRCULAR_BUFFER_CAPACITY);
    
    string consumer_url()
    {
        return base_url + "/consumers/" + consumer_name;
    }

    string instance_url()
    {
        return consumer_url() + "/instances/" + instance_name;
    }

    string fixJson(string value)
    {
        return ("{\"Items\":" + value + "}");
    }

    void ProduceMessages(string json, long time)
    {
        try
        {
            kafkaMessage[] msgs = JsonHelper.FromJson<kafkaMessage>(fixJson(json));
            
            ReceivedText = GameObject.Find("Received").GetComponent<Text>();
            ReceivedText.text = "Received : " + msgs.Length;

            kafkaMessage msg = msgs[msgs.Length - 1];

            byte[] data = Convert.FromBase64String(msg.value);

            GetComponent<Renderer>().material.mainTexture = tex;
            mat = GetComponent<MeshRenderer>().material;
            mat.SetTexture("_MainTex", tex);
            frames++;
        }
        catch (NullReferenceException e1)
        {
            print("Null Reference Exception :: " + e1.Message);
        }
    }

    void StartCoroutineGetRequest()
    {
        StartCoroutine(GetRequest());
    }

    void UnloadDirties()
    {  
        Resources.UnloadUnusedAssets();
    }

    void Awake()
    {
        
    }

    void setParametersKafka()
    {
        base_url = GameObject.Find("Kafka IP").GetComponent<InputField>().text;
        base_url = "http://" + base_url + ":8082";
        topic_name = GameObject.Find("Topic Name").GetComponent<InputField>().text;
        instance_name = GameObject.Find("Instance Name").GetComponent<InputField>().text;
        consumer_name = GameObject.Find("Consumer Name").GetComponent<InputField>().text;
    }

    /*
    void renderFrame()
    {
        kafkaMessage msg;
        byte[] data;

        if (!cb.IsEmpty)
        {
            msg = cb.Front();
            cb.PopFront();
            data = Convert.FromBase64String(msg.value);


            tex.LoadImage(data);
            GetComponent<Renderer>().material.mainTexture = tex;
            mat = GetComponent<MeshRenderer>().material;
            mat.SetTexture("_MainTex", tex);
        }
    }
    */
    

    void PlayVideo()
    {
        if (!isVideoPlay)
        {
            isVideoPlay = true;
            //hasInit = false;
            float time = Time.time;

            setParametersKafka();//parameters
            StartCoroutine(SetConsumer());

            tex = new Texture2D(2, 2, TextureFormat.RGB24, false);

            InvokeRepeating("StartCoroutineGetRequest", (2f), 0.11f);
            //InvokeRepeating("renderFrame", (2f), 0.09f);
            InvokeRepeating("GetFPS", (0f), 1.0f);


            GameObject.Find("Start Button").GetComponentInChildren<Text>().text = "PLAYING VIDEO";
            GameObject.Find("Stop Button").GetComponentInChildren<Text>().text = "Stop Video";
        }
    }


    void StopVideo()
    {
        if (isVideoPlay)
        {
            isVideoPlay = false;
            //hasInit = false;
            float time = Time.time;
            //print("Stop Video :: " + time);
            CancelInvoke();
            //clear();


            GameObject.Find("Start Button").GetComponentInChildren<Text>().text = "Start Video";
            GameObject.Find("Stop Button").GetComponentInChildren<Text>().text = "VIDEO STOPPED";
        }
    }

    void GetFPS()
    {
        FPSText = GameObject.Find("FPS").GetComponent<Text>();
        FPSText.text = "FPS : " + frames;
        frames = 0;
        if(number_5 == 5)//5o sec gia delete seek prompt.
        {
            GameObject.Find("Seeking").GetComponent<Text>().text = "";
            number_5 = 0;
        }
        number_5++;
        //StartCoroutine(GetOffset());
    }


    void Start()
    {
        Button startButton = GameObject.Find("Start Button").GetComponent<Button>();
        startButton.onClick.AddListener(PlayVideo);

        Button stopButton = GameObject.Find("Stop Button").GetComponent<Button>();
        stopButton.onClick.AddListener(StopVideo);

        
    }


    /*
    IEnumerator GetOffset()
    {
        using (UnityWebRequest webRequest = UnityWebRequest.Get(base_url + "/topics/" + topic_name + "/partitions/0/offsets"))
        {
            //webRequest.SetRequestHeader("Accept", "application/vnd.kafka.json.v2+json");
            // Request and wait for the desired page.
            yield return webRequest.SendWebRequest();

            string s = webRequest.downloadHandler.text;
            Offset offset = new Offset();
            print(s);

            offset = JsonUtility.FromJson<Offset>(s);

            print(offset.end_offset);

            print("End Offset is :: " + offset.end_offset);
            StartCoroutine(Seek(offset.end_offset-1));
            print("Offset is : " + s);
        }
    }

    IEnumerator Seek(int offset)
    {
        string data;
        //create consumer.
        data = "{\"offsets\":[{\"topic\": \"" + topic_name + "\",\"partition\": 0,\"offset\":" + offset + "}]}";
        print(instance_url() + "/positions");
        print(data);


        using (UnityWebRequest req = UnityWebRequest.Put(instance_url() + "/positions", data))
        {
            req.method = UnityWebRequest.kHttpVerbPOST;
            req.SetRequestHeader("Content-Type", "application/vnd.kafka.v2+json");
            req.SetRequestHeader("Accept", "application/vnd.kafka.v2+json");
            yield return req.SendWebRequest();


            Debug.Log("Seek to position. "+ offset + " code: " + req.downloadHandler.text);
        }
    }
    */




    IEnumerator GetRequest()
    {
        webRequest = UnityWebRequest.Get(instance_url() + "/records");
        var watch = System.Diagnostics.Stopwatch.StartNew();

        webRequest.SetRequestHeader("Accept", "application/vnd.kafka.json.v2+json");
        // Request and wait for the desired page.
        yield return webRequest.SendWebRequest();

        total_time_getrequest += watch.ElapsedMilliseconds;//time for get request

        string json = webRequest.downloadHandler.text;
        long time = watch.ElapsedMilliseconds;

        if (json.Length > 2)
        {
            ProduceMessages(json, time);
        }

        watch.Stop();
        total_time += watch.ElapsedMilliseconds;

        total_photos++;
    }

    IEnumerator SetConsumer()
    {
        string data = "{\"name\": \"" + instance_name + "\", \"format\": \"json\", \"auto.offset.reset\": \"latest\", \"consumer.request.timeout.ms\": \"0\"}";

        using (req = UnityWebRequest.Put(consumer_url(), data))
        {
            Debug.Log("Start ( create consumer : " + consumer_name + " ).");

            req.method = UnityWebRequest.kHttpVerbPOST;
            req.SetRequestHeader("Content-Type","application/vnd.kafka.v2+json");
            req.SetRequestHeader("Accept", "application/vnd.kafka.v2+json");
            yield return req.SendWebRequest();


            Debug.Log(consumer_name + " created successfully. " + req.downloadHandler.text);
        }

        //subscribe to topic
        data = "{\"topics\":[\""+ topic_name + "\"]}";
        using (req = UnityWebRequest.Put(instance_url() + "/subscription", data))
        {
            Debug.Log("Start ( subscribe ).");

            req.method = UnityWebRequest.kHttpVerbPOST;
            req.SetRequestHeader("Content-Type","application/vnd.kafka.v2+json");
            yield return req.SendWebRequest();

            Debug.Log("Completed (subscribe): " + req.downloadHandler.text);
        }
        //seek to end/beggining
        string partitions = "{\"partitions\": [{\"topic\":\"" + topic_name + "\", \"partition\": 0}]}";
        using (req = UnityWebRequest.Put(instance_url() + "/positions/end", partitions))
        {
            UnityEngine.Debug.Log("seek to end");

            req.method = UnityWebRequest.kHttpVerbPOST;
            req.SetRequestHeader("Content-Type", "application/vnd.kafka.v2+json");
            yield return req.SendWebRequest();


            GameObject.Find("Seeking").GetComponent<Text>().text = "seek to end completed :: " + req.downloadHandler.text;
        }
        
    }

    public static class JsonHelper
    {
        public static T[] FromJson<T>(string json)
        {
            try
            {
                Wrapper<T> wrapper = JsonUtility.FromJson<Wrapper<T>>(json);
                return wrapper.Items;
            }
            catch (Exception e)
            {
                print("Exception e :: " + e.Message);
                return null;
            }
            
        }
        public static string ToJson<T>(T[] array)
        {
            Wrapper<T> wrapper = new Wrapper<T>();
            wrapper.Items = array;
            return JsonUtility.ToJson(wrapper);
        }
        public static string ToJson<T>(T[] array, bool prettyPrint)
        {
            Wrapper<T> wrapper = new Wrapper<T>();
            wrapper.Items = array;
            return JsonUtility.ToJson(wrapper, prettyPrint);
        }
        [System.Serializable]
        private class Wrapper<T>
        {
            public T[] Items; //fixJson("Items { }")
        }
    }

    //Deserialization from array
    //https://stackoverflow.com/questions/36239705/serialize-and-deserialize-json-and-json-array-in-unity
    //message == json
    [System.Serializable]
    public class kafkaMessage
    {
        public string key;
        public string value;
        public int partition;
        public long offset;
        public string topic;
    }


    private void average_times()
    {
        print(total_photos);

        if (total_photos == 1000)
        {
            float avg_time_getrequest = ((float)total_time_getrequest / (float)total_photos);
            float avg_time_producemessage = ((float)total_time_producemessage / (float)total_photos);
            float avg_time_loadimage = ((float)total_time_loadimage / (float)total_photos);
            float avg_time = ((float)total_time / (float)total_photos);


            Debug.LogWarning("150 photos : avg time for get request = " + avg_time_getrequest);
            Debug.LogWarning("150 photos : avg time for produce message function = " + avg_time_producemessage);
            Debug.LogWarning("150 photos : avg time for tex.LoadImage(data) = " + avg_time_loadimage);
            Debug.LogWarning("150 photos : Total average time = " + avg_time);

            GameObject.Find("Seeking").GetComponent<Text>().text = "150 photos : avg time for get request = " + avg_time_getrequest +
            "\n150 photos : avg time for produce message function = " + avg_time_producemessage +
            "\n150 photos : avg time for tex.LoadImage(data) = " + avg_time_loadimage +
            "\n150 photos : Total average time = " + avg_time;

            total_photos = 0;

            total_time = 0;
            total_time_getrequest = 0;
            total_time_producemessage = 0;
            total_time_loadimage = 0;

            StopVideo();
        }
    }



}
/*
    private bool decoder_init()
    {
        byte[] data;
        kafkaMessage msg;
        
        if (!cb.IsEmpty)
        {
            do
            {
                msg = cb.Front();
                cb.PopFront();
                data = Convert.FromBase64String(msg.value);
            } while (data.Length < 15000 && !cb.IsEmpty);
            if (cb.IsEmpty)
            {
                return false;
            }
            else
            {
                var handle = GCHandle.Alloc(data, GCHandleType.Pinned);
                IntPtr p = handle.AddrOfPinnedObject();


                //s_dimension dims = new s_dimension();

                IntPtr pointer = init(p, data.Length);
                if (pointer == IntPtr.Zero)
                {
                    print("init pointer == zero");
                    return false;
                }
                dimension = (s_dimension)Marshal.PtrToStructure(pointer, typeof(s_dimension));

                handle.Free();

                cb.PushFront(msg);
                tex = new Texture2D(dimension.width, dimension.height, TextureFormat.RGB24, false);

                

                return true;
            }
        }
        else
        {
            print("Circular Buffer is Empty");
            return false;
        }
    }
    */

/*
bool decode_frame()
{
    System.Diagnostics.Stopwatch stopWatch = new System.Diagnostics.Stopwatch();
    stopWatch.Start();

    if (!cb.IsEmpty)
    {
        kafkaMessage msg;
        byte[] data;

        if (!hasInit)
        {
            print("start initialization");
            bool initCompleted = decoder_init();
            if (initCompleted)
            {
                print("Initialize completed " + initCompleted);
                hasInit = true;
            }
            else
            {
                print("I need more frames. Initialize again.");
                return false;
            }
        }


        msg = cb.Front();
        cb.PopFront();
        data = Convert.FromBase64String(msg.value);




        print("Frame " + frames + " data : ");
        //print("data length as messg : " + msg.value.Length);
        print("data length as bytes : " + data.Length + " offset of message: :=> " + msg.offset);

        var handle = GCHandle.Alloc(data, GCHandleType.Pinned);
        IntPtr pData = handle.AddrOfPinnedObject();

        s_decoded_frame structDecodedImage = new s_decoded_frame();

        IntPtr pointer = decode(pData, data.Length);

        if(pointer == IntPtr.Zero)
        {
            print("pointer == zero");
            return false;
        }


        if (counter % FRAME_RATE_DIV == 0)
        {
            structDecodedImage = (s_decoded_frame)Marshal.PtrToStructure(pointer, typeof(s_decoded_frame));

            print("file " + index);

            int len = dimension.height * dimension.width * 3;
            buffer = new byte[len];
            Marshal.Copy(structDecodedImage.data, buffer, 0, len);

            handle.Free();

            tex.SetPixelData(buffer, 0);
            tex.Apply();
            GetComponent<Renderer>().material.mainTexture = tex;
            mat = GetComponent<MeshRenderer>().material;
            mat.SetTexture("_MainTex", tex);
            frames++;
            counter = 1;
        }
        else
        {
            counter++;
        }

        stopWatch.Stop();
        long time = stopWatch.ElapsedMilliseconds;
        print("Decode Time with rendering: " + time);
        return true;
    }
    else
    {
        print("cb is empty");
        return false;
    }
}
*/


