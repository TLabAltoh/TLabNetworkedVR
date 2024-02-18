using UnityEngine;

namespace TLab.Network.WebRTC.Voice
{
    [AddComponentMenu("TLab/NetworkedVR/" + nameof(VoiceChatFilter) + " (TLab)")]
    public class VoiceChatFilter : MonoBehaviour
    {
        void OnAudioFilterRead(float[] data, int channels)
        {
        }
    }
}
