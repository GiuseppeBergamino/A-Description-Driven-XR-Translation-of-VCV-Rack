// Mini server HTTP locale per esporre l'ultima patch_snapshot completa.
// Usato dal Bridge come cache + endpoint read-only per Unity/Quest.

#pragma once

#include <string>
#include <thread>
#include <mutex>
#include <atomic>

class VeryVCVHttpSnapshotServer {

// ===================== LIFECYCLE & API =====================
public:
    explicit VeryVCVHttpSnapshotServer(int port = 5510);
    ~VeryVCVHttpSnapshotServer();

    void start();
    void stop();

    void setSnapshotJson(const std::string& json);
    std::string getSnapshotJsonCopy() const;

    bool isRunning() const;
    int getPort() const;

// ===================== INTERNAL STATE =====================
private:
    void run();
    
    // Snapshot JSON corrente servita da GET /snapshot
    mutable std::mutex mutex_;
    std::string latestSnapshotJson_ = "{\"error\":\"no snapshot yet\"}";
    
    // Stato runtime del server HTTP e sincronizzazione shutdown/thread
    // Protegge i file descriptor runtime
    mutable std::mutex socketMutex_;
    int activeClientFd_ = -1;
    int serverFd_ = -1;

    std::atomic<bool> stopRequested_{false};
    std::atomic<bool> running_{false};

    std::thread thread_;
    int port_ = 5510;
};
