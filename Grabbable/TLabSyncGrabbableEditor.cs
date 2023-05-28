using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

#if UNITY_EDITOR
[CustomEditor(typeof(TLabSyncGrabbable))]
[CanEditMultipleObjects]

public class TLabSyncGrabbableEditor : Editor
{
    public override void OnInspectorGUI()
    {
        base.OnInspectorGUI();

        TLabSyncGrabbable grabbable = target as TLabSyncGrabbable;

        TLabVRRotatable rotatable = grabbable.gameObject.GetComponent<TLabVRRotatable>();

        if (rotatable != null)
            grabbable.InitializeRotatable();
    }
}
#endif