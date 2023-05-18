using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public class TLabVRTrackingHand : MonoBehaviour
{
    [Header("Hand Data")]
    [SerializeField] private OVRHand m_hand;
    [SerializeField] private LaserPointer m_laserPointer;
    [SerializeField] private float m_maxDistance = 10.0f;
    [SerializeField] private Transform m_grabbAnchor;
    [SerializeField] private LayerMask m_layerMask;

    [Header("Gesture")]
    [SerializeField] private OVRSkeleton m_skeleton;
    [SerializeField] private List<Gesture> m_gestures;
    [SerializeField] private bool m_debugMode;

    private List<OVRBone> m_fingerBones;
    private Transform m_anchor;
    private TLabVRGrabbable m_grabbable;

    private RaycastHit m_raycastHit;

    private bool m_skeltonInitialized;
    private const float threshold = 0.1f;

    [System.Serializable]
    public struct Gesture
    {
        public string name;
        public List<Vector3> fingerDatas;
        public UnityEvent onRecognized;
    }

    public TLabVRGrabbable CurrentGrabbable
    {
        get
        {
            return m_grabbable;
        }
    }

    private void SavePose()
    {
        Gesture g = new Gesture();
        g.name = "New Gesture";
        List<Vector3> data = new List<Vector3>();
        foreach(var bone in m_fingerBones)
        {
            data.Add(m_skeleton.transform.InverseTransformPoint(bone.Transform.position));
        }

        g.fingerDatas = data;
        m_gestures.Add(g);
    }

    private string DetectGesture()
    {
        string result = null;
        float currentMin = Mathf.Infinity;

        foreach(var gesture in m_gestures)
        {
            float sumDistance = 0.0f;
            bool isDiscarded = false;
            for(int i = 0; i < m_fingerBones.Count; i++)
            {
                Vector3 currentData = m_skeleton.transform.InverseTransformPoint(m_fingerBones[i].Transform.position);
                float distance = Vector3.Distance(currentData, gesture.fingerDatas[i]);

                if(distance > threshold)
                {
                    isDiscarded = true;
                    break;
                }

                sumDistance += distance;
            }

            if(!isDiscarded && sumDistance < currentMin)
            {
                currentMin = sumDistance;
                result = gesture.name;
            }
        }

        return result;
    }

    IEnumerator WaitForSkeltonInitialized()
    {
        // https://communityforums.atmeta.com/t5/Unity-VR-Development/Bones-list-is-empty/td-p/880261
        while (m_skeleton.Bones.Count == 0)
        {
            yield return null;
        }

        m_fingerBones = new List<OVRBone>(m_skeleton.Bones);
        m_skeltonInitialized = true;
    }

    void Start()
    {
        m_skeltonInitialized = false;

        if(m_skeleton != null)
        {
            StartCoroutine(WaitForSkeltonInitialized());
        }

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
        if (m_debugMode && Input.GetKeyDown(KeyCode.Space))
        {
            SavePose();
            return;
        }

        if (!m_skeltonInitialized)
            return;

        bool grip = DetectGesture() == "Grabb";

        m_laserPointer.maxLength = (m_hand.GetFingerPinchStrength(OVRHand.HandFinger.Index) > 0.1f) ? m_maxDistance : 0.0f;

        if (Physics.Raycast(m_grabbAnchor.position, m_grabbAnchor.forward, out m_raycastHit, 0.25f, m_layerMask))
        {
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
