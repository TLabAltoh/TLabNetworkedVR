using UnityEngine;

public class TLabVRTrackingHand : MonoBehaviour
{
    [SerializeField] private OVRHand m_hand;
    [SerializeField] private Transform m_grabbAnchor;
    [SerializeField] private LayerMask m_layerMask;

    private Transform m_anchor;
    private TLabVRGrabbable m_grabbable;

    private RaycastHit m_raycastHit;

    void Start()
    {
        if(m_hand == null)
        {
            Debug.LogError("ovrhand is null");
        }
        else
        {
            m_anchor = this.transform;
        }
    }

    void Update()
    {
        float indexStrength = m_hand.GetFingerPinchStrength(OVRHand.HandFinger.Index);
        float thumbStrength = m_hand.GetFingerPinchStrength(OVRHand.HandFinger.Thumb);
        float middleStrength = m_hand.GetFingerPinchStrength(OVRHand.HandFinger.Middle);

        bool isPointer = indexStrength > 0.5f && thumbStrength > 0.5f && middleStrength <= 0.5f;
        bool grip = indexStrength > 0.5f;

        if (isPointer)
        {

        }

        if (Physics.Raycast(m_grabbAnchor.position, m_grabbAnchor.forward, out m_raycastHit, 0.25f, m_layerMask))
        {
            Debug.Log("hit");
            if(m_grabbable != null)
            {
                if (!grip)
                {
                    m_grabbable.RemoveParent(this.gameObject);
                    m_grabbable = null;
                }
            }
            else
            {
                if (grip)
                {
                    GameObject target = m_raycastHit.collider.gameObject;

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
        else if (m_grabbable && !grip)
        {
            m_grabbable.RemoveParent(this.gameObject);
            m_grabbable = null;
        }
    }
}
