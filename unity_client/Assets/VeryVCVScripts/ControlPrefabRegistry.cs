using UnityEngine;
// ===================== CONTROL PREFAB REGISTRY =====================
// Registro dei prefab generici usati dallo spawner
// Mappa un ControlRole al prefab Unity corrispondente

// Esposto in Inspector per assegnare i prefab base della patch
public class ControlPrefabRegistry : MonoBehaviour
{
    [Header("Generic Control Prefabs")]
    public GameObject knobPrefab;
    public GameObject sliderPrefab;
    public GameObject buttonPrefab;
    public GameObject togglePrefab;
    public GameObject jackInPrefab;
    public GameObject jackOutPrefab;
    public GameObject lightPrefab;
    public GameObject labelPrefab;

// ===================== LOOKUP =====================
    public GameObject GetPrefab(ControlRole role)
    {
        switch (role)
        {
            case ControlRole.Knob:
                return knobPrefab;

            case ControlRole.Slider:
                return sliderPrefab;

            case ControlRole.Button:
                return buttonPrefab;

            case ControlRole.Toggle:
                return togglePrefab;

            case ControlRole.JackIn:
                return jackInPrefab;

            case ControlRole.JackOut:
                return jackOutPrefab;

            case ControlRole.Light:
                return lightPrefab;

            case ControlRole.Label:
                return labelPrefab;

            default:
                return null;
        }
    }

// Helper per verificare se un ruolo ha un prefab assegnato
    public bool HasPrefab(ControlRole role)
    {
        return GetPrefab(role) != null;
    }
}