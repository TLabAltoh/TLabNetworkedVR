using UnityEngine;

namespace TLab.Network
{
    [AddComponentMenu("TLab/NetworkedVR/" + nameof(SeatIdentifier) + " (TLab)")]
    public class SeatIdentifier : Identifier
    {
        public int seatIndex { get => m_id; set => m_id = value; }
    }
}
