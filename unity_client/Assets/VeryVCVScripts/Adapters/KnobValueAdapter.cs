using UnityEngine;

// ===================== KNOB VALUE ADAPTER =====================
// Adapter visivo del knob.
// Traduce un normalizedValue [0..1] in una rotazione locale del pivot.

public class KnobValueAdapter : MonoBehaviour
{
    [SerializeField] private Transform knobPivot;
    [SerializeField] private float minAngle = -135f;
    [SerializeField] private float maxAngle = 135f;
    [SerializeField, Range(0f, 1f)] private float normalizedValue = 0f;

    public Transform KnobPivot => knobPivot;
    public float MinAngle => minAngle;
    public float MaxAngle => maxAngle;
    public float NormalizedValue => normalizedValue;

    private void Awake()
    {
        RefreshVisual();
    }

    private void OnValidate()
    {
        normalizedValue = Mathf.Clamp01(normalizedValue);

        if (knobPivot != null)
            RefreshVisual();
    }

    public void SetNormalizedValue(float value)
    {
        normalizedValue = Mathf.Clamp01(value);
        RefreshVisual();
    }

    public void SetAngle(float angle)
    {
        normalizedValue = AngleToNormalized(angle);
        RefreshVisual();
    }

    public float GetCurrentAngle()
    {
        return Mathf.Lerp(minAngle, maxAngle, normalizedValue);
    }

    public float GetCurrentSignedPivotAngle()
    {
        if (knobPivot == null)
            return 0f;

        float z = knobPivot.localEulerAngles.z;
        if (z > 180f)
            z -= 360f;

        return z;
    }

    public float ClampAngle(float angle)
    {
        return Mathf.Clamp(angle, minAngle, maxAngle);
    }

    public float AngleToNormalized(float angle)
    {
        return Mathf.InverseLerp(minAngle, maxAngle, ClampAngle(angle));
    }

    public float NormalizedToAngle(float value)
    {
        return Mathf.Lerp(minAngle, maxAngle, Mathf.Clamp01(value));
    }

    public void SyncFromPivot()
    {
        normalizedValue = AngleToNormalized(GetCurrentSignedPivotAngle());
    }

    private void RefreshVisual()
    {
        if (knobPivot == null)
        {
            Debug.LogError("KnobValueAdapter: knobPivot non assegnato.");
            return;
        }

        float angle = Mathf.Lerp(minAngle, maxAngle, normalizedValue);
        knobPivot.localRotation = Quaternion.Euler(0f, 0f, angle);
    }
}