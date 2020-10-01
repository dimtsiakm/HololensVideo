using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Video;

public class DemoVideoScript : MonoBehaviour
{
    public GameObject go;
    private VideoPlayer videoPlayer;
    public VideoClip video;
    void Start()
    {

        videoPlayer = go.AddComponent<VideoPlayer>();
        videoPlayer.playOnAwake = true;
        //StartCoroutine(playVideo());  //function call

        videoPlayer.source = VideoSource.VideoClip;

        videoPlayer.clip = video;
        videoPlayer.Prepare();

        Debug.Log("Done preparing video");

        videoPlayer.Play();

/*
        WaitForSeconds waitTime = new WaitForSeconds(5);
        while (!videoPlayer.isPrepared)
        {
            Debug.Log("Preparing Video");
            yield return waitTime;
            break;
        }
        */

        

    }




    IEnumerator playVideo()
    {

        videoPlayer.source = VideoSource.VideoClip;

        videoPlayer.clip = video;
        videoPlayer.Prepare();

        WaitForSeconds waitTime = new WaitForSeconds(5);
        while (!videoPlayer.isPrepared)
        {
            Debug.Log("Preparing Video");
            yield return waitTime;
            break;
        }

        Debug.Log("Done preparing video");

        videoPlayer.Play();

    }

    void Update()
    {
        
    }
}
