using System.Collections.Generic;
using System.Collections;
using UnityEngine;
using NativeWebSocket;
using TLab.XR.Interact;
using TLab.XR.Humanoid;
using TLab.Network;
using TLab.Network.WebRTC;

namespace TLab.XR.Network
{
    [AddComponentMenu("TLab/NetworkedVR/" + nameof(SyncClient) + " (TLab)")]
    [RequireComponent(typeof(WebRTCClient))]
    public class SyncClient : MonoBehaviour
    {
        private string THIS_NAME => "[" + this.GetType().Name + "] ";

        [Header("RoomConfig")]
        [SerializeField] private RoomConfig m_roomConfig;

        [Header("Client Settings")]
        [SerializeField] private string m_clientName = "Default Client";

        [Header("Avator Settings")]
        [SerializeField] private Transform m_cameraRig;
        [SerializeField] private BodyTracker.TrackTarget[] m_trackTargets;

        [SerializeField] private AvatorConfig m_guestAvatorConf;

        [Header("Respown Anchor")]
        [SerializeField] private Transform[] m_respownAnchors;
        [SerializeField] private Transform m_instantiateAnchor;

        [Tooltip("WebRTCClient for synchronizing Transforms between players")]
        [Header("WebRTCClient")]
        [SerializeField] private WebRTCClient m_webrtcClient;

        [Tooltip("Custom message callbacks")]
        [Header("Custom Event")]
        [SerializeField] private CustomCallback[] m_customCallbacks;

        [Tooltip("Whether the user is Host")]
        [Header("User Role")]
        [SerializeField] private WebRole m_role = WebRole.GUEST;

        [SerializeField] private bool m_buildDebug = false;
#if UNITY_EDITOR
        [SerializeField] private bool m_editorDebug = false;
#endif

        public static SyncClient Instance;

        public static bool Initialized => Instance != null;

        private WebSocket m_ws;

        public const int SEAT_LENGTH_LIMMIT = 5;
        public const int HOST_INDEX = 0;
        public const int NOT_REGISTED = -1;

        private int m_seatIndex = NOT_REGISTED;
        private bool[] m_guestTable = new bool[SEAT_LENGTH_LIMMIT];

        private delegate void ReceiveCallback(TLabSyncJson obj);
        private ReceiveCallback[] m_receiveCallbacks = new ReceiveCallback[(int)WebAction.CUSTOM_ACTION + 1];

        private const string COMMA = ".";
        private const string PREFAB_NAME = "OVR_GUEST_ANCHOR";
        private Queue<GameObject>[] m_avatorInstanceQueue = new Queue<GameObject>[SEAT_LENGTH_LIMMIT];

        private IAsyncEnumerator<int> m_connect = null;

        #region [PROPERTY] CLIENT_STATE

        /// <summary>
        /// 
        /// </summary>
        public int seatIndex => m_seatIndex;

        /// <summary>
        /// 
        /// </summary>
        public bool isHost { get => m_role == WebRole.HOST; set => m_role = value == true ? WebRole.HOST : WebRole.GUEST; }

        /// <summary>
        /// 
        /// </summary>
        public bool isRegisted => socketIsNull ? false : m_seatIndex != NOT_REGISTED;

        #endregion [PROPERTY] CLIENT_STATE

        #region [PROPERTY] SOCKET_CONNECTION_STATE

        /// <summary>
        /// 
        /// </summary>
        public bool socketIsNull => m_ws == null;

        /// <summary>
        /// 
        /// </summary>
        public bool socketIsOpen => socketIsNull ? false : m_ws.State == WebSocketState.Open;

        /// <summary>
        /// 
        /// </summary>
        public bool socketIsConnecting => socketIsNull ? false : m_ws.State == WebSocketState.Connecting;

        #endregion [PROPERTY] SOCKET_CONNECTION_STATE

        /// <summary>
        /// 
        /// </summary>
        /// <param name="index"></param>
        /// <returns></returns>
        public bool IsGuestExist(int index) => m_guestTable[index];

        /// <summary>
        /// 
        /// </summary>
        /// <param name="index"></param>
        /// <returns></returns>
        public string GetClientID(int index)
        {
            return m_clientName + "_" + index.ToString();
        }

        #region AVATOR_MANAGEMENT

        /// <summary>
        /// 
        /// </summary>
        /// <param name="go"></param>
        /// <param name="transform"></param>
        /// <returns></returns>
        private GameObject InstantiateWithTransform(GameObject go, Transform transform)
        {
            if (transform != null)
            {
                return Instantiate(go, transform.position, transform.rotation);
            }
            else
            {
                return Instantiate(go);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="playerName"></param>
        /// <param name="seatIndex"></param>
        /// <param name="type"></param>
        /// <returns></returns>
        private string GetBodyTrackerID(string playerName, int seatIndex, AvatorConfig.PartsType type)
        {
            return playerName + COMMA + seatIndex.ToString() + COMMA + type.ToString();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="index"></param>
        /// <param name="parts"></param>
        private void CacheAvatorParts(int index, GameObject parts)
        {
            if (m_avatorInstanceQueue[index] == null)
            {
                m_avatorInstanceQueue[index] = new Queue<GameObject>();
            }

            m_avatorInstanceQueue[index].Enqueue(parts);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="index"></param>
        /// <param name="config"></param>
        private void CloneAvator(int index, AvatorConfig config)
        {
            foreach (var parts in config.parts)
            {
                var type = parts.type;
                var prefab = parts.prefab;

                var instance = InstantiateWithTransform(prefab, m_instantiateAnchor);

                var trackerName = GetBodyTrackerID(PREFAB_NAME, index, type);
                instance.name = trackerName;

                var identifier = instance.GetComponent<AvatorIdentifier>();
                identifier.id = index;
                identifier.partsId = trackerName;
                identifier.avatorId = GetClientID(index);

                CacheAvatorParts(index, instance);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="index"></param>
        public void DeleteAvator(int index)
        {
            var avatorInstanceQueue = m_avatorInstanceQueue[index];
            if (avatorInstanceQueue != null)
            {
                while (avatorInstanceQueue.Count > 0)
                {
                    var go = avatorInstanceQueue.Dequeue();
                    BodyTracker.ClearObject(go);
                }
            }
        }

        #endregion AVATOR_MANAGEMENT

        #region REFLESH

        /// <summary>
        /// Let the server organize cached object information (e.g., Rigidbody allocation)
        /// Request the results of organized object information.
        /// </summary>
        /// <param name="reloadWorldData"></param>
        public void ForceReflesh(bool reloadWorldData) => SendWsMessage(action: WebAction.REFLESH, active: reloadWorldData);

        /// <summary>
        /// Refresh only specific objects.
        /// </summary>
        /// <param name="targetID"></param>
        public void UniReflesh(string targetID) => SendWsMessage(action: WebAction.UNI_REFLESH_TRANSFORM, transform: new WebObjectInfo { id = targetID });

        #endregion REFLESH

        #region CONNECT_SERVER

        /// <summary>
        /// 
        /// </summary>
        public void Exit() => SendWsMessage(WebAction.EXIT);

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        private async IAsyncEnumerator<int> ConnectServerTask()
        {
            Debug.Log(THIS_NAME + "Pass: 0");

            yield return 0;

            if (m_ws != null)
            {
                m_ws.Close();
                m_ws = null;
            }

            Debug.Log(THIS_NAME + "Pass: 1");

            yield return 0;

            m_receiveCallbacks[(int)WebAction.REGIST] = (obj) => { };
            m_receiveCallbacks[(int)WebAction.REGECT] = (obj) => { };
            m_receiveCallbacks[(int)WebAction.ACEPT] = (obj) => {

                m_seatIndex = obj.dstIndex;

                m_guestTable[m_seatIndex] = true;

                // Enable sync own avator

                foreach (var trackTarget in m_trackTargets)
                {
                    var parts = trackTarget.parts;
                    var target = trackTarget.target;

                    // BodyTrackerを動的に追加するため，idのハッシュが使えない．
                    // -----> 代わりに座席番号をgameObject.nameの末尾に追加して一意のidを作成する．

                    var trackerName = GetBodyTrackerID(PREFAB_NAME, m_seatIndex, parts);
                    target.name = trackerName;

                    var tracker = target.gameObject.AddComponent<BodyTracker>();
                    tracker.Init(parts, true);
                    tracker.enableSync = true;

                    CacheAvatorParts(m_seatIndex, tracker.gameObject);
                }

                if (m_instantiateAnchor != null)
                {
                    m_cameraRig.SetLocalPositionAndRotation(m_instantiateAnchor.position, m_instantiateAnchor.rotation);
                }
                else
                {
                    m_cameraRig.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
                }

                var anchor = m_respownAnchors[m_seatIndex];
                m_cameraRig.SetPositionAndRotation(anchor.position, anchor.rotation);

                // Connect to signaling server

                m_webrtcClient.Join(GetClientID(m_seatIndex), m_roomConfig.id);

            };
            m_receiveCallbacks[(int)WebAction.EXIT] = (obj) => { };
            m_receiveCallbacks[(int)WebAction.GUEST_DISCONNECT] = (obj) => {

                int index = obj.srcIndex;

                if (!m_guestTable[index])
                {
                    return;
                }

                DeleteAvator(index);

                m_guestTable[index] = false;

                foreach (var callback in m_customCallbacks)
                {
                    callback.OnGuestDisconnected(index);
                }

                Debug.Log(THIS_NAME + "Guest disconncted: " + index.ToString());

            };
            m_receiveCallbacks[(int)WebAction.GUEST_PARTICIPATION] = (obj) => {

                var index = obj.srcIndex;

                if (m_guestTable[index])
                {
                    Debug.LogError(THIS_NAME + $"Guest already exists: {index}");
                    return;
                }

                CloneAvator(index, m_guestAvatorConf);

                m_guestTable[index] = true;

                foreach (var callback in m_customCallbacks)
                {
                    callback.OnGuestParticipated(index);
                }

                Debug.Log(THIS_NAME + $"Guest participated: {index}");

                return;
            };
            m_receiveCallbacks[(int)WebAction.ALLOCATE_GRAVITY] = (obj) => {

                var rigidbodyAllocated = obj.active;
                var webTransform = obj.transform;
                var controller = ExclusiveController.GetById(webTransform.id);
                controller?.AllocateGravity(rigidbodyAllocated);

            };
            m_receiveCallbacks[(int)WebAction.REGIST_RB_OBJ] = (obj) => { };
            m_receiveCallbacks[(int)WebAction.GRABB_LOCK] = (obj) => {

                var grabIndex = obj.grabIndex;
                var webTransform = obj.transform;
                var controller = ExclusiveController.GetById(webTransform.id);
                controller?.GrabbLock(grabIndex);

            };
            m_receiveCallbacks[(int)WebAction.FORCE_RELEASE] = (obj) => {

                var self = false;
                var webTransform = obj.transform;
                var controller = ExclusiveController.GetById(webTransform.id);
                controller?.ForceRelease(self);

            };
            m_receiveCallbacks[(int)WebAction.DIVIDE_GRABBER] = (obj) => {

                var webTransform = obj.transform;
                var controller = ExclusiveController.GetById(webTransform.id);
                controller?.Divide(obj.active);

            };
            m_receiveCallbacks[(int)WebAction.SYNC_TRANSFORM] = (obj) => {

                var webTransform = obj.transform;
                var syncTransformer = SyncTransformer.GetById(webTransform.id);
                syncTransformer?.SyncFromOutside(webTransform);

            };
            m_receiveCallbacks[(int)WebAction.SYNC_ANIM] = (obj) => {

                var webAnimator = obj.animator;
                var syncAnim = SyncAnimator.GetById(webAnimator.id);
                syncAnim?.SyncAnimFromOutside(webAnimator);

            };
            m_receiveCallbacks[(int)WebAction.CLEAR_TRANSFORM] = (obj) => { };
            m_receiveCallbacks[(int)WebAction.CLEAR_ANIM] = (obj) => { };
            m_receiveCallbacks[(int)WebAction.REFLESH] = (obj) => { };
            m_receiveCallbacks[(int)WebAction.UNI_REFLESH_TRANSFORM] = (obj) => { };
            m_receiveCallbacks[(int)WebAction.UNI_REFLESH_ANIM] = (obj) => { };
            m_receiveCallbacks[(int)WebAction.CUSTOM_ACTION] = (obj) => {

                m_customCallbacks[obj.customIndex].OnMessage(obj.custom);

            };

            m_ws = new WebSocket(m_roomConfig.syncSeverAddr.addr);

            m_ws.OnOpen += () =>
            {
                SendWsMessage(action: WebAction.REGIST);

                Debug.Log(THIS_NAME + "Connection open!");
            };

            m_ws.OnError += (e) =>
            {
                Debug.Log(THIS_NAME + "Error! :" + e);
            };

            m_ws.OnClose += (e) =>
            {
                Debug.Log(THIS_NAME + "Connection closed! :" + e);
            };

            m_ws.OnMessage += (bytes) =>
            {
                var message = System.Text.Encoding.UTF8.GetString(bytes);

                var obj = JsonUtility.FromJson<TLabSyncJson>(message);

#if UNITY_EDITOR
                Debug.Log(THIS_NAME + "OnMessage: " + message);
#endif

                m_receiveCallbacks[obj.action].Invoke(obj);
            };

            Debug.Log(THIS_NAME + "Pass: 2");

            m_ws.Connect();

            m_connect = null;

            Debug.Log(THIS_NAME + "Pass: 3");

            yield return 1;
        }

        /// <summary>
        /// Connect to a Websocekt server asynchronously
        /// </summary>
        public void ConnectServerAsync()
        {
            m_connect = ConnectServerTask();
        }

        #endregion CONNECT_SERVER

        #region RTC_MESSAGE

        /// <summary>
        /// 
        /// </summary>
        /// <param name="src"></param>
        /// <param name="dst"></param>
        /// <param name="count"></param>
        private static unsafe void LongCopy(byte* src, byte* dst, int count)
        {
            // https://github.com/neuecc/MessagePack-CSharp/issues/117

            while (count >= 8)
            {
                *(ulong*)dst = *(ulong*)src;
                dst += 8;
                src += 8;
                count -= 8;
            }

            if (count >= 4)
            {
                *(uint*)dst = *(uint*)src;
                dst += 4;
                src += 4;
                count -= 4;
            }

            if (count >= 2)
            {
                *(ushort*)dst = *(ushort*)src;
                dst += 2;
                src += 2;
                count -= 2;
            }

            if (count >= 1)
            {
                *dst = *src;
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="dst"></param>
        /// <param name="src"></param>
        /// <param name="bytes"></param>
        public void OnRTCMessage(string dst, string src, byte[] bytes)
        {
            int nameBytesLen = bytes[0];
            int subBytesStart = 1 + nameBytesLen;
            int subBytesLen = bytes.Length - subBytesStart;

            byte[] nameBytes = new byte[nameBytesLen];

            unsafe
            {
                fixed (byte* iniP = nameBytes, iniD = bytes)    // id
                {
                    LongCopy(iniD + 1, iniP, nameBytesLen);
                }
            }

            string targetName = System.Text.Encoding.UTF8.GetString(nameBytes);

            var networkedObject = NetworkedObject.GetById(targetName);
            if (networkedObject == null)
            {
                Debug.LogError($"Networked object not found: {targetName}");

                return;
            }

            byte[] subBytes = new byte[subBytesLen];
            System.Array.Copy(bytes, subBytesStart, subBytes, 0, subBytesLen);

            networkedObject.OnRTCMessage(dst, src, subBytes);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="bytes"></param>
        public void SendRTCMessage(byte[] bytes) => m_webrtcClient.SendRTCMsg(bytes);

        #endregion RTC_MESSAGE

        #region WEBSOCKET_MESSAGE

        /// <summary>
        /// 
        /// </summary>
        /// <param name="action"></param>
        /// <param name="dstIndex"></param>
        /// <param name="grabIndex"></param>
        /// <param name="active"></param>
        /// <param name="transform"></param>
        /// <param name="animator"></param>
        /// <param name="customIndex"></param>
        /// <param name="custom"></param>
        public void SendWsMessage(WebAction action, int dstIndex = -1, int grabIndex = -1, bool active = false,
                                  WebObjectInfo transform = null, WebAnimInfo animator = null, int customIndex = -1, string custom = "")
        {
            var obj = new TLabSyncJson
            {
                roomID = m_roomConfig.id,
                action = (int)action,
                role = (int)m_role,
                srcIndex = seatIndex,
                dstIndex = dstIndex,
                grabIndex = grabIndex,
                active = active,
                transform = transform,
                animator = animator,
                customIndex = customIndex,
                custom = custom
            };
            SendWsMessage(JsonUtility.ToJson(obj));
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="obj"></param>
        public void SendWsMessage(TLabSyncJson obj)
        {
            obj.roomID = m_roomConfig.id;
            obj.role = (int)m_role;
            obj.srcIndex = m_seatIndex;
            SendWsMessage(JsonUtility.ToJson(obj));
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="json"></param>
        public async void SendWsMessage(string json)
        {
            if (socketIsOpen)
            {
                await m_ws.SendText(json);
            }
        }

        #endregion WEBSOCKET_MESSAGE

        /// <summary>
        /// 
        /// </summary>
        public void CloseRTC() => m_webrtcClient.Exit();

        /// <summary>
        /// 
        /// </summary>
        public void ConfirmRTCCallbackRegisted()
        {
            if (m_webrtcClient == null)
            {
                m_webrtcClient = GetComponent<WebRTCClient>();
            }

            if (m_webrtcClient.eventCount == 0)
            {
                m_webrtcClient.SetCallback(OnRTCMessage);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        void Reset() => ConfirmRTCCallbackRegisted();

        /// <summary>
        /// 
        /// </summary>
        void Awake()
        {
            Instance = this;

            if (m_webrtcClient == null)
            {
                m_webrtcClient = GetComponent<WebRTCClient>();
            }
        }

        /// <summary>
        /// 
        /// </summary>
        private void Start()
        {
            for (int i = 0; i < m_avatorInstanceQueue.Length; i++)
            {
                if (m_avatorInstanceQueue[i] != null)
                {
                    m_avatorInstanceQueue[i] = new Queue<GameObject>();
                }
            }

            ConnectServerAsync();

#if UNITY_EDITOR
            if (m_editorDebug)
            {
                isHost = true;
            }
#endif

            if (m_buildDebug)
            {
#if UNITY_EDITOR
                isHost = false;
#elif UNITY_ANDROID
                isHost = true;
#endif
            }
        }

        /// <summary>
        /// 
        /// </summary>
        private async void Update()
        {
            if (m_connect != null)
            {
                await m_connect.MoveNextAsync();
            }

#if !UNITY_WEBGL || UNITY_EDITOR
            if (m_ws != null)
            {
                m_ws.DispatchMessageQueue();
            }
#endif
        }

        /// <summary>
        /// 
        /// </summary>
        private async void CloseWebSocket()
        {
            if (m_ws != null)
            {
                await m_ws.Close();
            }

            m_ws = null;
        }

        /// <summary>
        /// 
        /// </summary>
        void OnDestroy() => CloseWebSocket();

        /// <summary>
        /// 
        /// </summary>
        void OnApplicationQuit() => CloseWebSocket();
    }
}
