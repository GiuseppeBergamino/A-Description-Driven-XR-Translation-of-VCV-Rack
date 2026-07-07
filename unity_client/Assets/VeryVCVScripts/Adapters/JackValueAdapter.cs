using UnityEngine;

// ===================== JACK VALUE ADAPTER =====================
// Adapter visivo minimale per i prefab jack.
// Gestisce la direzione semantica della porta (input/output)
// e aggiorna il marker visivo associato.

public class JackValueAdapter : MonoBehaviour
{
    // Direzione semantica della porta nel pannello MR.
    public enum JackDirection
    {
        Input,
        Output
    }

    [SerializeField] private Transform directionMarker;
    [SerializeField] private Renderer ringRenderer;
    [SerializeField] private Renderer innerRenderer;
    [SerializeField] private JackDirection defaultDirection = JackDirection.Input;

    private void Reset()
    {
        if (directionMarker == null) {
            Transform marker = transform.Find("DirectionMarker");
            if (marker != null)
                directionMarker = marker;
        }

        if (ringRenderer == null){
            Transform ring = transform.Find("Ring");
            if (ring != null)
                ringRenderer = ring.GetComponent<Renderer>();
        }

        if (innerRenderer == null) {
            Transform inner = transform.Find("Inner");
            if (inner != null)
                innerRenderer = inner.GetComponent<Renderer>();
        }
    }

    private void Awake()
    {
        ApplyDirection(defaultDirection);
    }

    public void SetDirection(JackDirection direction)
    {
        ApplyDirection(direction);
    }

    // Convenzione v1:
    // - Input  -> marker verde
    // - Output -> marker rosso
    // La geometria del jack resta invariata; cambia solo il marker visivo.
    private void ApplyDirection(JackDirection direction)
    {
        defaultDirection = direction;

        if (directionMarker != null) {
                Renderer markerRenderer = directionMarker.GetComponent<Renderer>();
                if (markerRenderer != null) {
                    markerRenderer.material.color =
                        direction == JackDirection.Input
                        ? new Color(0.2f, 0.85f, 0.2f, 1f)   // verde
                        : new Color(0.9f, 0.2f, 0.2f, 1f);   // rosso
                }
            }

        if (ringRenderer != null) {
            //todo
        }

        if (innerRenderer != null) {
            //todo
        }
    }
}