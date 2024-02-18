using System.Collections;
using UnityEngine;
using TLab.Network.WebRTC.Voice;

namespace TLab.XR.Network
{
    [AddComponentMenu("TLab/NetworkedVR/" + nameof(VoiceChatEntry) + " (TLab)")]
    public class VoiceChatEntry : MonoBehaviour
    {
        [SerializeField] private VoiceChat m_voiceChat;

        private string THIS_NAME => "[" + GetType() + "] ";

        private bool socketIsOpen
        {
            get
            {
                return (SyncClient.Instance != null &&
                        SyncClient.Instance.socketIsOpen &&
                        SyncClient.Instance.seatIndex != SyncClient.NOT_REGISTED);
            }
        }

        private IEnumerator WaitForConnection()
        {
            while (!socketIsOpen)
            {
                yield return new WaitForSeconds(1f);
            }

            m_voiceChat.StartVoiceChat();

            yield break;
        }

        void Start()
        {
            StartCoroutine(WaitForConnection());
        }
    }
}
