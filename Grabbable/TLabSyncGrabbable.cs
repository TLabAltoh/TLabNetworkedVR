using System.Text;
using UnityEngine;

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
            role        = (int)WebRole.guest,
            action      = (int)WebAction.forceRelease,
            transform   = new WebObjectInfo
            {
                id = this.gameObject.name
            }
        };
        string json = JsonUtility.ToJson(obj);
        TLabSyncClient.Instalce.SendWsMessage(json);

        Debug.Log("tlabsyncgrabbable: " + "force release");
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
        if (m_rbAllocated) SetGravity(!active);

        TLabSyncJson obj = new TLabSyncJson
        {
            role        = (int)WebRole.guest,
            action      = (int)WebAction.grabbLock,
            seatIndex   = active ? TLabSyncClient.Instalce.SeatIndex : -1,
            transform   = new WebObjectInfo
            {
                id = this.gameObject.name
            }
        };
        string json = JsonUtility.ToJson(obj);
        TLabSyncClient.Instalce.SendWsMessage(json);

        Debug.Log("tlabsyncgrabbable: " + "grabb lock");
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
            role        = (int)WebRole.guest,
            action      = (int)WebAction.grabbLock,
            seatIndex   = active ? -2 : -1,
            transform   = new WebObjectInfo
            {
                id = this.gameObject.name
            }
        };
        string json = JsonUtility.ToJson(obj);
        TLabSyncClient.Instalce.SendWsMessage(json);

        Debug.Log("tlabsyncgrabbable: " + "simple lock");
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

    public void SyncTransform()
    {
        if (m_enableSync == false) return;

        #region StringBuilderでパケットの生成の高速化

        builder.Clear();

        builder.Append("{");
            builder.Append(TLabSyncClientConst.ROLE);
            builder.Append(((int)WebRole.guest).ToString());
            builder.Append(TLabSyncClientConst.COMMA);
            
            builder.Append(TLabSyncClientConst.ACTION);
            builder.Append(((int)WebAction.syncTransform).ToString());
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
            role        = (int)WebRole.guest,
            action      = (int)WebAction.divideGrabber,
            active      = active,
            transform   = new WebObjectInfo
            {
                id = this.gameObject.name
            }
        };
        string json = JsonUtility.ToJson(obj);
        TLabSyncClient.Instalce.SendWsMessage(json);

        return result;
    }

#if UNITY_EDITOR
    protected override void TestFunc()
    {
        Debug.Log("After Override");
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
        if(TLabSyncClient.Instalce.SeatIndex == m_grabbed && m_grabbed != -1) GrabbLock(false);
    }
}
