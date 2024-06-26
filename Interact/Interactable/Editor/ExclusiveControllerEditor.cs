using UnityEngine;
using UnityEditor;

#if TLAB_WITH_OCULUS_SDK
using Oculus.Interaction;
using Oculus.Interaction.Surfaces;
#endif

namespace TLab.XR.Interact.Editor
{
    [CustomEditor(typeof(ExclusiveController))]
    [CanEditMultipleObjects]
    public class ExclusiveControllerEditor : UnityEditor.Editor
    {
        private ExclusiveController m_controller;

        private void OnEnable()
        {
            m_controller = target as ExclusiveController;
        }

        // Editor created on the assumption that the controller
        // uses the grabbableHandle and rotateble; modify as
        // appropriate to suit your needs.

        private void InitializeForRotateble(ExclusiveController controller)
        {
            controller.InitializeRotatable();
            EditorUtility.SetDirty(controller);
        }

        private void InitializeForDivibable(GameObject target, bool isRoot)
        {
            var meshFilter = target.RequireComponent<MeshFilter>();

            var meshCollider = target.RequireComponent<MeshCollider>();
            meshCollider.enabled = isRoot;
            meshCollider.convex = true;     // meshCollider.ClosestPoint only works with convex = true

            var controller = target.RequireComponent<ExclusiveController>();
            controller.enableSync = true;
            controller.CreateHashID();
            controller.UseRigidbody(false, false);  // Disable Rigidbody.useGrabity

            var grabbable = target.RequireComponent<Grabbable>();
            grabbable.enableCollision = true;

            var rotatable = target.RequireComponent<Rotatable>();
            rotatable.enableCollision = true;

#if TLAB_WITH_OCULUS_SDK
            var rayInteractable = target.RequireComponent<RayInteractable>();
            var colliderSurface = target.RequireComponent<ColliderSurface>();
#endif

            EditorUtility.SetDirty(meshFilter);
            EditorUtility.SetDirty(meshCollider);
            EditorUtility.SetDirty(controller);
            EditorUtility.SetDirty(rotatable);
            EditorUtility.SetDirty(grabbable);

#if TLAB_WITH_OCULUS_SDK
            EditorUtility.SetDirty(rayInteractable);
            EditorUtility.SetDirty(colliderSurface);
#endif
        }

        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();

            var rotatable = m_controller.gameObject.GetComponent<Rotatable>();
            if (rotatable != null && GUILayout.Button("Initialize for Rotatable"))
            {
                InitializeForRotateble(m_controller);
            }

            if (m_controller.enableDivide && GUILayout.Button("Initialize for Devibable"))
            {
                InitializeForDivibable(m_controller.gameObject, true);

                foreach (var divideTarget in m_controller.divideTargets)
                {
                    GameObjectUtility.RemoveMonoBehavioursWithMissingScript(divideTarget);

                    InitializeForDivibable(divideTarget, false);

                    EditorUtility.SetDirty(divideTarget);
                }
            }

            if (Application.isPlaying)
            {
                EditorGUILayout.Space();
                GUILayout.Label($"current grab index: {m_controller.grabbedIndex}", GUILayout.ExpandWidth(false));
                EditorGUILayout.Space();
            }

            EditorGUILayout.Space();

            EditorGUILayout.LabelField($"Rigidbody allocated: {m_controller.rbAllocated}");

            EditorGUILayout.Space();

            if (GUILayout.Button("Create Hash ID"))
            {
                m_controller.CreateHashID();
            }
        }
    }
}
