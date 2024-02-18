using System.Text;
using UnityEngine;
using TLab.Network.WebRTC;

[ExecuteAlways]
[RequireComponent(typeof(WebRTCClient))]
public class WebRTCClientSample : MonoBehaviour
{
    [SerializeField] private WebRTCClient m_client;

    [SerializeField] private string m_userID;
    [SerializeField] private string m_roomID;

    void Reset()
    {
        if (m_client == null)
        {
            m_client = GetComponent<WebRTCClient>();
        }
    }

    public void Join()
    {
        m_client.Join(m_userID, m_roomID);
    }

    public void Exit()
    {
        m_client.Exit();
    }

    public void SendMessageTest(string message)
    {
        m_client.SendRTCMsg(Encoding.UTF8.GetBytes(message));
    }

    public void OnMessage(string dst, string src, byte[] bytes)
    {
        string receive = Encoding.UTF8.GetString(bytes);
        Debug.Log(src + " ===> " + dst + ": " + receive + ", " + "len: " + bytes.Length.ToString());
    }
}
