using UnityEngine;
using Oculus.Interaction;

// ===================== KNOB BEHAVIOUR =====================
// Meta ruota un pivot invisibile (DriverPivot_Meta) tramite OneGrabRotateTransformer.
// Questo script legge il delta del driver e lo applica al knob visibile
// come angolo assoluto clampato nel range del parametro.

public class KnobBehaviour : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private KnobValueAdapter knobAdapter;
    [SerializeField] private Transform visualPivot;
    [SerializeField] private Transform driverPivot;
    [SerializeField] private Grabbable driverGrabbable;
    [SerializeField] private VeryVCVSnapshotLoader snapshotLoader;
    [SerializeField] private VeryVCVOscSender oscSender;

    [Header("Behaviour")]
    [SerializeField] private bool invertDriverDelta = false;

    [Header("Send")]
    [SerializeField] private float sendThreshold = 0.002f;

    [Header("Debug")]
    [SerializeField] private bool verboseLogs = false;

    private VeryVCVParamBinding paramBinding;

    private bool wasGrabbed = false;
    private float lastDriverAngle = 0f;
    private float accumulatedVisualAngle = 0f;
    private float lastSentValue = -1f;

    private string descriptorBehavior = "";
    private bool isStepped = false;

    private void Awake()
    {
        if (knobAdapter == null)
            knobAdapter = GetComponent<KnobValueAdapter>();

        if (visualPivot == null && knobAdapter != null)
            visualPivot = knobAdapter.KnobPivot;

        if (driverGrabbable == null && driverPivot != null)
            driverGrabbable = driverPivot.GetComponent<Grabbable>();

        if (snapshotLoader == null)
            snapshotLoader = FindAnyObjectByType<VeryVCVSnapshotLoader>();

        if (oscSender == null)
            oscSender = FindAnyObjectByType<VeryVCVOscSender>();
    }

    private void Start()
    {
        paramBinding = GetComponent<VeryVCVParamBinding>();
        if (paramBinding == null)
            paramBinding = GetComponentInParent<VeryVCVParamBinding>();

        if (knobAdapter == null || visualPivot == null)
        {
            Debug.LogError("KnobBehaviour: knobAdapter o visualPivot non assegnati.");
            enabled = false;
            return;
        }

        if (driverPivot == null || driverGrabbable == null)
        {
            Debug.LogError("KnobBehaviour: driverPivot o driverGrabbable non assegnati.");
            enabled = false;
            return;
        }

        knobAdapter.SyncFromPivot();
        accumulatedVisualAngle = knobAdapter.GetCurrentSignedPivotAngle();
        lastSentValue = knobAdapter.NormalizedValue;

        ResetDriverPivot();
    }

    private void LateUpdate()
    {
        bool grabbed = IsGrabbed();

        if (grabbed && !wasGrabbed)
        {
            accumulatedVisualAngle = knobAdapter.GetCurrentSignedPivotAngle();
            lastDriverAngle = GetSignedLocalZAngle(driverPivot);

            if (verboseLogs)
            {
                Debug.Log(
                    $"KnobBehaviour: begin grab visualAngle={accumulatedVisualAngle:F1} driverAngle={lastDriverAngle:F1}"
                );
            }
        }

        if (grabbed)
        {
            float currentDriverAngle = GetSignedLocalZAngle(driverPivot);
            float delta = Mathf.DeltaAngle(lastDriverAngle, currentDriverAngle);

            if (invertDriverDelta)
                delta = -delta;

            accumulatedVisualAngle = Mathf.Clamp(
                accumulatedVisualAngle + delta,
                knobAdapter.MinAngle,
                knobAdapter.MaxAngle
            );

            knobAdapter.SetAngle(accumulatedVisualAngle);

            float normalizedValue = knobAdapter.AngleToNormalized(accumulatedVisualAngle);

            if (isStepped)
            {
                // TODO futura quantizzazione stepped
                normalizedValue = Mathf.Clamp01(normalizedValue);
            }

            SendIfNeeded(normalizedValue, accumulatedVisualAngle);

            lastDriverAngle = currentDriverAngle;
        }
        else if (wasGrabbed && !grabbed)
        {
            knobAdapter.SyncFromPivot();
            accumulatedVisualAngle = knobAdapter.GetCurrentSignedPivotAngle();
            lastSentValue = knobAdapter.NormalizedValue;

            ResetDriverPivot();

            if (verboseLogs)
            {
                Debug.Log(
                    $"KnobBehaviour: end grab finalValue={knobAdapter.NormalizedValue:F3} finalAngle={accumulatedVisualAngle:F1}"
                );
            }
        }

        wasGrabbed = grabbed;
    }

    public void ConfigureFromDescriptor(ControlDescriptor control)
    {
        descriptorBehavior = control != null ? control.behavior ?? "" : "";
        isStepped = control != null && control.IsStepped;

        if (verboseLogs)
        {
            Debug.Log(
                $"KnobBehaviour: control={(control != null ? control.controlId : "null")} " +
                $"behavior='{descriptorBehavior}' stepped={isStepped}"
            );
        }
    }

    public void SetNormalizedValue(float value)
    {
        if (IsGrabbed())
            return;

        value = Mathf.Clamp01(value);
        knobAdapter.SetNormalizedValue(value);
        accumulatedVisualAngle = knobAdapter.GetCurrentSignedPivotAngle();
        lastSentValue = value;

        ResetDriverPivot();
    }

    public bool IsDraggingLocally()
    {
        return IsGrabbed();
    }

    public string GetResolvedBehaviorDebug()
    {
        return descriptorBehavior;
    }

    private bool IsGrabbed()
    {
        return driverGrabbable != null && driverGrabbable.SelectingPointsCount > 0;
    }

    private void ResetDriverPivot()
    {
        if (driverPivot != null)
            driverPivot.localRotation = Quaternion.identity;
    }

    private void SendIfNeeded(float normalizedValue, float angle)
    {
        if (Mathf.Abs(normalizedValue - lastSentValue) < sendThreshold)
            return;

        if (paramBinding == null || snapshotLoader == null || oscSender == null)
            return;

        string patchSessionId = snapshotLoader.CurrentPatchSessionId;
        if (string.IsNullOrWhiteSpace(patchSessionId))
            return;

        if (verboseLogs)
        {
            Debug.Log(
                $"KnobBehaviour: sending param_set session={patchSessionId} " +
                $"module={paramBinding.ModuleId} param={paramBinding.ParamId} " +
                $"value={normalizedValue:F3} angle={angle:F1}"
            );
        }

        oscSender.SendParamSet(
            patchSessionId,
            paramBinding.ModuleId,
            paramBinding.ParamId,
            normalizedValue
        );

        lastSentValue = normalizedValue;
    }

    private static float GetSignedLocalZAngle(Transform t)
    {
        float z = t.localEulerAngles.z;
        if (z > 180f)
            z -= 360f;
        return z;
    }
}