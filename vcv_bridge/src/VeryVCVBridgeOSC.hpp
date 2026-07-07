//  VeryVCVBridgeOSC.hpp
//  Created by Giuseppe Bergamino on 23/04/26

// Sender OSC UDP  usato dal Bridge per notifiche runtime verso Unity/Quest e viceversa
// Nessun thread, nessun listener: solo invio fire-and-forget di messaggi OSC

#pragma once

#include <string>
#include <queue>
#include <thread>
#include <mutex>
#include <atomic>

namespace veryvcv {
    namespace oscproto {

    // ===================== PROTOCOL CONSTANTS =====================
    static constexpr int kUnityListenPort = 5511; 
    static constexpr int kBridgeListenPort = 5512;
   
    /*
    static constexpr const char* kSnapshotAvailableAddress = "/veryvcv/snapshot/available";
    static constexpr const char* kParamUpdateAddress = "/veryvcv/param/update";
    static constexpr const char* kParamSetAddress = "/veryvcv/param/set";

    static constexpr const char* kSnapshotAvailableTypeTag = ",si";
    static constexpr const char* kParamUpdateTypeTag = ",sisif";
    static constexpr const char* kParamSetTypeTag = ",ssif";
    */
    
    // mod for IS2 test
    static constexpr const char* kSnapshotAvailableAddress = "/veryvcv/snapshot/available";
    static constexpr const char* kParamUpdateAddress = "/veryvcv/param/update";
    static constexpr const char* kParamSetAddress = "/veryvcv/param/set";
    static constexpr const char* kSnapshotProfileAddress = "/veryvcv/profile/snapshot";

    static constexpr const char* kSnapshotAvailableTypeTag = ",si";
    static constexpr const char* kParamUpdateTypeTag = ",sisif";
    static constexpr const char* kParamSetTypeTag = ",ssif";
    static constexpr const char* kSnapshotProfileTypeTag = ",iiffff";
     

    // ===================== SHARED MESSAGE TYPES =====================
    struct ParamValueMessage {
        std::string patchSessionId;
        std::string moduleId;
        int paramId = -1;
        float normalizedValue = 0.f;
    };

    struct ParamSetMessage : ParamValueMessage {
    };

    struct ParamUpdateMessage : ParamValueMessage {
        int revision = 0;
    };

    struct SnapshotAvailableMessage {
        std::string patchSessionId;
        int revision = 0;
    };
    
    struct SnapshotProfileMessage { //add for IS2 test
        int revision = 0;
        int status = 0;
        float httpMs = 0.f;
        float parseMs = 0.f;
        float instantiateMs = 0.f;
        float clientTotalMs = 0.f;

        // Internal receiver timestamp, filled by the Bridge OSC receiver thread.
        // Not part of the OSC payload.
        double receivedAtMs = 0.0;
    };

    } // namespace oscproto
} // namespace veryvcv


// ===================== OSC SENDER =====================
// Sender OSC UDP minimale usato dal Bridge per notifiche runtime verso Unity/Quest.
// Nessun thread, nessun listener: solo invio fire-and-forget di messaggi OSC.
class VeryVCVOscSender {
public:
    explicit VeryVCVOscSender(
        const std::string& host = "127.0.0.1",
        int port = veryvcv::oscproto::kUnityListenPort
    );

    void setTargetHost(const std::string& host);
    void setTargetPort(int port);

    std::string getTargetHost() const;
    int getTargetPort() const;

    bool sendSnapshotAvailable(const veryvcv::oscproto::SnapshotAvailableMessage& msg) const;
    bool sendParamUpdate(const veryvcv::oscproto::ParamUpdateMessage& msg) const;

    // Convenience overloads
    bool sendSnapshotAvailable(const std::string& patchSessionId, int revision) const;
    bool sendParamUpdate(
        const std::string& patchSessionId,
        int revision,
        const std::string& moduleId,
        int paramId,
        float normalizedValue
    ) const;

private:
    std::string host_;
    int port_ = veryvcv::oscproto::kUnityListenPort;
};


// ===================== OSC RECEIVER =====================
// Receiver OSC UDP minimale usato dal Bridge per ricevere param_set da Unity/Quest.
// Il thread socket riceve e accoda messaggi; l'applicazione dei valori resta nel Bridge.
class VeryVCVOscReceiver {
public:
    explicit VeryVCVOscReceiver(int port = veryvcv::oscproto::kBridgeListenPort);
    ~VeryVCVOscReceiver();

    void start();
    void stop();

    bool isRunning() const;
    int getListenPort() const;

    bool popNextParamSet(veryvcv::oscproto::ParamSetMessage& outMessage);
    
    bool popNextSnapshotProfile(veryvcv::oscproto::SnapshotProfileMessage& outMessage); // add for IS2 test

private:
    void run();

    std::queue<veryvcv::oscproto::ParamSetMessage> paramSetQueue_;
    std::queue<veryvcv::oscproto::SnapshotProfileMessage> snapshotProfileQueue_; // add for IS2 test
    mutable std::mutex queueMutex_;

    std::atomic<bool> stopRequested_{false};
    std::atomic<bool> running_{false};

    std::thread thread_;
    int port_ = veryvcv::oscproto::kBridgeListenPort;
};

