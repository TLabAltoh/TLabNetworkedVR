using UnityEngine;

namespace TLab.Network
{
    public class Identifier : MonoBehaviour
    {
        protected int m_id;

        public int id { get => m_id; set => m_id = value; }
    }
}
