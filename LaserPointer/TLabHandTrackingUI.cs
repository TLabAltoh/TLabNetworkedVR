using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

public class TLabHandTrackingUI : MonoBehaviour
{
    [SerializeField] private TLabOVRInputModule m_inputModule;
    [SerializeField] private OVRHand m_leftHand;
    [SerializeField] private OVRHand m_rightHand;

    void Start()
    {
        m_inputModule.rayTransformLeft = m_leftHand.PointerPose;
        m_inputModule.rayTransformRight = m_rightHand.PointerPose;
    }
}
