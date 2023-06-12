using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

#if UNITY_EDITOR
[CustomEditor(typeof(TLabVRGrabbable))]
[CanEditMultipleObjects]

public class TLabVRGrabbableEditor : Editor
{
    public override void OnInspectorGUI()
    {
        base.OnInspectorGUI();

        serializedObject.Update();

        TLabVRGrabbable grabbable = target as TLabVRGrabbable;

        TLabVRRotatable rotatable = grabbable.gameObject.GetComponent<TLabVRRotatable>();

        if (rotatable != null)
        {
            if (GUILayout.Button("Initialize for Rotatable"))
            {
                grabbable.InitializeRotatable();
                EditorUtility.SetDirty(grabbable);
                EditorUtility.SetDirty(rotatable);
            }
        }

        serializedObject.ApplyModifiedProperties();
    }
}
#endif