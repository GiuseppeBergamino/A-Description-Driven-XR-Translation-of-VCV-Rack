using UnityEngine;

// ===================== MODULE PANEL REFS =====================
// Contenitore leggero di riferimenti strutturali del prefab pannello.
// Nessuna logica: lo spawner resta responsabile di configurazione e spawn.

public class ModulePanelRefs : MonoBehaviour
{
    [Header("Structure")]
    [SerializeField] private Transform panelVisual;
    [SerializeField] private Transform controlsRoot;
    [SerializeField] private Transform labelsRoot;

    [Header("Handles")]
    [SerializeField] private Transform topLeftHandle;
    [SerializeField] private Transform bottomRightHandle;

    public Transform PanelVisual => panelVisual;
    public Transform ControlsRoot => controlsRoot;
    public Transform LabelsRoot => labelsRoot;
    public Transform TopLeftHandle => topLeftHandle;
    public Transform BottomRightHandle => bottomRightHandle;

    public Renderer PanelRenderer
    {
        get
        {
            return panelVisual != null ? panelVisual.GetComponent<Renderer>() : null;
        }
    }

    public Collider TopLeftHandleCollider
    {
        get
        {
            return topLeftHandle != null ? topLeftHandle.GetComponent<Collider>() : null;
        }
    }

    public Collider BottomRightHandleCollider
    {
        get
        {
            return bottomRightHandle != null ? bottomRightHandle.GetComponent<Collider>() : null;
        }
    }

    private void OnValidate()
    {
        if (panelVisual == null)
            Debug.LogWarning("ModulePanelRefs: panelVisual non assegnato.", this);

        if (controlsRoot == null)
            Debug.LogWarning("ModulePanelRefs: controlsRoot non assegnato.", this);

        if (labelsRoot == null)
            Debug.LogWarning("ModulePanelRefs: labelsRoot non assegnato.", this);

        if (topLeftHandle == null)
            Debug.LogWarning("ModulePanelRefs: topLeftHandle non assegnato.", this);

        if (bottomRightHandle == null)
            Debug.LogWarning("ModulePanelRefs: bottomRightHandle non assegnato.", this);
    }
}