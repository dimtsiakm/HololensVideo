using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ShowFPS : MonoBehaviour
{
    TextMesh textMesh;

 // Use this for initialization
    void Start ()
    {
        textMesh = gameObject.GetComponentInChildren<TextMesh>();
    }
    

    

    public void LogMessage(string message, string stackTrace, LogType type)
    {
        if (textMesh.text.Length > 300)
        {
            textMesh.text = message + "\n";
        }
        else
        {
            textMesh.text += message + "\n";
        }
    }
}
