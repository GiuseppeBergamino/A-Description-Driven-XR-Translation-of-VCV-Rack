using UnityEngine;

// ===================== BUTTON BEHAVIOUR =====================
// Meta SDK gestisce il movimento del pulsante tramite PokeInteractableVisual.
// Questo script osserva il movimento del visual, lo converte in stato logico
// e invia param_set a VCV.
// Inoltre riceve param_update remoti tramite SetNormalizedValue().
//
// Setup previsto:
// - movingVisualTransform = PokeSurface   (oggetto mosso da Meta)
// - buttonBaseTransform   = Surface       (backstop/base)
// - buttonRenderer        = ButtonMesh    (feedback colore)

public class ButtonBehaviour : MonoBehaviour
{
    public enum ButtonMode
    {
        UseDescriptor,
        Momentary,
        Toggle
    }

    [Header("References")]
    [SerializeField] private Transform movingVisualTransform;
    [SerializeField] private Transform buttonBaseTransform;
    [SerializeField] private Renderer buttonRenderer;
    [SerializeField] private VeryVCVSnapshotLoader snapshotLoader;
    [SerializeField] private VeryVCVOscSender oscSender;

    [Header("Logic")]
    [SerializeField] private ButtonMode modeOverride = ButtonMode.UseDescriptor;
    [SerializeField] private float pressThreshold = 0.75f;
    [SerializeField] private float releaseThreshold = 0.25f;

    [Header("Visuals")]
    [SerializeField] private Color idleColor = new Color(0.75f, 0.15f, 0.15f);
    [SerializeField] private Color pressedColor = new Color(1.0f, 0.35f, 0.35f);

    [Header("Debug")]
    [SerializeField] private bool verboseLogs = false;

    private VeryVCVParamBinding paramBinding;

    private Vector3 releasedLocalPos;
    private Vector3 travelDirLocal;
    private float travelLength = 0f;

    private ButtonMode resolvedMode = ButtonMode.Momentary;
    private string descriptorBehavior = "";

    private bool isPressedLocal = false;   // stato fisico locale con isteresi
    private bool toggleState = false;      // stato logico toggle
    private float remoteNormalizedValue = 0f;

    private MaterialPropertyBlock propertyBlock;
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");

    private void Awake()
    {
        if (snapshotLoader == null)
            snapshotLoader = FindAnyObjectByType<VeryVCVSnapshotLoader>();

        if (oscSender == null)
            oscSender = FindAnyObjectByType<VeryVCVOscSender>();

        // Fallback iniziale: se nessun descriptor arriva, restiamo momentary
        resolvedMode = modeOverride == ButtonMode.UseDescriptor
            ? ButtonMode.Momentary
            : modeOverride;
    }

    private void Start()
    {
        ResolveParamBinding();

        if (movingVisualTransform == null)
        {
            Debug.LogError("ButtonBehaviour: movingVisualTransform non assegnato.");
            enabled = false;
            return;
        }

        if (buttonBaseTransform == null)
        {
            Debug.LogError("ButtonBehaviour: buttonBaseTransform non assegnato.");
            enabled = false;
            return;
        }

        releasedLocalPos = movingVisualTransform.localPosition;

        Vector3 travel = buttonBaseTransform.localPosition - releasedLocalPos;
        travelLength = travel.magnitude;

        if (travelLength < 1e-6f)
        {
            Debug.LogError("ButtonBehaviour: corsa del bottone nulla o troppo piccola.");
            enabled = false;
            return;
        }

        travelDirLocal = travel / travelLength;

        propertyBlock = new MaterialPropertyBlock();
        UpdateVisualColor();
    }

    private void Update()
    {
        if (paramBinding == null)
            ResolveParamBinding();

        float visualPress01 = GetVisualPress01();

        if (!isPressedLocal && visualPress01 >= pressThreshold)
        {
            isPressedLocal = true;
            HandleLocalPress();
        }
        else if (isPressedLocal && visualPress01 <= releaseThreshold)
        {
            isPressedLocal = false;
            HandleLocalRelease();
        }

        UpdateVisualColor(visualPress01);
    }

    private void OnValidate()
    {
        pressThreshold = Mathf.Clamp01(pressThreshold);
        releaseThreshold = Mathf.Clamp01(releaseThreshold);

        if (releaseThreshold > pressThreshold)
            releaseThreshold = pressThreshold;
    }

    // ===================== CONFIG FROM DESCRIPTOR =====================

    public void ConfigureFromDescriptor(ControlDescriptor control)
    {
        descriptorBehavior = control != null ? control.behavior ?? "" : "";

        if (modeOverride == ButtonMode.UseDescriptor)
        {
            if (control != null && control.IsToggle)
                resolvedMode = ButtonMode.Toggle;
            else
                resolvedMode = ButtonMode.Momentary;
        }
        else
        {
            resolvedMode = modeOverride;
        }

        if (verboseLogs)
        {
            Debug.Log(
                $"ButtonBehaviour: control={(control != null ? control.controlId : "null")} " +
                $"behavior='{descriptorBehavior}' resolvedMode={resolvedMode}"
            );
        }

        UpdateVisualColor();
    }

    // ===================== REMOTE UPDATE =====================

    public void SetNormalizedValue(float value)
    {
        remoteNormalizedValue = Mathf.Clamp01(value);

        if (resolvedMode == ButtonMode.Toggle)
        {
            toggleState = remoteNormalizedValue >= 0.5f;
        }

        UpdateVisualColor();
    }

    // ===================== LOCAL INTERACTION =====================

    public float GetVisualPress01()
    {
        if (movingVisualTransform == null || travelLength < 1e-6f)
            return 0f;

        Vector3 delta = movingVisualTransform.localPosition - releasedLocalPos;
        float signedTravel = Vector3.Dot(delta, travelDirLocal);
        return Mathf.Clamp01(signedTravel / travelLength);
    }

    public bool IsPressedVisual()
    {
        return isPressedLocal;
    }

    public bool IsToggleMode()
    {
        return resolvedMode == ButtonMode.Toggle;
    }

    public string GetResolvedBehaviorDebug()
    {
        return descriptorBehavior;
    }

    public ButtonMode GetResolvedMode()
    {
        return resolvedMode;
    }

    private void HandleLocalPress()
    {
        if (verboseLogs)
            Debug.Log($"ButtonBehaviour: local press ({resolvedMode})");

        if (resolvedMode == ButtonMode.Momentary)
        {
            SendParamSet(1f);
        }
        else
        {
            toggleState = !toggleState;
            remoteNormalizedValue = toggleState ? 1f : 0f;
            SendParamSet(remoteNormalizedValue);
        }
    }

    private void HandleLocalRelease()
    {
        if (verboseLogs)
            Debug.Log($"ButtonBehaviour: local release ({resolvedMode})");

        if (resolvedMode == ButtonMode.Momentary)
        {
            remoteNormalizedValue = 0f;
            SendParamSet(0f);
        }
    }

    private void SendParamSet(float normalizedValue)
    {
        if (paramBinding == null || snapshotLoader == null || oscSender == null)
            return;

        string patchSessionId = snapshotLoader.CurrentPatchSessionId;
        if (string.IsNullOrWhiteSpace(patchSessionId))
            return;

        oscSender.SendParamSet(
            patchSessionId,
            paramBinding.ModuleId,
            paramBinding.ParamId,
            Mathf.Clamp01(normalizedValue)
        );
    }

    // ===================== INTERNAL =====================

    private void ResolveParamBinding()
    {
        if (paramBinding == null)
        {
            paramBinding = GetComponent<VeryVCVParamBinding>();
            if (paramBinding == null)
                paramBinding = GetComponentInParent<VeryVCVParamBinding>();
        }
    }

    private void UpdateVisualColor()
    {
        UpdateVisualColor(GetVisualPress01());
    }

    private void UpdateVisualColor(float localPress01)
    {
        if (buttonRenderer == null)
            return;

        float amount;

        if (resolvedMode == ButtonMode.Toggle)
        {
            amount = toggleState ? 1f : 0f;
        }
        else
        {
            // In modalità momentary usiamo il massimo tra:
            // - stato locale della corsa
            // - stato remoto ricevuto da VCV
            amount = Mathf.Max(localPress01, remoteNormalizedValue);
        }

        Color currentColor = Color.Lerp(idleColor, pressedColor, Mathf.Clamp01(amount));

        if (propertyBlock == null)
            propertyBlock = new MaterialPropertyBlock();

        buttonRenderer.GetPropertyBlock(propertyBlock);

        // Compatibilità URP / Standard
        propertyBlock.SetColor(BaseColorId, currentColor);
        propertyBlock.SetColor(ColorId, currentColor);

        buttonRenderer.SetPropertyBlock(propertyBlock);
    }
}