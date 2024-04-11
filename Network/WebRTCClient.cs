#region DEBUG_SYMBOL

//#define DEBUG_LOG_WEBSOCKET
//#undef DEBUG_LOG_WEBSOCKET

//#define DEBUG_LOG_PEERCONNECTION
//#undef DEBUG_LOG_PEERCONNECTION

//#define DEBUG_LOG_DATACHANNEL
//#undef DEBUG_LOG_DATACHANNEL

//#define DEBUG_LOG_MEDIASTREAM
//#undef DEBUG_LOG_MEDIASTREAM

#endregion DEBUG_SYMBOL

using System.Collections.Generic;
using System.Collections;
using System.Linq;
using Unity.WebRTC;
using UnityEngine;
using UnityEngine.Events;
using NativeWebSocket;
using TLab.XR.Network;
using TLab.Network.WebRTC.Voice;

namespace TLab.Network.WebRTC
{
    [AddComponentMenu("TLab/NetworkedVR/" + nameof(WebRTCClient) + " (TLab)")]
    public class WebRTCClient : MonoBehaviour
    {
        [Header("Signaling Server Address")]
        [SerializeField] private ServerAddress m_signalingServerAddr;

        [Header("Audio Streaming Option")]
        [SerializeField] private bool m_streamAudioOnConnect = false;
        [SerializeField] private VoiceChat m_voiceChat;

        [Header("Video Streaming Option")]
        [SerializeField] private bool m_streamVideoOnConnect = false;
        [SerializeField] private Texture m_videoStreamDst;
        [SerializeField] private Texture m_videoStreamSrc;

        [Header("On Message Callback")]
        [SerializeField] private UnityEvent<string, string, byte[]> m_onMessage;

        #region Session Manage Dictionary
        private Dictionary<string, RTCPeerConnection> m_peerConnectionDic = new Dictionary<string, RTCPeerConnection>();
        private Dictionary<string, RTCDataChannel> m_dataChannelDic = new Dictionary<string, RTCDataChannel>();
        private Dictionary<string, bool> m_dataChannelFlagDic = new Dictionary<string, bool>();
        private Dictionary<string, List<RTCIceCandidate>> m_candidateDic = new Dictionary<string, List<RTCIceCandidate>>();
        #endregion Session Manage Dictionary

        private WebSocket m_ws;

        private RTCDataChannelInit m_dataChannelCnf;

        private MediaStream m_receiveMediaStream;
        private MediaStream m_sendMediaStream;
        private Dictionary<string, string> m_mediaStreamIdDic = new Dictionary<string, string>();

        private string m_userID;
        private string m_roomID;

        private IAsyncEnumerator<int> m_connect = null;

        private string THIS_NAME => "[" + this.GetType().Name + "] ";

        public int eventCount => m_onMessage.GetPersistentEventCount();

        public bool streamAudioOnConnect => m_streamAudioOnConnect;

        public bool streamVideoOnConnect => m_streamVideoOnConnect;

        public string userID => m_userID;

        public string roomID => m_roomID;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="callback"></param>
        public void SetCallback(UnityAction<string, string, byte[]> callback)
        {
            m_onMessage.RemoveAllListeners();
            m_onMessage.AddListener(callback);
        }

        #region ICE_CANDIDATE
        /// <summary>
        /// 
        /// </summary>
        /// <param name="src"></param>
        /// <param name="tlabIce"></param>
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

        /// <summary>
        /// 
        /// </summary>
        /// <param name="dst"></param>
        /// <param name="candidate"></param>
        private void SendIceCandidate(string dst, RTCIceCandidate candidate)
        {
            RTCICE tlabICE = new RTCICE();
            tlabICE.sdpMLineIndex = candidate.SdpMLineIndex.ToJson();
            tlabICE.sdpMid = candidate.SdpMid;
            tlabICE.candidate = candidate.Candidate;

            SendWsMeg(RTCSigAction.ICE, null, tlabICE, dst);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="dst"></param>
        /// <param name="candidate"></param>
        private void OnIceCandidate(string dst, RTCIceCandidate candidate)
        {
            SendIceCandidate(dst, candidate);

#if DEBUG_LOG_PEERCONNECTION
            Debug.Log(THIS_NAME + $"On ICE candidate:\n {candidate.Candidate}");
#endif
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="state"></param>
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
        /// <summary>
        /// 
        /// </summary>
        private void DestroyMediaStream()
        {
            m_receiveMediaStream?.Dispose();
            m_receiveMediaStream = null;

            m_sendMediaStream?.Dispose();
            m_sendMediaStream = null;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="active"></param>
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

        /// <summary>
        /// 
        /// </summary>
        /// <param name="active"></param>
        public void OnPauseVideo(bool active)
        {
            // TODO
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="dst"></param>
        public void InitAudioStream(string dst)
        {
            var audioCodecs = new List<RTCRtpCodecCapability>();
            var excludeCodecTypes = new[] { "audio/CN", "audio/telephone-event" };
            foreach (var codec in RTCRtpSender.GetCapabilities(TrackKind.Audio).codecs)
            {
                if (excludeCodecTypes.Count(type => codec.mimeType.Contains(type)) > 0)
                {
                    continue;
                }

                audioCodecs.Add(codec);
            }

            if (m_streamAudioOnConnect)
            {
                var track = new AudioStreamTrack(m_voiceChat.microphoneSource);

                // One transceiver is added at this timing, so AddTransceiver does not need to be executed.
                var sender = m_peerConnectionDic[dst].AddTrack(track, m_sendMediaStream);

                var transceiver = m_peerConnectionDic[dst].GetTransceivers().First(t => t.Sender == sender);
                transceiver.Direction = RTCRtpTransceiverDirection.SendRecv;
                var errorType = transceiver.SetCodecPreferences(audioCodecs.ToArray());
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

        /// <summary>
        /// 
        /// </summary>
        /// <param name="dst"></param>
        private void InitVideoStream(string dst)
        {
            // To be implemented in the future

#if false
            if (m_streamVideoOnConnect)
            {
                var track = new VideoStreamTrack(m_videoStreamSrc);

                m_peerConnectionDic[dst].AddTrack(track, m_sendMediaStream);
            }
#endif
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="dst"></param>
        private void InitMediaStream(string dst)
        {
            InitAudioStream(dst);
            InitVideoStream(dst);
        }
        #endregion MEDIA_STREAMING

        #region SESSION_DESCRIPTION
        /// <summary>
        /// 
        /// </summary>
        /// <param name="pc"></param>
        private void OnSetLocalSuccess(RTCPeerConnection pc)
        {
#if DEBUG_LOG_PEERCONNECTION
            Debug.Log(THIS_NAME + $"SetLocalDescription complete.");
#endif
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="pc"></param>
        private void OnSetRemoteSuccess(RTCPeerConnection pc)
        {
#if DEBUG_LOG_PEERCONNECTION
            Debug.Log(THIS_NAME + $"SetRemoteDescription complete.");
#endif
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="error"></param>
        private void OnCreateSessionDescriptionError(RTCError error)
        {
            Debug.LogError(THIS_NAME + $"Filed to create Session Description: {error.message}");
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="error"></param>
        private void OnSetSessionDescriptionError(ref RTCError error)
        {
            Debug.LogError(THIS_NAME + $"Filed to set Sesstion Description: {error.message}");
        }
        #endregion SESSION_DESCRIPTION

        #region SIGNALING
        /// <summary>
        /// 
        /// </summary>
        /// <param name="dst"></param>
        /// <param name="call"></param>
        private void CreatePeerConnection(string dst, bool call)
        {
            if (m_peerConnectionDic.ContainsKey(dst))
            {
                // When peer connection has already been created,
                // Disconnect existing data channel and create a new one.
                HangUpDataChannel(dst);
            }

            var configuration = GetSelectedSdpSemantics();
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

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        private RTCConfiguration GetSelectedSdpSemantics()
        {
            RTCConfiguration config = default;
            config.iceServers = new RTCIceServer[]
            {
                //new RTCIceServer { urls = new string[] { "stun:stun.l.google.com:19302" } }
            };

            return config;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="desc"></param>
        /// <returns></returns>
        private RTCDesc EncodeSesstionDescription(RTCSessionDescription desc)
        {
            RTCDesc tlabDesc = new RTCDesc();
            tlabDesc.type = (int)desc.type;
            tlabDesc.sdp = desc.sdp;

            Debug.Log(THIS_NAME + $"Encode Session Description: {desc.sdp}");

            return tlabDesc;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="tlabDesc"></param>
        /// <returns></returns>
        private RTCSessionDescription DecodeSesstionDescription(RTCDesc tlabDesc)
        {
            RTCSessionDescription result = new RTCSessionDescription();
            result.type = (RTCSdpType)tlabDesc.type;
            result.sdp = tlabDesc.sdp;

            Debug.Log(THIS_NAME + $"Decode Session Description: { result.sdp }");

            return result;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="dst"></param>
        /// <param name="desc"></param>
        /// <returns></returns>
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

            SendWsMeg(RTCSigAction.ANSWER, EncodeSesstionDescription(desc), null, dst);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="dst"></param>
        /// <param name="desc"></param>
        /// <returns></returns>
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

            SendWsMeg(RTCSigAction.OFFER, EncodeSesstionDescription(desc), null, dst);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="src"></param>
        /// <param name="desc"></param>
        /// <returns></returns>
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

        /// <summary>
        /// 
        /// </summary>
        /// <param name="src"></param>
        /// <param name="desc"></param>
        /// <returns></returns>
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

        /// <summary>
        /// 
        /// </summary>
        /// <param name="dst"></param>
        /// <returns></returns>
        private IEnumerator CreateOffer(string dst)
        {
            Debug.Log(THIS_NAME + $"Create offer to {dst}");

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

        /// <summary>
        /// 
        /// </summary>
        /// <param name="dst"></param>
        /// <returns></returns>
        private IEnumerator Call(string dst)
        {
            if (m_dataChannelDic.ContainsKey(dst) || m_dataChannelFlagDic.ContainsKey(dst) || m_peerConnectionDic.ContainsKey(dst))
            {
                Debug.LogWarning(THIS_NAME + $"dst: {dst} is already exist.");
            }

            CreatePeerConnection(dst, true);

            InitMediaStream(dst);

            StartCoroutine(CreateOffer(dst));

            yield return null;
        }
        #endregion SIGNALING

        #region UTILITY
        /// <summary>
        /// 
        /// </summary>
        /// <param name="userID"></param>
        /// <param name="roomID"></param>
        /// <param name="dataChannelOnf"></param>
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
                    Debug.Log(THIS_NAME + $"OnAudioTrack");

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

        /// <summary>
        /// 
        /// </summary>
        /// <param name="src"></param>
        public void HangUpDataChannel(string src)
        {
            // Close Datachannel befor offer

            if (m_dataChannelDic.ContainsKey(src))
            {
                m_dataChannelDic[src].Close();
                m_dataChannelDic[src] = null;
                m_dataChannelDic.Remove(src);

#if DEBUG_LOG_DATACHANNEL
                Debug.Log(THIS_NAME + "Hung up Datachannel: " + src);
#endif
            }

            // Datachannel flag delete

            if (m_dataChannelFlagDic.ContainsKey(src))
            {
                m_dataChannelFlagDic.Remove(src);

#if DEBUG_LOG_DATACHANNEL
                Debug.Log(THIS_NAME + "Remove Datachannle flag: " + src);
#endif
            }

            // Close peerConnection befor offer

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

        /// <summary>
        /// 
        /// </summary>
        public void HangUpDataChannelAll()
        {
            if (m_dataChannelDic.Count > 0)
            {
                var dsts = new List<string>(m_dataChannelDic.Keys);
                foreach (var dst in dsts)
                {
                    HangUpDataChannel(dst);
                }
            }
        }

        /// <summary>
        /// 
        /// </summary>
        public void Exit()
        {
            HangUpDataChannelAll();

            DestroyMediaStream();

            SendWsMeg(RTCSigAction.EXIT, null, null, null);

            this.m_userID = "";
            this.m_roomID = "";
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="bytes"></param>
        public void SendRTCMsg(byte[] bytes)
        {
            foreach (var id in m_dataChannelDic.Keys)
            {
                var dataChannel = m_dataChannelDic[id];
                if (m_dataChannelFlagDic[id])
                {
                    dataChannel.Send(bytes);
                }
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="action"></param>
        /// <param name="desc"></param>
        /// <param name="ice"></param>
        /// <param name="dst"></param>
        public async void SendWsMeg(RTCSigAction action, RTCDesc desc, RTCICE ice, string dst)
        {
            if (m_ws != null && m_ws.State == WebSocketState.Open)
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

                await m_ws.SendText(json);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="message"></param>
        public void OnWsMsg(string message)
        {
            var parse = JsonUtility.FromJson<RTCSigJson>(message);

            switch (parse.action)
            {
                case (int)RTCSigAction.ICE:
                    AddIceCandidate(parse.src, parse.ice);
                    break;
                case (int)RTCSigAction.OFFER:
                    StartCoroutine(OnOffer(parse.src, DecodeSesstionDescription(parse.desc)));
                    break;
                case (int)RTCSigAction.ANSWER:
                    StartCoroutine(OnAnswer(parse.src, DecodeSesstionDescription(parse.desc)));
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
        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        private async IAsyncEnumerator<int> ConnectServerTask()
        {
            yield return 0;

            if (m_ws != null)
            {
                m_ws.Close();
                m_ws = null;
            }

            yield return 0;

#if DEBUG_LOG_WEBSOCKET
            Debug.Log(THIS_NAME + "Create call back start.");
            Debug.Log(THIS_NAME + "Connect to signaling server start");
#endif

            m_ws = new WebSocket(m_signalingServerAddr.addr);

            m_ws.OnOpen += () =>
            {
#if DEBUG_LOG_WEBSOCKET
                Debug.Log(THIS_NAME + "Connection open!");
#endif
            };

            m_ws.OnError += (e) =>
            {
                Debug.Log(THIS_NAME + "Error! " + e);
            };

            m_ws.OnClose += (e) =>
            {
#if DEBUG_LOG_WEBSOCKET
                Debug.Log(THIS_NAME + "Connection closed!");
#endif
            };

            m_ws.OnMessage += (bytes) =>
            {
                var message = System.Text.Encoding.UTF8.GetString(bytes);

#if DEBUG_LOG_WEBSOCKET
                Debug.Log(THIS_NAME + "On websocket message: " + message);
#endif

                OnWsMsg(message);
            };

            m_ws.Connect();

            m_connect = null;

            yield return 1;
        }

        public void ConnectToSignalintServer()
        {
            m_connect = ConnectServerTask();
        }

        private void Start()
        {
            ConnectToSignalintServer();
        }

        private void Update()
        {
            if (m_connect != null)
            {
                m_connect.MoveNextAsync();
            }

#if !UNITY_WEBGL || UNITY_EDITOR
            if (m_ws != null)
            {
                m_ws.DispatchMessageQueue();
            }
#endif
        }

        private async void CloseWebSocket()
        {
            if (m_ws != null)
            {
                await m_ws.Close();
            }

            m_ws = null;
        }

        void OnDestroy()
        {
            CloseWebSocket();
        }

        void OnApplicationQuit()
        {
            CloseWebSocket();
        }
    }
}
