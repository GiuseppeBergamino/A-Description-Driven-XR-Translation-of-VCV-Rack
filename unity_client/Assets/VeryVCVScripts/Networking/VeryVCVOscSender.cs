using UnityEngine;
using System;
using System.Net;
using System.Net.Sockets;
using System.Text;

// ===================== VERY VCV OSC PARAM SENDER =====================
// Sender OSC UDP minimale lato Unity.
// Invia param_set al Bridge in esecuzione su Mac.

public class VeryVCVOscSender : MonoBehaviour
{
    [Header("Target Bridge")]
    // IP mac casa: 192.168.1.47
    // IP mac avellino: 192.168.1.136
    [SerializeField] private string targetIp = "10.69.123.232"; //Ip computer con VCV
    [SerializeField] private int targetPort = 5512;

    private static readonly string ParamSetAddress = "/veryvcv/param/set";
    private static readonly string ParamSetTypeTag = ",ssif";
    // for IS2 testing
    private static readonly string SnapshotProfileAddress = "/veryvcv/profile/snapshot";
    private static readonly string SnapshotProfileTypeTag = ",iiffff";

    public void SendParamSet(string patchSessionId, string moduleId, int paramId, float normalizedValue)
    {
        if (string.IsNullOrWhiteSpace(patchSessionId))
        {
            Debug.LogWarning("VeryVCVOscSender: patchSessionId vuota.");
            return;
        }

        if (string.IsNullOrWhiteSpace(moduleId))
        {
            Debug.LogWarning("VeryVCVOscParamSender: moduleId vuota.");
            return;
        }

        normalizedValue = Mathf.Clamp01(normalizedValue);

        try
        {
            using (UdpClient udp = new UdpClient())
            {
                byte[] packet = BuildParamSetPacket(
                    patchSessionId,
                    moduleId,
                    paramId,
                    normalizedValue
                );

                udp.Send(packet, packet.Length, targetIp, targetPort);
                Debug.Log($"VeryVCVOscSender: param_set session={patchSessionId} module={moduleId} param={paramId} value={normalizedValue}");
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"VeryVCVOscSender: send failed: {ex.Message}");
        }
    }

    public void SendSnapshotProfile( //for IS2 testing
        int revision,
        int status,
        float httpMs,
        float parseMs,
        float instantiateMs,
        float clientTotalMs)
    {
        try
        {
            using (UdpClient udp = new UdpClient())
            {
                byte[] packet = BuildSnapshotProfilePacket(
                    revision,
                    status,
                    httpMs,
                    parseMs,
                    instantiateMs,
                    clientTotalMs
                );

                udp.Send(packet, packet.Length, targetIp, targetPort);

                Debug.Log(
                    $"VeryVCVOscSender: snapshot_profile rev={revision} " +
                    $"status={status} httpMs={httpMs:F3} parseMs={parseMs:F3} " +
                    $"instantiateMs={instantiateMs:F3} clientTotalMs={clientTotalMs:F3}"
                );
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"VeryVCVOscSender: snapshot profile send failed: {ex.Message}");
        }
    }
    private static byte[] BuildParamSetPacket(string patchSessionId, string moduleId, int paramId, float normalizedValue)
    {
        using (var stream = new System.IO.MemoryStream())
        {
            AppendOscString(stream, ParamSetAddress);
            AppendOscString(stream, ParamSetTypeTag);
            AppendOscString(stream, patchSessionId);
            AppendOscString(stream, moduleId);
            AppendOscInt32(stream, paramId);
            AppendOscFloat32(stream, normalizedValue);

            return stream.ToArray();
        }
    }

    private static byte[] BuildSnapshotProfilePacket( // for IS2 testing
    int revision,
    int status,
    float httpMs,
    float parseMs,
    float instantiateMs,
    float clientTotalMs)
    {
        using (var stream = new System.IO.MemoryStream())
        {
            AppendOscString(stream, SnapshotProfileAddress);
            AppendOscString(stream, SnapshotProfileTypeTag);

            AppendOscInt32(stream, revision);
            AppendOscInt32(stream, status);
            AppendOscFloat32(stream, httpMs);
            AppendOscFloat32(stream, parseMs);
            AppendOscFloat32(stream, instantiateMs);
            AppendOscFloat32(stream, clientTotalMs);

            return stream.ToArray();
        }
    }

    private static void AppendOscString(System.IO.MemoryStream stream, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
        stream.Write(bytes, 0, bytes.Length);
        stream.WriteByte(0);

        while (stream.Length % 4 != 0)
        {
            stream.WriteByte(0);
        }
    }

    private static void AppendOscInt32(System.IO.MemoryStream stream, int value)
    {
        byte[] bytes = BitConverter.GetBytes(IPAddress.HostToNetworkOrder(value));
        stream.Write(bytes, 0, bytes.Length);
    }

    private static void AppendOscFloat32(System.IO.MemoryStream stream, float value)
    {
        byte[] bytes = BitConverter.GetBytes(value);

        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(bytes);
        }

        stream.Write(bytes, 0, bytes.Length);
    }
}