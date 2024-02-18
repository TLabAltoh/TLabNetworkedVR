using UnityEngine;
using Unity.WebRTC;

namespace TLab.Network.WebRTC.Voice
{
    [AddComponentMenu("TLab/NetworkedVR/" + nameof(VoiceChatPlayer) + " (TLab)")]
    [RequireComponent(typeof(AudioSource))]
    public class VoiceChatPlayer : MonoBehaviour
    {
        [Header("Avator Identifier")]
        [SerializeField] private AvatorIdentifier m_identifier;

        [Header("Audio")]
        [SerializeField] private AudioSource m_outputAudioSource;
        [SerializeField] private VoiceChatFilter m_outputAudioFilter;

        public void SetAudioStreamTrack(AudioStreamTrack track)
        {
            m_outputAudioSource.SetTrack(track);
            m_outputAudioSource.loop = true;
            m_outputAudioSource.Play();
        }

        void Start()
        {
            VoiceChat.Instance.RegistClient(m_identifier.avatorId, this);

            GameObject child = new GameObject("Player");
            child.transform.parent = this.gameObject.transform;

            m_outputAudioSource.playOnAwake = false;
        }
    }
}
