using UnityEngine;
using UnityEditor;

namespace TLab.XR.Network.Editor
{
    [CustomEditor(typeof(SyncTransformer))]
    public class SyncTransformerEditor : UnityEditor.Editor
    {
        private SyncTransformer m_instance;

        private void OnEnable()
        {
            m_instance = target as SyncTransformer;
        }

        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();

            EditorGUILayout.Space();

            EditorGUILayout.LabelField($"Rigidbody allocated: {m_instance.rbAllocated}");

            EditorGUILayout.Space();

            if (GUILayout.Button("Create Hash ID"))
            {
                m_instance.CreateHashID();
            }
        }
    }
}
