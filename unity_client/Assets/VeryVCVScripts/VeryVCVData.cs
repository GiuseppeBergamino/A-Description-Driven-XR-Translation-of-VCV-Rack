using System;
using System.Collections.Generic;
using UnityEngine;

// ===================== VERY VCV SHARED DATA MODEL =====================
// Modello dati condiviso tra loader, mapper e runtime spawner.
// Contiene la rappresentazione intermedia della patch già tradotta
// in termini utili alla scena Unity.

// Ruolo semantico del controllo nella rappresentazione Unity/MR.
public enum ControlRole
{
    Knob,
    Slider,
    Button,
    Toggle,
    JackIn,
    JackOut,
    Light,
    Label
}

// ===================== CONTROL DESCRIPTOR =====================
[Serializable]
public class ControlDescriptor
{
    [Header("Identity")]
    public string controlId;
    public string label;
    public ControlRole role = ControlRole.Knob;

    [Header("Local Transform (relative to module)")]
    public Vector3 localPosition = Vector3.zero;
    public Vector3 localRotation = Vector3.zero;
    public Vector3 localScale = Vector3.one;

    [Header("Value")]
    [Range(0f, 1f)]
    public float normalizedValue = 0f;

    [Header("Flags")]
    public bool isVisible = true;
    public bool isInteractable = true;

    [Header("Interaction")]
    public string behavior = "";

    // Dimensione del box originale del controllo, già convertita in unità Unity.
    // Utile per adattare la scala del prefab alla geometria proveniente da VCV.
    public Vector2 sourceBoxSize = Vector2.zero; // size del box originale (già convertita in unità Unity)

    [Header("Protocol Identity")]
    public int paramId = -1; // Identificatore del parametro, usato per mappature e comunicazione OSC

   private static readonly char[] BehaviorSeparators = { ',', ';', '|', ' ' };

    public bool HasBehavior(string token)
    {
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(behavior))
            return false;

        string[] parts = behavior.Split(BehaviorSeparators, StringSplitOptions.RemoveEmptyEntries);

        foreach (string part in parts)
        {
            if (string.Equals(part.Trim(), token, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    public bool IsMomentary => HasBehavior("momentary");
    public bool IsToggle => HasBehavior("toggle");
    public bool IsStepped => HasBehavior("stepped");
    public bool IsContinuous => HasBehavior("continuous");
}

// ===================== MODULE DESCRIPTOR =====================
[Serializable]
public class ModuleDescriptor
{
    [Header("Identity")]
    public string moduleId;
    public string pluginSlug;
    public string modelSlug;
    public string moduleName;

    [Header("World Transform")]
    //public Vector3 worldPosition = new Vector3(0f, 1.2f, 0.8f);
    public Vector3 patchLocalPosition = Vector3.zero;

    [Header("Panel")]
    public Vector2 panelSize = new Vector2(0.28f, 0.5f);

// Metadati opzionali per una rappresentazione MR specializzata del modulo.
    [Header("MR Specialization")]
    public bool isMrAware = false;
    public string mrViewKey = "";

    [Header("Controls")]
    public List<ControlDescriptor> controls = new List<ControlDescriptor>();
}

// ===================== PATCH DESCRIPTOR =====================
[Serializable]
public class PatchDescriptor
{
    [Header("Identity")]
    public string patchId = "patch_01";

    [Header("Modules")]
    public List<ModuleDescriptor> modules = new List<ModuleDescriptor>();
}