//  Created by Giuseppe Bergamino on 16/04/26.

#pragma once

#include "plugin.hpp"

namespace veryvcv {
    namespace mr {

    // ===================== MR CAPABILITY CONTRACT =====================
    // Contratto minimo per permettere al Bridge di riconoscere moduli MR-aware
    // e recuperare un descriptor statico con metadati base.

    static constexpr int kApiVersion = 1;

    // Descriptor leggero interrogabile a runtime dal Bridge.
    // In questa fase contiene solo metadati minimi; potrà essere esteso in futuro.
    struct Descriptor {
        int apiVersion = kApiVersion;
        bool isMRAware = false;

        // Metadati opzionali per identificare una rappresentazione MR dedicata
        const char* mrModelId = nullptr;
        const char* mrVariant = nullptr;
    };

    // Interfaccia implementabile dai moduli che vogliono dichiarare capability MR-aware.
    struct IModuleCapabilities {
        virtual ~IModuleCapabilities() = default;
        virtual Descriptor getMRDescriptor() const = 0;
    };

    // Helper runtime: restituisce il Descriptor MR del modulo se disponibile,
    // altrimenti un Descriptor di default con isMRAware = false.
    inline Descriptor queryDescriptor(rack::engine::Module* module) {
        Descriptor d;

        if (!module)
            return d;

        if (auto* caps = dynamic_cast<IModuleCapabilities*>(module)) {
            return caps->getMRDescriptor();
        }

        return d;
    }

    } // namespace mr
} // namespace veryvcv

