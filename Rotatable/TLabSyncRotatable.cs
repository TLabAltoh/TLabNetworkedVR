using UnityEngine;

public class TLabSyncRotatable : TLabVRRotatable
{
    private TLabSyncGrabbable m_grabbable;
    private bool m_onShot = false;

    protected override bool IsGrabbled
    {
        get
        {
            return m_grabbable.Grabbed || m_grabbable.GrabbedIndex != -1;
        }
    }

    private bool IsSyncFromOutside
    {
        get
        {
            return m_grabbable.IsSyncFromOutside;
        }
    }

    public override void SetHandAngulerVelocity(Vector3 axis, float angle)
    {
        if (IsGrabbled == false)
        {
            m_axis = axis;
            m_angle = angle;

            m_onShot = true;
        }
    }

    protected override void Start()
    {
        m_grabbable = GetComponent<TLabSyncGrabbable>();
        if (m_grabbable == null)
            Destroy(this);
    }

    protected override void Update()
    {
        if (IsGrabbled == false && (IsSyncFromOutside == false || m_onShot == true) && m_angle > 0f)
        {
            this.transform.rotation = Quaternion.AngleAxis(m_angle, m_axis) * this.transform.rotation;
            m_angle = Mathf.Clamp(m_angle - 0.1f * Time.deltaTime, 0, float.MaxValue);

            m_grabbable.SyncTransform();
        }
        else
            m_angle = 0f;

        m_onShot = false;
    }
}
