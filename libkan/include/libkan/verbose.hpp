#pragma once
// libkan's informational console lines (what ran, which device, progress as
// text) are for a developer at a terminal: the game has its own progress
// display and logs, and printed them only as noise (owner 2026-10-25: "it
// was needed while debugging; now that it works it is unnecessary").
// KAN_VERBOSE=1 brings them back. Failures are always reported.
#include <cstdlib>

namespace kan {

/// Whether informational lines go to the console (KAN_VERBOSE set, and not "0").
inline bool verbose() {
    static const bool on = [] {
        const char* v = std::getenv("KAN_VERBOSE");
        return v != nullptr && *v != '\0' && *v != '0';
    }();
    return on;
}

} // namespace kan
