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

        TLabVRGrabbable grabbable = target as TLabVRGrabbable;

        TLabVRRotatable rotatable = grabbable.gameObject.GetComponent<TLabVRRotatable>();

        if (rotatable != null)
            grabbable.InitializeRotatable();
    }
}
#endif