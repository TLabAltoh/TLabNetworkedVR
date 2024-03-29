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

        [Header("Sync Server Address (port 5000)")]
        [SerializeField] private string m_serverAddr = "ws://192.168.11.10:5000";
        [SerializeField] private string m_roomID = "VR_Classroom";

        [Header("Body Tracker")]
        [SerializeField] private Transform m_cameraRig;
        [SerializeField] private BodyTracker.TrackTarget[] m_trackTargets;

        [Tooltip("The avatar model of the other party as seen from you")]
        [Header("Guest Avator Preset")]
        [SerializeField] private AvatorConfig m_avatorConfig;

        [Tooltip("Responce position of each player")]
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

        private WebSocket m_websocket;

        public const int SEAT_LENGTH = 5;
        public const int HOST_INDEX = 0;
        public const int NOT_REGISTED = -1;

        private int m_seatIndex = NOT_REGISTED;
        private bool[] m_guestTable = new bool[SEAT_LENGTH];

        private const string COMMA = ".";
        private const string PREFAB_NAME = "OVR_GUEST_ANCHOR";
        private Queue<GameObject>[] m_avatorInstanceQueue = new Queue<GameObject>[SEAT_LENGTH];

        private delegate void ReceiveCallback(TLabSyncJson obj);

        ReceiveCallback[] receiveCallbacks = new ReceiveCallback[(int)WebAction.CUSTOM_ACTION + 1];

        /// <summary>
        /// 
        /// </summary>
        public int seatIndex => m_seatIndex;

        /// <summary>
        /// 
        /// </summary>
        public int seatLength => SEAT_LENGTH;

        /// <summary>
        /// 
        /// </summary>
        public bool socketIsNull => m_websocket == null;

        /// <summary>
        /// 
        /// </summary>
        public bool registed => socketIsNull ? false : m_seatIndex != NOT_REGISTED;

        /// <summary>
        /// 
        /// </summary>
        public bool socketIsOpen => socketIsNull ? false : m_websocket.State == WebSocketState.Open;

        /// <summary>
        /// 
        /// </summary>
        public bool socketIsConnecting => socketIsNull ? false : m_websocket.State == WebSocketState.Connecting;

        /// <summary>
        /// 
        /// </summary>
        public bool isHost { get => m_role == WebRole.HOST; set => m_role = value == true ? WebRole.HOST : WebRole.GUEST; }

        /// <summary>
        /// 
        /// </summary>
        public string serverAddr => m_serverAddr;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="addr"></param>
        public void SetServerAddr(string addr) => m_serverAddr = addr;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="index"></param>
        /// <returns></returns>
        public bool IsGuestExist(int index) => m_guestTable[index];

        /// <summary>
        /// 
        /// </summary>
        /// <param name="seatIndex"></param>
        /// <param name="go"></param>
        public void CacheAvator(int seatIndex, GameObject go)
        {
            if (m_avatorInstanceQueue[seatIndex] == null)
            {
                m_avatorInstanceQueue[seatIndex] = new Queue<GameObject>();
            }

            m_avatorInstanceQueue[seatIndex].Enqueue(go);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="seatIndex"></param>
        /// <param name="go"></param>
        /// <param name="respown"></param>
        public void CloneAvator(int seatIndex, AvatorConfig config)
        {
            foreach (var avatorParts in m_avatorConfig.body)
            {
                var parts = avatorParts.parts;
                var prefab = avatorParts.prefab;

                var guestParts = m_instantiateAnchor != null ? Instantiate(prefab, m_instantiateAnchor.position, m_instantiateAnchor.rotation) : Instantiate(prefab);

                var trackerName = GetBodyTrackerName("", seatIndex, parts);
                guestParts.name = trackerName;

                var identifier = guestParts.GetComponent<AvatorIdentifier>();
                identifier.id = seatIndex;
                identifier.partsId = trackerName;
                identifier.avatorId = gameObject.name + "_" + seatIndex.ToString();

                CacheAvator(seatIndex, guestParts);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="seatIndex"></param>
        public void DeleteAvator(int seatIndex)
        {
            var avatorInstanceQueue = m_avatorInstanceQueue[seatIndex];
            if (avatorInstanceQueue != null)
            {
                while (avatorInstanceQueue.Count > 0)
                {
                    var go = avatorInstanceQueue.Dequeue();
                    BodyTracker.ClearObject(go);
                }
            }
        }

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
        /// playerNameは現在使用していない
        /// </summary>
        /// <param name="playerName"></param>
        /// <param name="seatIndex"></param>
        /// <param name="parts"></param>
        /// <returns></returns>
        private string GetBodyTrackerName(string playerName, int seatIndex, AvatorConfig.BodyParts parts)
        {
            return PREFAB_NAME + COMMA + seatIndex.ToString() + COMMA + parts.ToString();
        }

        /// <summary>
        /// Send exit notice to the server.
        /// Exit only and do not close the socket
        /// </summary>
        public void Exit() => SendWsMessage(WebAction.EXIT);

        /// <summary>
        /// Coroutine to connect to Websocket server asynchronously.
        /// Control execution timing from ConnectServerTaskStart().
        /// I want to use await, so I used IAsyncEnumrator.
        /// </summary>
        /// <returns></returns>
        private async IAsyncEnumerator<int> ConnectServerTask()
        {
            yield return -1;

            receiveCallbacks[(int)WebAction.REGIST] = (obj) => { };
            receiveCallbacks[(int)WebAction.REGECT] = (obj) => { };
            receiveCallbacks[(int)WebAction.ACEPT] = (obj) => {

                /**
                 * Permission to join is granted by the server
                 */

                m_seatIndex = obj.dstIndex;

                m_guestTable[m_seatIndex] = true;

                /**
                 * Enable sync own avator
                 */
                foreach (var trackTarget in m_trackTargets)
                {
                    var parts = trackTarget.parts;
                    var target = trackTarget.target;

                    /**
                     * BodyTrackerを動的に追加するため，idのハッシュが使えない．
                     * -----> 代わりに座席番号を使用する．
                     */
                    var trackerName = GetBodyTrackerName("", m_seatIndex, parts);
                    target.name = trackerName;

                    var tracker = target.gameObject.AddComponent<BodyTracker>();
                    tracker.enableSync = true;
                    tracker.self = true;

                    CacheAvator(m_seatIndex, tracker.gameObject);
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

                /**
                 * Connect to signaling server
                 */
                var userID = gameObject.name + "_" + m_seatIndex.ToString();
                var roomID = m_roomID;
                m_webrtcClient.Join(userID, roomID);

            };
            receiveCallbacks[(int)WebAction.EXIT] = (obj) => { };
            receiveCallbacks[(int)WebAction.GUEST_DISCONNECT] = (obj) => {

                /**
                 * Guest disconnected
                 */

                int guestIndex = obj.srcIndex;

                if (!m_guestTable[guestIndex])
                {
                    return;
                }

                DeleteAvator(guestIndex);

                m_guestTable[guestIndex] = false;

                foreach (var callback in m_customCallbacks)
                {
                    callback.OnGuestDisconnected(guestIndex);
                }

                Debug.Log(THIS_NAME + "guest disconncted: " + guestIndex.ToString());

            };
            receiveCallbacks[(int)WebAction.GUEST_PARTICIPATION] = (obj) => {

                /**
                 * Processing when guest joins
                 */

                int guestIndex = obj.srcIndex;

                if (m_guestTable[guestIndex])
                {
                    Debug.LogError(THIS_NAME + $"guest already exists: {guestIndex}");
                    return;
                }

                CloneAvator(guestIndex, m_avatorConfig);

                m_guestTable[guestIndex] = true;

                foreach (var callback in m_customCallbacks)
                {
                    callback.OnGuestParticipated(guestIndex);
                }

                Debug.Log(THIS_NAME + $"guest participated: {guestIndex}");

                return;
            };
            receiveCallbacks[(int)WebAction.ALLOCATE_GRAVITY] = (obj) => {

                /**
                 * Object's gravity allocation
                 */

                var rigidbodyAllocated = obj.active;
                var webTransform = obj.transform;
                var controller = ExclusiveController.GetById(webTransform.id);
                controller?.AllocateGravity(rigidbodyAllocated);

            };
            receiveCallbacks[(int)WebAction.REGIST_RB_OBJ] = (obj) => { };
            receiveCallbacks[(int)WebAction.GRABB_LOCK] = (obj) => {

                /**
                 * Grabb lock from outside
                 */

                var grabIndex = obj.grabIndex;
                var webTransform = obj.transform;
                var controller = ExclusiveController.GetById(webTransform.id);
                controller?.GrabbLock(grabIndex);

            };
            receiveCallbacks[(int)WebAction.FORCE_RELEASE] = (obj) => {

                /**
                 * Force release request
                 */

                var self = false;
                var webTransform = obj.transform;
                var controller = ExclusiveController.GetById(webTransform.id);
                controller?.ForceRelease(self);

            };
            receiveCallbacks[(int)WebAction.DIVIDE_GRABBER] = (obj) => {

                /**
                 * Divide grabbable object
                 */

                var webTransform = obj.transform;
                var controller = ExclusiveController.GetById(webTransform.id);
                controller?.Divide(obj.active);

            };
            receiveCallbacks[(int)WebAction.SYNC_TRANSFORM] = (obj) => {

                /**
                 * Sync transform
                 */

                var webTransform = obj.transform;
                var syncTransformer = SyncTransformer.GetById(webTransform.id);
                syncTransformer?.SyncFromOutside(webTransform);

            };
            receiveCallbacks[(int)WebAction.SYNC_ANIM] = (obj) => {

                /**
                 * Sync animation
                 */

                var webAnimator = obj.animator;
                var syncAnim = SyncAnimator.GetById(webAnimator.id);
                syncAnim?.SyncAnimFromOutside(webAnimator);

            };
            receiveCallbacks[(int)WebAction.CLEAR_TRANSFORM] = (obj) => { };
            receiveCallbacks[(int)WebAction.CLEAR_ANIM] = (obj) => { };
            receiveCallbacks[(int)WebAction.REFLESH] = (obj) => { };
            receiveCallbacks[(int)WebAction.UNI_REFLESH_TRANSFORM] = (obj) => { };
            receiveCallbacks[(int)WebAction.UNI_REFLESH_ANIM] = (obj) => { };
            receiveCallbacks[(int)WebAction.CUSTOM_ACTION] = (obj) => {

                m_customCallbacks[obj.customIndex].OnMessage(obj.custom);

            };

            m_websocket = new WebSocket(m_serverAddr);

            m_websocket.OnOpen += () =>
            {
                SendWsMessage(action: WebAction.REGIST);

                Debug.Log(THIS_NAME + "Connection open!");
            };

            m_websocket.OnError += (e) =>
            {
                Debug.Log(THIS_NAME + "Error! " + e);
            };

            m_websocket.OnClose += (e) =>
            {
                Debug.Log(THIS_NAME + "Connection closed!");
            };

            m_websocket.OnMessage += (bytes) =>
            {
                string message = System.Text.Encoding.UTF8.GetString(bytes);

                TLabSyncJson obj = JsonUtility.FromJson<TLabSyncJson>(message);

#if UNITY_EDITOR
                Debug.Log(THIS_NAME + "OnMessage - " + message);
#endif

                receiveCallbacks[obj.action].Invoke(obj);
            };

            await m_websocket.Connect();

            yield break;
        }

        /// <summary>
        /// Process to control the execution timing of a coroutine that 
        /// connects to the server asynchronously.
        /// When reconnecting, close the socket once and reconnect.
        /// </summary>
        /// <returns></returns>
        private IEnumerator ConnectServerTaskStart()
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

        /// <summary>
        /// Connect to a Websocekt server asynchronously
        /// </summary>
        public void ConnectServerAsync() => StartCoroutine(ConnectServerTaskStart());

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
                Debug.LogError($"Target Networked Object Not Found: {targetName}");

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
                roomID = m_roomID,
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
            obj.roomID = m_roomID;
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
                await m_websocket.SendText(json);
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
        void Start()
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
        void Update()
        {
#if !UNITY_WEBGL || UNITY_EDITOR
            if (m_websocket != null)
            {
                m_websocket.DispatchMessageQueue();
            }
#endif
        }

        /// <summary>
        /// 
        /// </summary>
        private async void CloseWebSocket()
        {
            if (m_websocket != null)
            {
                await m_websocket.Close();
            }

            m_websocket = null;
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
