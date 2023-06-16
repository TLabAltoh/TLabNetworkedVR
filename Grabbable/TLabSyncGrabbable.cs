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
        WebVector3 scale = transform.scale;
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

    public void SetGravity(bool active)
    {
        if (m_rb != null) EnableGravity(active);
    }

    public void AllocateGravity(bool active)
    {
        m_rbAllocated = active;
        SetGravity((m_grabbed == -1 && active) ? true : false);
    }

    public void ForceReleaseSelf()
    {
        if (m_mainParent != null)
        {
            m_mainParent = null;
            m_subParent = null;
            m_grabbed = -1;

            RbGripSwitch(false);
        }
    }

    public void ForceReleaseFromOutside()
    {
        if(m_mainParent != null)
        {
            m_mainParent = null;
            m_subParent = null;
            m_grabbed = -1;

            RbGripSwitch(false);
        }
    }

    public void ForceRelease()
    {
        ForceReleaseSelf();

        TLabSyncJson obj = new TLabSyncJson
        {
            role = (int)WebRole.guest,
            action = (int)WebAction.forceRelease,
            transform = new WebObjectInfo
            {
                id = this.gameObject.name
            }
        };
        string json = JsonUtility.ToJson(obj);
        TLabSyncClient.Instalce.SendWsMessage(json);

        Debug.Log("tlabvrhand: " + "force release");
    }

    public void GrabbLockFromOutside(int index)
    {
        if (index != -1)
        {
            if (m_mainParent != null)
            {
                m_mainParent = null;
                m_subParent = null;
            }

            m_grabbed = index;
        }
        else
            m_grabbed = -1;
    }

    public void GrabbLockSelf(int index)
    {
        m_grabbed = index;
    }

    public void GrabbLock(bool active)
    {
        TLabSyncJson obj = new TLabSyncJson
        {
            role = (int)WebRole.guest,
            action = (int)WebAction.grabbLock,
            seatIndex = active ? TLabSyncClient.Instalce.SeatIndex : -1,
            transform = new WebObjectInfo
            {
                id = this.gameObject.name
            }
        };
        string json = JsonUtility.ToJson(obj);
        TLabSyncClient.Instalce.SendWsMessage(json);

        Debug.Log("tlabvrhand: " + "grabb lock");
    }

#if UNITY_EDITOR
    public override void InitializeRotatable()
    {
        base.InitializeRotatable();
    }

    public override void UseRigidbody(bool rigidbody, bool gravity)
    {
        base.UseRigidbody(rigidbody, gravity);
    }
#endif

    protected override void EnableGravity(bool active)
    {
        base.EnableGravity(active);
    }

    protected override void RbGripSwitch(bool grip)
    {
        if (m_rbAllocated && m_useGravity)
        {
            // 自分自身がGravityの計算を担当している
            EnableGravity(!grip);
        }
        else if (m_enableSync && m_useRigidbody && m_useGravity)
        {
            // このオブジェクトのGravityが有効化されている
            // このオブジェクトの同期が有効化されている

            // このRigidbodyのGravity計算担当に自分が掴むことを通知
            TLabSyncJson obj = new TLabSyncJson
            {
                role = (int)WebRole.guest,
                action = (int)WebAction.setGravity,
                active = !grip,
                transform = new WebObjectInfo
                {
                    id = this.gameObject.name
                }
            };
            string json = JsonUtility.ToJson(obj);
            TLabSyncClient.Instalce.SendWsMessage(json);

            Debug.Log("tlabvrhand: " + "set gravity");
        }
    }

    protected override void MainParentGrabbStart()
    {
        base.MainParentGrabbStart();
    }

    protected override void SubParentGrabStart()
    {
        base.SubParentGrabStart();
    }

    public override bool AddParent(GameObject parent)
    {
        // 掴むことが有効化されている
        // 誰も掴んでいない

        if(m_locked == true || m_grabbed != -1)
            return false;

        if (m_mainParent == null)
        {
            RbGripSwitch(true);

            m_mainParent = parent;

            MainParentGrabbStart();

            Debug.Log("tlabvrhand: " + parent.ToString() + " mainParent added");

            GrabbLock(true);

            return true;
        }
        else if (m_subParent == null)
        {
            m_subParent = parent;

            SubParentGrabStart();

            Debug.Log("tlabvrhand: " + parent.ToString() + " subParent added");
            return true;
        }

        Debug.Log("tlabvrhand: cannot add parent");
        return false;
    }

    public override bool RemoveParent(GameObject parent)
    {
        if (m_mainParent == parent)
        {
            if (m_subParent != null)
            {
                m_mainParent = m_subParent;
                m_subParent = null;

                MainParentGrabbStart();

                Debug.Log("tlabvrhand: " + "m_main released and m_sub added");

                return true;
            }
            else
            {
                RbGripSwitch(false);

                m_mainParent = null;

                Debug.Log("tlabvrhand: " + "m_main released");

                GrabbLock(false);

                return true;
            }
        }
        else if (m_subParent == parent)
        {
            m_subParent = null;

            MainParentGrabbStart();

            Debug.Log("tlabvrhand: m_sub released");

            return true;
        }

        return false;
    }

    public void SyncTransform()
    {
        if (m_enableSync == false)
            return;

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

    protected override void UpdateScale()
    {
        base.UpdateScale();
    }

    protected override void UpdatePosition()
    {
        base.UpdatePosition();
    }

    public void OnDevideButtonClick()
    {
        Devide();
    }

    public override int Devide()
    {
        // 分割/結合処理

        int result = base.Devide();

        if (result < 0)
            return -1;

        bool active = result == 0 ? true : false;

        // 結合/分割を切り替えたので，誰もこのオブジェクトを掴んでいない状態にする

        TLabSyncGrabbable[] grabbables = this.gameObject.GetComponentsInChildren<TLabSyncGrabbable>();
        foreach (TLabSyncGrabbable grabbable in grabbables)
            grabbable.ForceRelease();

        // オブジェクトの分割を通知

        TLabSyncJson obj = new TLabSyncJson
        {
            role = (int)WebRole.guest,
            action = (int)WebAction.divideGrabber,
            active = active,
            transform = new WebObjectInfo
            {
                id = this.gameObject.name
            }
        };
        string json = JsonUtility.ToJson(obj);
        TLabSyncClient.Instalce.SendWsMessage(json);

        return result;
    }

    public void DivideFromOutside(bool active)
    {
        base.Devide(active);
    }

    protected override void Start()
    {
        base.Start();
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

            if(m_enableSync && (m_autoSync || m_rbAllocated && CanRbSync))
                SyncTransform();
        }
    }
}
