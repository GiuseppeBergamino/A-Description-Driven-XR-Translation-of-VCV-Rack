using UnityEngine;
using System.Collections.Generic; //gestione liste e dictonary per runtime

// ===================== GENERIC MODULE SPAWNER =====================
// Runtime spawner della patch VeryVCV.
// Riceve un PatchDescriptor già pronto e genera moduli, controlli e label nella scena.
// Gestisce anche l'aggiornamento visivo dei parametri già spawnati.

public class GenericModuleSpawner : MonoBehaviour
{
    // ===================== SCENE REFERENCES =====================
    [Header("Scene References")]
    [SerializeField] private ControlPrefabRegistry registry;
    [SerializeField] private Material panelMaterial;

    // ===================== LAYOUT TUNING =====================
    private const float PanelThickness = 0.02f;
    private const float ControlSurfaceOffset = 0.005f;
    private const float LabelSurfaceOffset = 0.012f;

    //-----Layout
    // Module title
    private const float ModuleTitleTopMarginFactor = 0.035f;
    private const float ModuleTitleWidthScaleFactor = 0.4f;
    private const float MinModuleTitleScale = 0.1f;

    // Control labels
    private const float ParamLabelGapFactor = 0.35f;
    private const float PortLabelGapFactor = 0.25f;
    private const float ParamLabelScaleFactor = 1.40f;
    private const float PortLabelScaleFactor = 1.25f;
    private const float MinParamLabelScale = 0.06f;
    private const float MinPortLabelScale = 0.05f;

    // ===== Presentation layout (TODO futuro: esporre all'utente) =====
    // 1.0 = fedele alla patch Rack
    // private const float HorizontalSpacing = 1.0f;   private const float VerticalSpacing = 1.0f;
    private static readonly float HorizontalSpacing = 1.0f;
    private static readonly float VerticalSpacing = 1.0f;

    // <= 0 => layout piano
    // > 0 => warp su cilindro attorno all'asse Y
    // private const float CylinderRadius = -1.0f;
    private static readonly float CylinderRadius = Mathf.Infinity;//; -2 cilindrico stretto

    //------ fattori riscalamento
    private const float KnobScaleFactor = 1.0f;
    private const float ButtonScaleFactor = 1.0f;
    private const float JackScaleFactor = 1.0f;

    private const float SliderWidthScaleFactor = 1.0f;
    private const float SliderHeightScaleFactor = 1.0f;
    private const float SliderDepthScaleFactor = 1.0f;

    // Larghezza minima dello slider in funzione della sua altezza.
    // Serve a evitare slider troppo sottili quando il box VCV è molto stretto.
    private const float SliderMinWidthFromHeightRatio = 1.00f;



    // Safety minima
    private const float MinControlSize = 0.01f;

    // ===================== RUNTIME STATE =====================
    //per param_update da OSC, per tenere traccia degli oggetti istanziati e aggiornarli
    private readonly Dictionary<string, GameObject> paramInstances = new Dictionary<string, GameObject>();

    private PatchDescriptor patch = new PatchDescriptor();

// ===================== PATCH LIFECYCLE =====================
    public void LoadPatch(PatchDescriptor newPatch)
    {
        if (newPatch == null)
        {
            Debug.LogWarning("GenericModuleSpawner: newPatch nulla.");
            return;
        }

        if (newPatch.modules == null)
        {
            Debug.LogWarning("GenericModuleSpawner: newPatch.modules null.");
            return;
        }

        patch = newPatch;
        ClearChildren();
        SpawnPatch();
    }

// ===================== GEOMETRY HELPERS =====================
    private float GetFrontFaceZ()
    {
        return -PanelThickness * 0.5f;
    }
    private float GetControlZ()
    {
        return GetFrontFaceZ() - ControlSurfaceOffset;
    }
    private float GetLabelZ()
    {
        return GetFrontFaceZ() - LabelSurfaceOffset;
    }
    private Quaternion GetPatchFacingRotation()
    {
        Camera cam = Camera.main;
        if (cam == null)
            return Quaternion.identity;

        Vector3 forward = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized;

        if (forward.sqrMagnitude < 1e-6f)
            forward = Vector3.forward;

        return Quaternion.LookRotation(forward, Vector3.up);
    }

    private Vector3 GetSpacedPatchPosition(Vector3 flatPatchPos)
    {
        return new Vector3(
            flatPatchPos.x * HorizontalSpacing,
            flatPatchPos.y * VerticalSpacing,
            0f
        );
    }

private Vector3 GetPresentedModulePosition(Vector3 flatPatchPos)
        {
            Vector3 spaced = GetSpacedPatchPosition(flatPatchPos);

            if (float.IsInfinity(CylinderRadius))
                return spaced;

            float theta = -spaced.x / CylinderRadius;

            float x = Mathf.Sin(theta) * CylinderRadius;
            float z = CylinderRadius * (1f - Mathf.Cos(theta));

            return new Vector3(x, spaced.y, z);
        }
    private Quaternion GetPresentedModuleYaw(Vector3 flatPatchPos)
        {
            if (float.IsInfinity(CylinderRadius))
                return Quaternion.identity;

            Vector3 spaced = GetSpacedPatchPosition(flatPatchPos);
            float thetaDeg = (-spaced.x / CylinderRadius) * Mathf.Rad2Deg;

            return Quaternion.Euler(0f, -thetaDeg, 0f);
        }

// ===================== CONTROL SCALE HELPERS =====================
//  Utility per dimensionamento controlli 
    private bool IsKnob(ControlDescriptor control)
    {
        return control.role == ControlRole.Knob;
    }
    private bool IsButton(ControlDescriptor control)
    {
        return control.role == ControlRole.Button;
    }
    private bool IsJack(ControlDescriptor control)
    {
        return control.role == ControlRole.JackIn || control.role == ControlRole.JackOut;
    }

        private bool IsSlider(ControlRole role) //lo slider non ha box quadrato come gli altri
    {
        return role == ControlRole.Slider;
    }

    private float GetControlReferenceSize(ControlDescriptor control)
    {
        float sizeRef = Mathf.Min(control.sourceBoxSize.x, control.sourceBoxSize.y);
        return Mathf.Max(MinControlSize, sizeRef);
    }
    private Vector3 GetControlScale(ControlDescriptor control)
    {
        if (control.sourceBoxSize == Vector2.zero)
            return Vector3.one;

        if (IsSlider(control.role))
            return GetSliderScale(control);

        float referenceSize = GetControlReferenceSize(control);

        float scaleFactor = 1.0f;

        if (IsKnob(control))
            scaleFactor = KnobScaleFactor;
        else if (IsButton(control))
            scaleFactor = ButtonScaleFactor;
        else if (IsJack(control))
            scaleFactor = JackScaleFactor;

        float finalSize = referenceSize * scaleFactor;

        return Vector3.one * finalSize;
    }

    private Vector3 GetSliderScale(ControlDescriptor control)
    {
        float rawWidth = Mathf.Max(MinControlSize, control.sourceBoxSize.x);
        float rawHeight = Mathf.Max(MinControlSize, control.sourceBoxSize.y);

        float widthFromBox = rawWidth * SliderWidthScaleFactor;
        float widthFromHeight = rawHeight * SliderMinWidthFromHeightRatio;

        float finalWidth = Mathf.Max(widthFromBox, widthFromHeight);
        float finalHeight = rawHeight * SliderHeightScaleFactor;

        // La profondità la facciamo seguire alla larghezza finale,
        // così non resta troppo sottile o sproporzionata.
        float finalDepth = finalWidth * SliderDepthScaleFactor;

        return new Vector3(finalWidth, finalHeight, finalDepth);
    }

    private void ClearChildren()
    {
        paramInstances.Clear();

        foreach (Transform child in transform)
        {
            Destroy(child.gameObject);
        }
    }


// ===================== SPAWN =====================
    private void SpawnPatch()
    {
        if (registry == null)
        {
            Debug.LogError("GenericModuleSpawner: registry non assegnato");
            return;
        }

        if (patch == null || patch.modules == null)
        {
            Debug.LogError("GenericModuleSpawner: patch nulla");
            return;
        }

        foreach (ModuleDescriptor module in patch.modules)
        {
            SpawnModule(module);
        }
    }

    private void SpawnModule(ModuleDescriptor module)
    {
        Camera cam = Camera.main;
        if (cam == null)
        {
            Debug.LogError("Camera.main non trovata");
            return;
        }

        GameObject moduleRoot = new GameObject(module.moduleName);
        moduleRoot.transform.SetParent(transform, false);

        Vector3 presentedPos = GetPresentedModulePosition(module.patchLocalPosition);
        Quaternion presentedYaw = GetPresentedModuleYaw(module.patchLocalPosition);
        Quaternion patchFacing = GetPatchFacingRotation();

        moduleRoot.transform.localPosition = presentedPos;
        moduleRoot.transform.rotation = patchFacing * presentedYaw;
        moduleRoot.transform.localScale = Vector3.one;

        GameObject panel = GameObject.CreatePrimitive(PrimitiveType.Cube);
        panel.name = "Panel";
        panel.transform.SetParent(moduleRoot.transform, false);
        panel.transform.localPosition = Vector3.zero;
        panel.transform.localRotation = Quaternion.identity;
        panel.transform.localScale = new Vector3(module.panelSize.x, module.panelSize.y, PanelThickness);

        RemoveCollider(panel);
        ApplyMaterial(panel, panelMaterial);

        SpawnModuleTitle(moduleRoot.transform, module);

        foreach (ControlDescriptor control in module.controls)
        {
            SpawnControl(moduleRoot.transform, module, control);
        }
    }

    private void SpawnControl(Transform moduleRoot, ModuleDescriptor module, ControlDescriptor control)
    {
        if (!control.isVisible)
            return;

        GameObject prefab = registry.GetPrefab(control.role);
        if (prefab == null)
        {
            Debug.LogWarning($"Nessun prefab assegnato per role {control.role}");
            return;
        }

        GameObject instance = Instantiate(prefab, moduleRoot);
        instance.name = $"{control.role}_{control.controlId}";

        instance.transform.localPosition = new Vector3(
            control.localPosition.x,
            control.localPosition.y,
            GetControlZ()
        );

        instance.transform.localRotation =
            Quaternion.Euler(control.localRotation) * Quaternion.Euler(0f, 180f, 0f);

        instance.transform.localScale = GetControlScale(control);

        ApplyInitialValue(instance, control);

        if (control.paramId >= 0) //per associarlo a paramInstance e ggiornare valori OSC in runtime
        {
            VeryVCVParamBinding binding = instance.GetComponent<VeryVCVParamBinding>();
            if (binding == null)
            {
                binding = instance.AddComponent<VeryVCVParamBinding>();
            }

            binding.Initialize(
                module.moduleId,
                control.paramId,
                control.role,
                control.normalizedValue
            );
        }

        if (control.controlId != null && control.controlId.StartsWith("param_"))
        {
            string key = BuildParamKey(module.moduleId, control.controlId);
            paramInstances[key] = instance;
        }

        SpawnControlLabel(moduleRoot, control);
    }

    private void SpawnModuleTitle(Transform moduleRoot, ModuleDescriptor module)
    {
        if (registry == null || !registry.HasPrefab(ControlRole.Label))
            return;

        if (string.IsNullOrWhiteSpace(module.moduleName))
            return;

        GameObject labelPrefab = registry.GetPrefab(ControlRole.Label);
        if (labelPrefab == null)
            return;

        GameObject labelInstance = Instantiate(labelPrefab, moduleRoot);
        labelInstance.name = $"Label_ModuleTitle_{module.moduleName}";

        float titleY = GetModuleTitleY(module);

        labelInstance.transform.localPosition = new Vector3(0f, titleY, GetLabelZ());
        labelInstance.transform.localRotation = Quaternion.identity;
        labelInstance.transform.localScale = Vector3.one * GetModuleTitleScale(module);

        LabelValueAdapter adapter = labelInstance.GetComponent<LabelValueAdapter>();
        if (adapter != null)
        {
            adapter.SetText(module.moduleName, LabelValueAdapter.LabelVisualRole.ModuleTitle);
        }
    }

    private void SpawnControlLabel(Transform moduleRoot, ControlDescriptor control)
    {
        if (registry == null || !registry.HasPrefab(ControlRole.Label))
            return;

        if (string.IsNullOrWhiteSpace(control.label))
            return;

        GameObject labelPrefab = registry.GetPrefab(ControlRole.Label);
        if (labelPrefab == null)
            return;

        GameObject labelInstance = Instantiate(labelPrefab, moduleRoot);
        labelInstance.name = $"Label_{control.controlId}";

        float yOffset = GetControlLabelYOffset(control);

        labelInstance.transform.localPosition = new Vector3(
            control.localPosition.x,
            control.localPosition.y + yOffset,
            GetLabelZ()
        );

        labelInstance.transform.localRotation = Quaternion.identity;
        labelInstance.transform.localScale = Vector3.one * GetControlLabelScale(control);

        LabelValueAdapter adapter = labelInstance.GetComponent<LabelValueAdapter>();
        if (adapter != null)
        {
            adapter.SetText(
                control.label,
                IsJack(control)
                    ? LabelValueAdapter.LabelVisualRole.PortLabel
                    : LabelValueAdapter.LabelVisualRole.ParamLabel
            );
        }
    }

// ===================== RUNTIME PARAM UPDATES =====================
    // metodi per gestione param_update da OSC
    private string BuildParamKey(string moduleId, string controlId)
    {
        return moduleId + "|" + controlId;
    }

    public bool ApplyParamUpdate(string moduleId, int paramId, float normalizedValue)
    {
        
        if (float.IsNaN(normalizedValue) || float.IsInfinity(normalizedValue)) //per evitare di avere valori non validi/strani
        {
            Debug.LogWarning($"GenericModuleSpawner: invalid normalizedValue for module={moduleId} param={paramId}: {normalizedValue}");
            return false;
        }

        normalizedValue = Mathf.Clamp01(normalizedValue);

        string controlId = "param_" + paramId;
        string key = BuildParamKey(moduleId, controlId);

        if (!paramInstances.TryGetValue(key, out GameObject instance) || instance == null)
        {
            return false;
        }

        KnobBehaviour knobBehaviour = instance.GetComponent<KnobBehaviour>();
        if (knobBehaviour != null)
        {
            if (knobBehaviour.IsDraggingLocally())
                return true;

            knobBehaviour.SetNormalizedValue(normalizedValue);
            return true;
        }

        SliderBehaviour sliderBehaviour = instance.GetComponentInChildren<SliderBehaviour>();
        if (sliderBehaviour != null && sliderBehaviour.IsDraggingLocally())
        {
            return true;
        }
        SliderValueAdapter slider = instance.GetComponent<SliderValueAdapter>();
        if (slider != null)
        {
            slider.SetNormalizedValue(normalizedValue);
            return true;
        }

        ButtonBehaviour button = instance.GetComponent<ButtonBehaviour>();
        if (button != null)
        {
            button.SetNormalizedValue(normalizedValue);
            return true;
        }

        Debug.LogWarning($"GenericModuleSpawner: nessun adapter compatibile per {key}");
        return false;
    }
// ===================== LABEL LAYOUT =====================
    private float GetModuleTitleY(ModuleDescriptor module)
    {
        float topMargin = module.panelSize.y * ModuleTitleTopMarginFactor;
        return (module.panelSize.y * 0.5f) - topMargin;
    }

    private float GetModuleTitleScale(ModuleDescriptor module)
    {
        return Mathf.Max(MinModuleTitleScale, module.panelSize.x * ModuleTitleWidthScaleFactor);
    }

    private float GetControlLabelYOffset(ControlDescriptor control)
    {
        float boxHeight = Mathf.Max(0.001f, control.sourceBoxSize.y);

        float gapFactor = IsJack(control) ? PortLabelGapFactor : ParamLabelGapFactor;

        float halfHeight = boxHeight * 0.5f;
        float extraGap = boxHeight * gapFactor;

        return -(halfHeight + extraGap);
    }

   private float GetControlLabelScale(ControlDescriptor control)
    {
        //mi proteggo da valori troppo piccoli o strani, e prendo come riferimento la dimensione del box del controllo
        float sizeRef = Mathf.Max(MinControlSize, Mathf.Min(control.sourceBoxSize.x, control.sourceBoxSize.y));

        if (IsJack(control))
            return Mathf.Max(MinPortLabelScale, sizeRef * PortLabelScaleFactor);

        return Mathf.Max(MinParamLabelScale, sizeRef * ParamLabelScaleFactor);
    }

// ===================== PREFAB INITIALIZATION =====================
    private void ApplyInitialValue(GameObject instance, ControlDescriptor control)
    {
            KnobBehaviour knobBehaviour = instance.GetComponent<KnobBehaviour>();
            if (knobBehaviour != null)
            {
                knobBehaviour.ConfigureFromDescriptor(control);
                knobBehaviour.SetNormalizedValue(control.normalizedValue);
            }
            else
            {
                KnobValueAdapter knob = instance.GetComponent<KnobValueAdapter>();
                if (knob != null)
                {
                    knob.SetNormalizedValue(control.normalizedValue);
                }
            }

            SliderValueAdapter slider = instance.GetComponent<SliderValueAdapter>();
            if (slider != null)
            {
                slider.SetNormalizedValue(control.normalizedValue);
            }

            ButtonBehaviour button = instance.GetComponent<ButtonBehaviour>();
            if (button != null)
            {
                button.ConfigureFromDescriptor(control);
                button.SetNormalizedValue(control.normalizedValue);
            }

            JackValueAdapter jack = instance.GetComponent<JackValueAdapter>();
            if (jack != null)
            {
                if (control.role == ControlRole.JackIn)
                    jack.SetDirection(JackValueAdapter.JackDirection.Input);
                else if (control.role == ControlRole.JackOut)
                    jack.SetDirection(JackValueAdapter.JackDirection.Output);
            }
        }

    private void RemoveCollider(GameObject go)
    {
        Collider col = go.GetComponent<Collider>();
        if (col != null)
        {
            Destroy(col);
        }
    }

    private void ApplyMaterial(GameObject go, Material mat)
    {
        Renderer rend = go.GetComponent<Renderer>();
        if (rend != null && mat != null)
        {
            rend.sharedMaterial = mat;
        }
    }

}