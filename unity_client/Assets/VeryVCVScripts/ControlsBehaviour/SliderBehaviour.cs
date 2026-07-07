using UnityEngine;

// ===================== SLIDER BEHAVIOUR =====================
// Interazione touch-latch + drag vincolato su asse.
// Va messo su InteractionTarget.
//
// Flusso:
// - controlla ogni frame se un fingertip è dentro il volume del target
// - se nessun dito è attivo, lo slider si aggancia al primo dito valido
// - il valore segue la proiezione del dito sull'asse del fader
// - quando il dito esce dal volume (con una piccola tolleranza), il drag termina
//
// Setup previsto:
// - SliderValueAdapter sul root del prefab
// - InteractionTarget con BoxCollider
// - SliderPivot controllato dallo SliderValueAdapter

public class SliderBehaviour : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private SliderValueAdapter sliderAdapter;
    [SerializeField] private VeryVCVSnapshotLoader snapshotLoader;
    [SerializeField] private VeryVCVOscSender oscSender;

    [Header("Finger Targets")]
    [SerializeField] private Transform leftIndexTip;
    [SerializeField] private Transform rightIndexTip;

    [Header("Touch Volume")]
    [SerializeField] private BoxCollider interactionBox;
    [SerializeField] private float enterMargin = 0.005f;
    [SerializeField] private float exitMargin = 0.015f;

    [Header("Feel")]
    [SerializeField] private bool useSmoothing = true;
    [SerializeField] private float smoothTime = 0.04f;

    private float currentNormalizedValue = 0f;
    private float targetNormalizedValue = 0f;
    private float normalizedVelocity = 0f;

    [Header("Send")]
    [SerializeField] private float sendThreshold = 0.002f;

    [Header("Debug")]
    [SerializeField] private bool verboseLogs = false;

    private VeryVCVParamBinding paramBinding;

    private Transform activeFinger = null;
    private bool isDragging = false;

    private float dragStartFingerAxisCoord = 0f;
    private float dragStartNormalizedValue = 0f;
    private float lastSentValue = -1f;

    private Vector3 axisOriginLocal;
    private Vector3 axisDirectionLocal;
    private float axisLength = 0f;

    private Transform sliderSpace;

    private void Awake()
    {
        if (sliderAdapter == null)
            sliderAdapter = GetComponentInParent<SliderValueAdapter>();

        if (snapshotLoader == null)
            snapshotLoader = FindAnyObjectByType<VeryVCVSnapshotLoader>();

        if (oscSender == null)
            oscSender = FindAnyObjectByType<VeryVCVOscSender>();

        if (interactionBox == null)
            interactionBox = GetComponent<BoxCollider>();

        ResolveFingerTipsIfNeeded();
    }

    private void Start()
    {
        paramBinding = GetComponentInParent<VeryVCVParamBinding>();

        if (sliderAdapter == null || sliderAdapter.SliderPivot == null)
        {
            Debug.LogError("SliderBehaviour: sliderAdapter o sliderPivot non assegnati.");
            enabled = false;
            return;
        }

        if (interactionBox == null)
        {
            Debug.LogError("SliderBehaviour: BoxCollider non assegnato.");
            enabled = false;
            return;
        }

        ResolveFingerTipsIfNeeded();

        if (leftIndexTip == null && rightIndexTip == null)
        {
            Debug.LogWarning("SliderBehaviour: nessun HandIndexFingertip trovato in scena.");
        }

        sliderSpace = sliderAdapter.SliderPivot.parent;
        if (sliderSpace == null)
        {
            Debug.LogError("SliderBehaviour: sliderPivot senza parent, impossibile definire lo spazio locale dello slider.");
            enabled = false;
            return;
        }

        Vector3 minLocal = sliderAdapter.MinLocalPosition;
        Vector3 maxLocal = sliderAdapter.MaxLocalPosition;

        Vector3 axis = maxLocal - minLocal;
        axisLength = axis.magnitude;

        if (axisLength < 1e-6f)
        {
            Debug.LogError("SliderBehaviour: corsa dello slider nulla o troppo piccola.");
            enabled = false;
            return;
        }

        axisOriginLocal = minLocal;
        axisDirectionLocal = axis / axisLength;

        currentNormalizedValue = sliderAdapter.NormalizedValue;
        targetNormalizedValue = currentNormalizedValue;
        lastSentValue = currentNormalizedValue;

        //lastSentValue = sliderAdapter.NormalizedValue;
    }

    private void Update()
    {
        // Se lo slider è stato istanziato runtime molto presto, riprovo finché non trovo i fingertip
        if (leftIndexTip == null || rightIndexTip == null)
        {
            ResolveFingerTipsIfNeeded();
        }

        if (!isDragging)
        {
            TryAcquireFinger();
            return;
        }

        if (activeFinger == null)
        {
            EndDrag();
            return;
        }

        float currentFingerAxisCoord = GetFingerAxisCoord(activeFinger);
        float deltaAxis = currentFingerAxisCoord - dragStartFingerAxisCoord;

        float normalizedValue = dragStartNormalizedValue + (deltaAxis / axisLength);
        normalizedValue = Mathf.Clamp01(normalizedValue);

        targetNormalizedValue = normalizedValue;

        if (useSmoothing)
        {
            currentNormalizedValue = Mathf.SmoothDamp(
                currentNormalizedValue,
                targetNormalizedValue,
                ref normalizedVelocity,
                smoothTime
            );
        }
        else
        {
            currentNormalizedValue = targetNormalizedValue;
        }

        currentNormalizedValue = Mathf.Clamp01(currentNormalizedValue);

        sliderAdapter.SetNormalizedValue(currentNormalizedValue);
        SendIfNeeded(currentNormalizedValue);

        if (!IsFingerInsideTarget(activeFinger, exitMargin))
        {
            EndDrag();
        }
    }

    public bool IsDraggingLocally()
    {
        return isDragging;
    }

    private void TryAcquireFinger()
    {
        if (leftIndexTip != null && IsFingerInsideTarget(leftIndexTip, enterMargin))
        {
            BeginDrag(leftIndexTip);
            return;
        }

        if (rightIndexTip != null && IsFingerInsideTarget(rightIndexTip, enterMargin))
        {
            BeginDrag(rightIndexTip);
        }
    }

    private void BeginDrag(Transform finger)
    {
        activeFinger = finger;
        isDragging = true;

        dragStartFingerAxisCoord = GetFingerAxisCoord(activeFinger);
        dragStartNormalizedValue = sliderAdapter.NormalizedValue;

        if (verboseLogs)
        {
            Debug.Log($"SliderBehaviour: begin drag param={GetParamDebug()} startValue={dragStartNormalizedValue:F3} finger={finger.name}");
        }
    }

    private void EndDrag()
    {
        if (verboseLogs)
        {
            Debug.Log($"SliderBehaviour: end drag param={GetParamDebug()} finalValue={sliderAdapter.NormalizedValue:F3}");
        }

        activeFinger = null;
        isDragging = false;
    }

    private float GetFingerAxisCoord(Transform finger)
    {
        Vector3 fingerLocal = sliderSpace.InverseTransformPoint(finger.position);
        Vector3 fromOrigin = fingerLocal - axisOriginLocal;
        return Vector3.Dot(fromOrigin, axisDirectionLocal);
    }

    private bool IsFingerInsideTarget(Transform finger, float margin)
    {
        if (finger == null || interactionBox == null)
            return false;

        Vector3 localPoint = interactionBox.transform.InverseTransformPoint(finger.position) - interactionBox.center;
        Vector3 halfSize = interactionBox.size * 0.5f;

        return Mathf.Abs(localPoint.x) <= halfSize.x + margin &&
               Mathf.Abs(localPoint.y) <= halfSize.y + margin &&
               Mathf.Abs(localPoint.z) <= halfSize.z + margin;
    }

    private void SendIfNeeded(float normalizedValue)
    {
        if (Mathf.Abs(normalizedValue - lastSentValue) < sendThreshold)
            return;

        if (paramBinding == null || snapshotLoader == null || oscSender == null)
            return;

        string patchSessionId = snapshotLoader.CurrentPatchSessionId;
        if (string.IsNullOrWhiteSpace(patchSessionId))
            return;

        oscSender.SendParamSet(
            patchSessionId,
            paramBinding.ModuleId,
            paramBinding.ParamId,
            normalizedValue
        );

        lastSentValue = normalizedValue;
    }

    private void ResolveFingerTipsIfNeeded()
    {
        if (leftIndexTip != null && rightIndexTip != null)
            return;

        Transform[] transforms = FindObjectsByType<Transform>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None
        );

        Transform fallbackA = null;
        Transform fallbackB = null;

        foreach (Transform t in transforms)
        {
            if (t == null)
                continue;

            if (!string.Equals(t.name, "HandIndexFingertip", System.StringComparison.OrdinalIgnoreCase))
                continue;

            string hierarchy = BuildHierarchyPath(t).ToLowerInvariant();

            bool looksLeft = hierarchy.Contains("left");
            bool looksRight = hierarchy.Contains("right");

            if (looksLeft && leftIndexTip == null)
            {
                leftIndexTip = t;
                continue;
            }

            if (looksRight && rightIndexTip == null)
            {
                rightIndexTip = t;
                continue;
            }

            if (fallbackA == null)
            {
                fallbackA = t;
                continue;
            }

            if (fallbackB == null && t != fallbackA)
            {
                fallbackB = t;
            }
        }

        // Fallback se il naming gerarchico non aiuta
        if (leftIndexTip == null && fallbackA != null)
            leftIndexTip = fallbackA;

        if (rightIndexTip == null)
        {
            if (fallbackB != null)
                rightIndexTip = fallbackB;
            else if (fallbackA != null && fallbackA != leftIndexTip)
                rightIndexTip = fallbackA;
        }

        if (verboseLogs)
        {
            Debug.Log(
                $"SliderBehaviour: fingertip resolve -> left={(leftIndexTip != null ? leftIndexTip.name : "null")} " +
                $"right={(rightIndexTip != null ? rightIndexTip.name : "null")}"
            );
        }
    }

    private static string BuildHierarchyPath(Transform t)
    {
        if (t == null)
            return "";

        string path = t.name;
        Transform current = t.parent;

        while (current != null)
        {
            path = current.name + "/" + path;
            current = current.parent;
        }

        return path;
    }

    private string GetParamDebug()
    {
        if (paramBinding == null)
            return "unknown";

        return $"{paramBinding.ModuleId}:{paramBinding.ParamId}";
    }
}