using System.Collections.Generic;
using System.Collections;
using UnityEngine;
using UnityEngine.Events;
#if UNITY_EDITOR
using UnityEditor;
#endif
using Unity.WebRTC;
using NativeWebSocket;

public enum TLabRTCSigAction
{
    OFFER,
    ANSWER,
    ICE,
    JOIN,
    EXIT
}

[System.Serializable]
public class TLabRTCICE
{
    public string candidate;
    public string sdpMid;
    public string sdpMLineIndex;
}

[System.Serializable]
public class TLabRTCDesc
{
    public int type;
    public string sdp;
}

[System.Serializable]
public class TLabRTCSigJson
{
    public int action;
    public string room;
    public string src;
    public string dst;
    public TLabRTCDesc desc;
    public TLabRTCICE ice;
}

public class TLabWebRTCDataChannel : MonoBehaviour
{
    private Dictionary<string, RTCPeerConnection> peerConnectionDic = new Dictionary<string, RTCPeerConnection>();
    private Dictionary<string, RTCDataChannel> dataChannelDic = new Dictionary<string, RTCDataChannel>();
    private Dictionary<string, bool> dataChannelFlagDic = new Dictionary<string, bool>();
    private Dictionary<string, List<RTCIceCandidate>> candidateDic = new Dictionary<string, List<RTCIceCandidate>>();

    private WebSocket m_websocket;

    private const string thisName = "[tlabwebrtc] ";

    [SerializeField] private string serverAddr = "ws://localhost:3001";

    [Header("Connection State")]
    [SerializeField] private string userID;
    [SerializeField] private string roomID;

    [SerializeField] private UnityEvent<string, string, byte[]> onMessage;

    public void SetSignalingServerAddr(string addr)
    {
        serverAddr = addr;
    }

    #region ICE Candidate
    private void AddIceCandidate(string src, TLabRTCICE tlabIce)
    {
        int i;
        bool isNull = !int.TryParse(tlabIce.sdpMLineIndex, out i);

        RTCIceCandidateInit candidateInfo = new RTCIceCandidateInit();
        candidateInfo.candidate = tlabIce.candidate;
        candidateInfo.sdpMid = tlabIce.sdpMid;
        if (isNull == false) candidateInfo.sdpMLineIndex = i;

        RTCIceCandidate candidate = new RTCIceCandidate(candidateInfo);

        if (candidateDic.ContainsKey(src) == false)
            candidateDic[src] = new List<RTCIceCandidate>();

        candidateDic[src].Add(candidate);

        if (peerConnectionDic.ContainsKey(src) == true)
        {
            foreach (RTCIceCandidate tmp in candidateDic[src])
            {
                peerConnectionDic[src].AddIceCandidate(tmp);
                Debug.Log(thisName + "add ice candidate");
            }
            candidateDic[src].Clear();
        }
    }

    private void SendIceCandidate(string dst, RTCIceCandidate candidate)
    {
        TLabRTCICE tlabICE = new TLabRTCICE();
        tlabICE.candidate = candidate.Candidate;
        tlabICE.sdpMid = candidate.SdpMid;
        if (candidate.SdpMLineIndex != null)
            tlabICE.sdpMLineIndex = candidate.SdpMLineIndex.ToString();

        SendWsMeg(TLabRTCSigAction.ICE, null, tlabICE, dst);
    }

    private void OnIceConnectionChange(RTCIceConnectionState state)
    {
        switch (state)
        {
            case RTCIceConnectionState.New:
                Debug.Log(thisName + "IceConnectionState: New");
                break;
            case RTCIceConnectionState.Checking:
                Debug.Log(thisName + "IceConnectionState: Checking");
                break;
            case RTCIceConnectionState.Closed:
                Debug.Log(thisName + "IceConnectionState: Closed");
                break;
            case RTCIceConnectionState.Completed:
                Debug.Log(thisName + "IceConnectionState: Completed");
                break;
            case RTCIceConnectionState.Connected:
                Debug.Log(thisName + "IceConnectionState: Connected");
                break;
            case RTCIceConnectionState.Disconnected:
                Debug.Log(thisName + "IceConnectionState: Disconnected");
                break;
            case RTCIceConnectionState.Failed:
                Debug.Log(thisName + "IceConnectionState: Failed");
                break;
            case RTCIceConnectionState.Max:
                Debug.Log(thisName + "IceConnectionState: Max");
                break;
            default:
                break;
        }
    }

    private void OnIceCandidate(string dst, RTCIceCandidate candidate)
    {
        SendIceCandidate(dst, candidate);

        Debug.Log(thisName + $"ICE candidate:\n {candidate.Candidate}");
    }
    #endregion ICE Candidate

    #region SessionDescription
    private void OnSetLocalSuccess(RTCPeerConnection pc)
    {
        Debug.Log(thisName + $"SetLocalDescription complete");
    }

    private void OnSetRemoteSuccess(RTCPeerConnection pc)
    {
        Debug.Log(thisName + $"SetRemoteDescription complete");
    }

    private void OnCreateSessionDescriptionError(RTCError e) { }

    private void OnSetSessionDescriptionError(ref RTCError error) { }
    #endregion SessionDescription

    #region Signaling
    private void CreatePeerConnectionTask(string id, bool call)
    {
        Debug.Log(thisName + "create new peerConnection start");

        RTCConfiguration configuration = GetSelectedSdpSemantics();
        peerConnectionDic[id] = new RTCPeerConnection(ref configuration);
        peerConnectionDic[id].OnIceCandidate = candidate => { OnIceCandidate(id, candidate); };
        peerConnectionDic[id].OnIceConnectionChange = state => { OnIceConnectionChange(state); };

        if (call == true)
        {
            Debug.Log(thisName + "create new dataChennel start");

            RTCDataChannelInit conf = new RTCDataChannelInit();
            dataChannelDic[id] = peerConnectionDic[id].CreateDataChannel("data", conf);
            dataChannelDic[id].OnMessage = bytes => { onMessage.Invoke(userID, id, bytes); };
            dataChannelDic[id].OnOpen = () => {
                Debug.Log(thisName + id + ": DataChannel Open");
                dataChannelFlagDic[id] = true;
            };
            dataChannelDic[id].OnClose = () => {
                Debug.Log(thisName + id + ": DataChannel Close");
            };
            dataChannelFlagDic[id] = false;
        }
        else
        {
            peerConnectionDic[id].OnDataChannel = channel =>
            {
                Debug.Log(thisName + "dataChannel created on offer peerConnection");

                dataChannelFlagDic[id] = true;
                dataChannelDic[id] = channel;
                dataChannelDic[id].OnMessage = bytes => { onMessage.Invoke(userID, id, bytes); };
                dataChannelDic[id].OnClose = () => {
                    Debug.Log(thisName + id + ": DataChannel Close");
                };
            };
        }
    }

    private void CreatePeerConnection(string id, bool call)
    {
        CreatePeerConnectionTask(id, call);
    }

    private RTCConfiguration GetSelectedSdpSemantics()
    {
        RTCConfiguration config = default;
        config.iceServers = new RTCIceServer[]
        {
            new RTCIceServer { urls = new string[] { "stun:stun.l.google.com:19302" } }
        };

        return config;
    }

    private IEnumerator OnAnswer(string src, RTCSessionDescription desc)
    {
        Debug.Log(thisName + "peerConnection.setRemoteDescription start");

        var op2 = peerConnectionDic[src].SetRemoteDescription(ref desc);
        yield return op2;
        if (!op2.IsError)
            OnSetRemoteSuccess(peerConnectionDic[src]);
        else
        {
            var error = op2.Error;
            OnSetSessionDescriptionError(ref error);
        }

        yield break;
    }

    private IEnumerator OnCreateAnswerSuccess(string dst, RTCSessionDescription desc)
    {
        Debug.Log(thisName + $"create answer success:\n{desc.sdp}");

        Debug.Log(thisName + "peerConnection.setLocalDescription start");

        var op = peerConnectionDic[dst].SetLocalDescription(ref desc);
        yield return op;

        if (!op.IsError)
            OnSetLocalSuccess(peerConnectionDic[dst]);
        else
        {
            var error = op.Error;
            OnSetSessionDescriptionError(ref error);
        }

        Debug.Log(thisName + "peerConnection send local description start");

        TLabRTCDesc tlabDesc = new TLabRTCDesc();
        tlabDesc.type = (int)desc.type;
        tlabDesc.sdp = desc.sdp;

        SendWsMeg(TLabRTCSigAction.ANSWER, tlabDesc, null, dst);
    }

    private IEnumerator OnOffer(string src, RTCSessionDescription desc)
    {
        CreatePeerConnection(src, false);

        Debug.Log(thisName + "peerConnection.setRemoteDescription start");

        var op2 = peerConnectionDic[src].SetRemoteDescription(ref desc);
        yield return op2;

        if (!op2.IsError)
            OnSetRemoteSuccess(peerConnectionDic[src]);
        else
        {
            var error = op2.Error;
            OnSetSessionDescriptionError(ref error);
        }

        Debug.Log(thisName + "peerConnection.createAnswer start");

        var op3 = peerConnectionDic[src].CreateAnswer();
        yield return op3;

        if (!op3.IsError)
            yield return StartCoroutine(OnCreateAnswerSuccess(src, op3.Desc));
        else
            OnCreateSessionDescriptionError(op3.Error);

        yield break;
    }

    private IEnumerator OnCreateOfferSuccess(string dst, RTCSessionDescription desc)
    {
        Debug.Log(thisName + $"Offer from pc\n{desc.sdp}");

        Debug.Log(thisName + "pc setLocalDescription start");
        var op = peerConnectionDic[dst].SetLocalDescription(ref desc);
        yield return op;

        if (!op.IsError)
            OnSetLocalSuccess(peerConnectionDic[dst]);
        else
        {
            var error = op.Error;
            OnSetSessionDescriptionError(ref error);
        }

        Debug.Log(thisName + "pc send local description start");

        TLabRTCDesc tlabDesc = new TLabRTCDesc();
        tlabDesc.type = (int)desc.type;
        tlabDesc.sdp = desc.sdp;

        SendWsMeg(TLabRTCSigAction.OFFER, tlabDesc, null, dst);
    }

    private IEnumerator Call(string dst)
    {
        if (dataChannelDic.ContainsKey(dst) || dataChannelFlagDic.ContainsKey(dst) || peerConnectionDic.ContainsKey(dst))
        {
            Debug.LogError(thisName + "dst is already exist");
            yield break;
        }

        CreatePeerConnection(dst, true);

        Debug.Log(thisName + "pc createOffer start");
        var op = peerConnectionDic[dst].CreateOffer();
        yield return op;

        if (!op.IsError)
            yield return StartCoroutine(OnCreateOfferSuccess(dst, op.Desc));
        else
            OnCreateSessionDescriptionError(op.Error);
    }
    #endregion Signaling

    #region Utility
    private RTCSessionDescription GetDescription(TLabRTCDesc tlabDesc)
    {
        RTCSessionDescription result = new RTCSessionDescription();
        result.type = (RTCSdpType)tlabDesc.type;
        result.sdp = tlabDesc.sdp;
        return result;
    }

    public void Join(string userID, string roomID)
    {
        this.userID = userID;
        this.roomID = roomID;

        SendWsMeg(TLabRTCSigAction.JOIN, null, null, null);
    }

    public void HangUp(string src)
    {
        // close datachannel befor offer
        if (dataChannelDic.ContainsKey(src) == true)
        {
            dataChannelDic[src].Close();
            dataChannelDic[src] = null;
            dataChannelDic.Remove(src);
        }

        // datachannel flag delete
        if (dataChannelFlagDic.ContainsKey(src) == true)
            dataChannelFlagDic.Remove(src);

        // close datachannel befor offer
        if (peerConnectionDic.ContainsKey(src) == true)
        {
            peerConnectionDic[src].Close();
            peerConnectionDic[src] = null;
            peerConnectionDic.Remove(src);
        }
    }

    public void HangUpAll()
    {
        // https://dobon.net/vb/dotnet/programing/dictionarytoarray.html

        // close datachannel befor offer
        if (dataChannelDic.Count > 0)
        {
            List<string> dsts = new List<string>(dataChannelDic.Keys);
            foreach (string dst in dsts)
            {
                dataChannelDic[dst].Close();
                dataChannelDic[dst] = null;
                dataChannelDic.Remove(dst);
            }
        }

        // datachannel flag delete
        if(dataChannelFlagDic.Count > 0)
        {
            List<string> dsts = new List<string>(dataChannelFlagDic.Keys);
            foreach (string dst in dsts)
                dataChannelFlagDic.Remove(dst);
        }

        // close datachannel befor offer
        if(peerConnectionDic.Count > 0)
        {
            List<string> dsts = new List<string>(peerConnectionDic.Keys);
            foreach (string dst in dsts)
            {
                peerConnectionDic[dst].Close();
                peerConnectionDic[dst] = null;
                peerConnectionDic.Remove(dst);
            }
        }
    }

    public void Exit()
    {
        HangUpAll();

        SendWsMeg(TLabRTCSigAction.EXIT, null, null, null);

        this.userID = "";
        this.roomID = "";
    }

    public void SendRTCMsg(byte[] bytes)
    {
        foreach(string id in dataChannelDic.Keys)
        {
            RTCDataChannel dataChannel = dataChannelDic[id];

            if (dataChannelFlagDic[id] == true) dataChannel.Send(bytes);
        }
    }

    public async void SendWsMeg(
        TLabRTCSigAction action,
        TLabRTCDesc desc, TLabRTCICE ice, string dst)
    {
        if (m_websocket.State == WebSocketState.Open)
        {
            TLabRTCSigJson obj = new TLabRTCSigJson();
            obj.src = userID;
            obj.room = roomID;
            obj.dst = dst;
            obj.action = (int)action;
            obj.desc = desc;
            obj.ice = ice;

            string json = JsonUtility.ToJson(obj);

            Debug.Log(thisName + "send ws message: " + json);

            await m_websocket.SendText(json);
        }
    }

    public void OnWsMsg(string message)
    {
        TLabRTCSigJson parse = JsonUtility.FromJson<TLabRTCSigJson>(message);

        switch (parse.action)
        {
            case (int)TLabRTCSigAction.ICE:
                AddIceCandidate(parse.src, parse.ice);
                break;
            case (int)TLabRTCSigAction.OFFER:
                StartCoroutine(OnOffer(parse.src, GetDescription(parse.desc)));
                break;
            case (int)TLabRTCSigAction.ANSWER:
                StartCoroutine(OnAnswer(parse.src, GetDescription(parse.desc)));
                break;
            case (int)TLabRTCSigAction.JOIN:
                StartCoroutine(Call(parse.src));
                break;
            case (int)TLabRTCSigAction.EXIT:
                HangUp(parse.src);
                break;
        }
    }
    #endregion Utility

    private async void Start()
    {
        Debug.Log(thisName + "create call back start");

        Debug.Log(thisName + "connect to signaling server start");

        m_websocket = new WebSocket(serverAddr);

        m_websocket.OnOpen += () =>
        {
            Debug.Log(thisName + "Connection open!");
        };

        m_websocket.OnError += (e) =>
        {
            Debug.Log(thisName + "Error! " + e);
        };

        m_websocket.OnClose += (e) =>
        {
            Debug.Log(thisName + "Connection closed!");
        };

        m_websocket.OnMessage += (bytes) =>
        {
            string message = System.Text.Encoding.UTF8.GetString(bytes);

            Debug.Log(thisName + "OnWsMessage: " + message);

            OnWsMsg(message);
        };

        // waiting for messages
        await m_websocket.Connect();
    }

    void Update()
    {
#if !UNITY_WEBGL || UNITY_EDITOR
        m_websocket.DispatchMessageQueue();
#endif
    }

    public async void Close()
    {
        if (m_websocket == null) return;

        Exit();

        await m_websocket.Close();

        m_websocket = null;
    }

    private void OnDestroy()
    {
        Close();
    }

    private void OnApplicationQuit()
    {
        Close();
    }
}

#if UNITY_EDITOR
[CustomEditor(typeof(TLabWebRTCDataChannel))]
[CanEditMultipleObjects]

public class TLabWebRTCDataChannelEditor : Editor
{
    public override void OnInspectorGUI()
    {
        base.OnInspectorGUI();

        serializedObject.Update();

        serializedObject.ApplyModifiedProperties();
    }
}
#endif
