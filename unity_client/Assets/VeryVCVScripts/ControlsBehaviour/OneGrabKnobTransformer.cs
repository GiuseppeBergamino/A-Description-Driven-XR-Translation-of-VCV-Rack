using UnityEngine;
using Oculus.Interaction;

// ===================== ONE GRAB KNOB TRANSFORMER =====================
// Transformer custom per knob assoluto.
// Meta gestisce il lifecycle del grab;
// questo componente usa un approccio IBRIDO:
//
// 1) delta angolare dalla ROTAZIONE del grab point (stabile)
// 2) contributo secondario dalla POSIZIONE del grab point (più naturale)
//
// Va messo su KnobPivot e assegnato come One Grab Transformer del Grabbable.

public class OneGrabKnobTransformer : MonoBehaviour, ITransformer
{
    private enum GrabReferenceAxis
    {
        Up,
        Right,
        Forward
    }

    [Header("References")]
    [SerializeField] private Transform pivotTransform;
    [SerializeField] private SphereCollider interactionSphere;
    [SerializeField] private KnobValueAdapter knobAdapter;

    [Header("Rotation Contribution")]
    [SerializeField] private GrabReferenceAxis primaryAxis = GrabReferenceAxis.Up;
    [SerializeField] private GrabReferenceAxis secondaryAxis = GrabReferenceAxis.Right;
    [SerializeField, Range(0f, 1f)] private float rotationWeight = 0.75f;

    [Header("Position Contribution")]
    [SerializeField, Range(0f, 1f)] private float positionWeight = 0.25f;
    [SerializeField] private float positionMinRadius = 0.01f;
    [SerializeField] private float positionMaxRadius = 0.04f;

    [Header("Behaviour")]
    [SerializeField] private bool invertRotation = false;

    [Header("Debug")]
    [SerializeField] private bool verboseLogs = false;

    private IGrabbable grabbable;
    private Transform targetTransform;

    private Vector3 rotationAxisWorld;

    private Vector3 lastProjectedRotationReference;
    private bool hasLastProjectedRotationReference = false;

    private Vector3 lastProjectedPositionReference;
    private bool hasLastProjectedPositionReference = false;

    private float accumulatedAngle = 0f;

    public void Initialize(IGrabbable grabbable)
    {
        this.grabbable = grabbable;
        targetTransform = grabbable.Transform;

        if (pivotTransform == null)
            pivotTransform = targetTransform;

        if (interactionSphere == null)
            interactionSphere = GetComponent<SphereCollider>();

        if (knobAdapter == null)
            knobAdapter = GetComponentInParent<KnobValueAdapter>();
    }

    public void BeginTransform()
    {
        if (!IsReady())
            return;

        rotationAxisWorld = pivotTransform.forward;
        accumulatedAngle = knobAdapter.GetCurrentSignedPivotAngle();

        if (TryGetProjectedGrabRotationReference(out Vector3 projectedRotationReference))
        {
            lastProjectedRotationReference = projectedRotationReference;
            hasLastProjectedRotationReference = true;
        }
        else
        {
            hasLastProjectedRotationReference = false;
        }

        if (TryGetProjectedGrabPositionReference(out Vector3 projectedPositionReference, out _))
        {
            lastProjectedPositionReference = projectedPositionReference;
            hasLastProjectedPositionReference = true;
        }
        else
        {
            hasLastProjectedPositionReference = false;
        }

        if (verboseLogs)
        {
            Debug.Log($"OneGrabKnobTransformer: begin grab angle={accumulatedAngle:F1}");
        }
    }

    public void UpdateTransform()
    {
        if (!IsReady())
            return;

        rotationAxisWorld = pivotTransform.forward;

        bool hasCurrentRot = TryGetProjectedGrabRotationReference(out Vector3 currentProjectedRotationReference);
        bool hasCurrentPos = TryGetProjectedGrabPositionReference(
            out Vector3 currentProjectedPositionReference,
            out float currentPositionRadius
        );

        float? deltaFromRotation = null;
        float? deltaFromPosition = null;

        if (hasCurrentRot)
        {
            if (hasLastProjectedRotationReference)
            {
                float deltaRot = Vector3.SignedAngle(
                    lastProjectedRotationReference,
                    currentProjectedRotationReference,
                    rotationAxisWorld
                );

                deltaFromRotation = invertRotation ? -deltaRot : deltaRot;
            }

            lastProjectedRotationReference = currentProjectedRotationReference;
            hasLastProjectedRotationReference = true;
        }

        if (hasCurrentPos)
        {
            if (hasLastProjectedPositionReference)
            {
                float deltaPos = Vector3.SignedAngle(
                    lastProjectedPositionReference,
                    currentProjectedPositionReference,
                    rotationAxisWorld
                );

                deltaFromPosition = invertRotation ? -deltaPos : deltaPos;
            }

            lastProjectedPositionReference = currentProjectedPositionReference;
            hasLastProjectedPositionReference = true;
        }

        if (!deltaFromRotation.HasValue && !deltaFromPosition.HasValue)
            return;

        float effectivePositionBlend = 0f;
        if (deltaFromPosition.HasValue)
        {
            effectivePositionBlend = Mathf.InverseLerp(
                positionMinRadius,
                positionMaxRadius,
                currentPositionRadius
            );

            effectivePositionBlend = Mathf.Clamp01(effectivePositionBlend) * positionWeight;
        }

        float effectiveRotationBlend = deltaFromRotation.HasValue ? rotationWeight : 0f;

        float totalBlend = effectiveRotationBlend + effectivePositionBlend;
        if (totalBlend <= 1e-6f)
            return;

        float blendedDelta = 0f;

        if (deltaFromRotation.HasValue)
            blendedDelta += deltaFromRotation.Value * effectiveRotationBlend;

        if (deltaFromPosition.HasValue)
            blendedDelta += deltaFromPosition.Value * effectivePositionBlend;

        blendedDelta /= totalBlend;

        accumulatedAngle = Mathf.Clamp(
            accumulatedAngle + blendedDelta,
            knobAdapter.MinAngle,
            knobAdapter.MaxAngle
        );

        SetLocalZAngle(pivotTransform, accumulatedAngle);
    }

    public void EndTransform()
    {
        if (knobAdapter != null)
            knobAdapter.SyncFromPivot();

        hasLastProjectedRotationReference = false;
        hasLastProjectedPositionReference = false;

        if (verboseLogs)
        {
            Debug.Log($"OneGrabKnobTransformer: end grab angle={accumulatedAngle:F1}");
        }
    }

    private bool IsReady()
    {
        return grabbable != null &&
               knobAdapter != null &&
               pivotTransform != null &&
               interactionSphere != null &&
               grabbable.GrabPoints != null &&
               grabbable.GrabPoints.Count > 0;
    }

    private bool TryGetProjectedGrabRotationReference(out Vector3 projectedReference)
    {
        projectedReference = Vector3.zero;

        if (grabbable == null || grabbable.GrabPoints == null || grabbable.GrabPoints.Count == 0)
            return false;

        Quaternion grabRotation = grabbable.GrabPoints[0].rotation;

        Vector3 primaryWorld = grabRotation * AxisToVector(primaryAxis);
        Vector3 secondaryWorld = grabRotation * AxisToVector(secondaryAxis);

        Vector3 projectedPrimary = Vector3.ProjectOnPlane(primaryWorld, rotationAxisWorld);
        Vector3 projectedSecondary = Vector3.ProjectOnPlane(secondaryWorld, rotationAxisWorld);

        float primaryMag = projectedPrimary.sqrMagnitude;
        float secondaryMag = projectedSecondary.sqrMagnitude;

        if (primaryMag >= secondaryMag && primaryMag > 1e-8f)
        {
            projectedReference = projectedPrimary.normalized;
            return true;
        }

        if (secondaryMag > 1e-8f)
        {
            projectedReference = projectedSecondary.normalized;
            return true;
        }

        return false;
    }

    private bool TryGetProjectedGrabPositionReference(out Vector3 projectedReference, out float projectedRadius)
    {
        projectedReference = Vector3.zero;
        projectedRadius = 0f;

        if (grabbable == null || grabbable.GrabPoints == null || grabbable.GrabPoints.Count == 0)
            return false;

        Vector3 grabPointWorld = grabbable.GrabPoints[0].position;
        Vector3 sphereCenter = GetSphereCenterWorld();

        Vector3 radial = grabPointWorld - sphereCenter;
        Vector3 projected = Vector3.ProjectOnPlane(radial, rotationAxisWorld);

        projectedRadius = projected.magnitude;

        if (projectedRadius < 1e-8f)
            return false;

        projectedReference = projected / projectedRadius;
        return true;
    }

    private Vector3 GetSphereCenterWorld()
    {
        return interactionSphere.transform.TransformPoint(interactionSphere.center);
    }

    private static Vector3 AxisToVector(GrabReferenceAxis axis)
    {
        switch (axis)
        {
            case GrabReferenceAxis.Right:
                return Vector3.right;
            case GrabReferenceAxis.Forward:
                return Vector3.forward;
            default:
                return Vector3.up;
        }
    }

    private static void SetLocalZAngle(Transform t, float angle)
    {
        t.localRotation = Quaternion.Euler(0f, 0f, angle);
    }
}