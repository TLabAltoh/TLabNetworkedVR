using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace TLab.Network.WebRTC.Editor
{
    [CustomEditor(typeof(WebRTCClient))]
    public class WebRTCClientEditor : UnityEditor.Editor
    {
        private WebRTCClient m_instance;

        private void OnEnable()
        {
            m_instance = target as WebRTCClient;
        }

        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();

            EditorGUILayout.LabelField("Connection State");

            EditorGUILayout.BeginHorizontal();

            EditorGUILayout.LabelField($"User ID: {m_instance.userID}");

            EditorGUILayout.LabelField($"Room ID: {m_instance.roomID}");

            EditorGUILayout.EndHorizontal();
        }
    }
}
