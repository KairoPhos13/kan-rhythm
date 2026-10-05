# tools/

Command-line utilities that support development and content creation for
kan-rhythm.

**Status:** planned — this folder is a placeholder.

## Intended tools

- **Chart validator** — schema/timing/pitch checks for files in `charts/`
- **Audio analysis** — offline onset/pitch extraction to help author charts
- **Latency calibration** — measure and report input/audio offset per device
- **Asset packer** — process files from `assets/` for the game client

## Ground rules

- English only (see root `CONTRIBUTING.md`)
- One executable per tool, each in its own subfolder with its own
  `CMakeLists.txt`, wired into the root superbuild
- Tools may depend on `libkan/`; `libkan/` must never depend on tools
- Each tool stays extractable into its own repository later
