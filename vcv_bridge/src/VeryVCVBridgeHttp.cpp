#include "VeryVCVBridgeHttp.hpp"

#include <sstream>
#include <cstring>

// MVP POSIX/BSD sockets per macOS/Linux
// Per Windows servirebbe una variante Winsock dedicata
#if defined(__APPLE__) || defined(__linux__)
#include <sys/types.h>
#include <sys/socket.h>
#include <sys/select.h>
#include <netinet/in.h>
#include <arpa/inet.h>
#include <unistd.h>
#endif

// ===================== LIFECYCLE =====================
VeryVCVHttpSnapshotServer::VeryVCVHttpSnapshotServer(int port)
    : port_(port) {
}

VeryVCVHttpSnapshotServer::~VeryVCVHttpSnapshotServer() {
    stop();
}

// Avvio/arresto del thread server.
// Lo shutdown deve essere non bloccante per evitare hang in chiusura di Rack.
void VeryVCVHttpSnapshotServer::start() {
#if defined(__APPLE__) || defined(__linux__)
    if (running_)
        return;

    stopRequested_ = false;
    thread_ = std::thread(&VeryVCVHttpSnapshotServer::run, this);
#endif
}

void VeryVCVHttpSnapshotServer::stop() {
#if defined(__APPLE__) || defined(__linux__)
    stopRequested_ = true;

    // Sblocca un eventuale recv() sul client attivo.
    // Evitiamo close() concorrenti nel thread chiamante: la chiusura vera del socket
    // resta nel thread server, che termina poi tramite join().
    {
        std::lock_guard<std::mutex> lock(socketMutex_);
        if (activeClientFd_ >= 0) {
            ::shutdown(activeClientFd_, SHUT_RDWR);
        }
    }

    if (thread_.joinable()) {
        thread_.join();
    }

    running_ = false;
#endif
}

// ===================== SNAPSHOT CACHE =====================
void VeryVCVHttpSnapshotServer::setSnapshotJson(const std::string& json) {
    std::lock_guard<std::mutex> lock(mutex_);
    latestSnapshotJson_ = json;
}

std::string VeryVCVHttpSnapshotServer::getSnapshotJsonCopy() const {
    std::lock_guard<std::mutex> lock(mutex_);
    return latestSnapshotJson_;
}

bool VeryVCVHttpSnapshotServer::isRunning() const {
    return running_;
}

int VeryVCVHttpSnapshotServer::getPort() const {
    return port_;
}

// ===================== SERVER LOOP =====================
//
// Loop principale del mini server HTTP:
// - bind/listen su porta locale
// - attesa connessioni con timeout breve
// - gestione minimale di GET /snapshot e GET /health
// - nessuna logica di business: serve solo l'ultima snapshot JSON in cache
void VeryVCVHttpSnapshotServer::run() {
#if defined(__APPLE__) || defined(__linux__)
    // Creazione e configurazione del listening socket
    int localServerFd = ::socket(AF_INET, SOCK_STREAM, 0);
    if (localServerFd < 0) {
        running_ = false;
        return;
    }

    {
        std::lock_guard<std::mutex> lock(socketMutex_);
        serverFd_ = localServerFd;
    }

    int opt = 1;
    ::setsockopt(localServerFd, SOL_SOCKET, SO_REUSEADDR, &opt, sizeof(opt));

    sockaddr_in addr;
    std::memset(&addr, 0, sizeof(addr));
    addr.sin_family = AF_INET;
    addr.sin_addr.s_addr = htonl(INADDR_ANY);
    addr.sin_port = htons((uint16_t)port_);

    if (::bind(localServerFd, (sockaddr*)&addr, sizeof(addr)) < 0) {
        ::close(localServerFd);
        {
            std::lock_guard<std::mutex> lock(socketMutex_);
            serverFd_ = -1;
        }
        running_ = false;
        return;
    }

    if (::listen(localServerFd, 8) < 0) {
        ::close(localServerFd);
        {
            std::lock_guard<std::mutex> lock(socketMutex_);
            serverFd_ = -1;
        }
        running_ = false;
        return;
    }

    running_ = true;

    // Accept loop con timeout breve per permettere shutdown rapido
    while (!stopRequested_) {
        fd_set readfds;
        FD_ZERO(&readfds);
        FD_SET(localServerFd, &readfds);

        timeval tv;
        tv.tv_sec = 0;
        tv.tv_usec = 200000; // 200 ms

        int sel = ::select(localServerFd + 1, &readfds, nullptr, nullptr, &tv);
        if (sel < 0) {
            if (stopRequested_)
                break;
            continue;
        }
        if (sel == 0) {
            continue;
        }

        int clientFd = ::accept(localServerFd, nullptr, nullptr);
        if (clientFd < 0) {
            if (stopRequested_)
                break;
            continue;
        }

        // timeout anche sul client, per evitare recv bloccante lunga
        timeval clientTv;
        clientTv.tv_sec = 0;
        clientTv.tv_usec = 200000; // 200 ms
        ::setsockopt(clientFd, SOL_SOCKET, SO_RCVTIMEO, &clientTv, sizeof(clientTv));

        {
            std::lock_guard<std::mutex> lock(socketMutex_);
            activeClientFd_ = clientFd;
        }

        // Lettura della request HTTP e costruzione della response
        char buffer[4096];
        std::memset(buffer, 0, sizeof(buffer));
        ssize_t n = ::recv(clientFd, buffer, sizeof(buffer) - 1, 0);

        std::string body;
        std::string status;
        std::string contentType = "application/json";

        if (n > 0) {
            std::string req(buffer, (size_t)n);

            if (req.find("GET /snapshot ") == 0 || req.find("GET /snapshot\r") == 0) {
                body = getSnapshotJsonCopy();
                status = "HTTP/1.1 200 OK\r\n";
            }
            else if (req.find("GET /health ") == 0 || req.find("GET /health\r") == 0) {
                body = "ok";
                status = "HTTP/1.1 200 OK\r\n";
                contentType = "text/plain";
            }
            else {
                body = "{\"error\":\"not found\"}";
                status = "HTTP/1.1 404 Not Found\r\n";
            }

            std::ostringstream oss;
            oss << status
                << "Content-Type: " << contentType << "\r\n"
                << "Content-Length: " << body.size() << "\r\n"
                << "Connection: close\r\n\r\n"
                << body;

            std::string resp = oss.str();
            ::send(clientFd, resp.c_str(), resp.size(), 0);
        }

        ::close(clientFd);

        {
            std::lock_guard<std::mutex> lock(socketMutex_);
            if (activeClientFd_ == clientFd) {
                activeClientFd_ = -1;
            }
        }
    }

    // Cleanup finale dello stato runtime del server
    ::close(localServerFd);

    {
        std::lock_guard<std::mutex> lock(socketMutex_);
        if (serverFd_ == localServerFd) {
            serverFd_ = -1;
        }
        activeClientFd_ = -1;
    }

    running_ = false;
#endif
}
