using System;
using System.Text;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class TLabSyncGrabbable : TLabVRGrabbable
{
    [Header("Sync Setting")]

    [Tooltip("これが有効化されていないとオブジェクトは同期されない")]
    [SerializeField] public bool m_enableSync = false;

    [Tooltip("有効化すると毎フレーム自動でオブジェクトを同期する")]
    [SerializeField] public bool m_autoSync = false;

    [Tooltip("有効化すると誰からもこのオブジェクトを掴めなくなる")]
    [SerializeField] public bool m_locked = false;

    private bool m_rbAllocated = false;
    private int m_grabbed = -1;

    private bool m_isSyncFromOutside = false;

    // https://www.fenet.jp/dotnet/column/language/4836/
    // A fast approach to string processing

    private StringBuilder builder = new StringBuilder();

    //
    private const string thisName = "[tlabsyncgrabbable] ";

    private bool CanRbSync
    {
        get
        {
            return (m_rb == null) ? false : m_rb.useGravity;
        }
    }

    public bool IsEnableGravity
    {
        get
        {
            return (m_rb == null) ? false : m_rb.useGravity;
        }
    }

    public bool IsUseGravity
    {
        get
        {
            return m_useGravity;
        }
    }

    public int GrabbedIndex
    {
        get
        {
            return m_grabbed;
        }
    }

    public bool RbAllocated
    {
        get
        {
            return m_rbAllocated;
        }
    }

    public bool IsSyncFromOutside
    {
        get
        {
            return m_isSyncFromOutside;
        }
    }

    public void SyncFromOutside(WebObjectInfo transform)
    {
        WebVector3 position = transform.position;
        WebVector3 scale    = transform.scale;
        WebVector4 rotation = transform.rotation;

        this.transform.localScale = new Vector3(scale.x, scale.y, scale.z);

        if (m_useRigidbody == true)
        {
            m_rb.MovePosition(new Vector3(position.x, position.y, position.z));
            m_rb.MoveRotation(new Quaternion(rotation.x, rotation.y, rotation.z, rotation.w));
        }
        else
        {
            this.transform.position = new Vector3(position.x, position.y, position.z);
            this.transform.rotation = new Quaternion(rotation.x, rotation.y, rotation.z, rotation.w);
        }

        m_isSyncFromOutside = true;
    }

    public void AllocateGravity(bool active)
    {
        m_rbAllocated = active;

        // 自分がこのオブジェクトのrigidbodyの担当
        // 誰も掴んでいない
        // ------> 重力計算を有効化
        SetGravity((m_grabbed == -1 && active) ? true : false);

        Debug.Log(thisName + "rb allocated " + (m_grabbed == -1 && active) + "\t" + this.gameObject.name);
    }

    public void ForceReleaseSelf()
    {
        if (m_mainParent != null)
        {
            m_mainParent    = null;
            m_subParent     = null;
            m_grabbed       = -1;

            SetGravity(false);
        }
    }

    public void ForceReleaseFromOutside()
    {
        if(m_mainParent != null)
        {
            m_mainParent    = null;
            m_subParent     = null;
            m_grabbed       = -1;

            SetGravity(false);
        }
    }

    public void ForceRelease()
    {
        ForceReleaseSelf();

        TLabSyncJson obj = new TLabSyncJson
        {
            role        = (int)WebRole.GUEST,
            action      = (int)WebAction.FORCERELEASE,
            transform   = new WebObjectInfo
            {
                id = this.gameObject.name
            }
        };
        string json = JsonUtility.ToJson(obj);
        TLabSyncClient.Instalce.SendWsMessage(json);

        Debug.Log(thisName + "force release");
    }

    public void GrabbLockFromOutside(int index)
    {
        if (index != -1)
        {
            if (m_mainParent != null)
            {
                m_mainParent    = null;
                m_subParent     = null;
            }

            m_grabbed = index;

            if (m_rbAllocated == true) SetGravity(false);
        }
        else
        {
            m_grabbed = -1;
            if (m_rbAllocated == true) SetGravity(true);
        }
    }

    public void GrabbLock(bool active)
    {
        if (m_rbAllocated == true) SetGravity(!active);

        TLabSyncJson obj = new TLabSyncJson
        {
            role        = (int)WebRole.GUEST,
            action      = (int)WebAction.GRABBLOCK,
            seatIndex   = active ? TLabSyncClient.Instalce.SeatIndex : -1,
            transform   = new WebObjectInfo
            {
                id = this.gameObject.name
            }
        };
        string json = JsonUtility.ToJson(obj);
        TLabSyncClient.Instalce.SendWsMessage(json);

        Debug.Log(thisName + "grabb lock");
    }

    public void SimpleLock(bool active)
    {
        /*
            -1 : No one is grabbing
            -2 : No one grabbed, but Rigidbody does not calculate
        */

        // Ensure that the object you are grasping does not cover
        // If someone has already grabbed the object, overwrite it

        // parse.seatIndex	: player index that is grabbing the object
        // seatIndex		: index of the socket actually communicating

        if (m_rbAllocated) SetGravity(!active);

        TLabSyncJson obj = new TLabSyncJson
        {
            role        = (int)WebRole.GUEST,
            action      = (int)WebAction.GRABBLOCK,
            seatIndex   = active ? -2 : -1,
            transform   = new WebObjectInfo
            {
                id = this.gameObject.name
            }
        };
        string json = JsonUtility.ToJson(obj);
        TLabSyncClient.Instalce.SendWsMessage(json);

        Debug.Log(thisName + "simple lock");
    }

    protected override void RbGripSwitch(bool grip)
    {
        // 自分自身がGravityの計算を担当している
        GrabbLock(grip);
    }

    public override bool AddParent(GameObject parent)
    {
        // 掴むことが有効化されている
        // 誰も掴んでいない

        if(m_locked == true || m_grabbed != -1) return false;

        return base.AddParent(parent);
    }

    #region SyncTransform
    private unsafe void LongCopy(byte* src, byte* dst, int count)
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
    /// WebRTC
    /// </summary>
    public void SyncRTCTransform()
    {
        if (m_enableSync == false) return;

        #region unsageコードを使用したパケットの生成

        // transform
        // (3 + 4 + 3) * 4 = 40 byte

        // id
        // 1 + (...)

        float[] rtcTransform = new float[10];

        rtcTransform[0] = this.transform.position.x;
        rtcTransform[1] = this.transform.position.y;
        rtcTransform[2] = this.transform.position.z;

        rtcTransform[3] = this.transform.rotation.x;
        rtcTransform[4] = this.transform.rotation.y;
        rtcTransform[5] = this.transform.rotation.z;
        rtcTransform[6] = this.transform.rotation.w;

        rtcTransform[7] = this.transform.localScale.x;
        rtcTransform[8] = this.transform.localScale.y;
        rtcTransform[9] = this.transform.localScale.z;

        byte[] id       = System.Text.Encoding.UTF8.GetBytes(this.gameObject.name);
        byte[] packet   = new byte[1 + name.Length + rtcTransform.Length * sizeof(float)];

        packet[0] = (byte)name.Length;

        int offset  = 1 + name.Length;
        int dataLen = rtcTransform.Length * sizeof(float);

        unsafe
        {
            // id
            fixed (byte* iniP = packet, iniD = id)
                for (byte* pt = iniP + 1, pd = iniD; pt < iniP + offset; pt++, pd++) *pt = *pd;

            // transform
            fixed (byte*  iniP = packet)
            fixed (float* iniD = &(rtcTransform[0]))
                for (byte* pt = iniP + offset, pd = (byte*)iniD; pt < iniP + offset + dataLen; pt++, pd++) *pt = *pd;
        }

        #endregion unsageコードを使用したパケットの生成

        TLabSyncClient.Instalce.SendRTCMessage(packet);

        m_isSyncFromOutside = false;
    }

    /// <summary>
    /// WebSocket
    /// </summary>
    public void SyncTransform()
    {
        if (m_enableSync == false) return;

        #region StringBuilderでパケットの生成の高速化

        builder.Clear();

        builder.Append("{");
            builder.Append(TLabSyncClientConst.ROLE);
            builder.Append(((int)WebRole.GUEST).ToString());
            builder.Append(TLabSyncClientConst.COMMA);
            
            builder.Append(TLabSyncClientConst.ACTION);
            builder.Append(((int)WebAction.SYNCTRANSFORM).ToString());
            builder.Append(TLabSyncClientConst.COMMA);
            
            builder.Append(TLabSyncClientConst.TRANSFORM);
            builder.Append("{");
                builder.Append(TLabSyncClientConst.TRANSFORM_ID);
                builder.Append("\"");
                builder.Append(this.gameObject.name);
                builder.Append("\"");
                builder.Append(TLabSyncClientConst.COMMA);
                
                builder.Append(TLabSyncClientConst.RIGIDBODY);
                builder.Append((m_useRigidbody ? "true" : "false"));
                builder.Append(TLabSyncClientConst.COMMA);

                builder.Append(TLabSyncClientConst.GRAVITY);
                builder.Append((m_useGravity ? "true" : "false"));
                builder.Append(TLabSyncClientConst.COMMA);

                builder.Append(TLabSyncClientConst.POSITION);
                builder.Append("{");
                    builder.Append(TLabSyncClientConst.X);
                    builder.Append((this.transform.position.x).ToString());
                    builder.Append(TLabSyncClientConst.COMMA);

                    builder.Append(TLabSyncClientConst.Y);
                    builder.Append((this.transform.position.y).ToString());
                    builder.Append(TLabSyncClientConst.COMMA);

                    builder.Append(TLabSyncClientConst.Z);
                    builder.Append((this.transform.position.z).ToString());
                builder.Append("}");
                builder.Append(TLabSyncClientConst.COMMA);

                builder.Append(TLabSyncClientConst.ROTATION);
                builder.Append("{");
                    builder.Append(TLabSyncClientConst.X);
                    builder.Append((this.transform.rotation.x).ToString());
                    builder.Append(TLabSyncClientConst.COMMA);

                    builder.Append(TLabSyncClientConst.Y);
                    builder.Append((this.transform.rotation.y).ToString());
                    builder.Append(TLabSyncClientConst.COMMA);

                    builder.Append(TLabSyncClientConst.Z);
                    builder.Append((this.transform.rotation.z).ToString());
                    builder.Append(TLabSyncClientConst.COMMA);

                    builder.Append(TLabSyncClientConst.W);
                    builder.Append((this.transform.rotation.w).ToString());
                builder.Append("}");
                builder.Append(TLabSyncClientConst.COMMA);

                builder.Append(TLabSyncClientConst.SCALE);
                builder.Append("{");
                    builder.Append(TLabSyncClientConst.X);
                    builder.Append((this.transform.localScale.x).ToString());
                    builder.Append(TLabSyncClientConst.COMMA);

                    builder.Append(TLabSyncClientConst.Y);
                    builder.Append((this.transform.localScale.y).ToString());
                    builder.Append(TLabSyncClientConst.COMMA);

                    builder.Append(TLabSyncClientConst.Z);
                    builder.Append((this.transform.localScale.z).ToString());
                builder.Append("}");
            builder.Append("}");
        builder.Append("}");

        string json = builder.ToString();
        #endregion

        #region 作りたいパケット
        //TLabSyncJson obj = new TLabSyncJson
        //{
        //    role = (int)WebRole.guest,
        //    action = (int)WebAction.syncTransform,

        //    transform = new WebObjectInfo
        //    {
        //        id = this.gameObject.name,

        //        rigidbody = m_useRigidbody,
        //        gravity = m_useGravity,

        //        position = new WebVector3
        //        {
        //            x = this.transform.position.x,
        //            y = this.transform.position.y,
        //            z = this.transform.position.z
        //        },
        //        rotation = new WebVector4
        //        {
        //            x = this.transform.rotation.x,
        //            y = this.transform.rotation.y,
        //            z = this.transform.rotation.z,
        //            w = this.transform.rotation.w,
        //        },
        //        scale = new WebVector3
        //        {
        //            x = this.transform.localScale.x,
        //            y = this.transform.localScale.y,
        //            z = this.transform.localScale.z
        //        }
        //    }
        //};

        //string json = JsonUtility.ToJson(obj);
        #endregion

        TLabSyncClient.Instalce.SendWsMessage(json);

        m_isSyncFromOutside = false;
    }
    #endregion SyncTransform

    public void OnDevideButtonClick()
    {
        Devide();
    }

    public void DivideFromOutside(bool active)
    {
        base.Devide(active);
    }

    public override int Devide()
    {
        // 分割/結合処理

        int result = base.Devide();

        if (result < 0) return -1;

        bool active = result == 0 ? true : false;

        // 結合/分割を切り替えたので，誰もこのオブジェクトを掴んでいない状態にする

        TLabSyncGrabbable[] grabbables = this.gameObject.GetComponentsInChildren<TLabSyncGrabbable>();
        foreach (TLabSyncGrabbable grabbable in grabbables) grabbable.ForceRelease();

        // オブジェクトの分割を通知

        TLabSyncJson obj = new TLabSyncJson
        {
            role        = (int)WebRole.GUEST,
            action      = (int)WebAction.DIVIDEGRABBER,
            active      = active,
            transform   = new WebObjectInfo
            {
                id = this.gameObject.name
            }
        };
        string json = JsonUtility.ToJson(obj);
        TLabSyncJson parse = JsonUtility.FromJson<TLabSyncJson>(json);
        TLabSyncClient.Instalce.SendWsMessage(json);

        return result;
    }

    public override void SetInitialChildTransform()
    {
        base.SetInitialChildTransform();

        if (m_enableDivide == false) return;

        TLabSyncGrabbable[] grabbables = this.gameObject.GetComponentsInChildren<TLabSyncGrabbable>();
        foreach (TLabSyncGrabbable grabbable in grabbables) grabbable.SyncTransform();
    }

#if UNITY_EDITOR
    protected override void TestFunc()
    {
        Debug.Log(thisName + "After Override");
    }
#endif

    protected override void Start()
    {
        base.Start();

        SetGravity(false);
        TLabSyncClient.Instalce.AddSyncGrabbable(this.gameObject.name, this);
    }

    protected override void Update()
    {
        if (m_mainParent != null)
        {
            if (m_subParent != null && m_scaling)
                UpdateScale();
            else
            {
                m_scaleInitialDistance = -1.0f;
                UpdatePosition();
            }

            SyncTransform();
        }
        else
        {
            m_scaleInitialDistance = -1.0f;

            if(m_enableSync && (m_autoSync || m_rbAllocated && CanRbSync)) SyncTransform();
        }
    }

    private void OnDestroy()
    {
        // このオブジェクトをロックしているのが自分だったら解除する
        if(TLabSyncClient.Instalce.SeatIndex == m_grabbed &&
            m_grabbed != -1 &&
            m_grabbed != -2) GrabbLock(false);
    }
}

#if UNITY_EDITOR
[CustomEditor(typeof(TLabSyncGrabbable))]
[CanEditMultipleObjects]

public class TLabSyncGrabbableEditor : Editor
{
    public override void OnInspectorGUI()
    {
        base.OnInspectorGUI();

        serializedObject.Update();

        TLabSyncGrabbable grabbable = target as TLabSyncGrabbable;
        TLabVRRotatable rotatable   = grabbable.gameObject.GetComponent<TLabVRRotatable>();

        if (rotatable != null && GUILayout.Button("Initialize for Rotatable"))
        {
            grabbable.InitializeRotatable();
            EditorUtility.SetDirty(grabbable);
            EditorUtility.SetDirty(rotatable);
        }

        if(grabbable.EnableDivide == true && GUILayout.Button("Initialize for Devibable"))
        {
            // Grabbable
            // RigidbodyのUseGravityを無効化する
            grabbable.m_enableSync = true;
            grabbable.m_autoSync = false;
            grabbable.m_locked = false;
            grabbable.UseRigidbody(false, false);

            if (grabbable.EnableDivide == true)
            {
                // If grabbable is enable devide
                MeshFilter meshFilter = grabbable.GetComponent<MeshFilter>();
                if (meshFilter == null) grabbable.gameObject.AddComponent<MeshFilter>();
            }
            else
            {
                MeshFilter meshFilter = grabbable.GetComponent<MeshFilter>();
                if (meshFilter != null) Destroy(meshFilter);

                MeshRenderer meshRenderer = grabbable.GetComponent<MeshRenderer>();
                if (meshRenderer != null) Destroy(meshRenderer);
            }

            // SetLayerMask
            grabbable.gameObject.layer = LayerMask.NameToLayer("TLabGrabbable");

            // Rotatable
            if (rotatable == null) grabbable.gameObject.AddComponent<TLabSyncRotatable>();

            // MeshCollider
            MeshCollider meshCollider = grabbable.gameObject.GetComponent<MeshCollider>();
            if (meshCollider == null)
                meshCollider = grabbable.gameObject.AddComponent<MeshCollider>();
            meshCollider.enabled = true;

            EditorUtility.SetDirty(grabbable);
            EditorUtility.SetDirty(rotatable);

            // Childlen

            foreach (GameObject divideAnchor in grabbable.DivideAnchors)
                foreach (Transform grabbableChildTransform in divideAnchor.GetComponentsInChildren<Transform>())
                {
                    if (grabbableChildTransform.gameObject == divideAnchor.gameObject)  continue;
                    if (grabbableChildTransform.gameObject.activeSelf == false)         continue;

                    // Grabbable
                    TLabSyncGrabbable grabbableChild = grabbableChildTransform.gameObject.GetComponent<TLabSyncGrabbable>();
                    if (grabbableChild == null)
                        grabbableChild = grabbableChildTransform.gameObject.AddComponent<TLabSyncGrabbable>();

                    // SetLayerMask
                    grabbableChild.gameObject.layer = LayerMask.NameToLayer("TLabGrabbable");

                    // Rotatable
                    grabbableChild.m_enableSync = true;
                    grabbableChild.m_autoSync = false;
                    grabbableChild.m_locked = false;
                    grabbableChild.UseRigidbody(false, false);

                    TLabSyncRotatable rotatableChild = grabbableChild.gameObject.GetComponent<TLabSyncRotatable>();
                    if (rotatableChild == null) rotatableChild = grabbableChild.gameObject.AddComponent<TLabSyncRotatable>();

                    // MeshCollider
                    MeshCollider meshColliderChild = grabbableChildTransform.gameObject.gameObject.GetComponent<MeshCollider>();
                    if (meshColliderChild == null)
                        meshColliderChild = grabbableChildTransform.gameObject.gameObject.AddComponent<MeshCollider>();
                    meshColliderChild.enabled = false;

                    EditorUtility.SetDirty(grabbableChild);
                    EditorUtility.SetDirty(rotatable);
                }
        }

        serializedObject.ApplyModifiedProperties();
    }
}
#endif