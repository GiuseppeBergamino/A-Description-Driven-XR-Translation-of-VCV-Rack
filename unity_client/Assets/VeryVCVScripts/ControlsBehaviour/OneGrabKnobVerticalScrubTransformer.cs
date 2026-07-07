using UnityEngine;
using Oculus.Interaction;

// ===================== ONE GRAB KNOB VERTICAL SCRUB TRANSFORMER =====================
// Meta gestisce il lifecycle del grab.
// Questo transformer usa il delta verticale della mano per controllare il knob.
//
// Comportamento:
// - pinch sul knob -> acquisizione
// - spostamento verticale della mano -> variazione del valore
// - clamp assoluto nel range del knob
// - nessuno snap al rilascio
//
// Da mettere su KnobPivot e assegnare come One Grab Transformer del Grabbable.

public class OneGrabKnobVerticalScrubTransformer : MonoBehaviour, ITransformer
{
    [Header("References")]
    [SerializeField] private Transform pivotTransform;
    [SerializeField] private KnobValueAdapter knobAdapter;

    [Header("Scrub")]
    [Tooltip("Quanto rapidamente cambia il valore per metro di movimento verticale.")]
    [SerializeField] private float verticalSensitivity = 8f;

    [Tooltip("Se true, invertiamo il verso dello scrub verticale.")]
    [SerializeField] private bool invertScrub = false;

    [Tooltip("Piccola deadzone in metri per evitare jitter.")]
    [SerializeField] private float deadZone = 0.002f;

    [Header("Ownership")]
    [Tooltip("Se true, solo un knob alla volta può essere attivo globalmente.")]
    [SerializeField] private bool exclusiveGlobalOwnership = true;

    [Header("Debug")]
    [SerializeField] private bool verboseLogs = false;

    private IGrabbable grabbable;
    private Transform targetTransform;

    private static OneGrabKnobVerticalScrubTransformer activeTransformer = null;

    private float grabStartWorldY = 0f;
    private float grabStartNormalizedValue = 0f;
    private bool ownsInteraction = false;

    public void Initialize(IGrabbable grabbable)
    {
        this.grabbable = grabbable;
        targetTransform = grabbable.Transform;

        if (pivotTransform == null)
            pivotTransform = targetTransform;

        if (knobAdapter == null)
            knobAdapter = GetComponentInParent<KnobValueAdapter>();
    }

    public void BeginTransform()
    {
        if (!IsReady())
            return;

        if (exclusiveGlobalOwnership)
        {
            if (activeTransformer != null && activeTransformer != this)
            {
                ownsInteraction = false;
                return;
            }

            activeTransformer = this;
        }

        ownsInteraction = true;

        grabStartNormalizedValue = knobAdapter.NormalizedValue;
        grabStartWorldY = GetCurrentGrabWorldY();

        if (verboseLogs)
        {
            Debug.Log(
                $"OneGrabKnobVerticalScrubTransformer: begin grab startValue={grabStartNormalizedValue:F3} startY={grabStartWorldY:F3}"
            );
        }
    }

    public void UpdateTransform()
    {
        if (!IsReady() || !ownsInteraction)
            return;

        float currentWorldY = GetCurrentGrabWorldY();
        float deltaY = currentWorldY - grabStartWorldY;

        if (invertScrub)
            deltaY = -deltaY;

        if (Mathf.Abs(deltaY) < deadZone)
            deltaY = 0f;
        else
            deltaY -= Mathf.Sign(deltaY) * deadZone;

        float normalizedValue = grabStartNormalizedValue + (deltaY * verticalSensitivity);
        normalizedValue = Mathf.Clamp01(normalizedValue);

        knobAdapter.SetNormalizedValue(normalizedValue);
    }

    public void EndTransform()
    {
        if (ownsInteraction && knobAdapter != null)
        {
            // Manteniamo il valore finale stabile e coerente
            knobAdapter.SetNormalizedValue(knobAdapter.NormalizedValue);
        }

        if (exclusiveGlobalOwnership && activeTransformer == this)
            activeTransformer = null;

        ownsInteraction = false;

        if (verboseLogs)
        {
            Debug.Log(
                $"OneGrabKnobVerticalScrubTransformer: end grab finalValue={(knobAdapter != null ? knobAdapter.NormalizedValue.ToString("F3") : "null")}"
            );
        }
    }

    private bool IsReady()
    {
        return grabbable != null &&
               knobAdapter != null &&
               pivotTransform != null &&
               grabbable.GrabPoints != null &&
               grabbable.GrabPoints.Count > 0;
    }

    private float GetCurrentGrabWorldY()
    {
        return grabbable.GrabPoints[0].position.y;
    }
}