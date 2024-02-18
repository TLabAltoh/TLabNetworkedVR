using UnityEngine;

namespace TLab.Network
{
    [AddComponentMenu("TLab/NetworkedVR/" + nameof(AvatorIdentifier) + " (TLab)")]
    public class AvatorIdentifier : Identifier
    {
        private string m_avatorId;

        private string m_partsId;

        public string avatorId { get => m_avatorId; set => m_avatorId = value; }

        public string partsId { get => m_partsId; set => m_partsId = value; }
    }
}
