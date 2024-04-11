using UnityEngine;
using UnityEditor;
using TLab.XR.Network.Security;

namespace TLab.XR.Network.Editor
{
    [CustomEditor(typeof(RoomConfig))]
    public class RoomConfigEditor : UnityEditor.Editor
    {
        private RoomConfig m_instance;

        private void OnEnable()
        {
            m_instance = target as RoomConfig;
        }

        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();

            if (GUILayout.Button("Regist Password"))
            {
                string hash = Authentication.GetHashString(m_instance.password);
                m_instance.passwordHash = hash;
                m_instance.password = "";

                EditorUtility.SetDirty(m_instance);
            }
        }
    }
}
