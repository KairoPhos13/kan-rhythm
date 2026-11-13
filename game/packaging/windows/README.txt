kan! - Windows 10/11 x64 test build
==================================

What this is
------------
A TEST build of kan! (the rhythm game / level editor), assembled on
2026-11-09 from source before anything is published. It has not been run
on real Windows hardware before you, so surprises are expected - that is
exactly what this build is for.

How to run
----------
1. Unzip this folder anywhere (for example Desktop\kan).
2. Double-click kan.exe. There is no installer; nothing is installed.

Windows SmartScreen ("Windows protected your PC")
------------------------------------------------
The build is UNSIGNED, so Windows may show a blue warning. That is normal
for software without a code-signing certificate. To run it:
click "More info", then "Run anyway".

Source separation model (first use)
-----------------------------------
Separating a song into stems needs a ~200 MB model file. kan! downloads it
automatically the first time you use "Separate", into a models\ folder
next to kan.exe. The download comes from a pinned, unchangeable source and
is verified against a pinned SHA-256 before it is used; a wrong or
truncated file is deleted and never loaded. Separation is slow the first
time; progress is shown in the game.

CPU-only separation
-------------------
kan_forge_native.dll in this build was compiled with the CPU-only ggml
backend (KAN_GGML_BACKEND=CPU): separation runs on the CPU, no GPU/
Vulkan is used. That is deliberate for this test build. The regular CI
builds add a Vulkan GPU path and always keep the same CPU fallback.

Data, settings and logs
-----------------------
Everything kan! keeps lives in your user profile:
    %APPDATA%\kan
(settings, beatmaps, scores; logs in the logs\ folder there).
If you report a problem, attach the newest log file from that folder.

Reporting problems
------------------
Report anything that misbehaves - crashes, wrong notes, no sound, no
microphone - as an issue at github.com/KairoPhos13/kan-rhythm/issues, with:
  - what you did (steps to reproduce),
  - the log file from %APPDATA%\kan\logs,
  - your Windows version.
