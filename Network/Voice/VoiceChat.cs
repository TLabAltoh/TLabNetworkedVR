using System.Collections;
using UnityEngine;
using Unity.WebRTC;

namespace TLab.Network.WebRTC.Voice
{
    [AddComponentMenu("TLab/NetworkedVR/" + nameof(VoiceChat) + " (TLab)")]
    public class VoiceChat : MonoBehaviour
    {
        [Header("WebRTCDataClinet")]
        [SerializeField] private WebRTCClient m_client;

        [Tooltip("Delivering input from the microphone or")]
        [SerializeField] public bool isStreaming = false;

        public static VoiceChat Instance;

        private AudioSource m_microphoneSource;
        private AudioClip m_microphoneClip;
        private string m_microphoneName;
        private bool m_recording = false;
        public int m_frequency = 16000;

        public const int VOICE_BUFFER_SIZE = 1024;
        public const int CHANNEL = 1;
        public const int LENGTH_SECOUND = 1;

        private Hashtable m_voicePlayers = new Hashtable();

        private string THIS_NAME => "[" + GetType().Name + "] ";

        public AudioSource microphoneSource => m_microphoneSource;

        public void RegistClient(string name, VoiceChatPlayer player) => m_voicePlayers[name] = player;

        public void ReleaseClient(string name) => m_voicePlayers.Remove(name);

        public void OnVoice(string dstID, string srcID, AudioStreamTrack track)
        {
            var player = m_voicePlayers[srcID] as VoiceChatPlayer;

            if (player == null)
            {
                Debug.LogError(THIS_NAME + $"Player not found: {srcID}");
                return;
            }

            player.SetAudioStreamTrack(track);
        }

        private string GetMicrophone()
        {
            if (Microphone.devices.Length > 0)
            {
                return Microphone.devices[0];
            }

            return null;
        }

        private bool StartRecording()
        {
            m_microphoneName = GetMicrophone();
            if (m_microphoneName == null)
            {
                Debug.LogError(THIS_NAME + "Mic Device is empty");
                return false;
            }

            Microphone.GetDeviceCaps(m_microphoneName, out int minFreq, out m_frequency);
            Debug.Log(THIS_NAME + $"minFreq: {minFreq}, maxFreq: {m_frequency}");

            m_microphoneClip = Microphone.Start(m_microphoneName, true, LENGTH_SECOUND, m_frequency);
            if (m_microphoneClip == null)
            {
                Debug.LogError(THIS_NAME + "Failed to recording, using " + m_microphoneName);
                return false;
            }

            Debug.Log(THIS_NAME + $"sampleRate: {m_microphoneClip.frequency}, channel: {m_microphoneClip.channels}, micName: {m_microphoneName}");

            return true;
        }

        public void StartVoiceChat()
        {
            m_recording = StartRecording();
            if (!m_recording)
            {
                return;
            }

            while (!(Microphone.GetPosition(m_microphoneName) > 0)) { }

            m_microphoneSource.clip = m_microphoneClip;
            m_microphoneSource.loop = true;
            m_microphoneSource.Play();
        }

        private void InitializeDPSBuffer()
        {
            var configuration = AudioSettings.GetConfiguration();
            configuration.dspBufferSize = VOICE_BUFFER_SIZE;
            if (!AudioSettings.Reset(configuration))
            {
                Debug.LogError(THIS_NAME + "Failed changing Audio Settings");
            }
        }

        public void CloseRTC() => m_client.Exit();

        private void Update()
        {
            if (!m_recording)
            {
                return;
            }
        }

        private void Reset()
        {
            if (m_client == null)
            {
                m_client = GetComponent<WebRTCClient>();
            }
        }

        private void Awake()
        {
            Instance = this;

            InitializeDPSBuffer();
        }

        private void Start()
        {
            if (m_microphoneSource == null)
            {
                m_microphoneSource = GetComponent<AudioSource>();
            }
        }
    }
}
