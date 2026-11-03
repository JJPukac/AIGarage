#include "GarageSafety.h"
#define CHECK(condition) if (!(condition)) return false


using aigarage::GarageSafety;
using aigarage::OpenResult;
constexpr int64_t Now = 1800000000;

constexpr GarageSafety Closed() {
    GarageSafety safety;
    safety.Observe(true, 0);
    safety.Observe(true, 100);
    return safety;
}

constexpr bool StartupAndRawInput() {
    GarageSafety safety;
    CHECK(safety.RequestOpen(1, Now + 10, Now, 0, true) == OpenResult::RejectedState);
    safety.Observe(true, 0);
    safety.Observe(true, 99);
    CHECK(safety.RequestOpen(2, Now + 10, Now, 99, true) == OpenResult::RejectedState);
    safety.Observe(true, 100);
    // Latest raw reading denies a stale debounced closed state.
    CHECK(safety.RequestOpen(3, Now + 10, Now, 101, false) == OpenResult::RejectedState);
    safety.Observe(false, 101);
    safety.Observe(true, 102);
    CHECK(safety.RequestOpen(4, Now + 10, Now, 201, true) == OpenResult::RejectedState);
    safety.Observe(true, 202);
    CHECK(safety.RequestOpen(5, Now + 10, Now, 202, true) == OpenResult::Pulsed);
    return true;
}

constexpr bool ExpiryReplayAndRejectedConsumption() {
    auto safety = Closed();
    CHECK(safety.RequestOpen(1, Now - 1, Now, 100, true) == OpenResult::RejectedExpired);
    CHECK(safety.RequestOpen(1, Now - 1, Now - 2, 100, true) == OpenResult::RejectedReplay);
    CHECK(safety.RequestOpen(2, Now + 16, Now, 100, true) == OpenResult::RejectedExpired);
    CHECK(safety.RequestOpen(3, Now + 10, 0, 100, true) == OpenResult::RejectedExpired);
    CHECK(safety.RequestOpen(4, Now + 10, Now, 100, false) == OpenResult::RejectedState);
    CHECK(safety.RequestOpen(4, Now + 10, Now, 100, true) == OpenResult::RejectedReplay);
    CHECK(safety.RequestOpen(0, Now + 10, Now, 100, true) == OpenResult::RejectedReplay);
    CHECK(safety.RequestOpen(5, Now, Now, 100, true) == OpenResult::Pulsed);
    CHECK(safety.RequestOpen(5, Now + 10, Now, 101, true) == OpenResult::RejectedReplay);
    CHECK(safety.RequestOpen(6, Now + 10, Now, 101, true) == OpenResult::RejectedState);
    return true;
}

constexpr bool PulseTimingAndFailedMovement() {
    auto safety = Closed();
    CHECK(safety.RequestOpen(1, Now + 10, Now, 100, true) == OpenResult::Pulsed);
    safety.MarkPulseStarted(110);
    CHECK(!safety.PulseDueToEnd(609));
    CHECK(safety.PulseDueToEnd(610));
    safety.CompletePulse();
    safety.Observe(true, 40000);
    CHECK(safety.RequestOpen(2, Now + 10, Now, 40000, true) == OpenResult::RejectedState);
    safety.NewCommandSession();
    CHECK(safety.RequestOpen(1, Now + 10, Now, 40001, true) == OpenResult::RejectedState);
    return true;
}

constexpr bool RealDepartureReturnAndCooldown() {
    auto safety = Closed();
    CHECK(safety.RequestOpen(1, Now + 10, Now, 100, true) == OpenResult::Pulsed);
    safety.CompletePulse();
    safety.Observe(false, 1000);
    safety.Observe(false, 1100);
    safety.Observe(true, 2000);
    safety.Observe(true, 2100);
    CHECK(safety.RequestOpen(2, Now + 10, Now, 2100, true) == OpenResult::RejectedState);
    safety.Observe(true, 30100);
    CHECK(safety.RequestOpen(3, Now + 10, Now, 30100, true) == OpenResult::Pulsed);
    return true;
}

constexpr bool MillisRollover() {
    GarageSafety safety;
    safety.Observe(true, UINT32_MAX - 50);
    safety.Observe(true, 49);
    CHECK(safety.RequestOpen(1, Now + 10, Now, 49, true) == OpenResult::Pulsed);
    safety.MarkPulseStarted(UINT32_MAX - 10);
    CHECK(!safety.PulseDueToEnd(488));
    CHECK(safety.PulseDueToEnd(489));
    return true;
}

static_assert(StartupAndRawInput(), "Startup, debounce or raw sensor interlock failed");
static_assert(ExpiryReplayAndRejectedConsumption(), "Expiry/replay or rejection consumption failed");
static_assert(PulseTimingAndFailedMovement(), "Pulse duration or failed-movement interlock failed");
static_assert(RealDepartureReturnAndCooldown(), "Departure/return cooldown interlock failed");
static_assert(MillisRollover(), "Millis rollover timing failed");
