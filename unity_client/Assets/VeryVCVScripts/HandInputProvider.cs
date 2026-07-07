using UnityEngine;

// ===================== HAND INPUT PROVIDER =====================
// Provider globale di scena per dati mano/pinch condivisi.
// Legge una sola volta per frame le feature utili di mano sinistra e destra,
// così knob e slider non devono fare polling diretto del tracking.
//
// Uso:
// - aggiungi questo componente a un GameObject unico in scena
// - puoi assegnare i riferimenti in Inspector oppure lasciarli vuoti
// - se lasciati vuoti, prova a risolverli automaticamente
//
// Espone:
// - stato mano sinistra/destra
// - pinch attivo
// - pinch midpoint world
// - posizioni index/thumb tip

public class HandInputProvider : MonoBehaviour
{
    public enum HandSide
    {
        Left,
        Right
    }

    [System.Serializable]
    public class HandState
    {
        public HandSide side;

        [Header("Resolved References")]
        public OVRHand hand;
        public Transform indexTip;
        public Transform thumbTip;

        [Header("Runtime State")]
        public bool referencesResolved = false;
        public bool isTracked = false;
        public bool isDataValid = false;
        public bool isHighConfidence = false;
        public bool isPinchingIndex = false;
        public bool hasValidPinchPose = false;

        public Vector3 indexTipWorld = Vector3.zero;
        public Vector3 thumbTipWorld = Vector3.zero;
        public Vector3 pinchMidpointWorld = Vector3.zero;
    }

    public static HandInputProvider Instance { get; private set; }

    [Header("Optional Explicit References")]
    [SerializeField] private OVRHand leftHand;
    [SerializeField] private OVRHand rightHand;

    [SerializeField] private OVRSkeleton leftSkeleton;
    [SerializeField] private OVRSkeleton rightSkeleton;

    [SerializeField] private Transform leftIndexTip;
    [SerializeField] private Transform rightIndexTip;
    [SerializeField] private Transform leftThumbTip;
    [SerializeField] private Transform rightThumbTip;

    [SerializeField] private bool dumpSkeletonBonesOnce = false;
    private bool dumpedSkeletonBones = false;

    [Header("Auto Resolve")]
    [SerializeField] private bool autoResolve = true;
    [SerializeField] private float autoResolveRetryInterval = 0.5f;

    [Header("Debug")]
    [SerializeField] private bool verboseLogs = false;

    private float nextResolveAttemptTime = 0f;

    private HandState leftState = new HandState { side = HandSide.Left };
    private HandState rightState = new HandState { side = HandSide.Right };

    public HandState Left => leftState;
    public HandState Right => rightState;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("HandInputProvider: trovato più di un provider in scena, distruggo il duplicato.");
            Destroy(this);
            return;
        }

        Instance = this;

        ApplyInspectorReferencesToStates();
    }

    private void Start()
    {
        if (autoResolve)
        {
            ResolveAllIfNeeded();
            nextResolveAttemptTime = Time.unscaledTime + autoResolveRetryInterval;
        }

        UpdateHandState(leftState);
        UpdateHandState(rightState);
        DumpSkeletonBonesIfNeeded();

        if (verboseLogs)
        {
            Debug.Log(BuildDebugSummary());
        }
    }

    private void Update()
    {
        if (autoResolve && MissingAnyReference())
        {
            if (Time.unscaledTime >= nextResolveAttemptTime)
            {
                ResolveAllIfNeeded();
                nextResolveAttemptTime = Time.unscaledTime + autoResolveRetryInterval;

                if (verboseLogs)
                {
                    Debug.Log(BuildDebugSummary());
                }
            }
        }

        UpdateHandState(leftState);
        UpdateHandState(rightState);
    }

    public HandState GetState(HandSide side)
    {
        return side == HandSide.Left ? leftState : rightState;
    }

    public bool TryGetPinchState(HandSide side, out HandState state)
    {
        state = GetState(side);
        return state != null && state.hasValidPinchPose;
    }

    public bool AnyHandHasValidPinch()
    {
        return leftState.hasValidPinchPose || rightState.hasValidPinchPose;
    }

    private void ApplyInspectorReferencesToStates()
    {
        leftState.hand = leftHand;
        rightState.hand = rightHand;

        leftState.indexTip = leftIndexTip;
        rightState.indexTip = rightIndexTip;

        leftState.thumbTip = leftThumbTip;
        rightState.thumbTip = rightThumbTip;
    }

    private bool MissingAnyReference()
    {
        return leftState.hand == null ||
               rightState.hand == null ||
               leftState.indexTip == null ||
               rightState.indexTip == null ||
               leftState.thumbTip == null ||
               rightState.thumbTip == null;
    }

    private void ResolveAllIfNeeded()
    {
        if (leftState.hand == null || rightState.hand == null)
            ResolveHandsIfNeeded();

        if (leftSkeleton == null || rightSkeleton == null)
            ResolveSkeletonsIfNeeded();

        if (leftState.indexTip == null || rightState.indexTip == null || leftState.thumbTip == null || rightState.thumbTip == null)
            ResolveTipsIfNeeded();

        leftState.referencesResolved = leftState.hand != null && leftState.indexTip != null && leftState.thumbTip != null;
        rightState.referencesResolved = rightState.hand != null && rightState.indexTip != null && rightState.thumbTip != null;
    }

    private void ResolveHandsIfNeeded()
    {
        OVRHand[] hands = FindObjectsByType<OVRHand>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None
        );

        foreach (OVRHand hand in hands)
        {
            if (hand == null)
                continue;

            string path = BuildHierarchyPath(hand.transform).ToLowerInvariant();

            if (leftState.hand == null && path.Contains("left"))
            {
                leftState.hand = hand;
                continue;
            }

            if (rightState.hand == null && path.Contains("right"))
            {
                rightState.hand = hand;
            }
        }

        // fallback prudente
        if (leftState.hand == null && hands.Length > 0)
            leftState.hand = hands[0];

        if (rightState.hand == null && hands.Length > 1)
        {
            for (int i = 0; i < hands.Length; i++)
            {
                if (hands[i] != leftState.hand)
                {
                    rightState.hand = hands[i];
                    break;
                }
            }
        }
    }

    private void ResolveTipsIfNeeded()
    {
        ResolveSkeletonsIfNeeded();

        if (leftState.indexTip == null && leftSkeleton != null)
            leftState.indexTip = FindBestFingerTipCandidate(leftSkeleton, "index");

        if (rightState.indexTip == null && rightSkeleton != null)
            rightState.indexTip = FindBestFingerTipCandidate(rightSkeleton, "index");

        if (leftState.thumbTip == null && leftSkeleton != null)
            leftState.thumbTip = FindBestFingerTipCandidate(leftSkeleton, "thumb");

        if (rightState.thumbTip == null && rightSkeleton != null)
            rightState.thumbTip = FindBestFingerTipCandidate(rightSkeleton, "thumb");

        // fallback vecchio, se per qualche motivo lo skeleton non è ancora pronto
        Transform[] transforms = FindObjectsByType<Transform>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None
        );

        if (leftState.indexTip == null)
            leftState.indexTip = FindTipByNameAndSide(transforms, "HandIndexFingertip", "left");

        if (rightState.indexTip == null)
            rightState.indexTip = FindTipByNameAndSide(transforms, "HandIndexFingertip", "right");

        if (leftState.thumbTip == null)
            leftState.thumbTip = FindTipBySideWithAnyName(
                transforms,
                "left",
                "HandThumbTip",
                "HandThumbFingertip",
                "ThumbTip",
                "thumb_tip",
                "thumb3",
                "b_l_thumb3"
            );

        if (rightState.thumbTip == null)
            rightState.thumbTip = FindTipBySideWithAnyName(
                transforms,
                "right",
                "HandThumbTip",
                "HandThumbFingertip",
                "ThumbTip",
                "thumb_tip",
                "thumb3",
                "b_r_thumb3"
            );
    }

    private Transform FindBestFingerTipCandidate(OVRSkeleton skeleton, string fingerToken)
    {
        if (skeleton == null || skeleton.Bones == null || string.IsNullOrWhiteSpace(fingerToken))
            return null;

        Transform bestTip = null;
        Transform bestDistal = null;
        Transform lastGeneric = null;

        foreach (OVRBone bone in skeleton.Bones)
        {
            if (bone == null || bone.Transform == null)
                continue;

            string boneId = bone.Id.ToString().ToLowerInvariant();
            string transformName = bone.Transform.name.ToLowerInvariant();

            bool matchesFinger =
                boneId.Contains(fingerToken) ||
                transformName.Contains(fingerToken);

            if (!matchesFinger)
                continue;

            lastGeneric = bone.Transform;

            // priorità 1: tip esplicito
            if (boneId.Contains("tip") || transformName.Contains("tip"))
            {
                bestTip = bone.Transform;
                break;
            }

            // priorità 2: falange distale "3"
            if (boneId.Contains("3") || transformName.Contains("3"))
            {
                bestDistal = bone.Transform;
            }
        }

        if (bestTip != null)
            return bestTip;

        if (bestDistal != null)
            return bestDistal;

        return lastGeneric;
    }

    private void ResolveSkeletonsIfNeeded()
    {
        if (leftSkeleton != null && rightSkeleton != null)
            return;

        OVRSkeleton[] skeletons = FindObjectsByType<OVRSkeleton>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None
        );

        foreach (OVRSkeleton skeleton in skeletons)
        {
            if (skeleton == null)
                continue;

            string path = BuildHierarchyPath(skeleton.transform).ToLowerInvariant();

            if (leftSkeleton == null && path.Contains("left"))
            {
                leftSkeleton = skeleton;
                continue;
            }

            if (rightSkeleton == null && path.Contains("right"))
            {
                rightSkeleton = skeleton;
            }
        }
    }

    private Transform FindBoneTransformByIdContains(OVRSkeleton skeleton, string token)
    {
        if (skeleton == null || skeleton.Bones == null || string.IsNullOrWhiteSpace(token))
            return null;

        foreach (OVRBone bone in skeleton.Bones)
        {
            if (bone == null || bone.Transform == null)
                continue;

            string boneId = bone.Id.ToString();

            if (boneId.IndexOf(token, System.StringComparison.OrdinalIgnoreCase) >= 0)
                return bone.Transform;
        }

        return null;
    }
    private void DumpSkeletonBonesIfNeeded()
{
    if (!dumpSkeletonBonesOnce || dumpedSkeletonBones)
        return;

    DumpSkeleton("LEFT", leftSkeleton);
    DumpSkeleton("RIGHT", rightSkeleton);

    dumpedSkeletonBones = true;
}

    private void DumpSkeleton(string label, OVRSkeleton skeleton)
    {
        if (skeleton == null)
        {
            Debug.Log($"HandInputProvider: {label} skeleton = null");
            return;
        }

        if (skeleton.Bones == null)
        {
            Debug.Log($"HandInputProvider: {label} skeleton bones = null");
            return;
        }

        Debug.Log($"HandInputProvider: dump bones for {label} skeleton '{skeleton.name}'");

        foreach (OVRBone bone in skeleton.Bones)
        {
            if (bone == null || bone.Transform == null)
                continue;

            Debug.Log(
                $"[{label}] boneId={bone.Id} transformName={bone.Transform.name} path={BuildHierarchyPath(bone.Transform)}"
            );
        }
    }

    private void UpdateHandState(HandState state)
    {
        if (state == null)
            return;

        state.isTracked = false;
        state.isDataValid = false;
        state.isHighConfidence = false;
        state.isPinchingIndex = false;
        state.hasValidPinchPose = false;

        if (state.hand == null || state.indexTip == null || state.thumbTip == null)
            return;

        state.isTracked = state.hand.IsTracked;
        state.isDataValid = state.hand.IsDataValid;
        state.isHighConfidence = state.hand.IsDataHighConfidence;
        state.isPinchingIndex = state.hand.GetFingerIsPinching(OVRHand.HandFinger.Index);

        state.indexTipWorld = state.indexTip.position;
        state.thumbTipWorld = state.thumbTip.position;
        state.pinchMidpointWorld = (state.indexTipWorld + state.thumbTipWorld) * 0.5f;

        state.hasValidPinchPose =
            state.isTracked &&
            state.isDataValid &&
            state.isHighConfidence &&
            state.isPinchingIndex;
    }

    private Transform FindTipByNameAndSide(Transform[] transforms, string exactName, string side)
    {
        foreach (Transform t in transforms)
        {
            if (t == null)
                continue;

            if (!string.Equals(t.name, exactName, System.StringComparison.OrdinalIgnoreCase))
                continue;

            string path = BuildHierarchyPath(t).ToLowerInvariant();
            if (path.Contains(side))
                return t;
        }

        return null;
    }

    private Transform FindTipBySideWithAnyName(Transform[] transforms, string side, params string[] possibleNames)
    {
        foreach (Transform t in transforms)
        {
            if (t == null)
                continue;

            bool nameMatch = false;
            for (int i = 0; i < possibleNames.Length; i++)
            {
                if (string.Equals(t.name, possibleNames[i], System.StringComparison.OrdinalIgnoreCase))
                {
                    nameMatch = true;
                    break;
                }
            }

            if (!nameMatch)
                continue;

            string path = BuildHierarchyPath(t).ToLowerInvariant();
            if (path.Contains(side))
                return t;
        }

        return null;
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

    public string BuildDebugSummary()
    {
        return
        $"HandInputProvider | " +
        $"L: hand={(leftState.hand != null ? leftState.hand.name : "null")} " +
        $"skel={(leftSkeleton != null ? leftSkeleton.name : "null")} " +
        $"index={(leftState.indexTip != null ? leftState.indexTip.name : "null")} " +
        $"thumb={(leftState.thumbTip != null ? leftState.thumbTip.name : "null")} | " +
        $"R: hand={(rightState.hand != null ? rightState.hand.name : "null")} " +
        $"skel={(rightSkeleton != null ? rightSkeleton.name : "null")} " +
        $"index={(rightState.indexTip != null ? rightState.indexTip.name : "null")} " +
        $"thumb={(rightState.thumbTip != null ? rightState.thumbTip.name : "null")}";
            
    }
}