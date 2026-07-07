using UnityEngine;

// ===================== PATCH SNAPSHOT MAPPER =====================
// Traduce la patch_snapshot ricevuta dal Bridge in descriptor runtime usati da Unity.
// Qui avviene il passaggio da coordinate e ruoli VCV a una rappresentazione pronta per lo spawner.

public static class PatchSnapshotMapper
{
    // ===================== COORDINATE CONVERSION =====================
    // Fattore di conversione da pixel Rack a unità Unity locali.
    // Mantiene coerenti proporzioni e densità del pannello, senza cercare una scala 1:1 fisica.
    public const float RackPixelToUnity = 0.002f;

// ===================== ROOT MAPPING =====================
    public static PatchDescriptor MapToPatchDescriptor(PatchSnapshotDto snapshot)
    {
        PatchDescriptor patch = new PatchDescriptor();

        if (snapshot == null)
            return patch;

        patch.patchId = snapshot.patchSessionId ?? "unknown_patch";

        if (snapshot.modules == null)
            return patch;

        foreach (ModuleDto moduleDto in snapshot.modules)
        {
            ModuleDescriptor module = MapModule(moduleDto, snapshot.patchBounds);
            patch.modules.Add(module);
        }

        return patch;
    }

// ===================== MODULE MAPPING =====================
    private static ModuleDescriptor MapModule(ModuleDto dto, RectDto patchBounds)
    {
        ModuleDescriptor module = new ModuleDescriptor();

        module.moduleId = dto.moduleInstanceId.ToString();
        module.pluginSlug = dto.pluginSlug;
        module.modelSlug = dto.modelSlug;
        module.moduleName = dto.modelName;

        Vector2 panelSize = MapPanelSize(dto.moduleBox);
        module.panelSize = panelSize;

        //mappa amoduleBox.pos in world/local patch layout.
        module.patchLocalPosition = MapModulePatchPosition(dto.moduleBox, patchBounds);

        if (dto.@params != null)
        {
            foreach (ParamDto paramDto in dto.@params)
            {
                ControlDescriptor control = MapParam(paramDto, panelSize);
                module.controls.Add(control);
            }
        }

        if (dto.ports != null)
        {
            foreach (PortDto portDto in dto.ports)
            {
                ControlDescriptor control = MapPort(portDto, panelSize);
                module.controls.Add(control);
            }
        }

        return module;
    }

// ===================== CONTROL MAPPING =====================
    private static ControlDescriptor MapParam(ParamDto dto, Vector2 panelSize)
    {
        ControlDescriptor control = new ControlDescriptor();

        control.controlId = $"param_{dto.paramId}";
        control.paramId = dto.paramId; // assegno identificatore del parametro per comunicazione OSC e mappature
        control.label = dto.label;
        control.role = MapParamRole(dto.role);
        control.behavior = string.IsNullOrWhiteSpace(dto.behavior) ? "" : dto.behavior;
        control.localPosition = MapLocalPosition(dto.paramBox, panelSize);
        control.localRotation = Vector3.zero;
        control.localScale = Vector3.one;
        control.normalizedValue = dto.normalizedValue;
        control.isVisible = true;
        control.sourceBoxSize = MapLocalSize(dto.paramBox);

        return control;
    }

    private static ControlDescriptor MapPort(PortDto dto, Vector2 panelSize)
    {
        ControlDescriptor control = new ControlDescriptor();

        control.controlId = $"port_{dto.portId}";
        control.label = dto.label;
        control.role = MapPortRole(dto.direction);
        control.localPosition = MapLocalPosition(dto.portBox, panelSize);
        control.localRotation = Vector3.zero;
        control.localScale = Vector3.one;
        control.normalizedValue = 0f;
        control.isVisible = true;
        control.sourceBoxSize = MapLocalSize(dto.portBox);

        return control;
    }

// ===================== ROLE MAPPING =====================
    private static ControlRole MapParamRole(string role)
    {
        switch (role)
        {
            case "knob":
                return ControlRole.Knob;
            case "button":
                return ControlRole.Button;
            case "slider":
                return ControlRole.Slider;
            case "switch":
                return ControlRole.Button; // per ora mappiamo gli switch come button, da migliorare
            default:
                return ControlRole.Label; // se il ruolo non è riconosciuto, lo trattiamo come un label (visivamente sarà un testo, senza interazione)
                                          // TODO: valutare se esporre un ruolo "Unknown" specifico, in modo da poter gestire meglio i casi non mappati 
                                          // es: un prefab che mostra un punto interrogativo o simile
        }
    }

    private static ControlRole MapPortRole(string direction)
    {
        switch (direction)
        {
            case "input":
                return ControlRole.JackIn;
            case "output":
                return ControlRole.JackOut;
            default:
                return ControlRole.JackIn;
        }
    }

// ===================== GEOMETRY MAPPING =====================
    private static Vector2 MapPanelSize(RectDto moduleBox)
    {
        if (moduleBox == null || moduleBox.size == null)
            return new Vector2(0.25f, 0.4f);

        return new Vector2(
            moduleBox.size.x * RackPixelToUnity,
            moduleBox.size.y * RackPixelToUnity
        );
    }

// Converte il box locale del controllo in una posizione locale sul pannello del modulo
    private static Vector3 MapLocalPosition(RectDto box, Vector2 panelSize)
    {
        if (box == null || box.pos == null || box.size == null)
            return Vector3.zero;

        float panelWidth = panelSize.x;
        float panelHeight = panelSize.y;

        // Centro del rect in coordinate Rack
        float centerX = box.pos.x + box.size.x * 0.5f;
        float centerY = box.pos.y + box.size.y * 0.5f;

        // Rack:
        // origin top-left, y verso il basso
        // Unity:
        // origine centro pannello, y verso l’alto
        float x = (centerX * RackPixelToUnity) - (panelWidth * 0.5f);
        float y = (panelHeight * 0.5f) - (centerY * RackPixelToUnity);

        return new Vector3(x, y, -0.015f);
    }

// Converte la posizione assoluta del modulo in VCV in una posizione locale relativa al root della patch
    private static Vector3 MapModulePatchPosition(RectDto moduleBox, RectDto patchBounds)
    {
        if (moduleBox == null || moduleBox.pos == null || moduleBox.size == null)
            return Vector3.zero;

        if (patchBounds == null || patchBounds.pos == null || patchBounds.size == null)
            return Vector3.zero;

        // Coordinate del modulo relative all'origine della patch
        float localLeft = moduleBox.pos.x - patchBounds.pos.x;
        float localTop = moduleBox.pos.y - patchBounds.pos.y;

        // Centro del modulo nella patch Rack
        float centerX = localLeft + moduleBox.size.x * 0.5f;
        float centerY = localTop + moduleBox.size.y * 0.5f;

        // Size della patch in Unity
        float patchWidth = patchBounds.size.x * RackPixelToUnity;
        float patchHeight = patchBounds.size.y * RackPixelToUnity;

        // Rack: origine top-left, y verso il basso
        // Unity patch local: origine al centro, y verso l’alto
        float x = (centerX * RackPixelToUnity) - (patchWidth * 0.5f);
        float y = (patchHeight * 0.5f) - (centerY * RackPixelToUnity);

        return new Vector3(x, y, 0f);
    }

// Converte la size del box originale in una size locale in unità Unity.
    private static Vector2 MapLocalSize(RectDto box)
    {
        if (box == null || box.size == null)
            return Vector2.zero;

        return new Vector2(
            box.size.x * RackPixelToUnity,
            box.size.y * RackPixelToUnity
        );
    }
}


