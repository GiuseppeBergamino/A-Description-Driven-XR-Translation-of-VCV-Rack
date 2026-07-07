//  Created by Giuseppe Bergamino on 14/04/26.

// gestione dati vcv -> JSON

#include "VeryVCVBridgeData.hpp"
#include <algorithm>
#include <cstdlib>

// ===================== INTERNAL HELPERS =====================
namespace {
rack::math::Rect computePatchBounds(const std::vector<BridgeModuleInfo>& modules) {
    if (modules.empty())
        return rack::math::Rect(rack::math::Vec(0.f, 0.f), rack::math::Vec(0.f, 0.f));

    float minX = modules.front().box.pos.x;
    float minY = modules.front().box.pos.y;
    float maxX = modules.front().box.pos.x + modules.front().box.size.x;
    float maxY = modules.front().box.pos.y + modules.front().box.size.y;

    for (const BridgeModuleInfo& m : modules) {
        minX = std::min(minX, m.box.pos.x);
        minY = std::min(minY, m.box.pos.y);
        maxX = std::max(maxX, m.box.pos.x + m.box.size.x);
        maxY = std::max(maxY, m.box.pos.y + m.box.size.y);
    }

    return rack::math::Rect(rack::math::Vec(minX, minY), rack::math::Vec(maxX - minX, maxY - minY));
  }
}

// ===================== BASIC GEOMETRY =====================
//------------GEOMETRIA e POS
json_t* rectToJson(const rack::math::Rect& r) {
    json_t* rectJ = json_object();

    json_t* posJ = json_object();
    json_object_set_new(posJ, "x", json_real(r.pos.x));
    json_object_set_new(posJ, "y", json_real(r.pos.y));

    json_t* sizeJ = json_object();
    json_object_set_new(sizeJ, "x", json_real(r.size.x));
    json_object_set_new(sizeJ, "y", json_real(r.size.y));

    json_object_set_new(rectJ, "pos", posJ);
    json_object_set_new(rectJ, "size", sizeJ);

    return rectJ;
}

// ===================== CONTROL SERIALIZATION =====================
//-----------------PARAMETRI
json_t* bridgeParamToJson(const BridgeParamInfo& p) {
    json_t* pJ = json_object();

    json_object_set_new(pJ, "paramId", json_integer(p.paramId));
    json_object_set_new(pJ, "label", json_string(p.label.c_str()));
    json_object_set_new(pJ, "role", json_string(p.role.c_str()));
    json_object_set_new(pJ, "behavior", json_string(p.behavior.c_str()));
    json_object_set_new(pJ, "paramBox", rectToJson(p.box));

    json_object_set_new(pJ, "minValue", json_real(p.minValue));
    json_object_set_new(pJ, "maxValue", json_real(p.maxValue));
    json_object_set_new(pJ, "defaultValue", json_real(p.defaultValue));
    json_object_set_new(pJ, "value", json_real(p.value));
    json_object_set_new(pJ, "normalizedValue", json_real(p.normalizedValue));
    json_object_set_new(pJ, "unit", json_string(p.unit.c_str()));
    json_object_set_new(pJ, "displayValueString", json_string(p.displayValueString.c_str()));

    return pJ;
}

// ===================== CONNECTION SERIALIZATION =====================
//-----------------JACK IN e OUT
json_t* bridgePortToJson(const BridgePortInfo& p) {
    json_t* pJ = json_object();

    json_object_set_new(pJ, "portId", json_integer(p.portId));
    json_object_set_new(pJ, "label", json_string(p.label.c_str()));
    json_object_set_new(pJ, "direction", json_string(p.direction.c_str()));
    json_object_set_new(pJ, "portBox", rectToJson(p.box));
    json_object_set_new(pJ, "signalKind", json_string(p.signalKind.c_str()));

    return pJ;
}

json_t* bridgeConnectionToJson(const BridgeConnectionInfo& c) {
    json_t* cJ = json_object();

    json_object_set_new(cJ, "fromModuleInstanceId", json_integer(c.fromModuleInstanceId));
    json_object_set_new(cJ, "fromPortId", json_integer(c.fromPortId));
    json_object_set_new(cJ, "toModuleInstanceId", json_integer(c.toModuleInstanceId));
    json_object_set_new(cJ, "toPortId", json_integer(c.toPortId));

    return cJ;
}

// ===================== LIGHT SERIALIZATION =====================
//-----------------LIGHTS
json_t* bridgeLightToJson(const BridgeLightInfo& l) {
    json_t* lJ = json_object();

    json_object_set_new(lJ, "lightId", json_integer(l.lightId));
    json_object_set_new(lJ, "label", json_string(l.label.c_str()));
    json_object_set_new(lJ, "lightBox", rectToJson(l.box));
    json_object_set_new(lJ, "value", json_real(l.value));

    return lJ;
}

// ===================== MODULE SERIALIZATION =====================
//-----------------MODULE GENERAL
json_t* bridgeModuleToJson(const BridgeModuleInfo& m) {
    json_t* mJ = json_object();

    json_object_set_new(mJ, "moduleInstanceId", json_integer(m.moduleInstanceId));
    json_object_set_new(mJ, "pluginSlug", json_string(m.pluginSlug.c_str()));
    json_object_set_new(mJ, "modelSlug", json_string(m.modelSlug.c_str()));
    json_object_set_new(mJ, "modelName", json_string(m.modelName.c_str()));
    json_object_set_new(mJ, "isMRAware", json_boolean(m.isMRAware));
    json_object_set_new(mJ, "moduleBox", rectToJson(m.box));

    json_t* paramsJ = json_array();
    for (const BridgeParamInfo& p : m.params) {
        json_array_append_new(paramsJ, bridgeParamToJson(p));
    }
    json_object_set_new(mJ, "params", paramsJ);

    json_t* portsJ = json_array();
    for (const BridgePortInfo& p : m.ports) {
        json_array_append_new(portsJ, bridgePortToJson(p));
    }
    json_object_set_new(mJ, "ports", portsJ);

    json_t* lightsJ = json_array();
    for (const BridgeLightInfo& l : m.lights) {
        json_array_append_new(lightsJ, bridgeLightToJson(l));
    }
    json_object_set_new(mJ, "lights", lightsJ);

    return mJ;
}

// ===================== SNAPSHOT ROOT SERIALIZATION =====================
json_t* snapshotToJson(const std::vector<BridgeModuleInfo>& modules,
                       const BridgeSnapshotMetadata& meta) {
    json_t* rootJ = json_object();

    json_object_set_new(rootJ, "type", json_string("patch_snapshot"));
    json_object_set_new(rootJ, "schemaVersion", json_integer(meta.schemaVersion));
    json_object_set_new(rootJ, "revision", json_integer(meta.revision));
    json_object_set_new(rootJ, "patchSessionId", json_string(meta.patchSessionId.c_str()));

    json_t* layoutJ = json_object();
    json_object_set_new(layoutJ, "coordinateSpace", json_string("vcv_rack_pixels"));
    json_object_set_new(layoutJ, "origin", json_string("top_left"));
    json_object_set_new(rootJ, "layout", layoutJ);

    json_object_set_new(rootJ, "patchBounds", rectToJson(computePatchBounds(modules)));

    json_t* modulesJ = json_array();
    for (const BridgeModuleInfo& m : modules) {
        json_array_append_new(modulesJ, bridgeModuleToJson(m));
    }
    json_object_set_new(rootJ, "modules", modulesJ);

    json_t* connectionsJ = json_array();
    for (const BridgeConnectionInfo& c : meta.connections) {
        json_array_append_new(connectionsJ, bridgeConnectionToJson(c));
    }
    json_object_set_new(rootJ, "connections", connectionsJ);

    return rootJ;
}

// ottengo il JSON serializzato finale, pronto da utilizzare (mostrare, inviare HTTP, salvare)
std::string snapshotToJsonString(const std::vector<BridgeModuleInfo>& modules,
                                 const BridgeSnapshotMetadata& meta) {
    json_t* rootJ = snapshotToJson(modules, meta);

    char* dumped = json_dumps(rootJ, JSON_INDENT(2));
    std::string out = dumped ? dumped : "";

    if (dumped)
        std::free(dumped);

    json_decref(rootJ);
    return out;
}
