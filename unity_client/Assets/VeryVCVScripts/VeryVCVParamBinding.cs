using UnityEngine;

// ===================== VERY VCV PARAM BINDING =====================
// Componente runtime attaccato ai controlli parametrizzabili spawnati.
// Espone identità protocollo (moduleId/paramId) e aggiorna la visual locale.

public class VeryVCVParamBinding : MonoBehaviour
{
    [SerializeField] private string moduleId;
    [SerializeField] private int paramId = -1;
    [SerializeField] private ControlRole role = ControlRole.Knob;
    [SerializeField, Range(0f, 1f)] private float normalizedValue = 0f;

    private KnobValueAdapter knobAdapter;
    private SliderValueAdapter sliderAdapter;
    private ButtonBehaviour buttonBehaviour;

    public string ModuleId => moduleId;
    public int ParamId => paramId;
    public ControlRole Role => role;
    public float NormalizedValue => normalizedValue;

    public void Initialize(string newModuleId, int newParamId, ControlRole newRole, float initialNormalizedValue)
    {
        moduleId = newModuleId;
        paramId = newParamId;
        role = newRole;

        knobAdapter = GetComponent<KnobValueAdapter>();
        sliderAdapter = GetComponent<SliderValueAdapter>();
        buttonBehaviour = GetComponent<ButtonBehaviour>();

        SetVisualNormalizedValue(initialNormalizedValue);
    }

    public void SetVisualNormalizedValue(float value)
    {
        normalizedValue = Mathf.Clamp01(value);

        if (knobAdapter == null)
        knobAdapter = GetComponent<KnobValueAdapter>();

        if (sliderAdapter == null)
            sliderAdapter = GetComponent<SliderValueAdapter>();

        if (buttonBehaviour == null)
            buttonBehaviour = GetComponent<ButtonBehaviour>();

        if (knobAdapter != null)
        {
            knobAdapter.SetNormalizedValue(normalizedValue);
        }

        if (sliderAdapter != null)
        {
            sliderAdapter.SetNormalizedValue(normalizedValue);
        }

        if (buttonBehaviour != null)
        {
            buttonBehaviour.SetNormalizedValue(normalizedValue);
        }
    }

    public bool IsContinuous()
    {
        return role == ControlRole.Knob || role == ControlRole.Slider;
    }

    public bool IsBinary()
    {
        return role == ControlRole.Button || role == ControlRole.Toggle;
    }
}