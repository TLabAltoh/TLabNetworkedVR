using System.Text;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class TLabSyncGrabbable : TLabVRGrabbable
{
    [Header("Sync Setting")]

    [Tooltip("Be sure to enable this field for objects you want to synchronize")]
    [SerializeField] public bool m_enableSync = false;

    [Tooltip("While this item is enabled, objects will automatically keep their transforms in sync regardless of player interaction.")]
    [SerializeField] public bool m_autoSync = false;

    [Tooltip("Objects with this item disabled cannot be grabbed by any player")]
    [SerializeField] public bool m_locked = false;

    private bool m_rbAllocated = true;
    private int m_grabbed = -1;

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





    /// <summary>
    /// Synchronize object transforms updated externally
    /// </summary>
    /// <param name="transform"></param>
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
    }





    /// <summary>
    /// Externally enable/disable the Rigidbody's Gravity
    /// </summary>
    /// <param name="active"></param>
    public void SetGravity(bool active)
    {
        if (m_rb != null)
            EnableGravity(active);
    }

    /// <summary>
    /// Determines if this object should calculate gravity
    /// </summary>
    /// <param name="active"></param>
    public void AllocateGravity(bool active)
    {
        m_rbAllocated = active;
        SetGravity((m_grabbed == -1 && active) ? true : false);
    }





    /// <summary>
    /// Forcibly sever the parent relationship between the object and itself
    /// </summary>
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

    /// <summary>
    /// Forcibly sever the parent relationship between the object and itself on external request
    /// </summary>
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

    /// <summary>
    /// Forcibly sever the parent relationship between this object and all players
    /// </summary>
    public void ForceRelease()
    {
        // sever parenting with yourself
        ForceReleaseSelf();

        // Requesting other players to sever the parental relationship with the object
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





    /// <summary>
    /// This object is locked/unlocked by another player
    /// </summary>
    /// <param name="index"></param>
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

    /// <summary>
    /// lock/unlock an object from itself
    /// </summary>
    /// <param name="index"></param>
    public void GrabbLockSelf(int index)
    {
        m_grabbed = index;
    }

    /// <summary>
    /// Lock/Unlock this object from other players
    /// </summary>
    /// <param name="active"></param>
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
        if (EditorApplication.isPlaying == true)
            return;

        base.InitializeRotatable();
        m_autoSync = false;
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
            // When I myself was in charge of calculating the gravity of this object
            EnableGravity(!grip);
        }
        else if (m_enableSync && m_useRigidbody && m_useGravity)
        {
            // Requests the player responsible for computing gravity for this object to temporarily stop computing gravity
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
        if(m_locked == true && m_grabbed != -1)
        {
            return false;
        }

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

    /// <summary>
    /// Synchronize this object's transforms externally
    /// Optimized using StringBuilder compared to built-in Json generation function
    /// </summary>
    public void SyncTransform()
    {
        if (m_enableSync == false)
            return;

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

        #region Packet to make
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
    }

    protected override void UpdateScale()
    {
        base.UpdateScale();

        if (m_scaleInitialDistance != -1.0f)
            SyncTransform();
    }

    protected override void UpdatePosition()
    {
        base.UpdatePosition();

        SyncTransform();
    }

    protected override void Start()
    {
        base.Start();
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
        }
        else
        {
            m_scaleInitialDistance = -1.0f;

            if(m_enableSync && (m_autoSync || m_rbAllocated && CanRbSync))
                SyncTransform();
        }
    }
}
