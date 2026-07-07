using TMPro;
using UnityEngine;

// ===================== LABEL VALUE ADAPTER =====================
// Adapter visivo minimale per i prefab label.
// Gestisce contenuto testuale e stile base in funzione del ruolo semantico.

public class LabelValueAdapter : MonoBehaviour
{
    // Ruolo visivo della label nel pannello MR.
    public enum LabelVisualRole
    {
        ModuleTitle,
        ParamLabel,
        PortLabel
    }

// ===================== INSPECTOR REFERENCES =====================
    [SerializeField] private TMP_Text textComponent;
    [SerializeField] private LabelVisualRole defaultRole = LabelVisualRole.ParamLabel;

// Auto-setup comodo in editor: prova a trovare il TMP_Text nel prefab.
    private void Reset()
    {
        if (textComponent == null)
            textComponent = GetComponentInChildren<TMP_Text>();
    }

// Applica subito lo stile di default all'istanza della label
    private void Awake()
    {
        ApplyRoleStyle(defaultRole);
    }

// Aggiorna solo il contenuto testuale, mantenendo lo stile corrente
    public void SetText(string value)
    {
        if (textComponent == null)
            return;

        textComponent.text = value ?? string.Empty;
    }

// Aggiorna stile e contenuto in un solo passaggio.
    public void SetText(string value, LabelVisualRole role)
    {
        ApplyRoleStyle(role);
        SetText(value);
    }

    public void ApplyRoleStyle(LabelVisualRole role)
    {
        if (textComponent == null)
            return;

        defaultRole = role;

        switch (role)
        {
            case LabelVisualRole.ModuleTitle:
                textComponent.alignment = TextAlignmentOptions.Center;
                textComponent.fontSize = 3.2f;
                textComponent.color = new Color(0.95f, 0.95f, 0.95f, 1f);
                break;

            case LabelVisualRole.ParamLabel:
                textComponent.alignment = TextAlignmentOptions.Center;
                textComponent.fontSize = 2.0f;
                textComponent.color = new Color(0.9f, 0.9f, 0.9f, 1f);
                break;

            case LabelVisualRole.PortLabel:
                textComponent.alignment = TextAlignmentOptions.Center;
                textComponent.fontSize = 1.8f;
                textComponent.color = new Color(0.85f, 0.85f, 0.85f, 1f);
                break;
        }
    }
}