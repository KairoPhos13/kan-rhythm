# kan! game client — build defaults (owner 2026-10-13: "leave all the
# compile options as defaults in the makefile").
#
# Everything the client build needs is defaulted here — no flags to remember:
#
#   make            # everything: native bridge + C# (forge, ruleset, deploy)
#   make test       # native self-test + C# suite
#   make run        # launch the client (pass ARGS=path/to/file.osz)
#   make clean      # remove build outputs
#
# Defaults:
#   BUILD_TYPE   = Release
#   GGML_VULKAN  = auto (ON when Vulkan headers + glslc exist — the RoFormer
#                  runtime then uses the GPU via ggml_backend_init_best();
#                  set KAN_DISABLE_VULKAN=1 to force CPU, or BSR_FORCE_CPU=1
#                  at runtime)
#   KAN_ENABLE_ROFORMER = ON (self-contained separation, editor-pipeline §7b)
#
# Each component stays self-contained (its own CMake project / dotnet
# project) — this file only sequences them.

BUILD_TYPE ?= Release
# dotnet may be user-local (~/.dotnet) on this machine — resolve it by default.
DOTNET ?= $(shell command -v dotnet 2>/dev/null || echo $$HOME/.dotnet/dotnet)
CMAKE ?= cmake
JOBS ?= $(shell nproc 2>/dev/null || echo 4)

NATIVE_DIR := forge/native
NATIVE_BUILD := $(NATIVE_DIR)/build
RULESET_DIR := ruleset
CLIENT_OUT := client/osu.Desktop/bin/$(BUILD_TYPE)/net10.0

.PHONY: all native forge ruleset deploy test test-native test-csharp run clean

all: native forge ruleset deploy

# --- native C-ABI bridge (libkan + RoFormer runtime + miniaudio) -----------
native:
	$(CMAKE) -S $(NATIVE_DIR) -B $(NATIVE_BUILD) -DCMAKE_BUILD_TYPE=$(BUILD_TYPE)
	$(CMAKE) --build $(NATIVE_BUILD) -j $(JOBS)

# --- C# sides (Kan.Forge library; ruleset plugin + deploy to the client) ---
forge:
	cd forge && $(DOTNET) build -c $(BUILD_TYPE)

ruleset: forge
	cd $(RULESET_DIR) && $(DOTNET) build -c $(BUILD_TYPE)

# The ruleset build copies Kan.Forge.dll / osu.Game.Rulesets.Kan.dll /
# libkan_forge_native.so next to the client executable (post-build target).
deploy: ruleset
	@test -f $(CLIENT_OUT)/osu.Game.Rulesets.Kan.dll && \
		test -f $(CLIENT_OUT)/libkan_forge_native.so && \
		echo "deployed to $(CLIENT_OUT)" || \
		(echo "deploy check failed" && exit 1)

# --- tests -----------------------------------------------------------------
test: test-native test-csharp

test-native: native
	ctest --test-dir $(NATIVE_BUILD) --output-on-failure

test-csharp: native
	cd forge/tests && $(DOTNET) test -c $(BUILD_TYPE)

# --- run -------------------------------------------------------------------
run: all
	cd $(CLIENT_OUT) && DOTNET_ROOT=$$HOME/.dotnet ./kan $(ARGS)

# --- clean -----------------------------------------------------------------
clean:
	rm -rf $(NATIVE_BUILD) forge/bin forge/obj forge/tests/bin forge/tests/obj \
		$(RULESET_DIR)/bin $(RULESET_DIR)/obj
