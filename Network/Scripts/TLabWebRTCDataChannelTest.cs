using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TLabWebRTCDataChannelTest : MonoBehaviour
{
    public void OnMessage(string src, byte[] bytes)
    {
        string receive = System.Text.Encoding.UTF8.GetString(bytes);
        Debug.Log("receive: " + receive);
    }
}
