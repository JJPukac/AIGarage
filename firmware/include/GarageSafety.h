#pragma once

#include <stdint.h>

namespace aigarage {

enum class OpenResult { Pulsed, RejectedState, RejectedReplay, RejectedExpired };

// Pure state logic, independent of Arduino/network/sensors for host tests.
// A single reed confirms only the fully closed limit. A successful request
// permits one pulse; it never asserts that the garage actually opened.
class GarageSafety {
public:
    static constexpr uint32_t DebounceMs = 100;
    static constexpr uint32_t PulseMs = 500;
    static constexpr uint32_t MovementEstimateMs = 30000;
    static constexpr uint32_t CooldownMs = 30000;
    static constexpr int64_t MinimumValidEpoch = 1700000000;

    constexpr void Observe(bool closed, uint32_t now) {
        if (!initialized_) {
            initialized_ = true;
            rawClosed_ = closed;
            edgeAt_ = now;
        } else if (closed != rawClosed_) {
            rawClosed_ = closed;
            edgeAt_ = now;
        }

        if (Elapsed(now, edgeAt_) >= DebounceMs) {
            stableClosed_ = rawClosed_;
            ready_ = true;

            if (inhibited_ && !stableClosed_)
                sawNotClosed_ = true;

            // A failed-to-move door remains inhibited until a real departure
            // and return to closed are observed. MQTT reconnect never rearms it.
            if (inhibited_ && sawNotClosed_ && stableClosed_ &&
                !pulsing_ && Elapsed(now, lastPulseAt_) >= CooldownMs)
                inhibited_ = false;
        }
    }

    constexpr OpenResult RequestOpen(int64_t sequence, int64_t expires,
                           int64_t epoch, uint32_t now, bool currentRawClosed) {
        if (sequence <= 0 || sequence <= lastSequence_)
            return OpenResult::RejectedReplay;

        // Consume every new authenticated session sequence before checking
        // wall-clock expiry: a later clock rollback must never revive it.
        lastSequence_ = sequence;
        if (epoch < MinimumValidEpoch || expires < epoch ||
            expires - epoch > 15)
            return OpenResult::RejectedExpired;

        if (!ready_ || !stableClosed_ || !rawClosed_ ||
            !currentRawClosed || Elapsed(now, edgeAt_) < DebounceMs ||
            pulsing_ || inhibited_)
            return OpenResult::RejectedState;

        pulsing_ = true;
        inhibited_ = true;
        sawNotClosed_ = false;
        lastPulseAt_ = now;
        return OpenResult::Pulsed;
    }

    constexpr bool PulseDueToEnd(uint32_t now) const {
        return pulsing_ && Elapsed(now, lastPulseAt_) >= PulseMs;
    }

    constexpr void CompletePulse() { pulsing_ = false; }
    constexpr void MarkPulseStarted(uint32_t now) { lastPulseAt_ = now; }
    constexpr bool IsPulsing() const { return pulsing_; }

    // New MQTT-session nonce invalidates all prior signed/retained commands.
    // Sensor/movement interlocks intentionally survive reconnect.
    constexpr void NewCommandSession() { lastSequence_ = 0; }

    const char* DoorState(uint32_t now) const {
        if (!ready_) return "unknown";
        if (inhibited_ && Elapsed(now, lastPulseAt_) < MovementEstimateMs)
            return "moving_estimated";
        return stableClosed_ ? "closed" : "not_closed";
    }

private:
    static constexpr uint32_t Elapsed(uint32_t now, uint32_t since) {
        return now - since; // unsigned subtraction tolerates millis() rollover.
    }

    bool initialized_ = false;
    bool rawClosed_ = false;
    bool stableClosed_ = false;
    bool ready_ = false;
    bool pulsing_ = false;
    bool inhibited_ = false;
    bool sawNotClosed_ = false;
    uint32_t edgeAt_ = 0;
    uint32_t lastPulseAt_ = 0;
    int64_t lastSequence_ = 0;
};

} // namespace aigarage
