// Created by Giuseppe Bergamino on 10/04/26.
// VeryVCVBridge.cpp

//#include "plugin.hpp"
#include "VeryVCVBridgeData.hpp" // structs and helpers
#include "VeryVCVBridgeHttp.hpp" // server snapshot
#include "VeryVCVBridgeOSC.hpp" // OSC in and out
#include <map>
#include <algorithm>
#include <cmath>
#include <cstdlib> //std::free(dumped);
// test IS2
#include <chrono>
#include <fstream>
#include <iomanip>


using namespace rack;

namespace { //for IS2 testing, time measurements with steady_clock, modules and parameters count, temporary cache

double veryvcvProfileNowMs() {
    using clock = std::chrono::steady_clock;
    const auto now = clock::now().time_since_epoch();
    return std::chrono::duration<double, std::milli>(now).count();
}

struct SnapshotProfileComplexity {
    int modules = 0;
    int controls = 0;
    int jacks = 0;
    int lights = 0;
};

SnapshotProfileComplexity countSnapshotComplexity(const std::vector<BridgeModuleInfo>& snapshot) {
    SnapshotProfileComplexity c;
    c.modules = static_cast<int>(snapshot.size());

    for (const BridgeModuleInfo& m : snapshot) {
        c.controls += static_cast<int>(m.params.size());
        c.jacks += static_cast<int>(m.ports.size());
        c.lights += static_cast<int>(m.lights.size());
    }

    return c;
}

struct PendingSnapshotProfile {
    int64_t revision = 0;
    double scanStartMs = 0.0;

    int modules = 0;
    int controls = 0;
    int jacks = 0;
    int lights = 0;
    size_t jsonBytes = 0;

    double bridgeScanMs = 0.0;
    double bridgeDebugTextMs = 0.0;
    double bridgeJsonMs = 0.0;
    double bridgeCacheMs = 0.0;
    double bridgeNotifyMs = 0.0;
    double bridgeTotalMs = 0.0;
};

} // anonymous namespace

// -----------------------------------------------------------------------------
// =============================== ENGINE ====================================//
// -----------------------------------------------------------------------------

struct VeryVCVBridge : engine::Module, veryvcv::mr::IModuleCapabilities { //aggiunta pr isMRAware
    enum ParamIds {
        SCAN_PARAM,
        NUM_PARAMS
    };
    enum InputIds {
        NUM_INPUTS
    };
    enum OutputIds {
        NUM_OUTPUTS
    };
    enum LightIds {
        SCAN_LED,
        NUM_LIGHTS
    };
    
    veryvcv::mr::Descriptor getMRDescriptor() const override {
        veryvcv::mr::Descriptor d;
        d.apiVersion = veryvcv::mr::kApiVersion;
        d.isMRAware = true;
        d.mrModelId = "Berg.VeryVCVBridge";
        d.mrVariant = "default";
        return d;
    }
    
    std::vector<BridgeModuleInfo> snapshot;
    std::string debugText = "Right-click -> Scan rack now";
    std::string lastSnapshotJson;
    
    enum class DebugViewMode {
        SnapshotSummary,
        SnapshotJson
    };

    DebugViewMode debugViewMode = DebugViewMode::SnapshotSummary;

//-------
    BridgeSnapshotMetadata snapshotMeta;
    int schemaVersion = 1;
    int64_t currentRevision = 0;     // revision globale dei messaggi emessi dal Bridge
    int64_t lastSnapshotRevision = 0;     // revision dell’ultima snapshot strutturale valida

    std::string patchSessionId; // id semplice della sessione/patch corrente

    std::map<ParamKey, float> lastSentNormalizedValues;     // cache ultimi valori normalized inviati
    
    // se durante il polling trovi incoerenze rispetto alla snapshot
    bool snapshotNeedsRefresh = false;
    
    bool readCurrentNormalizedValue(int64_t moduleInstanceId, int paramId, float& outNorm);
    std::vector<ParamUpdateEntry> collectParamUpdates();
    
    //add for IS2 Testing
    
    std::map<int64_t, PendingSnapshotProfile> pendingSnapshotProfiles;

    void consumePendingSnapshotProfiles();

    void writeSnapshotProfileCsv(
        const PendingSnapshotProfile& bridgeProfile,
        const veryvcv::oscproto::SnapshotProfileMessage& questProfile,
        double scanToClientReadyAckMs
    );

    std::string getSnapshotProfileCsvPath() const;
        
//------
    //bool autoScanned = true;     // per autoscan futuro se aggiungo, elimino o cambio posizione a un modulo
    bool scanRequested = false;  // richiesta di scan dal pulsante pannello
    
    
    dsp::SchmittTrigger scanDetector;
    dsp::PulseGenerator scanLED;
    
 //----salvo e recallo da file JSON
    json_t* dataToJson() override {
        json_t* rootJ = json_object();

        snapshotMeta.schemaVersion = schemaVersion;
        snapshotMeta.patchSessionId = patchSessionId;
        snapshotMeta.revision = lastSnapshotRevision;

        json_object_set_new(rootJ, "bridgeSnapshot", snapshotToJson(snapshot, snapshotMeta));
        json_object_set_new(rootJ, "bridgePatchSessionId", json_string(patchSessionId.c_str()));
        json_object_set_new(rootJ, "bridgeLastSnapshotRevision", json_integer(lastSnapshotRevision));
        json_object_set_new(rootJ, "bridgeLastMessageRevision", json_integer(currentRevision));

        return rootJ;
    }

    void dataFromJson(json_t* rootJ) override {
        json_t* patchSessionJ = json_object_get(rootJ, "bridgePatchSessionId");
        if (json_is_string(patchSessionJ)) {
            patchSessionId = json_string_value(patchSessionJ);
            snapshotMeta.patchSessionId = patchSessionId;
        }
        json_t* snapshotRevisionJ = json_object_get(rootJ, "bridgeLastSnapshotRevision");
        if (json_is_integer(snapshotRevisionJ)) {
            lastSnapshotRevision = (int64_t) json_integer_value(snapshotRevisionJ);
            snapshotMeta.revision = lastSnapshotRevision;
        }
        json_t* messageRevisionJ = json_object_get(rootJ, "bridgeLastMessageRevision");
        if (json_is_integer(messageRevisionJ)) {
            currentRevision = (int64_t) json_integer_value(messageRevisionJ);
        }
        snapshotMeta.schemaVersion = schemaVersion; // sicurezza: schema allineato
        
        json_t* snapshotJ = json_object_get(rootJ, "bridgeSnapshot");
        if (snapshotJ) {
            debugText = "TODO: Snapshot restored from patch";
        }
    }

    //======================== Trasmissione / Ricezione =============
    void applyPendingRemoteParamSets(); //per ricezione OSC anti feedback
    void pollAndSendParamUpdates(); //per invio dati OSC su valore parametri
    
    VeryVCVHttpSnapshotServer httpSnapshotServer{5510}; //HTTP per snapshot
    //quest avellino: 192.168.1.128
    //questo casa: 192.168.1.40
    VeryVCVOscSender oscSender{"10.69.123.157", 5511}; //OSC, IP del Quest (OSC->Quest)
    VeryVCVOscReceiver oscReceiver{5512}; //OSC input (osc -> VCV)
    // altrimenti visto che ho il namespace VeryVCVOscReceiver oscReceiver{veryvcv::oscproto::kBridgeListenPort};
     
    
   //----distruttore
    ~VeryVCVBridge() override {
        httpSnapshotServer.stop(); //per chiudere HTTP
        oscReceiver.stop(); //per chiudere listening
    }

    //----------------------------------------------------------
    //=================== CONFIG ==============================
    //----------------------------------------------------------

    VeryVCVBridge() { //costrutture
        config(NUM_PARAMS, NUM_INPUTS, NUM_OUTPUTS, NUM_LIGHTS);
        configButton(SCAN_PARAM, "Scan rack");

        patchSessionId = "bridge-" + std::to_string(reinterpret_cast<uintptr_t>(this)); //per rendere patchSessioId unico
        snapshotMeta.schemaVersion = schemaVersion;
        snapshotMeta.patchSessionId = patchSessionId;
        snapshotMeta.revision = 0; //incremengta rispetto a n doScan()
        currentRevision = 0;
        lastSnapshotRevision = 0;
        
        lastSnapshotJson = "{\"error\":\"no snapshot yet\"}"; //evito stati vuoti prima del primo scan
        httpSnapshotServer.setSnapshotJson(lastSnapshotJson);
        
        httpSnapshotServer.start(); //avvio server HTTP per snapshot
        oscReceiver.start();        //avvio ascolto UDP OSC
    }

    void process(const ProcessArgs& args) override {
        bool buttScan = params[SCAN_PARAM].getValue();

        if (scanDetector.process(buttScan)) {
            scanRequested = true; //triggero richiesta a widgetUI che lo farà in step()
            scanLED.trigger(0.1f);
        }

        // LED del pulsante
        lights[SCAN_LED].setBrightness(scanLED.process(args.sampleTime));
    }
}; //end struct

// add for IS2 testing
std::string VeryVCVBridge::getSnapshotProfileCsvPath() const {
    return asset::user("VeryVCVBridge_snapshot_profile.csv");
}

void VeryVCVBridge::writeSnapshotProfileCsv(
    const PendingSnapshotProfile& bridgeProfile,
    const veryvcv::oscproto::SnapshotProfileMessage& questProfile,
    double scanToClientReadyAckMs
) {
    const std::string path = getSnapshotProfileCsvPath();
    const bool writeHeader = !std::ifstream(path).good();

    std::ofstream out(path, std::ios::app);
    if (!out.is_open()) {
        return;
    }

    if (writeHeader) {
        out << "revision,modules,controls,jacks,lights,jsonBytes,"
            << "bridgeScanMs,bridgeDebugTextMs,bridgeJsonMs,bridgeCacheMs,bridgeNotifyMs,bridgeTotalMs,"
            << "questHttpMs,questParseMs,questInstantiateMs,questClientTotalMs,"
            << "scanToClientReadyAckMs,status\n";
    }

    out << std::fixed << std::setprecision(3)
        << bridgeProfile.revision << ','
        << bridgeProfile.modules << ','
        << bridgeProfile.controls << ','
        << bridgeProfile.jacks << ','
        << bridgeProfile.lights << ','
        << bridgeProfile.jsonBytes << ','
        << bridgeProfile.bridgeScanMs << ','
        << bridgeProfile.bridgeDebugTextMs << ','
        << bridgeProfile.bridgeJsonMs << ','
        << bridgeProfile.bridgeCacheMs << ','
        << bridgeProfile.bridgeNotifyMs << ','
        << bridgeProfile.bridgeTotalMs << ','
        << questProfile.httpMs << ','
        << questProfile.parseMs << ','
        << questProfile.instantiateMs << ','
        << questProfile.clientTotalMs << ','
        << scanToClientReadyAckMs << ','
        << questProfile.status << '\n';
}

void VeryVCVBridge::consumePendingSnapshotProfiles() {
    using namespace veryvcv::oscproto;

    SnapshotProfileMessage msg;

    while (oscReceiver.popNextSnapshotProfile(msg)) {
        const double receivedMs = msg.receivedAtMs > 0.0
            ? msg.receivedAtMs
            : veryvcvProfileNowMs();

        auto it = pendingSnapshotProfiles.find(static_cast<int64_t>(msg.revision));
        if (it == pendingSnapshotProfiles.end()) {
            continue;
        }

        const PendingSnapshotProfile bridgeProfile = it->second;
        const double scanToClientReadyAckMs = receivedMs - bridgeProfile.scanStartMs;

        writeSnapshotProfileCsv(bridgeProfile, msg, scanToClientReadyAckMs);
        pendingSnapshotProfiles.erase(it);
    }
}



// Poll dei parametri già presenti nella snapshot e invio dei soli cambiamenti via OSC.
// Chiamato dal widget a frame rate, fuori dal thread audio.
void VeryVCVBridge::pollAndSendParamUpdates() {
    if (snapshot.empty()) {
        return;
    }

    std::vector<ParamUpdateEntry> updates = collectParamUpdates();

    if (snapshotNeedsRefresh) {
        return;
    }

    if (updates.empty()) {
        return;
    }

    ++currentRevision;

    for (const ParamUpdateEntry& u : updates) {
        std::string moduleId = std::to_string(u.moduleInstanceId);
        oscSender.sendParamUpdate(
            patchSessionId,
            (int)currentRevision,
            moduleId,
            u.paramId,
            u.normalizedValue
        );
    }
}

// --------- Ricezione valori dal quest via OSC con prima strategia anti feedback
// il valore deve superare un certo delta per essere reinviato
void VeryVCVBridge::applyPendingRemoteParamSets() {
    using namespace veryvcv::oscproto;

    static constexpr float kEchoEpsilon = 0.001f; //delta variazione consentito

    auto normalizeValue = [](float value, float minValue, float maxValue) -> float {
        float range = maxValue - minValue;
        if (std::fabs(range) < 1e-6f) { //non mi interessano frazioni al milionesimo
            return 0.f;
        }

        float normalized = (value - minValue) / range;
        return std::max(0.f, std::min(1.f, normalized));
    };

    ParamSetMessage msg;

    while (oscReceiver.popNextParamSet(msg)) {
        if (msg.patchSessionId != patchSessionId) { //1) La patchSessionId deve combaciare con la patch attualmente nota al Bridge
            continue;
        }
        
        int64_t moduleInstanceId = -1; // 2) Parse moduleId string -> int64
        try {
            moduleInstanceId = std::stoll(msg.moduleId);
        }
        catch (...) {
            continue;
        }

        float requestedNormalized = std::max(0.f, std::min(1.f, msg.normalizedValue)); // 3) Clamp del valore richiesto

        app::ModuleWidget* mw = findModuleWidgetByInstanceId(moduleInstanceId); // 4) Risolvi il modulo target nella Rack live
        if (!mw || !mw->module) {
            snapshotNeedsRefresh = true;
            continue;
        }

        engine::Module* targetModule = mw->module;

        // 5) Verifica paramId valido
        if (msg.paramId < 0 || msg.paramId >= static_cast<int>(targetModule->paramQuantities.size())) {
            snapshotNeedsRefresh = true;
            continue;
        }

        engine::ParamQuantity* pq = targetModule->paramQuantities[msg.paramId];
        if (!pq) {
            snapshotNeedsRefresh = true;
            continue;
        }

        // 6) Stato precedente autoritativo
        float minValue = pq->getMinValue();
        float maxValue = pq->getMaxValue();
        float previousNormalized = normalizeValue(pq->getValue(), minValue, maxValue);

        // 7) Applica il set richiesto
        float requestedValue = minValue + requestedNormalized * (maxValue - minValue);
        pq->setValue(requestedValue);

        // 8) Readback autoritativo dopo applicazione
        float appliedNormalized = normalizeValue(pq->getValue(), minValue, maxValue);

        // 9) Anti-echo:
        // aggiorno la cache locale al valore realmente applicato,
        // così il successivo poll non lo considera automaticamente come delta da reinviare
        ParamKey key;
        key.moduleInstanceId = moduleInstanceId;
        key.paramId = msg.paramId;
        lastSentNormalizedValues[key] = appliedNormalized;

        // 10) Se lo stato reale è cambiato, incrementiamo la revision runtime
        bool stateChanged = std::fabs(appliedNormalized - previousNormalized) > kEchoEpsilon;
        if (stateChanged) {
            ++currentRevision;
        }

        // 11) Se il valore autoritativo differisce dal richiesto, invo una correzione
        // verso Unity/Quest. Se coincide, niente echo
        if (std::fabs(appliedNormalized - requestedNormalized) > kEchoEpsilon) {
            ParamUpdateMessage correction;
            correction.patchSessionId = patchSessionId;
            correction.revision = static_cast<int>(currentRevision);
            correction.moduleId = msg.moduleId;
            correction.paramId = msg.paramId;
            correction.normalizedValue = appliedNormalized;

            oscSender.sendParamUpdate(correction);
        }
    }
}

bool VeryVCVBridge::readCurrentNormalizedValue(int64_t moduleInstanceId,
                                               int paramId,
                                               float& outNorm) {
    return readCurrentNormalizedValueForBridge(moduleInstanceId, paramId, outNorm);
}

std::vector<ParamUpdateEntry> VeryVCVBridge::collectParamUpdates() {
    return collectParamUpdatesForBridge(
        snapshot,
        lastSentNormalizedValues,
        snapshotNeedsRefresh
    );
}

// -----------------Display testuale semplice----------------
// Widget custom che disegna il testo nel pannello con scroll
struct BridgeTextDisplay : widget::Widget {
    VeryVCVBridge* module = nullptr;

    float topPad = 16.f;
    float leftPad = 6.f;
    float lineH = 11.f;
    float bottomPad = 8.f;

    int getLineCount() const {
        if (!module)
            return 1;

        std::stringstream ss(module->debugText);
        std::string line;
        int count = 0;
        while (std::getline(ss, line)) {
            count++;
        }

        return std::max(1, count); // Evita altezza zero
    }

    void updateContentHeight() {
        int lines = getLineCount();

        float width = box.size.x; // Manteniamo la larghezza già assegnata dal parent
        float height = topPad + lines * lineH + bottomPad;// Altezza totale del contenuto testuale

        box.size = math::Vec(width, height);
    }

    void draw(const DrawArgs& args) override {
        if (!module)
            return;

        float cornerRadius = 12.f;

        // Sfondo del contenuto scrollabile
        nvgBeginPath(args.vg);
        nvgRoundedRect(args.vg, 0.f, 10.f, box.size.x, box.size.y - 15.f, cornerRadius);
        nvgFillColor(args.vg, nvgRGB(232, 230, 205));
        nvgFill(args.vg);

        // Bordo
        nvgBeginPath(args.vg);
        nvgRoundedRect(args.vg, 0.5f, 10.5f, box.size.x - 1.f, box.size.y - 16.f, cornerRadius);
        nvgStrokeColor(args.vg, nvgRGB(58, 65, 72));
        nvgStrokeWidth(args.vg, 1.f);
        nvgStroke(args.vg);

        // Testo
        nvgFillColor(args.vg, nvgRGB(58, 65, 72));
        nvgFontSize(args.vg, 10.f);
        nvgTextAlign(args.vg, NVG_ALIGN_LEFT | NVG_ALIGN_TOP);

        float x = leftPad;
        float y = topPad;

        std::stringstream ss(module->debugText);
        std::string line;
        while (std::getline(ss, line)) {
            nvgText(args.vg, x, y, line.c_str(), nullptr);
            y += lineH;
        }
    }
};


// -------------------------ModuleWidget----------------------------------------------------
struct VeryVCVBridgeWidget : app::ModuleWidget {
    ui::ScrollWidget* scrollWidget = nullptr;
    BridgeTextDisplay* display = nullptr;
    
    VeryVCVBridgeWidget(VeryVCVBridge* module) {
        setModule(module);
        setPanel(APP->window->loadSvg(asset::plugin(pluginInstance, "res/VeryVCVBridge.svg")));

        // viti
        addChild(createWidget<ScrewSilver>(Vec(RACK_GRID_WIDTH, 0)));
        addChild(createWidget<ScrewSilver>(Vec(box.size.x - 2 * RACK_GRID_WIDTH, 0)));
        addChild(createWidget<ScrewSilver>(Vec(RACK_GRID_WIDTH, RACK_GRID_HEIGHT - RACK_GRID_WIDTH)));
        addChild(createWidget<ScrewSilver>(Vec(box.size.x - 2 * RACK_GRID_WIDTH, RACK_GRID_HEIGHT - RACK_GRID_WIDTH)));

        //---------Viewport scrollabile
        scrollWidget = new ui::ScrollWidget();
        scrollWidget->box.pos = math::Vec(8.f, 28.f);
        scrollWidget->box.size = math::Vec(box.size.x - 16.f, box.size.y - 70.f);
        // scrollWidget->hideScrollbars = true; //nascone scrollbar
        addChild(scrollWidget);

        // Contenuto testuale scrollabile
        display = new BridgeTextDisplay();
        display->module = module;
        // La larghezza del contenuto coincide con quella del viewport;
        // l'altezza viene aggiornata dinamicamente dal testo.
        display->box.pos = math::Vec(0.f, 0.f);
        display->box.size = math::Vec(scrollWidget->box.size.x, 100.f);
        display->updateContentHeight();

        scrollWidget->container->addChild(display); // Il display va nel container dello ScrollWidget
        
        //-----Pulsante scan
        addParam(createLightParamCentered<VCVLightButton<MediumSimpleLight<GreenLight>>>(
            mm2px(Vec(40.64, 118.0)), //10.5
            module,
            VeryVCVBridge::SCAN_PARAM,
            VeryVCVBridge::SCAN_LED
        ));
    }

    void step() override {  // esegue azione a frame rate UI Rack
        app::ModuleWidget::step();

        VeryVCVBridge* m = getModule<VeryVCVBridge>();
        if (!m)
            return;

        if (m->scanRequested) { // se nel process DSP c'è la richiesta true
            doScan();
            m->scanRequested = false; // resetta richiesta
        }

        // Profiling snapshot: consuma eventuali messaggi OSC arrivati dal Quest
        // con i tempi di ricostruzione della scena MR.
        m->consumePendingSnapshotProfiles();

        // Runtime OSC in/out eseguito a ogni step UI Rack, senza suddivisioni.
        m->applyPendingRemoteParamSets(); // OSC in: param/set da Quest
        m->pollAndSendParamUpdates();     // OSC out: param/update verso Quest
    }

    // Ricostruisce la snapshot semantica della patch, aggiorna la cache HTTP
    // e notifica via OSC che una nuova snapshot completa è disponibile.
    void doScan() {
        VeryVCVBridge* m = getModule<VeryVCVBridge>();
        if (!m)
            return;

        const double t0 = veryvcvProfileNowMs();

        m->snapshot = scanRack(this);
        const double t1 = veryvcvProfileNowMs();

        m->lastSnapshotRevision = ++m->currentRevision; // nuova snapshot strutturale valida

        // allinea metadata snapshot
        m->snapshotMeta.schemaVersion = m->schemaVersion;
        m->snapshotMeta.patchSessionId = m->patchSessionId;
        m->snapshotMeta.revision = m->lastSnapshotRevision;

        // reset cache param_update
        m->lastSentNormalizedValues.clear();
        m->snapshotNeedsRefresh = false;

        // aggiorna testo e json snapshot
        m->debugViewMode = VeryVCVBridge::DebugViewMode::SnapshotSummary; // ritorna a vista summary dopo lo scan
        m->debugText = snapshotToText(m->snapshot);
        const double t2 = veryvcvProfileNowMs();

        m->lastSnapshotJson = snapshotToJsonString(m->snapshot, m->snapshotMeta);
        const double t3 = veryvcvProfileNowMs();

        m->httpSnapshotServer.setSnapshotJson(m->lastSnapshotJson); // invio snapshot al server
        const double t4 = veryvcvProfileNowMs();

        SnapshotProfileComplexity complexity = countSnapshotComplexity(m->snapshot);

        PendingSnapshotProfile profile;
        profile.revision = m->lastSnapshotRevision;
        profile.scanStartMs = t0;

        profile.modules = complexity.modules;
        profile.controls = complexity.controls;
        profile.jacks = complexity.jacks;
        profile.lights = complexity.lights;
        profile.jsonBytes = m->lastSnapshotJson.size();

        profile.bridgeScanMs = t1 - t0;
        profile.bridgeDebugTextMs = t2 - t1;
        profile.bridgeJsonMs = t3 - t2;
        profile.bridgeCacheMs = t4 - t3;

        // Inseriamo il profilo pending prima della notifica OSC.
        // Così, se il Quest risponde molto rapidamente, il Bridge ha già una revision da associare.
        m->pendingSnapshotProfiles[profile.revision] = profile;

        m->oscSender.sendSnapshotAvailable(m->patchSessionId, (int)m->lastSnapshotRevision); // invio disponibilità snapshot via OSC
        const double t5 = veryvcvProfileNowMs();

        profile.bridgeNotifyMs = t5 - t4;
        profile.bridgeTotalMs = t5 - t0;

        // Aggiorniamo il profilo pending con i tempi finali dopo la notifica.
        m->pendingSnapshotProfiles[profile.revision] = profile;

        if (display) {
            display->updateContentHeight(); // se riscanno devo ricalcolare il numero di righe
        }

        if (scrollWidget) {
            scrollWidget->offset = math::Vec(0.f, 0.f);
        }
    }

    void appendContextMenu(ui::Menu* menu) override {
        app::ModuleWidget::appendContextMenu(menu);

        menu->addChild(new ui::MenuSeparator());

        menu->addChild(createMenuLabel("VeryVCV Bridge"));

        menu->addChild(createMenuItem("Scan rack now", "",
            [=]() {
                doScan();
            }
        ));

        menu->addChild(createMenuItem("Show snapshot JSON", "",
            [=]() {
                VeryVCVBridge* m = getModule<VeryVCVBridge>();
                if (!m)
                    return;

                m->debugViewMode = VeryVCVBridge::DebugViewMode::SnapshotJson;

                if (m->lastSnapshotJson.empty()) {
                    m->debugText = "JSON empty. Run Scan first.";
                }
                else {
                    m->debugText = m->lastSnapshotJson;
                }

                if (display) {
                    display->updateContentHeight();
                }

                if (scrollWidget) {
                    scrollWidget->offset = math::Vec(0.f, 0.f);
                }
            }
        ));

        menu->addChild(createMenuItem("Show compact summary", "",
            [=]() {
                VeryVCVBridge* m = getModule<VeryVCVBridge>();
                if (!m)
                    return;

                m->debugViewMode = VeryVCVBridge::DebugViewMode::SnapshotSummary;
                m->debugText = snapshotToText(m->snapshot);

                if (display) {
                    display->updateContentHeight();
                }

                if (scrollWidget) {
                    scrollWidget->offset = math::Vec(0.f, 0.f);
                }
            }
        ));
    }
};
// -------------------------Model registration----------------------------------------------
Model* modelVeryVCVBridge = createModel<VeryVCVBridge, VeryVCVBridgeWidget>("VeryVCVBridge");



// Ricostruisce la snapshot semantica della patch, aggiorna la cache HTTP
// e notifica via OSC che una nuova snapshot completa è disponibile.
    
/*
    void doScan() {
        VeryVCVBridge* m = getModule<VeryVCVBridge>();
        if (!m)
            return;

        m->snapshot = scanRack(this);
        m->lastSnapshotRevision = ++m->currentRevision; // nuova snapshot strutturale valida

        // allinea metadata snapshot
        m->snapshotMeta.schemaVersion = m->schemaVersion;
        m->snapshotMeta.patchSessionId = m->patchSessionId;
        m->snapshotMeta.revision = m->lastSnapshotRevision;

        // reset cache param_update
        m->lastSentNormalizedValues.clear();
        m->snapshotNeedsRefresh = false;

        // aggiorna testo e json snapshot
        m->debugViewMode = VeryVCVBridge::DebugViewMode::SnapshotSummary; //ritorna a vista summary dopo lo scan
        m->debugText = snapshotToText(m->snapshot);
        m->lastSnapshotJson = snapshotToJsonString(m->snapshot, m->snapshotMeta);
        
        m->httpSnapshotServer.setSnapshotJson(m->lastSnapshotJson); //invio snapshot al server
        m->oscSender.sendSnapshotAvailable(m->patchSessionId, (int)m->lastSnapshotRevision); //invio disponibilità snapshot via OSC

        if (display) {
            display->updateContentHeight(); //se riscanno devo ricalcolar eil numero di righe
        }

        if (scrollWidget) {
            scrollWidget->offset = math::Vec(0.f, 0.f);
        }
    }
    
    void appendContextMenu(ui::Menu* menu) override {
        app::ModuleWidget::appendContextMenu(menu);

        menu->addChild(new ui::MenuSeparator());

        menu->addChild(createMenuLabel("VeryVCV Bridge"));
        menu->addChild(createMenuItem("Scan rack now", "",
            [=]() {
                doScan();
            }
        ));
        
        menu->addChild(createMenuItem("Show snapshot JSON", "",
            [=]() {
                VeryVCVBridge* m = getModule<VeryVCVBridge>();
                if (!m)
                    return;
                
                m->debugViewMode = VeryVCVBridge::DebugViewMode::SnapshotJson;

                if (m->lastSnapshotJson.empty()) {
                    m->debugText = "JSON empty. Run Scan first.";
                }
                else {
                    m->debugText = m->lastSnapshotJson;
                }

                if (display) {
                    display->updateContentHeight(); //per scroll update
                }
                if (scrollWidget) {
                    scrollWidget->offset = math::Vec(0.f, 0.f);
                }
            }
        ));
        
        menu->addChild(createMenuItem("Show compact summary", "",
            [=]() {
                VeryVCVBridge* m = getModule<VeryVCVBridge>();
                if (!m)
                    return;
               
                m->debugViewMode = VeryVCVBridge::DebugViewMode::SnapshotSummary;
                m->debugText = snapshotToText(m->snapshot);

                if (display) {
                    display->updateContentHeight();
                }
                if (scrollWidget) {
                    scrollWidget->offset = math::Vec(0.f, 0.f);
                }
            }
*/ //original
