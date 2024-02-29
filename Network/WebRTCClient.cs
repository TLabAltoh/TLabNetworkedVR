//#define DEBUG_LOG_WEBSOCKET
//#undef DEBUG_LOG_WEBSOCKET

//#define DEBUG_LOG_PEERCONNECTION
//#undef DEBUG_LOG_PEERCONNECTION

//#define DEBUG_LOG_DATACHANNEL
//#undef DEBUG_LOG_DATACHANNEL

#define DEBUG_LOG_MEDIASTREAM
//#undef DEBUG_LOG_MEDIASTREAM

using System.Collections.Generic;
using System.Collections;
using System.Linq;
using Unity.WebRTC;
using UnityEngine;
using UnityEngine.Events;
using NativeWebSocket;
using TLab.Network.WebRTC.Voice;

namespace TLab.Network.WebRTC
{
    [AddComponentMenu("TLab/NetworkedVR/" + nameof(WebRTCClient) + " (TLab)")]
    public class WebRTCClient : MonoBehaviour
    {
        [Header("Signaling Server Address")]
        [SerializeField] private string m_serverAddr = "ws://localhost:3001";

        [Header("Connection State (Debug)")]
        [SerializeField] private string m_userID;
        [SerializeField] private string m_roomID;

        [Header("Audio Streaming Option")]
        [SerializeField] private bool m_streamAudio = false;
        [SerializeField] private VoiceChat m_voiceChat;

        [Header("Video Streaming Option")]
        [SerializeField] private bool m_streamVideo = false;
        [SerializeField] private Texture m_videoStreamDst;
        [SerializeField] private Texture m_videoStreamSrc;

        [Header("On Message Callback")]
        [SerializeField] private UnityEvent<string, string, byte[]> m_onMessage;

        /**
         * Session Dictionary
         */
        private Dictionary<string, RTCPeerConnection> m_peerConnectionDic = new Dictionary<string, RTCPeerConnection>();
        private Dictionary<string, RTCDataChannel> m_dataChannelDic = new Dictionary<string, RTCDataChannel>();
        private Dictionary<string, bool> m_dataChannelFlagDic = new Dictionary<string, bool>();
        private Dictionary<string, List<RTCIceCandidate>> m_candidateDic = new Dictionary<string, List<RTCIceCandidate>>();

        private WebSocket m_websocket;

        private RTCDataChannelInit m_dataChannelCnf;

        private MediaStream m_receiveMediaStream;
        private MediaStream m_sendMediaStream;
        private Dictionary<string, string> m_mediaStreamIdDic = new Dictionary<string, string>();

        private string THIS_NAME => "[" + this.GetType().Name + "] ";

        public int eventCount => m_onMessage.GetPersistentEventCount();

        public void SetSignalingServerAddr(string addr) => m_serverAddr = addr;

        public void SetCallback(UnityAction<string, string, byte[]> callback)
        {
            m_onMessage.RemoveAllListeners();
            m_onMessage.AddListener(callback);
        }

        #region ICE_CANDIDATE
        private void AddIceCandidate(string src, RTCICE tlabIce)
        {
            var candidateInfo = new RTCIceCandidateInit();
            candidateInfo.sdpMLineIndex = tlabIce.sdpMLineIndex.TryToInt();
            candidateInfo.sdpMid = tlabIce.sdpMid;
            candidateInfo.candidate = tlabIce.candidate;
            var candidate = new RTCIceCandidate(candidateInfo);

            if (!m_candidateDic.ContainsKey(src))
            {
                m_candidateDic[src] = new List<RTCIceCandidate>();
            }

            m_candidateDic[src].Add(candidate);

            if (m_peerConnectionDic.ContainsKey(src))
            {
                foreach (RTCIceCandidate tmp in m_candidateDic[src])
                {
                    m_peerConnectionDic[src].AddIceCandidate(tmp);
                }
                m_candidateDic[src].Clear();
            }
        }

        private void SendIceCandidate(string dst, RTCIceCandidate candidate)
        {
            RTCICE tlabICE = new RTCICE();
            tlabICE.sdpMLineIndex = candidate.SdpMLineIndex.ToJson();
            tlabICE.sdpMid = candidate.SdpMid;
            tlabICE.candidate = candidate.Candidate;

            SendWsMeg(RTCSigAction.ICE, null, tlabICE, dst);
        }

        private void OnIceCandidate(string dst, RTCIceCandidate candidate)
        {
            SendIceCandidate(dst, candidate);

#if DEBUG_LOG_PEERCONNECTION
            Debug.Log(THIS_NAME + $"On ICE candidate:\n {candidate.Candidate}");
#endif
        }

        private void OnIceConnectionChange(RTCIceConnectionState state)
        {
            switch (state)
            {
                case RTCIceConnectionState.New:
                    Debug.Log(THIS_NAME + "IceConnectionState: New");
                    break;
                case RTCIceConnectionState.Checking:
                    Debug.Log(THIS_NAME + "IceConnectionState: Checking");
                    break;
                case RTCIceConnectionState.Closed:
                    Debug.Log(THIS_NAME + "IceConnectionState: Closed");
                    break;
                case RTCIceConnectionState.Completed:
                    Debug.Log(THIS_NAME + "IceConnectionState: Completed");
                    break;
                case RTCIceConnectionState.Connected:
                    Debug.Log(THIS_NAME + "IceConnectionState: Connected");
                    break;
                case RTCIceConnectionState.Disconnected:
                    Debug.Log(THIS_NAME + "IceConnectionState: Disconnected");
                    break;
                case RTCIceConnectionState.Failed:
                    Debug.Log(THIS_NAME + "IceConnectionState: Failed");
                    break;
                case RTCIceConnectionState.Max:
                    Debug.Log(THIS_NAME + "IceConnectionState: Max");
                    break;
                default:
                    break;
            }
        }
        #endregion ICE_CANDIDATE

        #region MEDIA_STREAMING
        private void DestroyMediaStream()
        {
            m_receiveMediaStream?.Dispose();
            m_receiveMediaStream = null;

            m_sendMediaStream?.Dispose();
            m_sendMediaStream = null;
        }

        public void OnPauseAudio(bool active)
        {
            foreach (RTCPeerConnection pc in m_peerConnectionDic.Values)
            {
                foreach (RTCRtpTransceiver transceiver in pc.GetTransceivers())
                {
                    transceiver.Sender.Track.Enabled = active;
                }
            }
        }

        public void OnPauseVideo(bool active)
        {
            // TODO
        }

        public void InitAudioStream(string dst)
        {
            List<RTCRtpCodecCapability> audioCodecs = new List<RTCRtpCodecCapability>();
            string[] excludeCodecTypes = new[] { "audio/CN", "audio/telephone-event" };
            foreach (var codec in RTCRtpSender.GetCapabilities(TrackKind.Audio).codecs)
            {
                if (excludeCodecTypes.Count(type => codec.mimeType.Contains(type)) > 0)
                {
                    continue;
                }

                audioCodecs.Add(codec);
            }

            if (m_streamAudio)
            {
                AudioStreamTrack track = new AudioStreamTrack(m_voiceChat.microphoneSource);

                /**
                 * One transceiver is added at this timing, so AddTransceiver does not need to be executed.
                 */
                RTCRtpSender sender = m_peerConnectionDic[dst].AddTrack(track, m_sendMediaStream);

                RTCRtpTransceiver transceiver = m_peerConnectionDic[dst].GetTransceivers().First(t => t.Sender == sender);
                transceiver.Direction = RTCRtpTransceiverDirection.SendRecv;
                RTCErrorType errorType = transceiver.SetCodecPreferences(audioCodecs.ToArray());
                if (errorType != RTCErrorType.None)
                {
                    Debug.LogError(THIS_NAME + $"SetCodecPreferences Error: {errorType}");
                }
            }
            else
            {
                m_peerConnectionDic[dst].AddTransceiver(TrackKind.Audio);
            }
        }

        private void InitVideoStream(string dst)
        {
            /**
             * To be implemented in the future
             */
            //if (m_streamVideo)
            //{
            //    VideoStreamTrack track = new VideoStreamTrack(m_videoStreamSrc);

            //    m_peerConnectionDic[dst].AddTrack(track, m_sendMediaStream);
            //}
        }

        private void InitMediaStream(string dst)
        {
            InitAudioStream(dst);
            InitVideoStream(dst);
        }
        #endregion MEDIA_STREAMING

        #region SESSION_DESCRIPTION
        private void OnSetLocalSuccess(RTCPeerConnection pc)
        {
#if DEBUG_LOG_PEERCONNECTION
            Debug.Log(THIS_NAME + $"SetLocalDescription complete.");
#endif
        }

        private void OnSetRemoteSuccess(RTCPeerConnection pc)
        {
#if DEBUG_LOG_PEERCONNECTION
            Debug.Log(THIS_NAME + $"SetRemoteDescription complete.");
#endif
        }

        private void OnCreateSessionDescriptionError(RTCError error)
        {
            Debug.LogError(THIS_NAME + $"Filed to create Session Description: {error.message}");
        }

        private void OnSetSessionDescriptionError(ref RTCError error)
        {
            Debug.LogError(THIS_NAME + $"Filed to set Sesstion Description: {error.message}");
        }
        #endregion SESSION_DESCRIPTION

        #region SIGNALING
        private void CreatePeerConnection(string dst, bool call)
        {
            if (m_peerConnectionDic.ContainsKey(dst))
            {
                // peer has already been created
                return;
            }

            RTCConfiguration configuration = GetSelectedSdpSemantics();
            m_peerConnectionDic[dst] = new RTCPeerConnection(ref configuration);
            m_peerConnectionDic[dst].OnIceCandidate = candidate => { OnIceCandidate(dst, candidate); };
            m_peerConnectionDic[dst].OnIceConnectionChange = state => { OnIceConnectionChange(state); };
            m_peerConnectionDic[dst].OnTrack = eventTrack => {
                m_mediaStreamIdDic[eventTrack.Track.Id] = dst;
                m_receiveMediaStream.AddTrack(eventTrack.Track);
            };

            if (call)
            {
                m_dataChannelDic[dst] = m_peerConnectionDic[dst].CreateDataChannel("data", m_dataChannelCnf);
                m_dataChannelDic[dst].OnMessage = bytes => { m_onMessage.Invoke(m_userID, dst, bytes); };
                m_dataChannelDic[dst].OnOpen = () => {
#if DEBUG_LOG_DATACHANNEL
                    Debug.Log(THIS_NAME + dst + ": DataChannel open.");
#endif
                    m_dataChannelFlagDic[dst] = true;
                };
                m_dataChannelDic[dst].OnClose = () => {
#if DEBUG_LOG_DATACHANNEL
                    Debug.Log(THIS_NAME + dst + ": DataChannel close.");
#endif
                };
                m_dataChannelFlagDic[dst] = false;
            }
            else
            {
                m_peerConnectionDic[dst].OnDataChannel = channel =>
                {
#if DEBUG_LOG_DATACHANNEL
                    Debug.Log(THIS_NAME + "DataChannel created on offer peerConnection.");
#endif

                    m_dataChannelFlagDic[dst] = true;
                    m_dataChannelDic[dst] = channel;
                    m_dataChannelDic[dst].OnMessage = bytes => { m_onMessage.Invoke(m_userID, dst, bytes); };
                    m_dataChannelDic[dst].OnClose = () => {
#if DEBUG_LOG_DATACHANNEL
                        Debug.Log(THIS_NAME + dst + ": DataChannel Close.");
#endif
                    };
                };
            }
        }

        private RTCConfiguration GetSelectedSdpSemantics()
        {
            RTCConfiguration config = default;
            config.iceServers = new RTCIceServer[]
            {
                //new RTCIceServer { urls = new string[] { "stun:stun.l.google.com:19302" } }
            };

            return config;
        }

        private RTCDesc EncodeSesstionDesc(RTCSessionDescription desc)
        {
            RTCDesc tlabDesc = new RTCDesc();
            tlabDesc.type = (int)desc.type;
            tlabDesc.sdp = desc.sdp;

            Debug.Log(THIS_NAME + $"Encode Session Description: {desc.sdp}");

            return tlabDesc;
        }

        private RTCSessionDescription DecodeSesstionDesc(RTCDesc tlabDesc)
        {
            RTCSessionDescription result = new RTCSessionDescription();
            result.type = (RTCSdpType)tlabDesc.type;
            result.sdp = tlabDesc.sdp;

            Debug.Log(THIS_NAME + $"Decode Session Description: { result.sdp }");

            return result;
        }

        private IEnumerator OnCreateAnswerSuccess(string dst, RTCSessionDescription desc)
        {
            var op = m_peerConnectionDic[dst].SetLocalDescription(ref desc);
            yield return op;

            if (!op.IsError)
            {
                OnSetLocalSuccess(m_peerConnectionDic[dst]);
            }
            else
            {
                var error = op.Error;
                OnSetSessionDescriptionError(ref error);
            }

            SendWsMeg(RTCSigAction.ANSWER, EncodeSesstionDesc(desc), null, dst);
        }

        private IEnumerator OnCreateOfferSuccess(string dst, RTCSessionDescription desc)
        {
            var op = m_peerConnectionDic[dst].SetLocalDescription(ref desc);
            yield return op;

            if (!op.IsError)
            {
                OnSetLocalSuccess(m_peerConnectionDic[dst]);
            }
            else
            {
                var error = op.Error;
                OnSetSessionDescriptionError(ref error);
            }

            SendWsMeg(RTCSigAction.OFFER, EncodeSesstionDesc(desc), null, dst);
        }

        private IEnumerator OnAnswer(string src, RTCSessionDescription desc)
        {
            var op2 = m_peerConnectionDic[src].SetRemoteDescription(ref desc);
            yield return op2;

            if (!op2.IsError)
            {
                OnSetRemoteSuccess(m_peerConnectionDic[src]);
            }
            else
            {
                var error = op2.Error;
                OnSetSessionDescriptionError(ref error);
            }

            yield break;
        }

        private IEnumerator OnOffer(string src, RTCSessionDescription desc)
        {
            CreatePeerConnection(src, false);

            InitMediaStream(src);

            var op2 = m_peerConnectionDic[src].SetRemoteDescription(ref desc);
            yield return op2;

            if (!op2.IsError)
            {
                OnSetRemoteSuccess(m_peerConnectionDic[src]);
            }
            else
            {
                var error = op2.Error;
                OnSetSessionDescriptionError(ref error);
            }

            var op3 = m_peerConnectionDic[src].CreateAnswer();
            yield return op3;

            if (!op3.IsError)
            {
                yield return StartCoroutine(OnCreateAnswerSuccess(src, op3.Desc));
            }
            else
            {
                OnCreateSessionDescriptionError(op3.Error);
            }

            yield break;
        }

        private IEnumerator CreateOffer (string dst)
        {
            Debug.Log(THIS_NAME + $"Create Offer");

            var op = m_peerConnectionDic[dst].CreateOffer();
            yield return op;

            if (!op.IsError)
            {
                yield return StartCoroutine(OnCreateOfferSuccess(dst, op.Desc));
            }
            else
            {
                OnCreateSessionDescriptionError(op.Error);
            }
        }

        private IEnumerator Call(string dst)
        {
            if (m_dataChannelDic.ContainsKey(dst) || m_dataChannelFlagDic.ContainsKey(dst) || m_peerConnectionDic.ContainsKey(dst))
            {
                Debug.LogError(THIS_NAME + $"dst: {dst} is already exist.");
                yield break;
            }

            CreatePeerConnection(dst, true);

            InitMediaStream(dst);

            StartCoroutine(CreateOffer(dst));
        }
        #endregion SIGNALING

        #region UTILITY
        public void Join(string userID, string roomID, RTCDataChannelInit dataChannelOnf = null)
        {
            m_userID = userID;
            m_roomID = roomID;

            m_dataChannelCnf = dataChannelOnf;

            DestroyMediaStream();

            m_sendMediaStream = new MediaStream();
            m_receiveMediaStream = new MediaStream();
            m_receiveMediaStream.OnAddTrack = eventTrack =>
            {
                if (eventTrack.Track is VideoStreamTrack videoTrack)
                {
                    Debug.Log(THIS_NAME + $"OnVideoTrack");

                    videoTrack.OnVideoReceived += texture =>
                    {
                        Debug.Log(THIS_NAME + $"OnVideoTrackReceived {videoTrack}, texture={texture.width}x{texture.height}");

                        m_videoStreamDst = (Texture2D)texture;
                    };
                }

                if (eventTrack.Track is AudioStreamTrack audioTrack)
                {
                    Debug.Log(THIS_NAME + $"OnAudioTrack.");

                    if (m_mediaStreamIdDic.ContainsKey(eventTrack.Track.Id))
                    {
                        string dst = m_mediaStreamIdDic[eventTrack.Track.Id];

                        m_voiceChat.OnVoice(m_userID, dst, audioTrack);

                        m_mediaStreamIdDic.Remove(eventTrack.Track.Id);
                    }
                }
            };

            SendWsMeg(RTCSigAction.JOIN, null, null, null);
        }

        public void HangUpDataChannel(string src)
        {
            /**
             * Close Datachannel befor offer
             */
            if (m_dataChannelDic.ContainsKey(src))
            {
                m_dataChannelDic[src].Close();
                m_dataChannelDic[src] = null;
                m_dataChannelDic.Remove(src);

#if DEBUG_LOG_DATACHANNEL
                Debug.Log(THIS_NAME + "Hung up Datachannel: " + src);
#endif
            }

            /**
             * Datachannel flag delete
             */
            if (m_dataChannelFlagDic.ContainsKey(src))
            {
                m_dataChannelFlagDic.Remove(src);

#if DEBUG_LOG_DATACHANNEL
                Debug.Log(THIS_NAME + "Remove Datachannle flag: " + src);
#endif
            }

            /**
             * Close peerConnection befor offer
             */
            if (m_peerConnectionDic.ContainsKey(src))
            {
                foreach (RTCRtpTransceiver transceiver in m_peerConnectionDic[src].GetTransceivers())
                {
                    if (transceiver.Sender != null)
                    {
                        transceiver.Sender.Track.Stop();
                        m_sendMediaStream.RemoveTrack(transceiver.Sender.Track);
                        transceiver.Sender.Track.Dispose();
                    }

                    if (transceiver.Receiver != null)
                    {
                        transceiver.Receiver.Track.Stop();
                        m_receiveMediaStream.RemoveTrack(transceiver.Receiver.Track);
                        transceiver.Receiver.Track.Dispose();
                    }
                }

                m_peerConnectionDic[src].Close();
                m_peerConnectionDic[src] = null;
                m_peerConnectionDic.Remove(src);

#if DEBUG_LOG_DATACHANNEL
                Debug.Log(THIS_NAME + "Remove peerConnection: " + src);
#endif
            }
        }

        public void HangUpDataChannelAll()
        {
            if (m_dataChannelDic.Count > 0)
            {
                List<string> dsts = new List<string>(m_dataChannelDic.Keys);
                foreach (string dst in dsts)
                {
                    HangUpDataChannel(dst);
                }
            }
        }

        public void Exit()
        {
            HangUpDataChannelAll();

            DestroyMediaStream();

            SendWsMeg(RTCSigAction.EXIT, null, null, null);

            this.m_userID = "";
            this.m_roomID = "";
        }

        public void SendRTCMsg(byte[] bytes)
        {
            foreach (string id in m_dataChannelDic.Keys)
            {
                RTCDataChannel dataChannel = m_dataChannelDic[id];
                if (m_dataChannelFlagDic[id])
                {
                    dataChannel.Send(bytes);
                }
            }
        }

        public async void SendWsMeg(RTCSigAction action, RTCDesc desc, RTCICE ice, string dst)
        {
            if (m_websocket != null && m_websocket.State == WebSocketState.Open)
            {
                var obj = new RTCSigJson();
                obj.src = m_userID;
                obj.room = m_roomID;
                obj.dst = dst;
                obj.action = (int)action;
                obj.desc = desc;
                obj.ice = ice;

                string json = JsonUtility.ToJson(obj);

#if DEBUG_LOG_WEBSOCKET
                Debug.Log(THIS_NAME + "Send websocket message: " + json);
#endif

                await m_websocket.SendText(json);
            }
        }

        public void OnWsMsg(string message)
        {
            var parse = JsonUtility.FromJson<RTCSigJson>(message);

            switch (parse.action)
            {
                case (int)RTCSigAction.ICE:
                    AddIceCandidate(parse.src, parse.ice);
                    break;
                case (int)RTCSigAction.OFFER:
                    StartCoroutine(OnOffer(parse.src, DecodeSesstionDesc(parse.desc)));
                    break;
                case (int)RTCSigAction.ANSWER:
                    StartCoroutine(OnAnswer(parse.src, DecodeSesstionDesc(parse.desc)));
                    break;
                case (int)RTCSigAction.JOIN:
                    StartCoroutine(Call(parse.src));
                    break;
                case (int)RTCSigAction.EXIT:
                    HangUpDataChannel(parse.src);
                    break;
            }
        }
        #endregion UTILITY

        private async IAsyncEnumerator<int> ConnectServerTask()
        {
            yield return -1;

#if DEBUG_LOG_WEBSOCKET
            Debug.Log(THIS_NAME + "Create call back start.");
            Debug.Log(THIS_NAME + "Connect to signaling server start");
#endif

            m_websocket = new WebSocket(m_serverAddr);

            m_websocket.OnOpen += () =>
            {
#if DEBUG_LOG_WEBSOCKET
                Debug.Log(THIS_NAME + "Connection open!");
#endif
            };

            m_websocket.OnError += (e) =>
            {
                Debug.Log(THIS_NAME + "Error! " + e);
            };

            m_websocket.OnClose += (e) =>
            {
#if DEBUG_LOG_WEBSOCKET
                Debug.Log(THIS_NAME + "Connection closed!");
#endif
            };

            m_websocket.OnMessage += (bytes) =>
            {
                string message = System.Text.Encoding.UTF8.GetString(bytes);

#if DEBUG_LOG_WEBSOCKET
                Debug.Log(THIS_NAME + "On websocket message: " + message);
#endif

                OnWsMsg(message);
            };

            /**
             * Waiting for messages
             */
            await m_websocket.Connect();

            yield break;
        }

        private IEnumerator ConnectToSignalingServerStart()
        {
            /**
             * I don't know how many frames it takes to close the Websocket client.
             * So I'll wait for one frame anyway.
             */

            yield return null;

            if (m_websocket != null)
            {
                m_websocket.Close();
                m_websocket = null;
            }

            yield return null;

            IAsyncEnumerator<int> task = ConnectServerTask();
            task.MoveNextAsync();

            yield return null;

            task.MoveNextAsync();

            yield break;
        }

        public void ConnectToSignalintServer()
        {
            StartCoroutine(ConnectToSignalingServerStart());
        }

        private void Start()
        {
            ConnectToSignalintServer();
        }

        void Update()
        {
#if !UNITY_WEBGL || UNITY_EDITOR
            if (m_websocket != null)
            {
                m_websocket.DispatchMessageQueue();
            }
#endif
        }

        private async void CloseWebSocket()
        {
            if (m_websocket != null)
            {
                await m_websocket.Close();
            }

            m_websocket = null;
        }

        void OnDestroy()
        {
            /**
             * OnDestroy() waits for CloseWebSocket() to exit ?
             */
            CloseWebSocket();
        }

        void OnApplicationQuit()
        {
            CloseWebSocket();
        }
    }
}
