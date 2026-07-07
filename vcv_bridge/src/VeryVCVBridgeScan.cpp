//  Created by Giuseppe Bergamino on 14/04/26.
#include "VeryVCVBridgeData.hpp"
#include <algorithm>
#include <cctype>

using namespace rack;

// ===================== INTERNAL HELPERS =====================
namespace {
float safeNormalize(float value, float minValue, float maxValue) {
    if (maxValue <= minValue)
        return 0.f;

    float norm = (value - minValue) / (maxValue - minValue);
    return clamp(norm, 0.f, 1.f);
    }

    std::string toLowerCopy(std::string s) {
        std::transform(s.begin(), s.end(), s.begin(),
            [](unsigned char c) { return static_cast<char>(std::tolower(c)); });
        return s;
    }
}

// ===================== TEXT HELPERS =====================
// Formattazione sintetica della geometria per il display testuale del Bridge
// Utility di formattazione coordinate per testo UI
std::string rectToString(const math::Rect& r) {
    return string::f("(x=%.1f y=%.1f  w=%.1f h=%.1f)",
        r.pos.x, r.pos.y, r.size.x, r.size.y);
}

// Costruisce un summary testuale della snapshot per il display scrollabile nel modulo Bridge
//-----------------Per testo UI-------------
std::string snapshotToText(const std::vector<BridgeModuleInfo>& modules) {
    std::stringstream ss;
    bool verbose = false; // testo più lungo e informativo nella UI

    ss << "Modules found: " << modules.size() << "\n";

    for (size_t i = 0; i < modules.size(); ++i) {
        const BridgeModuleInfo& m = modules[i];

        ss << i << ") "
           << m.pluginSlug << ": " << m.modelName << "\n";

        if (verbose) {
            ss << "    id: " << m.moduleInstanceId << "\n";
            ss << "    params: " << m.params.size()
               << "  ports: " << m.ports.size() << "\n";

            int shownParams = 0;
            int maxShowedParams = 2;
            for (const BridgeParamInfo& p : m.params) {
                ss << "      P" << p.paramId
                   << "  " << p.label;
                if (!p.unit.empty())
                    ss << "[" << p.unit << "]";
                ss << "  val=" << string::f("%.4f", p.value)
                   << "  norm=" << string::f("%.3f", p.normalizedValue);
                if (!p.displayValueString.empty())
                    ss << "  disp=" << p.displayValueString;
                ss << "  " << rectToString(p.box) << "\n";

                shownParams++;
                if (shownParams >= maxShowedParams) {
                    if ((int)m.params.size() > shownParams) {
                        ss << "      ...\n";
                    }
                    break;
                }
            }

            int shownPorts = 0;
            int maxShowedPorts = 2;
            for (const BridgePortInfo& p : m.ports) {
                ss << "      " << p.direction
                   << " #" << p.portId
                   << rectToString(p.box) << "\n";

                shownPorts++;
                if (shownPorts >= maxShowedPorts) {
                    if ((int)m.ports.size() > shownPorts) {
                        ss << "      ...\n";
                    }
                    break;
                }
            }
        } //parte vrbosa
    }

    return ss.str();
}

// ===================== SEMANTIC INFERENCE =====================
//---Inferisce il ruolo semantico del controllo a partire dal tipo di widget Rack.
std::string inferParamRole(app::ParamWidget* pw) {
    if (!pw)
        return "unknown";

    // Slider
    if (dynamic_cast<app::SvgSlider*>(pw) || dynamic_cast<app::SliderKnob*>(pw))
        return "slider";
    
    // Switch / button
    if (app::Switch* sw = dynamic_cast<app::Switch*>(pw)) {
        if (sw->momentary)
            return "button";
        else
            return "switch";
    }
    
    // Knob
    if (dynamic_cast<app::SvgKnob*>(pw) || dynamic_cast<app::Knob*>(pw))
        return "knob";

    return "unknown";
}

//---------------------Gestione comportamenti in base al ruolo---------------
// Inferisce il comportamento del parametro:
// continuous, stepped, toggle, momentary, ...
std::string inferParamBehavior(app::ParamWidget* pw) {
    if (!pw)
        return "unknown";

    // Button / switch family
    if (app::Switch* sw = dynamic_cast<app::Switch*>(pw)) {
        if (sw->momentary)
            return "momentary";

        if (engine::ParamQuantity* q = pw->getParamQuantity()) {
            float minV = q->getMinValue();
            float maxV = q->getMaxValue();
            float range = maxV - minV;

            if (range <= 1.0001f)
                return "toggle";
            else
                return "stepped";
        }

        return "toggle";
    }

    // Knob / slider family
    if (engine::ParamQuantity* q = pw->getParamQuantity()) {
        if (dynamic_cast<app::SvgSlider*>(pw) || dynamic_cast<app::SliderKnob*>(pw)) {
            return q->snapEnabled ? "stepped" : "continuous";
        }

        if (dynamic_cast<app::SvgKnob*>(pw) || dynamic_cast<app::Knob*>(pw)) {
            return q->snapEnabled ? "stepped" : "continuous";
        }
    }

    return "unknown";
}

// Verifica se il modulo dichiara capability MR-aware tramite l'interfaccia dedicata.
bool inferIsMRAware(app::ModuleWidget* mw) {
    if (!mw)
        return false;

    engine::Module* engineModule = mw->getModule();
    if (!engineModule)
        return false;

    // capability dichiarata dal modulo
    veryvcv::mr::Descriptor desc = veryvcv::mr::queryDescriptor(engineModule);
    if (desc.apiVersion == veryvcv::mr::kApiVersion && desc.isMRAware)
        return true;

    // temporaneo per debug
   /* plugin::Model* model = mw->getModel();
    if (model && model->plugin) {
        if (model->plugin->slug == "Berg" && model->slug == "VeryVCVBridge")
            return true;
    } */

    return false;
}

// TODO futuro: inferenza di signalKind basata su label/metadata della porta.
// In questa versione v1 le porte restano conservative: signalKind = "unknown".
std::string inferPortSignalKind(const std::string& label) {
    std::string low = toLowerCopy(label);

    if (low.find("gate") != std::string::npos || low.find("trig") != std::string::npos)
        return "gate";
    if (low.find("audio") != std::string::npos || low.find("out") != std::string::npos || low.find("in") != std::string::npos)
        return "unknown"; // volutamente conservativo in v1
    if (low.find("cv") != std::string::npos || low.find("volt") != std::string::npos)
        return "cv";

    return "unknown";
}


//----------------------------------------------------------------------------
// --------------------------Scansione di VCV---------------------------------
//----------------------------------------------------------------------------
// Ricostruisce la snapshot semantica della patch leggendo ModuleWidget, ParamWidget e PortWidget
// https://vcvrack.com/docs-v2/structrack_1_1app_1_1RackWidget#ad750ce69648dbb91c37ee49030423cdb

std::vector<BridgeModuleInfo> scanRack(app::ModuleWidget* selfWidget) {
    std::vector<BridgeModuleInfo> out;

    if (!selfWidget)
        return out;

    app::RackWidget* rackWidget = selfWidget->getAncestorOfType<app::RackWidget>();
    if (!rackWidget)
        return out;

    for (app::ModuleWidget* mw : rackWidget->getModules()) {
        if (!mw)
            continue;

        engine::Module* engineModule = mw->getModule();
        plugin::Model* model = mw->getModel();

        if (!engineModule || !model || !model->plugin)
            continue;

        BridgeModuleInfo info;
        info.moduleInstanceId = engineModule->getId();
        info.pluginSlug = model->plugin->slug;
        info.modelSlug = model->slug;
        info.modelName = model->name;
        info.isMRAware = inferIsMRAware(mw);
        
        // Geometria del pannello modulo in coordinate Rack
        info.box = mw->getBox();

        // Parametri controllabili del modulo
        for (app::ParamWidget* pw : mw->getParams()) {
            if (!pw)
                continue;

            BridgeParamInfo p;
            p.paramId = pw->paramId;
            p.box = pw->getBox();
            p.role = inferParamRole(pw);
            p.behavior = inferParamBehavior(pw);

            if (engine::ParamQuantity* q = pw->getParamQuantity()) {
                p.label = q->getLabel();
                p.unit = q->getUnit();

                p.value = q->getValue();
                p.minValue = q->getMinValue();
                p.maxValue = q->getMaxValue();
                p.defaultValue = q->getDefaultValue();
                p.normalizedValue = safeNormalize(p.value, p.minValue, p.maxValue);

                p.smoothEnabled = q->smoothEnabled;
                p.snapEnabled = q->snapEnabled;

                p.displayValueString = q->getDisplayValueString();
            }

            info.params.push_back(p);
        }

        // Inputs
        for (app::PortWidget* in : mw->getInputs()) {
            if (!in)
                continue;

            BridgePortInfo p;
            p.portId = in->portId;
            p.direction = "input";
            p.box = in->getBox();
            p.signalKind = "unknown";

            info.ports.push_back(p);
        }

        // Outputs
        for (app::PortWidget* outPort : mw->getOutputs()) {
            if (!outPort)
                continue;

            BridgePortInfo p;
            p.portId = outPort->portId;
            p.direction = "output";
            p.box = outPort->getBox();
            p.signalKind = "unknown";

            info.ports.push_back(p);
        }

        out.push_back(info);
    }

    return out;
}

app::ModuleWidget* findModuleWidgetByInstanceId(int64_t moduleInstanceId) {
    if (!APP || !APP->scene || !APP->scene->rack)
        return nullptr;

    app::RackWidget* rack = APP->scene->rack;

    for (app::ModuleWidget* mw : rack->getModules()) {
        if (!mw)
            continue;

        engine::Module* module = mw->getModule();
        if (!module)
            continue;

        if (module->getId() == moduleInstanceId)
            return mw;
    }

    return nullptr;
}

bool readCurrentNormalizedValueForBridge(int64_t moduleInstanceId,
                                         int paramId,
                                         float& outNorm) {
    outNorm = 0.f;

    app::ModuleWidget* mw = findModuleWidgetByInstanceId(moduleInstanceId);
    if (!mw)
        return false;

    for (app::ParamWidget* pw : mw->getParams()) {
        if (!pw)
            continue;

        if (pw->paramId != paramId)
            continue;

        ParamQuantity* pq = pw->getParamQuantity();
        if (!pq)
            return false;

        outNorm = clamp(pq->getScaledValue(), 0.f, 1.f);
        return true;
    }

    return false;
}

std::vector<ParamUpdateEntry> collectParamUpdatesForBridge(
    const std::vector<BridgeModuleInfo>& snapshot,
    std::map<ParamKey, float>& lastSentNormalizedValues,
    bool& snapshotNeedsRefresh
) {
    std::vector<ParamUpdateEntry> updates;
    snapshotNeedsRefresh = false;

    constexpr float kContinuousEps = 0.001f;
    constexpr float kDiscreteEps = 1e-6f;

    for (const BridgeModuleInfo& moduleInfo : snapshot) {
        app::ModuleWidget* mw = findModuleWidgetByInstanceId(moduleInfo.moduleInstanceId);
        if (!mw) {
            snapshotNeedsRefresh = true;
            continue;
        }

        for (const BridgeParamInfo& paramInfo : moduleInfo.params) {
            float currentNorm = 0.f;
            if (!readCurrentNormalizedValueForBridge(moduleInfo.moduleInstanceId,
                                                     paramInfo.paramId,
                                                     currentNorm)) {
                snapshotNeedsRefresh = true;
                continue;
            }

            ParamKey key;
            key.moduleInstanceId = moduleInfo.moduleInstanceId;
            key.paramId = paramInfo.paramId;

            auto it = lastSentNormalizedValues.find(key);

            if (it == lastSentNormalizedValues.end()) {
                lastSentNormalizedValues[key] = currentNorm;
                continue;
            }

            float eps = (paramInfo.behavior == "continuous") ? kContinuousEps : kDiscreteEps;

            if (std::fabs(currentNorm - it->second) > eps) {
                ParamUpdateEntry entry;
                entry.moduleInstanceId = moduleInfo.moduleInstanceId;
                entry.paramId = paramInfo.paramId;
                entry.normalizedValue = currentNorm;
                updates.push_back(entry);

                it->second = currentNorm;
            }
        }
    }

    return updates;
}
