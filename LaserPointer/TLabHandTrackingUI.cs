using UnityEngine;
using UnityEngine.EventSystems;

public class TLabHandTrackingUI : MonoBehaviour
{
    [SerializeField] private TLabOVRInputModule m_inputModule;
    [SerializeField] private OVRHand m_leftHand;
    [SerializeField] private OVRHand m_rightHand;
    [SerializeField] private bool m_useHandTracking;

    void Start()
    {
        if (m_useHandTracking)
        {
            m_inputModule.rayTransformLeft = m_leftHand.PointerPose;
            m_inputModule.rayTransformRight = m_rightHand.PointerPose;
        }
    }
}
