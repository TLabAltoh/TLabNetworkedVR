using UnityEngine;

public class TLabVRHand : MonoBehaviour
{
    [SerializeField] private OVRInput.Controller m_controller;
    [SerializeField] private LaserPointer m_laserPointer;
    [SerializeField] private OVRInput.Axis1D m_gripAxis;
    [SerializeField] private OVRInput.Button m_gripButton;
    [SerializeField] private float m_maxDistance = 10.0f;
    [SerializeField] private LayerMask m_layerMask;

    private Transform m_anchor;
    private OVRCameraRig m_cameraRig;
    private TLabVRGrabbable m_grabbable;
    private bool m_handInitialized = false;

    //
    // Raycast Info
    //

    private GameObject m_raycastResult = null;
    private RaycastHit m_raycastHit;

    public GameObject RaycstResult
    {
        get
        {
            return m_raycastResult;
        }
    }

    public RaycastHit RaycastHit
    {
        get
        {
            return m_raycastHit;
        }
    }

    void Start()
    {
        m_handInitialized = true;
        m_cameraRig = FindObjectOfType<OVRCameraRig>();

        if(m_controller == OVRInput.Controller.RTouch)
        {
            m_anchor = m_cameraRig.rightHandAnchor;
        }
        else if(m_controller == OVRInput.Controller.LTouch)
        {
            m_anchor = m_cameraRig.leftHandAnchor;
        }
        else
        {
            m_handInitialized = false;
            Debug.LogError("The controller type is not properly selected. Select RTouch or LTouch.");
        }
    }

    void Update()
    {
        if(m_handInitialized == false)
        {
            return;
        }

        Ray ray = new Ray(m_anchor.position, m_anchor.forward);

        if (Physics.Raycast(ray, out m_raycastHit, m_maxDistance, m_layerMask))
        {
            if (m_grabbable)
            {
                bool grip = OVRInput.Get(m_gripAxis, m_controller) > 0.0f;
                if (grip == false)
                {
                    m_grabbable.RemoveParent(this.gameObject);

                    m_grabbable = null;
                }
            }
            else
            {
                GameObject target = m_raycastHit.collider.gameObject;
                m_raycastResult = target;

                m_laserPointer.maxLength = m_raycastHit.distance;

                //
                // Outline
                //

                TLabOutlineSelectable selectable = target.GetComponent<TLabOutlineSelectable>();

                if (selectable != null)
                {
                    selectable.Selected = true;
                }

                //
                // Grip
                //

                bool grip = OVRInput.GetDown(m_gripButton, m_controller);
                if (grip == true)
                {
                    TLabVRGrabbable grabbable = target.GetComponent<TLabVRGrabbable>();

                    if (grabbable == null)
                    {
                        return;
                    }

                    if (grabbable.AddParent(this.gameObject) == true)
                    {
                        m_grabbable = grabbable;
                    }
                }
            }
        }
        else
        {
            m_laserPointer.maxLength = m_maxDistance;

            if (m_grabbable)
            {
                bool grip = OVRInput.Get(m_gripAxis, m_controller) > 0.0f;
                if (grip == false)
                {
                    m_grabbable.RemoveParent(this.gameObject);
                    m_grabbable = null;
                }
            }

            m_raycastResult = null;
        }
    }
}
