using UnityEngine;

public class TLabOutlineSelectable : MonoBehaviour
{
    [SerializeField] [Range(0f, 0.1f)] protected float m_outlineWidth = 0.025f;

    public virtual bool Selected
    {
        set
        {
            m_selected = value;
        }
    }

    public virtual Material OutlineMat
    {
        get
        {
            return m_material;
        }

        set
        {
            m_material = value;
        }
    }

    [SerializeField] protected Material m_material;
    protected bool m_selected = false;

    protected virtual void Start()
    {
        m_material.SetFloat("_OutlineWidth", 0.0f);
    }

    protected virtual void Update()
    {
        m_material.SetFloat("_OutlineWidth", m_selected == true ? m_outlineWidth : 0.0f);
        m_selected = false;
    }
}
