//  VeryVCVBridgeOSC.cpp
//  Created by Giuseppe Bergamino on 23/04/26.
//

#include "VeryVCVBridgeOSC.hpp"

#include <vector>
#include <cstring>
#include <cerrno>
#include <cstdint>
#include <chrono> // timing for IS2 test

#if defined(__APPLE__) || defined(__linux__)
#include <sys/types.h>
#include <sys/socket.h>
#include <sys/select.h>
#include <arpa/inet.h>
#include <netinet/in.h>
#include <unistd.h>
#include <fcntl.h>
#endif

// MVP POSIX/BSD sockets per macOS/Linux.
// Per Windows servirebbe una variante Winsock dedicata.

namespace {

double oscReceiverNowMs() { //add for IS2
    using clock = std::chrono::steady_clock;
    const auto now = clock::now().time_since_epoch();
    return std::chrono::duration<double, std::milli>(now).count();
}

// ===================== OSC PACKING HELPERS =====================
void appendOscString(std::vector<uint8_t>& buffer, const std::string& s) {
    buffer.insert(buffer.end(), s.begin(), s.end());
    buffer.push_back('\0');

    while (buffer.size() % 4 != 0) {
        buffer.push_back('\0');
    }
}

void appendOscInt32(std::vector<uint8_t>& buffer, int32_t value) {
    uint32_t be = htonl(static_cast<uint32_t>(value));
    const uint8_t* p = reinterpret_cast<const uint8_t*>(&be);
    buffer.insert(buffer.end(), p, p + 4);
}

void appendOscFloat32(std::vector<uint8_t>& buffer, float value) {
    static_assert(sizeof(float) == 4, "OSC float32 requires 4-byte float");

    uint32_t raw = 0;
    std::memcpy(&raw, &value, sizeof(uint32_t));
    raw = htonl(raw);

    const uint8_t* p = reinterpret_cast<const uint8_t*>(&raw);
    buffer.insert(buffer.end(), p, p + 4);
}


// ===================== OSC UNPACKING HELPERS =====================
bool readOscString(const uint8_t* data, size_t size, size_t& offset, std::string& outString) {
    if (!data || offset >= size) {
        return false;
    }

    size_t start = offset;

    while (offset < size && data[offset] != 0) {
        offset++;
    }

    if (offset >= size) {
        return false;
    }

    outString.assign(reinterpret_cast<const char*>(data + start), offset - start);

    offset++; // skip '\0'

    while (offset % 4 != 0) {
        offset++;
    }

    return offset <= size;
}

bool readOscInt32(const uint8_t* data, size_t size, size_t& offset, int32_t& outValue) {
    if (!data || offset + 4 > size) {
        return false;
    }

    uint32_t raw = 0;
    raw |= static_cast<uint32_t>(data[offset]) << 24;
    raw |= static_cast<uint32_t>(data[offset + 1]) << 16;
    raw |= static_cast<uint32_t>(data[offset + 2]) << 8;
    raw |= static_cast<uint32_t>(data[offset + 3]);

    outValue = static_cast<int32_t>(raw);
    offset += 4;
    return true;
}

bool readOscFloat32(const uint8_t* data, size_t size, size_t& offset, float& outValue) {
    if (!data || offset + 4 > size) {
        return false;
    }

    uint32_t raw = 0;
    raw |= static_cast<uint32_t>(data[offset]) << 24;
    raw |= static_cast<uint32_t>(data[offset + 1]) << 16;
    raw |= static_cast<uint32_t>(data[offset + 2]) << 8;
    raw |= static_cast<uint32_t>(data[offset + 3]);

    offset += 4;
    std::memcpy(&outValue, &raw, sizeof(uint32_t));
    return true;
}


// ===================== MESSAGE PARSERS =====================
bool tryParseParamSetMessage(
    const uint8_t* data,
    size_t size,
    veryvcv::oscproto::ParamSetMessage& outMessage
) {
    using namespace veryvcv::oscproto;

    size_t offset = 0;
    std::string address;
    std::string typeTag;

    if (!readOscString(data, size, offset, address)) {
        return false;
    }

    if (address != kParamSetAddress) {
        return false;
    }

    if (!readOscString(data, size, offset, typeTag)) {
        return false;
    }

    if (typeTag != kParamSetTypeTag) {
        return false;
    }

    int32_t paramId = -1;
    float normalizedValue = 0.f;

    if (!readOscString(data, size, offset, outMessage.patchSessionId)) {
        return false;
    }

    if (!readOscString(data, size, offset, outMessage.moduleId)) {
        return false;
    }

    if (!readOscInt32(data, size, offset, paramId)) {
        return false;
    }

    if (!readOscFloat32(data, size, offset, normalizedValue)) {
        return false;
    }

    outMessage.paramId = paramId;
    outMessage.normalizedValue = normalizedValue;
    return true;
}

bool tryParseSnapshotProfileMessage( //for IS2 testing
    const uint8_t* data,
    size_t size,
    veryvcv::oscproto::SnapshotProfileMessage& outMessage
) {
    using namespace veryvcv::oscproto;

    size_t offset = 0;
    std::string address;
    std::string typeTag;

    if (!readOscString(data, size, offset, address)) {
        return false;
    }

    if (address != kSnapshotProfileAddress) {
        return false;
    }

    if (!readOscString(data, size, offset, typeTag)) {
        return false;
    }

    if (typeTag != kSnapshotProfileTypeTag) {
        return false;
    }

    int32_t revision = 0;
    int32_t status = 0;
    float httpMs = 0.f;
    float parseMs = 0.f;
    float instantiateMs = 0.f;
    float clientTotalMs = 0.f;

    if (!readOscInt32(data, size, offset, revision)) {
        return false;
    }

    if (!readOscInt32(data, size, offset, status)) {
        return false;
    }

    if (!readOscFloat32(data, size, offset, httpMs)) {
        return false;
    }

    if (!readOscFloat32(data, size, offset, parseMs)) {
        return false;
    }

    if (!readOscFloat32(data, size, offset, instantiateMs)) {
        return false;
    }

    if (!readOscFloat32(data, size, offset, clientTotalMs)) {
        return false;
    }

    outMessage.revision = revision;
    outMessage.status = status;
    outMessage.httpMs = httpMs;
    outMessage.parseMs = parseMs;
    outMessage.instantiateMs = instantiateMs;
    outMessage.clientTotalMs = clientTotalMs;

    return true;
}

} // anonymous namespace


// ===================== SENDER =====================
VeryVCVOscSender::VeryVCVOscSender(const std::string& host, int port)
    : host_(host), port_(port) {
}

void VeryVCVOscSender::setTargetHost(const std::string& host) {
    host_ = host;
}

void VeryVCVOscSender::setTargetPort(int port) {
    port_ = port;
}

std::string VeryVCVOscSender::getTargetHost() const {
    return host_;
}

int VeryVCVOscSender::getTargetPort() const {
    return port_;
}

bool VeryVCVOscSender::sendSnapshotAvailable(const veryvcv::oscproto::SnapshotAvailableMessage& msg) const {
#if defined(__APPLE__) || defined(__linux__)
    using namespace veryvcv::oscproto;

    int sock = ::socket(AF_INET, SOCK_DGRAM, 0);
    if (sock < 0) {
        return false;
    }

    sockaddr_in addr;
    std::memset(&addr, 0, sizeof(addr));
    addr.sin_family = AF_INET;
    addr.sin_port = htons(static_cast<uint16_t>(port_));

    if (::inet_pton(AF_INET, host_.c_str(), &addr.sin_addr) != 1) {
        ::close(sock);
        return false;
    }

    std::vector<uint8_t> packet;
    packet.reserve(128);

    // address: /veryvcv/snapshot/available
    // args: string patchSessionId, int revision
    appendOscString(packet, kSnapshotAvailableAddress);
    appendOscString(packet, kSnapshotAvailableTypeTag);
    appendOscString(packet, msg.patchSessionId);
    appendOscInt32(packet, msg.revision);

    ssize_t sent = ::sendto(
        sock,
        packet.data(),
        packet.size(),
        0,
        reinterpret_cast<sockaddr*>(&addr),
        sizeof(addr)
    );

    ::close(sock);
    return sent == static_cast<ssize_t>(packet.size());
#else
    return false;
#endif
}

bool VeryVCVOscSender::sendParamUpdate(const veryvcv::oscproto::ParamUpdateMessage& msg) const {
#if defined(__APPLE__) || defined(__linux__)
    using namespace veryvcv::oscproto;

    int sock = ::socket(AF_INET, SOCK_DGRAM, 0);
    if (sock < 0) {
        return false;
    }

    sockaddr_in addr;
    std::memset(&addr, 0, sizeof(addr));
    addr.sin_family = AF_INET;
    addr.sin_port = htons(static_cast<uint16_t>(port_));

    if (::inet_pton(AF_INET, host_.c_str(), &addr.sin_addr) != 1) {
        ::close(sock);
        return false;
    }

    std::vector<uint8_t> packet;
    packet.reserve(128);

    // address: /veryvcv/param/update
    // args: string patchSessionId, int revision, string moduleId, int paramId, float normalizedValue
    appendOscString(packet, kParamUpdateAddress);
    appendOscString(packet, kParamUpdateTypeTag);
    appendOscString(packet, msg.patchSessionId);
    appendOscInt32(packet, msg.revision);
    appendOscString(packet, msg.moduleId);
    appendOscInt32(packet, msg.paramId);
    appendOscFloat32(packet, msg.normalizedValue);

    ssize_t sent = ::sendto(
        sock,
        packet.data(),
        packet.size(),
        0,
        reinterpret_cast<sockaddr*>(&addr),
        sizeof(addr)
    );

    ::close(sock);
    return sent == static_cast<ssize_t>(packet.size());
#else
    return false;
#endif
}

bool VeryVCVOscSender::sendSnapshotAvailable(const std::string& patchSessionId, int revision) const {
    veryvcv::oscproto::SnapshotAvailableMessage msg;
    msg.patchSessionId = patchSessionId;
    msg.revision = revision;
    return sendSnapshotAvailable(msg);
}

bool VeryVCVOscSender::sendParamUpdate(
    const std::string& patchSessionId,
    int revision,
    const std::string& moduleId,
    int paramId,
    float normalizedValue
) const {
    veryvcv::oscproto::ParamUpdateMessage msg;
    msg.patchSessionId = patchSessionId;
    msg.revision = revision;
    msg.moduleId = moduleId;
    msg.paramId = paramId;
    msg.normalizedValue = normalizedValue;
    return sendParamUpdate(msg);
}


// ===================== RECEIVER =====================
VeryVCVOscReceiver::VeryVCVOscReceiver(int port)
    : port_(port) {
}

VeryVCVOscReceiver::~VeryVCVOscReceiver() {
    stop();
}

void VeryVCVOscReceiver::start() {
#if defined(__APPLE__) || defined(__linux__)
    if (running_ || thread_.joinable()) {
        return;
    }

    stopRequested_ = false;
    thread_ = std::thread(&VeryVCVOscReceiver::run, this);
#endif
}

void VeryVCVOscReceiver::stop() {
#if defined(__APPLE__) || defined(__linux__)
    stopRequested_ = true;

    if (thread_.joinable()) {
        thread_.join();
    }

    running_ = false;
#endif
}

bool VeryVCVOscReceiver::isRunning() const {
    return running_;
}

int VeryVCVOscReceiver::getListenPort() const {
    return port_;
}

bool VeryVCVOscReceiver::popNextParamSet(veryvcv::oscproto::ParamSetMessage& outMessage) {
    std::lock_guard<std::mutex> lock(queueMutex_);

    if (paramSetQueue_.empty()) {
        return false;
    }

    outMessage = paramSetQueue_.front();
    paramSetQueue_.pop();
    return true;
}

bool VeryVCVOscReceiver::popNextSnapshotProfile(veryvcv::oscproto::SnapshotProfileMessage& outMessage) { //for IS2
    std::lock_guard<std::mutex> lock(queueMutex_);

    if (snapshotProfileQueue_.empty()) {
        return false;
    }

    outMessage = snapshotProfileQueue_.front();
    snapshotProfileQueue_.pop();
    return true;
}

void VeryVCVOscReceiver::run() {
#if defined(__APPLE__) || defined(__linux__)
    int sock = ::socket(AF_INET, SOCK_DGRAM, 0);
    if (sock < 0) {
        running_ = false;
        return;
    }

    int opt = 1;
    ::setsockopt(sock, SOL_SOCKET, SO_REUSEADDR, &opt, sizeof(opt));

    int flags = ::fcntl(sock, F_GETFL, 0);
    if (flags >= 0) {
        ::fcntl(sock, F_SETFL, flags | O_NONBLOCK);
    }

    sockaddr_in addr;
    std::memset(&addr, 0, sizeof(addr));
    addr.sin_family = AF_INET;
    addr.sin_addr.s_addr = htonl(INADDR_ANY);
    addr.sin_port = htons(static_cast<uint16_t>(port_));

    if (::bind(sock, reinterpret_cast<sockaddr*>(&addr), sizeof(addr)) < 0) {
        ::close(sock);
        running_ = false;
        return;
    }

    running_ = true;

    while (!stopRequested_) {
        fd_set readfds;
        FD_ZERO(&readfds);
        FD_SET(sock, &readfds);

        timeval tv;
        tv.tv_sec = 0;
        tv.tv_usec = 200000; // 200 ms

        int sel = ::select(sock + 1, &readfds, nullptr, nullptr, &tv);

        if (stopRequested_) {
            break;
        }

        if (sel < 0) {
            if (errno == EINTR) {
                continue;
            }
            continue;
        }

        if (sel == 0) {
            continue;
        }

        if (!FD_ISSET(sock, &readfds)) {
            continue;
        }

        uint8_t buffer[2048];
        std::memset(buffer, 0, sizeof(buffer));

        sockaddr_in fromAddr;
        socklen_t fromLen = sizeof(fromAddr);
        ssize_t received = ::recvfrom(
            sock,
            buffer,
            sizeof(buffer),
            0,
            reinterpret_cast<sockaddr*>(&fromAddr),
            &fromLen
        );

        if (received <= 0) {
            continue;
        }

        veryvcv::oscproto::ParamSetMessage paramMsg;
        if (tryParseParamSetMessage(buffer, static_cast<size_t>(received), paramMsg)) {
            std::lock_guard<std::mutex> lock(queueMutex_);
            paramSetQueue_.push(paramMsg);
            continue;
        }

        veryvcv::oscproto::SnapshotProfileMessage profileMsg;
        if (tryParseSnapshotProfileMessage(buffer, static_cast<size_t>(received), profileMsg)) {
            profileMsg.receivedAtMs = oscReceiverNowMs();

            std::lock_guard<std::mutex> lock(queueMutex_);
            snapshotProfileQueue_.push(profileMsg);
            continue;
        }
    }

    ::close(sock);
    running_ = false;
#endif
}
