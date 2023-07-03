using System.Text;
using UnityEngine;

public class TLabWebRTCDataChannelTest : MonoBehaviour
{
    [SerializeField] TLabWebRTCDataChannel dataChannel;

    public void Join(string id)
    {
        dataChannel.Join(id);
    }

    public void SendMessageTest(string message)
    {
        dataChannel.SendRTCMsg(Encoding.UTF8.GetBytes(message));
    }

    public void OnMessage(string dst, string src, byte[] bytes)
    {
        string receive = Encoding.UTF8.GetString(bytes);
        Debug.Log(src + " ===> " + dst + ": " + receive + ", " + "len: " + bytes.Length.ToString());
    }
}
