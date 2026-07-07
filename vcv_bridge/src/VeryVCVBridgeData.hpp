//  Created by Giuseppe Bergamino on 14/04/26.
//  dati e dichiarazioni comuni

#pragma once

#include "plugin.hpp"
#include "VeryVCVMR.hpp"
#include <cstdint> //per int64_t
#include <map>
#include <string>
#include <vector>

// ===================== DATA STRUCTS =====================
struct BridgeParamInfo {
    int paramId = -1;

    // Dati semantici
    std::string role;      // knob, slider, button, switch...
    std::string label;
    std::string unit;
    std::string behavior;  // continuous, momentary, toggle, stepped...

    // Geometria locale al modulo
    rack::math::Rect box;

    // Valori correnti del parametro
    float value = 0.f;
    float normalizedValue = 0.f;
    float minValue = 0.f;
    float maxValue = 1.f;
    float defaultValue = 0.f;

    // Flags utili per inferenza / debug interno
    bool smoothEnabled = false;
    bool snapEnabled = false;

    // Stringa leggibile per UI/debug e rendering lato Unity
    std::string displayValueString;
};

struct BridgePortInfo {
    int portId = -1;

    std::string label;
    std::string direction;   // input / output
    std::string signalKind;  // cv / gate / audio / unknown
    rack::math::Rect box;
};

//------TODO dati già previsti nel modello, ma non ancora sfruttati
struct BridgeLightInfo {
    int lightId = -1;
    std::string label;
    rack::math::Rect box;
    float value = 0.f;
};

//------TODO, Dati già previsti nel modello, ma non ancora sfruttati
struct BridgeConnectionInfo {
    int64_t fromModuleInstanceId = -1;
    int fromPortId = -1;
    int64_t toModuleInstanceId = -1;
    int toPortId = -1;
};

struct BridgeModuleInfo {
    int64_t moduleInstanceId = -1;

    // Identità del modulo
    std::string pluginSlug;
    std::string modelSlug;
    std::string modelName;
    bool isMRAware = false;

    // Box del modulo nel rack (globale)
    rack::math::Rect box;

    std::vector<BridgeParamInfo> params;
    std::vector<BridgePortInfo> ports;
    std::vector<BridgeLightInfo> lights;
};

//-----per root JSON
struct BridgeSnapshotMetadata {
    int schemaVersion = 1;
    int64_t revision = 0;
    std::string patchSessionId;
    std::vector<BridgeConnectionInfo> connections;
};


// ===================== PARAM UPDATE TRACKING =====================
struct ParamKey {
    int64_t moduleInstanceId = -1;
    int paramId = -1;

    bool operator<(const ParamKey& other) const {
        if (moduleInstanceId != other.moduleInstanceId)
            return moduleInstanceId < other.moduleInstanceId;
        return paramId < other.paramId;
    }
};

// Entry minimale per la propagazione runtime dei cambiamenti parametro
struct ParamUpdateEntry {
    int64_t moduleInstanceId = -1;
    int paramId = -1;
    float normalizedValue = 0.f;
};

// ===================== SCANNING FUNCTIONS =====================
std::string rectToString(const rack::math::Rect& r);
std::string snapshotToText(const std::vector<BridgeModuleInfo>& modules);

std::string inferParamRole(rack::app::ParamWidget* pw);
std::string inferParamBehavior(rack::app::ParamWidget* pw);
bool inferIsMRAware(rack::app::ModuleWidget* mw);
std::string inferPortSignalKind(const std::string& label);

std::vector<BridgeModuleInfo> scanRack(rack::app::ModuleWidget* selfWidget);

// ===================== JSON HELPERS =====================
json_t* rectToJson(const rack::math::Rect& r);
json_t* bridgeParamToJson(const BridgeParamInfo& p);
json_t* bridgePortToJson(const BridgePortInfo& p);
json_t* bridgeLightToJson(const BridgeLightInfo& l);
json_t* bridgeConnectionToJson(const BridgeConnectionInfo& c);
json_t* bridgeModuleToJson(const BridgeModuleInfo& m);
json_t* snapshotToJson(const std::vector<BridgeModuleInfo>& modules,
                       const BridgeSnapshotMetadata& meta);
std::string snapshotToJsonString(const std::vector<BridgeModuleInfo>& modules,
                                 const BridgeSnapshotMetadata& meta);


// ===================== RUNTIME HELPERS =====================
rack::app::ModuleWidget* findModuleWidgetByInstanceId(int64_t moduleInstanceId);

bool readCurrentNormalizedValueForBridge(int64_t moduleInstanceId,
                                         int paramId,
                                         float& outNorm);

std::vector<ParamUpdateEntry> collectParamUpdatesForBridge(
    const std::vector<BridgeModuleInfo>& snapshot,
    std::map<ParamKey, float>& lastSentNormalizedValues,
    bool& snapshotNeedsRefresh
);


