using UnityEngine;
using System;
using System.Net;
using System.Net.Sockets;
using System.Text;

// ===================== VERY VCV OSC RUNTIME LISTENER =====================
// Listener OSC UDP minimale lato Unity.
// Riceve notifiche runtime dal Bridge:
// - disponibilità di una nuova patch_snapshot
// - param_update per controlli già presenti nella scena

public class VeryVCVOscListener : MonoBehaviour
{
    // ===================== SCENE REFERENCES =====================
    [Header("Scene References")]
    [SerializeField] private VeryVCVSnapshotLoader snapshotLoader;
    [SerializeField] private GenericModuleSpawner spawner;

// ===================== OSC PROTOCOL =====================
    // ===== OSC UDP settings =====
    private static readonly int ListenPort = 5511;
    private static readonly string SnapshotAvailableAddress = "/veryvcv/snapshot/available";
    private static readonly string SnapshotAvailableTypeTag = ",si";

    private static readonly string ParamUpdateAddress = "/veryvcv/param/update";
    private static readonly string ParamUpdateTypeTag = ",sisif";

// ===================== RUNTIME STATE =====================
    private UdpClient udpClient;
    private IPEndPoint anyEndPoint;

// ===================== LIFECYCLE =====================
    private void Start()
    {
        Application.runInBackground = true; // Consente al listener di continuare a ricevere OSC anche senza focus.
        try
        {
            anyEndPoint = new IPEndPoint(IPAddress.Any, ListenPort);
            udpClient = new UdpClient(ListenPort);
            udpClient.Client.Blocking = false;

            Debug.Log($"VeryVCVOscListener: listening on UDP {ListenPort}");
        }
        catch (Exception ex)
        {
            Debug.LogError($"VeryVCVOscListener: failed to bind UDP port {ListenPort}: {ex.Message}");
        }
    }

    private void Update()
    {
        if (udpClient == null)
            return;

        try
        {
            while (udpClient.Available > 0)
            {
                byte[] packet = udpClient.Receive(ref anyEndPoint);

                if (TryParseSnapshotAvailable(packet, out string patchSessionId, out int revision)) //TODO int revision è attualmente inutillizata
                {
                    HandleSnapshotAvailable(patchSessionId, revision);
                    continue;
                }

                if (TryParseParamUpdate(packet, out string updateSessionId, out int updateRevision, out string moduleId, out int paramId, out float normalizedValue))
                {
                    HandleParamUpdate(updateSessionId, updateRevision, moduleId, paramId, normalizedValue);
                    continue;
                }
            }
        }
        catch (SocketException ex)
        {
            // Non-blocking socket: ignora il caso "nessun dato"
            if (ex.SocketErrorCode != SocketError.WouldBlock)
            {
                Debug.LogWarning($"VeryVCVOscListener: socket exception: {ex.Message}");
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"VeryVCVOscListener: unexpected exception: {ex.Message}");
        }
    }

    private void OnDestroy()
    {
        if (udpClient != null)
        {
            udpClient.Close();
            udpClient = null;
        }
    }

// ===================== MESSAGE PARSING =====================
// Parsing del messaggio OSC:
// /veryvcv/snapshot/available <string patchSessionId> <int revision>
    private static bool TryParseSnapshotAvailable(byte[] data, out string patchSessionId, out int revision)
    {
        patchSessionId = string.Empty;
        revision = -1;

        try
        {
            int offset = 0;

            string address = ReadOscString(data, ref offset);
            if (address != SnapshotAvailableAddress)
                return false;

            string typeTag = ReadOscString(data, ref offset);
            if (typeTag != SnapshotAvailableTypeTag)
                return false;

            patchSessionId = ReadOscString(data, ref offset);
            revision = ReadOscInt32(data, ref offset);

            return true;
        }
        catch
        {
            return false;
        }
    }

// ===================== OSC BINARY HELPERS =====================
    private static string ReadOscString(byte[] data, ref int offset)
    {
        if (offset >= data.Length)
            throw new IndexOutOfRangeException("OSC string offset out of range");

        int start = offset;
        while (offset < data.Length && data[offset] != 0)
        {
            offset++;
        }

        if (offset >= data.Length)
            throw new Exception("OSC string terminator not found");

        string result = Encoding.UTF8.GetString(data, start, offset - start);

        // salta il terminatore '\0'
        offset++;

        // padding a 4 byte
        while (offset % 4 != 0)
        {
            offset++;
        }

        return result;
    }

    private static int ReadOscInt32(byte[] data, ref int offset)
    {
        if (offset + 4 > data.Length)
            throw new IndexOutOfRangeException("OSC int32 out of range");

        int value =
            (data[offset] << 24) |
            (data[offset + 1] << 16) |
            (data[offset + 2] << 8) |
            (data[offset + 3]);

        offset += 4;
        return value;
    }
    
    //metodi per parser di param_update
    // Parsing del messaggio OSC:
    // /veryvcv/param/update <string patchSessionId> <int revision> <string moduleId> <int paramId> <float normalizedValue>
    private static bool TryParseParamUpdate(byte[] data,
        out string patchSessionId,
        out int revision,
        out string moduleId,
        out int paramId,
        out float normalizedValue)
    {
        patchSessionId = string.Empty;
        revision = -1;
        moduleId = string.Empty;
        paramId = -1;
        normalizedValue = 0f;

        try
        {
            int offset = 0;

            string address = ReadOscString(data, ref offset);
            if (address != ParamUpdateAddress)
                return false;

            string typeTag = ReadOscString(data, ref offset);
            if (typeTag != ParamUpdateTypeTag)
                return false;

            patchSessionId = ReadOscString(data, ref offset);
            revision = ReadOscInt32(data, ref offset);
            moduleId = ReadOscString(data, ref offset);
            paramId = ReadOscInt32(data, ref offset);
            normalizedValue = ReadOscFloat32(data, ref offset);

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static float ReadOscFloat32(byte[] data, ref int offset)
    {
        if (offset + 4 > data.Length)
            throw new IndexOutOfRangeException("OSC float32 out of range");

        uint raw =
            ((uint)data[offset] << 24) |
            ((uint)data[offset + 1] << 16) |
            ((uint)data[offset + 2] << 8) |
            ((uint)data[offset + 3]);

        offset += 4;

    #if NETSTANDARD2_1 || NET_5_0_OR_GREATER
        return BitConverter.Int32BitsToSingle((int)raw);
    #else
        byte[] bytes = BitConverter.GetBytes(raw);
        return BitConverter.ToSingle(bytes, 0);
    #endif
}

    private void HandleSnapshotAvailable(string patchSessionId, int revision)
    {
        Debug.Log($"VeryVCVOscListener: snapshot available session={patchSessionId}, rev={revision}");

        if (snapshotLoader != null)
        {
            // Passiamo la revision al loader così il profiling Quest può essere
            // associato alla snapshot corretta nel CSV lato Bridge.
            snapshotLoader.FetchSnapshotFromBridge(revision);
        }
        else
        {
            Debug.LogWarning("VeryVCVOscListener: snapshotLoader non assegnato.");
        }
    }

    private void HandleParamUpdate(string patchSessionId, int revision, string moduleId, int paramId, float normalizedValue)
    {
        if (snapshotLoader != null && !string.IsNullOrEmpty(snapshotLoader.CurrentPatchSessionId))
        {
            if (patchSessionId != snapshotLoader.CurrentPatchSessionId)
            {
                Debug.Log($"VeryVCVOscListener: ignored param_update for old session {patchSessionId}");
                return;
            }
        }

        Debug.Log($"VeryVCVOscListener: param update module={moduleId} param={paramId} value={normalizedValue}");

        if (spawner != null)
        {
            spawner.ApplyParamUpdate(moduleId, paramId, normalizedValue);
        }
        else
        {
            Debug.LogWarning("VeryVCVOscListener: spawner non assegnato.");
        }
    }
}