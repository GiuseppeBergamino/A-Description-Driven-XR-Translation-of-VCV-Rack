using UnityEngine;
using UnityEngine.Networking;
using System.Collections;
using Stopwatch = System.Diagnostics.Stopwatch;

// ===================== VERY VCV SNAPSHOT LOADER =====================
// Responsabile del caricamento della patch_snapshot lato Unity.
// Supporta fetch HTTP dal Bridge e fallback opzionale su TextAsset locale.
// Dopo parse e mapping, passa il PatchDescriptor allo spawner runtime.

public class VeryVCVSnapshotLoader : MonoBehaviour
{
    // ===================== SCENE REFERENCES =====================
    [Header("Scene References")]
    [SerializeField] private GenericModuleSpawner spawner;
    [SerializeField] private VeryVCVOscSender oscSender;
    [SerializeField] private TextAsset localSnapshotJson;

// ===================== SNAPSHOT SOURCE CONFIG =====================
    // ===== Source selection =====
    private static readonly bool AutoLoadOnStart = true;
    private static readonly bool PreferHttpSnapshot = true;
    private static readonly bool AllowLocalFallback = true;

    //http://192.168.1.47:5510/snapshot casa
    //http://192.168.1.136:5510/snapshot avellino
    private static readonly string SnapshotUrl = "http://10.69.123.232:5510/snapshot"; //IP computer con VCV

    private static readonly float StartupDelay = 1.0f;

    // ===== Runtime state =====
    private string lastPatchSessionId = string.Empty;
    private long lastRevision = -1;
    private bool isLoadingSnapshot = false;

    //per listner OSC, per verificare la sessione corrente, e non applicare snapshot/param_update obsoleti
    public string CurrentPatchSessionId => lastPatchSessionId;
    public long CurrentRevision => lastRevision;

    private static double NowMs()
    {
        return Stopwatch.GetTimestamp() * 1000.0 / Stopwatch.Frequency;
    }

    private void Awake()
    {
        if (oscSender == null)
        {
            oscSender = FindFirstObjectByType<VeryVCVOscSender>();
        }
    }

    private IEnumerator Start()
    {
        if (!AutoLoadOnStart)
            yield break;

        yield return new WaitForSeconds(StartupDelay);

        if (PreferHttpSnapshot)
        {
            yield return FetchSnapshotCoroutine();
        }
        else if (AllowLocalFallback)
        {
            LoadSnapshotFromTextAsset(localSnapshotJson, ignoreRevisionCheck: true);
        }
    }

    [ContextMenu("Fetch Snapshot From Bridge")]
    private void FetchSnapshotFromBridgeContextMenu()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("VeryVCVSnapshotLoader: entra prima in Play Mode.");
            return;
        }

        StartCoroutine(FetchSnapshotCoroutine());
    }

    [ContextMenu("Load Local Snapshot")]
    private void LoadLocalSnapshotContextMenu()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("VeryVCVSnapshotLoader: entra prima in Play Mode.");
            return;
        }

        LoadSnapshotFromTextAsset(localSnapshotJson, ignoreRevisionCheck: true);
    }

    public void FetchSnapshotFromBridge()
    {
        FetchSnapshotFromBridge(expectedRevision: -1);
    }

    public void FetchSnapshotFromBridge(int expectedRevision)
    {
        if (!isActiveAndEnabled)
        {
            Debug.LogWarning("VeryVCVSnapshotLoader: component non attivo.");
            return;
        }

        StartCoroutine(FetchSnapshotCoroutine(expectedRevision));
    }

    public void LoadLocalSnapshot()
    {
        LoadSnapshotFromTextAsset(localSnapshotJson, ignoreRevisionCheck: true);
    }

// Scarica la snapshot corrente dal Bridge via HTTP e, se valida e nuova,
// la applica allo spawner dopo parse e mapping.
/*
    private IEnumerator FetchSnapshotCoroutine()
    {
        if (isLoadingSnapshot)
            yield break;

        if (spawner == null)
        {
            Debug.LogError("VeryVCVSnapshotLoader: spawner non assegnato.");
            yield break;
        }

        isLoadingSnapshot = true;

        using (UnityWebRequest req = UnityWebRequest.Get(SnapshotUrl))
        {
            yield return req.SendWebRequest();

#if UNITY_2020_1_OR_NEWER
            bool failed = req.result != UnityWebRequest.Result.Success;
#else
            bool failed = req.isNetworkError || req.isHttpError;
#endif

            if (failed)
            {
                Debug.LogError($"VeryVCVSnapshotLoader: HTTP fetch failed: {req.error}");

                if (AllowLocalFallback && localSnapshotJson != null)
                {
                    Debug.Log("VeryVCVSnapshotLoader: provo fallback su localSnapshotJson.");
                    LoadSnapshotFromTextAsset(localSnapshotJson, ignoreRevisionCheck: true);
                }

                isLoadingSnapshot = false;
                yield break;
            }

            string json = req.downloadHandler.text;
            TryApplySnapshotPayload(json, ignoreRevisionCheck: false);
        }

        isLoadingSnapshot = false;
    }
*/
    private IEnumerator FetchSnapshotCoroutine(int expectedRevision = -1)
    {
        if (isLoadingSnapshot)
            yield break;

        if (spawner == null)
        {
            Debug.LogError("VeryVCVSnapshotLoader: spawner non assegnato.");
            yield break;
        }

        bool shouldReportProfile = expectedRevision >= 0;

        isLoadingSnapshot = true;

        double t0 = NowMs();
        float httpMs = 0f;
        float parseMs = 0f;
        float instantiateMs = 0f;
        float clientTotalMs = 0f;
        int status = 0;

        using (UnityWebRequest req = UnityWebRequest.Get(SnapshotUrl))
        {
            yield return req.SendWebRequest();

            double t1 = NowMs();
            httpMs = (float)(t1 - t0);

    #if UNITY_2020_1_OR_NEWER
            bool failed = req.result != UnityWebRequest.Result.Success;
    #else
            bool failed = req.isNetworkError || req.isHttpError;
    #endif

            if (failed)
            {
                Debug.LogError($"VeryVCVSnapshotLoader: HTTP fetch failed: {req.error}");

                clientTotalMs = (float)(NowMs() - t0);

                if (shouldReportProfile)
                {
                    SendSnapshotProfileSafe(
                        expectedRevision,
                        status: 0,
                        httpMs,
                        parseMs,
                        instantiateMs,
                        clientTotalMs
                    );
                }
                else if (AllowLocalFallback && localSnapshotJson != null)
                {
                    Debug.Log("VeryVCVSnapshotLoader: provo fallback su localSnapshotJson.");
                    LoadSnapshotFromTextAsset(localSnapshotJson, ignoreRevisionCheck: true);
                }

                isLoadingSnapshot = false;
                yield break;
            }

            string json = req.downloadHandler.text;

            long appliedRevision = -1;
            bool applied = TryApplySnapshotPayloadProfiled(
                json,
                ignoreRevisionCheck: false,
                out appliedRevision,
                out parseMs,
                out instantiateMs
            );

            status = applied ? 1 : 0;

            if (shouldReportProfile && appliedRevision >= 0 && appliedRevision != expectedRevision)
            {
                Debug.LogWarning(
                    $"VeryVCVSnapshotLoader: expected revision {expectedRevision}, " +
                    $"but received/applied revision {appliedRevision}."
                );
            }

            clientTotalMs = (float)(NowMs() - t0);

            if (shouldReportProfile)
            {
                SendSnapshotProfileSafe(
                    expectedRevision,
                    status,
                    httpMs,
                    parseMs,
                    instantiateMs,
                    clientTotalMs
                );
            }
        }

        isLoadingSnapshot = false;
    }

    private void SendSnapshotProfileSafe(
        int revision,
        int status,
        float httpMs,
        float parseMs,
        float instantiateMs,
        float clientTotalMs)
    {
        if (oscSender == null)
        {
            Debug.LogWarning("VeryVCVSnapshotLoader: oscSender non assegnato, profilo snapshot non inviato.");
            return;
        }

        oscSender.SendSnapshotProfile(
            revision,
            status,
            httpMs,
            parseMs,
            instantiateMs,
            clientTotalMs
        );
    }
    
// Fallback locale utile per debug o test offline senza Bridge attivo.
    private void LoadSnapshotFromTextAsset(TextAsset textAsset, bool ignoreRevisionCheck)
    {
        if (spawner == null)
        {
            Debug.LogError("VeryVCVSnapshotLoader: spawner non assegnato.");
            return;
        }

        if (textAsset == null)
        {
            Debug.LogWarning("VeryVCVSnapshotLoader: localSnapshotJson non assegnato.");
            return;
        }

        if (string.IsNullOrWhiteSpace(textAsset.text))
        {
            Debug.LogWarning("VeryVCVSnapshotLoader: localSnapshotJson vuoto.");
            return;
        }

        TryApplySnapshotPayload(textAsset.text, ignoreRevisionCheck);
    }

    private bool TryApplySnapshotPayloadProfiled( //mod for IS2 testing
        string json,
        bool ignoreRevisionCheck,
        out long appliedRevision,
        out float parseMs,
        out float instantiateMs)
    {
        appliedRevision = -1;
        parseMs = 0f;
        instantiateMs = 0f;

        if (string.IsNullOrWhiteSpace(json))
        {
            Debug.LogError("VeryVCVSnapshotLoader: JSON snapshot vuoto.");
            return false;
        }

        double tParse0 = NowMs();

        PatchSnapshotDto snapshot = JsonUtility.FromJson<PatchSnapshotDto>(json);
        if (snapshot == null)
        {
            parseMs = (float)(NowMs() - tParse0);
            Debug.LogError("VeryVCVSnapshotLoader: deserializzazione snapshot fallita.");
            return false;
        }

        appliedRevision = snapshot.revision;

        if (!ignoreRevisionCheck)
        {
            if (snapshot.patchSessionId == lastPatchSessionId && snapshot.revision == lastRevision)
            {
                parseMs = (float)(NowMs() - tParse0);
                Debug.Log($"VeryVCVSnapshotLoader: snapshot invariata ({snapshot.patchSessionId}, rev {snapshot.revision}).");
                return false;
            }
        }

        PatchDescriptor mappedPatch = PatchSnapshotMapper.MapToPatchDescriptor(snapshot);
        if (mappedPatch == null)
        {
            parseMs = (float)(NowMs() - tParse0);
            Debug.LogError("VeryVCVSnapshotLoader: mapping snapshot -> patch fallito.");
            return false;
        }

        double tParse1 = NowMs();
        parseMs = (float)(tParse1 - tParse0);

        spawner.LoadPatch(mappedPatch);

        double tInstantiate1 = NowMs();
        instantiateMs = (float)(tInstantiate1 - tParse1);

        lastPatchSessionId = snapshot.patchSessionId;
        lastRevision = snapshot.revision;

        Debug.Log($"VeryVCVSnapshotLoader: patch caricata. session={lastPatchSessionId}, rev={lastRevision}");
        return true;
    }

    private bool TryApplySnapshotPayload(string json, bool ignoreRevisionCheck)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            Debug.LogError("VeryVCVSnapshotLoader: JSON snapshot vuoto.");
            return false;
        }

        PatchSnapshotDto snapshot = JsonUtility.FromJson<PatchSnapshotDto>(json);
        if (snapshot == null)
        {
            Debug.LogError("VeryVCVSnapshotLoader: deserializzazione snapshot fallita.");
            return false;
        }

        if (!ignoreRevisionCheck)
        {
            if (snapshot.patchSessionId == lastPatchSessionId && snapshot.revision == lastRevision)
            {
                Debug.Log($"VeryVCVSnapshotLoader: snapshot invariata ({snapshot.patchSessionId}, rev {snapshot.revision}).");
                return false;
            }
        }

        PatchDescriptor mappedPatch = PatchSnapshotMapper.MapToPatchDescriptor(snapshot);
        if (mappedPatch == null)
        {
            Debug.LogError("VeryVCVSnapshotLoader: mapping snapshot -> patch fallito.");
            return false;
        }

        spawner.LoadPatch(mappedPatch);

        lastPatchSessionId = snapshot.patchSessionId;
        lastRevision = snapshot.revision;

        Debug.Log($"VeryVCVSnapshotLoader: patch caricata. session={lastPatchSessionId}, rev={lastRevision}");
        return true;
    }
}