using UnityEngine;

namespace TLab.XR.Network
{
    [CreateAssetMenu(fileName = "Room Config", menuName = "TLab/NetworkedVR/RoomConfig")]
    public class RoomConfig : ScriptableObject
    {
        [SerializeField] private string m_id = "Default";

        [SerializeField] private ServerAddress m_syncServerAddr;

        [SerializeField] private ServerAddress m_signalingServerAddr;

        [SerializeField] private string m_password = "";

        [SerializeField] private string m_passwordHash = "";

        public string id => m_id;

        public string password { get => m_password; set => m_password = value; }

        public string passwordHash { get => m_passwordHash; set => m_passwordHash = value; }

        public ServerAddress syncSeverAddr => m_syncServerAddr;

        public ServerAddress signalingServerAddr => m_signalingServerAddr;
    }
}
