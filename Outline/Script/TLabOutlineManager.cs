using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

#if UNITY_EDITOR
public class TLabOutlineManager : MonoBehaviour
{
    [Tooltip("All objects that have an Outline material assigned to them are stored here.\n" +
             "Outline will not function properly unless you edit the vertex colors on the objects beforehand from this script.")]
    [SerializeField] private GameObject[] m_outlineTarget;
    [SerializeField] private Shader m_outline;

    [Tooltip("Location of mesh copied for outline")]
    [SerializeField] private string m_savePathMesh;
    [SerializeField] private string m_savePathMaterial;

    const float error = 1e-8f;

    // https://blog.syn-sophia.co.jp/articles/2022/10/17/outline_rendering_01

    public void BakeNormal(GameObject obj)
    {
        var meshFilters = obj.GetComponentsInChildren<MeshFilter>();

        foreach (var meshFilter in meshFilters)
        {
            var mesh = meshFilter.sharedMesh;

            var normals     = mesh.normals;
            var vertices    = mesh.vertices;
            var vertexCount = mesh.vertexCount;

            Color[] softEdges = new Color[normals.Length];

            for (int i = 0; i < vertexCount; i++)
            {
                Vector3 softEdge = Vector3.zero;

                for (int j = 0; j < vertexCount; j++)
                {
                    var v = vertices[i] - vertices[j];
                    if (v.sqrMagnitude < error) softEdge += normals[j];
                }

                softEdge.Normalize();
                softEdges[i] = new Color(softEdge.x, softEdge.y, softEdge.z, 0);
            }
            mesh.colors = softEdges;

            string path     = m_savePathMesh + "/" + mesh.name + ".asset";
            Mesh copyMesh   = GameObject.Instantiate(mesh);
            Mesh asset      = AssetDatabase.LoadAssetAtPath<Mesh>(path);

            if (asset != null)
                EditorUtility.CopySerialized(asset, copyMesh);
            else
                AssetDatabase.CreateAsset(copyMesh, path);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            meshFilter.sharedMesh = copyMesh;
        }

        MeshRenderer meshRenderer = obj.GetComponent<MeshRenderer>();
        if(meshRenderer != null)
        {
            Material[] prevMaterials    = meshRenderer.sharedMaterials;
            Material outline = new Material(m_outline);
            outline.name = obj.name + "_Outline";
            Material[] newMaterials     = new Material[prevMaterials.Length + 1];
            for (int i = 0; i < prevMaterials.Length; i++) newMaterials[i] = prevMaterials[i];
            newMaterials[newMaterials.Length - 1] = outline;
            meshRenderer.sharedMaterials = newMaterials;

            string path         = m_savePathMaterial + "/" + outline.name + ".mat";
            Material prevMat    = AssetDatabase.LoadAssetAtPath<Material>(path);

            if (prevMat != null)
                EditorUtility.CopySerialized(prevMat, outline);
            else
                AssetDatabase.CreateAsset(outline, path);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }
    }

    public void BakeVertexColor()
    {
        if (m_savePathMesh == ""        ||
            m_savePathMesh == null      ||
            m_savePathMaterial == ""    ||
            m_savePathMaterial == null) return;

        for (int i = 0; i < m_outlineTarget.Length; i++) BakeNormal(m_outlineTarget[i]);
    }

    public void SelectMeshSavePath()
    {
        string path = EditorUtility.SaveFolderPanel("Save Path", "Assets", "");
        if (path == null) return;
        string fullPath = System.IO.Directory.GetCurrentDirectory();
        m_savePathMesh  = path.Remove(0, fullPath.Length + 1);
    }

    public void SelectMaterialSavePath()
    {
        string path = EditorUtility.SaveFolderPanel("Save Path", "Assets", "");
        if (path == null) return;
        string fullPath     = System.IO.Directory.GetCurrentDirectory();
        m_savePathMaterial  = path.Remove(0, fullPath.Length + 1);
    }
}
#endif

#if UNITY_EDITOR
[CustomEditor(typeof(TLabOutlineManager))]
public class TLabOutlineManagerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        base.OnInspectorGUI();

        TLabOutlineManager manager = target as TLabOutlineManager;

        if (GUILayout.Button("Process"))
        {
            manager.BakeVertexColor();
            EditorUtility.SetDirty(manager);
        }

        if (GUILayout.Button("Select Mesh Save Path"))
        {
            manager.SelectMeshSavePath();
            EditorUtility.SetDirty(manager);
        }

        if (GUILayout.Button("Select Material Save Path"))
        {
            manager.SelectMaterialSavePath();
            EditorUtility.SetDirty(manager);
        }
    }
}
#endif
