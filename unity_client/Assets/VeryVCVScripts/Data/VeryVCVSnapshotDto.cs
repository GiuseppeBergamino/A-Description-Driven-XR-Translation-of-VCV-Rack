using System;
using System.Collections.Generic;

// ===================== VERY VCV SNAPSHOT DTO =====================
// Data Transfer Object usato per deserializzare il payload JSON patch_snapshot proveniente dal Bridge.
// Rappresentano il formato wire/protocollo, non il modello runtime usato dallo spawner.

// ===================== SNAPSHOT ROOT DTO =====================
[Serializable]
public class PatchSnapshotDto
{
    public string type;
    public int schemaVersion;
    public long revision;
    public string patchSessionId;
    public LayoutDto layout;
    public RectDto patchBounds; // Rettangolo espresso come pos + size nel sistema di coordinate del Bridge.
    public List<ModuleDto> modules;
    public List<ConnectionDto> connections;
}

// ===================== BASIC GEOMETRY DTO =====================
[Serializable]
public class LayoutDto
{
    public string coordinateSpace;
    public string origin;
}

[Serializable]
public class Vec2Dto
{
    public float x;
    public float y;
}

[Serializable]
public class RectDto
{
    public Vec2Dto pos;
    public Vec2Dto size;
}

// ===================== MODULE DTO =====================
[Serializable]
public class ModuleDto
{
    public long moduleInstanceId;
    public string pluginSlug;
    public string modelSlug;
    public string modelName;
    public RectDto moduleBox;
    public bool isMRAware;
    public List<ParamDto> @params; // Il campo JSON si chiama "params"; in C# usiamo @params perché "params" è keyword riservata.
    public List<PortDto> ports;
    public List<LightDto> lights;
}

// ===================== CONTROL DTO =====================
[Serializable]
public class ParamDto
{
    public int paramId;
    public string label;
    public string role;
    public string behavior;
    public RectDto paramBox;
    public float minValue;
    public float maxValue;
    public float defaultValue;
    public float value;
    public float normalizedValue;
    public string unit;
    public string displayValueString;
}

[Serializable]
public class PortDto
{
    public int portId;
    public string label;
    public string direction;
    public RectDto portBox;
    public string signalKind;
}

[Serializable]
public class LightDto
{
    public int lightId;
    public string label;
    public RectDto lightBox;
    public float value;
}

// ===================== CONNECTION DTO =====================
[Serializable]
public class ConnectionDto
{
    public long fromModuleInstanceId;
    public int fromPortId;
    public long toModuleInstanceId;
    public int toPortId;
}