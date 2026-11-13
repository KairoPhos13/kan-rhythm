# kan! game client: build defaults (owner 2026-10-13: "leave all the
# compile options as defaults in the makefile").
#
# Everything the client build needs is defaulted here, no flags to remember:
#
#   make            # everything: native bridge + C# (forge, game client, ruleset, deploy)
#   make test       # native self-test + C# suites (core: no osu! needed; forge)
#   make check      # before a commit: kan's C# built with warnings as errors, then every test
#   make run        # launch the client (pass ARGS=path/to/file.osz)
#   make clean      # remove build outputs
#
# Defaults:
#   BUILD_TYPE   = Release
#   KAN_VERSION  = 0.0.0 (version stamp for kan_forge_version() and the
#                  kan assemblies; tag builds pass the release version)
#   GGML_VULKAN  = auto (ON when Vulkan headers + glslc exist: the RoFormer
#                  runtime then uses the GPU via ggml_backend_init_best();
#                  set KAN_DISABLE_VULKAN=1 to force CPU, or BSR_FORCE_CPU=1
#                  at runtime)
#   KAN_ENABLE_ROFORMER = ON (self-contained separation, editor-pipeline §7b)
#
# The native bridge always builds for the portable CPU baseline (x86-64-v2
# on x86_64, arm64 on Apple silicon), never -march=native: it is the
# shipped library (libkan/cmake/kan_build_policy.cmake, audit C2).
#
# Each component stays self-contained (its own CMake project / dotnet
# project); this file only sequences them.

BUILD_TYPE ?= Release
KAN_VERSION ?= 0.0.0
# dotnet may be user-local (~/.dotnet) on this machine. Resolve it by default.
DOTNET ?= $(shell command -v dotnet 2>/dev/null || echo $$HOME/.dotnet/dotnet)
CMAKE ?= cmake
JOBS ?= $(shell nproc 2>/dev/null || echo 4)

NATIVE_DIR := forge/native
NATIVE_BUILD := $(NATIVE_DIR)/build
RULESET_DIR := ruleset
CLIENT_OUT := client/osu.Desktop/bin/$(BUILD_TYPE)/net10.0

.PHONY: all native forge client ruleset deploy test test-native test-csharp check run clean

all: native forge client ruleset deploy

# --- native C-ABI bridge (libkan + RoFormer runtime + miniaudio) -----------
native:
	$(CMAKE) -S $(NATIVE_DIR) -B $(NATIVE_BUILD) -DCMAKE_BUILD_TYPE=$(BUILD_TYPE) \
		-DKAN_VERSION=$(KAN_VERSION)
	$(CMAKE) --build $(NATIVE_BUILD) -j $(JOBS)

# --- C# sides (Kan.Forge library; ruleset plugin + deploy to the client) ---
forge:
	cd forge && $(DOTNET) build -c $(BUILD_TYPE)

# The game client itself: fork changes (game/client, fork-divergence.md) only
# reach the executable's folder through this build: the plugin deploy below
# copies kan files only.
#
# `cd` into the fork on purpose (2026-11-12, release-hardening lane): dotnet
# must see the fork's physical path. In an agent worktree game/client is a
# symlink to the fork checkout (the worktree model, docs/agent-workflow.md),
# and entering a project under that link by path spelling breaks NuGet's
# restore: the fork's own project links then carry a second spelling of the
# same path and their package flow is lost (osu.Desktop compiles without
# osu.Framework). -p:CleanFile gives this leg its own MSBuild output
# bookkeeping: the kan legs reach the fork through the link and MSBuild's
# incremental clean compares paths as strings, so one shared list makes each
# leg delete the files the other leg just wrote (CS0006 on the reference
# assembly). In a normal checkout all spellings are the same directory and
# nothing changes.
client:
	cd client/osu.Desktop && $(DOTNET) build -c $(BUILD_TYPE) \
		-p:CleanFile=FileListAbsolute.fork.txt

ruleset: forge client
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
	cd core/tests && $(DOTNET) test -c $(BUILD_TYPE)
	cd forge/tests && $(DOTNET) test -c $(BUILD_TYPE)

# One gate before a commit: kan's own C# (core, forge, ruleset) must build
# without a warning (the fork's is upstream's), and every test must pass.
# NuGet's security audit of the fork's dependencies: low and moderate
# advisories (NU1901, NU1902) are shown, not fatal (a new advisory is
# fixed in the fork (fork-divergence.md F-18) or by updating it from
# upstream, not in kan's code); high and critical advisories (NU1903,
# NU1904) fail the build (audit C6).
STRICT := -warnaserror -p:WarningsNotAsErrors="NU1901%3BNU1902"

check: native client
	cd core && $(DOTNET) build -c $(BUILD_TYPE) $(STRICT)
	cd forge && $(DOTNET) build -c $(BUILD_TYPE) $(STRICT)
	cd $(RULESET_DIR) && $(DOTNET) build -c $(BUILD_TYPE) $(STRICT)
	$(MAKE) test

# --- run -------------------------------------------------------------------
run: all
	cd $(CLIENT_OUT) && DOTNET_ROOT=$$HOME/.dotnet ./kan $(ARGS)

# --- clean -----------------------------------------------------------------
clean:
	rm -rf $(NATIVE_BUILD) core/bin core/obj core/tests/bin core/tests/obj \
		forge/bin forge/obj forge/tests/bin forge/tests/obj \
		$(RULESET_DIR)/bin $(RULESET_DIR)/obj
