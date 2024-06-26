using UnityEngine;

namespace TLab.XR.Humanoid
{
    [CreateAssetMenu(menuName = "TLab/NetworkedVR/AvatorConfig")]
    public class AvatorConfig : ScriptableObject
    {
        public enum PartsType
        {
            HEAD,
            L_HAND,
            R_HAND
        };

        [System.Serializable]
        public class AvatorParts
        {
            public PartsType type;

            public GameObject prefab;
        }

        [SerializeField] private AvatorParts[] m_parts;

        public AvatorParts[] parts => m_parts;
    }
}
