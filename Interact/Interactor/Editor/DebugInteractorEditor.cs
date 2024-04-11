using UnityEngine;
using UnityEditor;

namespace TLab.XR.Interact.Editor
{
    [CustomEditor(typeof(DebugInteractor))]
    public class DebugInteractorEditor : UnityEditor.Editor
    {
        private DebugInteractor m_interactor;

        private void OnEnable()
        {
            m_interactor = target as DebugInteractor;
        }

        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();

            EditorGUILayout.BeginHorizontal();

            var width = GUILayout.Width(Screen.width / 3);

            if (GUILayout.Button("Grab", width))
            {
                m_interactor.Grab();
            }

            if (GUILayout.Button("Release", width))
            {
                m_interactor.Release();
            }

            EditorGUILayout.EndHorizontal();
        }
    }
}
