using UnityEngine;

// ===================== SLIDER VALUE ADAPTER =====================
// Adapter visivo minimale per i prefab slider.
// Traduce un normalizedValue [0..1] in una traslazione locale del pivot.

public class SliderValueAdapter : MonoBehaviour
{
    [SerializeField] private Transform sliderPivot;

    // Escursione locale del pivot tra valore 0 e valore 1.
    [SerializeField] private Vector3 minLocalPosition = new Vector3(0f, -0.22f, 0f);
    [SerializeField] private Vector3 maxLocalPosition = new Vector3(0f, 0.22f, 0f);

    [SerializeField, Range(0f, 1f)] private float normalizedValue = 0f;

    public Transform SliderPivot => sliderPivot;
    public Vector3 MinLocalPosition => minLocalPosition;
    public Vector3 MaxLocalPosition => maxLocalPosition;
    public float NormalizedValue => normalizedValue;

    private void Awake()
    {
        RefreshVisual();
    }

    private void OnValidate()
    {
        normalizedValue = Mathf.Clamp01(normalizedValue);

        if (sliderPivot != null)
        {
            RefreshVisual();
        }
    }

    public void SetNormalizedValue(float value)
    {
        normalizedValue = Mathf.Clamp01(value);
        RefreshVisual();
    }

    private void RefreshVisual()
    {
        if (sliderPivot == null)
        {
            Debug.LogError("SliderValueAdapter: sliderPivot non assegnato.");
            return;
        }

        sliderPivot.localPosition = Vector3.Lerp(minLocalPosition, maxLocalPosition, normalizedValue);
    }
}