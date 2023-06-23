using UnityEngine;

public class TLabOutlineSelectable : MonoBehaviour
{
    [SerializeField] [Range(0f, 0.1f)] private float m_outlineWidth = 0.025f;

    public bool Selected
    {
        set
        {
            m_selected = value;
        }
    }

    private Material m_material;
    private bool m_selected = false;

    void Start()
    {
        m_material = GetComponent<Renderer>().material;
    }

    private void Update()
    {
        m_material.SetFloat("_OutlineWidth", m_selected == true ? m_outlineWidth : 0.0f);
        m_selected = false;
    }
}
