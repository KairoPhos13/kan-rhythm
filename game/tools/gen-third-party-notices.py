#!/usr/bin/env python3
"""Generate THIRD_PARTY_NOTICES.md for the kan! client (game/client/).

Collects the license metadata of the osu!lazer fork and every NuGet package
in the osu.Desktop dependency closure, and writes a notices file that ships
with the client and is shown on the in-game about screen (ADR-0010 §5,
osu-integration.md §4). The native components, the osu! resources' fonts
and the separation model that is downloaded at first use are named by hand
in sections 1, 5 and 6 (they carry no NuGet metadata).

Procedure (also usable manually; see game/docs/environment.md):

1. Restore the fork once so the package graph exists:
       cd game/client && dotnet restore osu.Desktop/osu.Desktop.csproj
2. Run this script from anywhere:
       python3 game/tools/gen-third-party-notices.py
3. Review the diff of game/client/THIRD_PARTY_NOTICES.md and commit it in
   the fork repository (it is embedded into osu.Game and displayed by the
   about screen).

A lane that has the fork checked out elsewhere (read-only) can still
generate the file: point --fork-dir at that checkout and write a hand-off
copy for the fork lane to commit into the fork:

       python3 game/tools/gen-third-party-notices.py \
           --fork-dir /path/to/kan/game/client \
           --out game/tools/out/THIRD_PARTY_NOTICES.md

License texts of the individual packages are not copied verbatim here (they
are redistributable inside the NuGet packages themselves and each package
carries its own license file); this file records the license *identity* of
every redistributed component plus the full text of the base project's MIT
licence. The procedure to extract any individual text is documented in the
generated file.

Dependency policy: MIT / MIT-0 / BSD / Apache-2.0 / Zlib / ISC / CC0 only
(root CONTRIBUTING.md §6). Anything else is reported as a warning.
"""

from __future__ import annotations

import argparse
import datetime
import json
import pathlib
import re
import sys
import xml.etree.ElementTree as ET

REPO_ROOT = pathlib.Path(__file__).resolve().parents[2]
DEFAULT_FORK_DIR = REPO_ROOT / "game" / "client"
NUGET_CACHE = pathlib.Path.home() / ".nuget" / "packages"

PERMISSIVE = ("MIT", "MIT-0", "BSD", "Apache", "Zlib", "ISC", "CC0", "CC-PDDC",
              "0BSD", "BlueOak", "MPL-2.0", "Unlicense", "PostgreSQL", "WTFPL",
              "Microsoft .NET library licence", "public domain")


def package_dirs(assets_json: pathlib.Path) -> list[tuple[str, str]]:
    """Return (id, version) for every package in the osu.Desktop closure."""
    assets = json.loads(assets_json.read_text(encoding="utf-8"))
    out = []
    for key, meta in assets.get("libraries", {}).items():
        if meta.get("type") != "package":
            continue
        pkg_id, version = key.split("/", 1)
        out.append((pkg_id, version))
    return sorted(out, key=lambda t: t[0].lower())


def sniff_licence_text(text: str) -> str:
    """Classify a licence file's content into a short identifier."""
    lowered = text[:4000].lower()
    if "gnu lesser general public" in lowered:
        return "LGPL (see licence file in package)"
    if "gnu general public license" in lowered:
        return "GPL (see licence file in package)"
    if "mozilla public license" in lowered:
        return "MPL-2.0 (see licence file in package)"
    if "apache license" in lowered:
        return "Apache-2.0 (see licence file in package)"
    if "permission is hereby granted, free of charge" in lowered:
        return "MIT (see licence file in package)"
    if "the unlicense" in lowered or "unencumbered" in lowered:
        return "Unlicense (see licence file in package)"
    if "zlib software license" in lowered or "this software is provided 'as-is'" in lowered:
        return "Zlib (see licence file in package)"
    if "isc license" in lowered:
        return "ISC (see licence file in package)"
    if "bsd" in lowered:
        return "BSD (see licence file in package)"
    return "(custom; see licence file in package)"


def nuspec_metadata(pkg_id: str, version: str) -> dict[str, str]:
    """Extract license/project/repository metadata from a cached .nuspec."""
    meta = {"license": "(unknown)", "project": "", "repo": ""}
    pkg_dir = NUGET_CACHE / pkg_id.lower() / version
    nuspecs = sorted(pkg_dir.glob("*.nuspec")) if pkg_dir.is_dir() else []
    if not nuspecs:
        meta["license"] = "(package not found in NuGet cache; restore first)"
        return meta

    root = ET.fromstring(nuspecs[0].read_text(encoding="utf-8"))
    ns = re.match(r"\{.*\}", root.tag)
    prefix = ns.group(0) if ns else ""

    license_el = root.find(f"{prefix}metadata/{prefix}license")
    if license_el is not None:
        value = (license_el.text or "").strip()
        if license_el.get("type") == "file" or re.match(r"^[A-Za-z0-9._-]+$", value) \
                and value.lower().startswith(("license", "copying", "notice")):
            # `<license type="file">LICENSE.txt</license>`: resolve by content.
            candidates = [pkg_dir / value] + sorted(pkg_dir.glob("LICENSE*")) \
                + sorted(pkg_dir.glob("COPYING*"))
            for candidate in candidates:
                if candidate.is_file():
                    meta["license"] = sniff_licence_text(
                        candidate.read_text(encoding="utf-8", errors="replace"))
                    break
            else:
                meta["license"] = f"(licence file '{value}' not present in cache)"
        else:
            meta["license"] = value
    else:
        url_el = root.find(f"{prefix}metadata/{prefix}licenseUrl")
        if url_el is not None and url_el.text:
            url = url_el.text.strip()
            if "fwlink" in url or "microsoft" in url.lower():
                meta["license"] = "Microsoft .NET library licence (see licenceUrl)"
            else:
                meta["license"] = f"see {url}"
        else:
            # No metadata at all: fall back to sniffing any licence file.
            for candidate in sorted(pkg_dir.glob("LICENSE*")) + sorted(pkg_dir.glob("COPYING*")):
                if candidate.is_file():
                    meta["license"] = sniff_licence_text(
                        candidate.read_text(encoding="utf-8", errors="replace"))
                    break

    project_el = root.find(f"{prefix}metadata/{prefix}projectUrl")
    if project_el is not None and project_el.text:
        meta["project"] = project_el.text.strip()

    repo_el = root.find(f"{prefix}metadata/{prefix}repository")
    if repo_el is not None:
        url = repo_el.get("url", "").strip()
        commit = repo_el.get("commit", "").strip()
        meta["repo"] = url + (f" ({commit[:12]})" if commit else "")

    return meta


def parse_args(argv: list[str]) -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Generate THIRD_PARTY_NOTICES.md for the kan! client.")
    parser.add_argument(
        "--fork-dir", type=pathlib.Path, default=DEFAULT_FORK_DIR,
        help="checkout of the osu! fork to read (osu.Desktop/obj/"
             "project.assets.json and LICENCE; default: game/client)")
    parser.add_argument(
        "--out", type=pathlib.Path, default=None,
        help="output path (default: <fork-dir>/THIRD_PARTY_NOTICES.md, the "
             "file the fork embeds and the about screen shows)")
    return parser.parse_args(argv)


def main(argv: list[str] | None = None) -> int:
    args = parse_args(sys.argv[1:] if argv is None else argv)
    fork_dir = args.fork_dir
    assets_json = fork_dir / "osu.Desktop" / "obj" / "project.assets.json"
    fork_licence = fork_dir / "LICENCE"
    output = args.out if args.out is not None \
        else fork_dir / "THIRD_PARTY_NOTICES.md"

    if not assets_json.is_file():
        print(f"error: {assets_json} not found: run `dotnet restore` in the "
              "fork first", file=sys.stderr)
        return 1

    packages = package_dirs(assets_json)
    warnings: list[str] = []
    hard_warnings: list[str] = []

    rows = []
    for pkg_id, version in packages:
        meta = nuspec_metadata(pkg_id, version)
        license_id = meta["license"]
        if "LGPL" in license_id or "GPL" in license_id or "AGPL" in license_id \
                or "Affero" in license_id:
            hard_warnings.append(f"{pkg_id} {version}: license '{license_id}': GPL-family "
                                 "licence inherited from upstream osu!lazer. Kept as "
                                 "separate assemblies/libraries with dynamic linking "
                                 "(ADR-0013); re-review before any paid release "
                                 "(CONTRIBUTING.md §6).")
        elif not re.search(r"\b(MIT|BSD|Apache|Zlib|ISC|CC0|MPL|Unlicense|PostgreSQL|WTFPL|BlueOak|domain)\b",
                           license_id) \
                and not any(p in license_id for p in PERMISSIVE):
            warnings.append(f"{pkg_id} {version}: license '{license_id}' is not on the "
                            "permissive allow-list: verify manually (CONTRIBUTING.md §6)")
        rows.append((pkg_id, version, license_id, meta["project"] or meta["repo"]))

    licence_text = (fork_licence.read_text(encoding="utf-8").strip()
                    if fork_licence.is_file() else "(fork LICENCE file missing)")

    today = datetime.date.today().isoformat()

    lines: list[str] = []
    lines.append("# THIRD PARTY NOTICES: kan! client")
    lines.append("")
    lines.append(f"Generated {today} by `game/tools/gen-third-party-notices.py` "
                 "(monorepo kan-rhythm). Do not edit by hand: regenerate instead.")
    lines.append("")
    lines.append("The kan! client is built on the osu!lazer fork (github.com/KairoPhos13/kan, "
                 "itself a fork of `ppy/osu`). This file lists every redistributed "
                 "third-party component and its licence, and is shown on the in-game "
                 "about screen. Licence *texts* of individual packages ship inside the "
                 "corresponding NuGet package (`~/.nuget/packages/<id>/<version>/`); "
                 "extract any of them with:")
    lines.append("")
    lines.append("    unzip -p ~/.nuget/packages/<id>/<version>/<id>.<version>.nupkg "
                 "<path-inside> ")
    lines.append("")
    lines.append("## 1. Base project")
    lines.append("")
    lines.append("- **osu!lazer** (`ppy/osu`): MIT Licence, Copyright (c) ppy Pty Ltd "
                 "<contact@ppy.sh> and contributors. kan! is a rebranded fork (ADR-0011); "
                 "upstream copyright headers are preserved in every adapted file.")
    lines.append("- **osu!framework** (`ppy/osu-framework`): MIT Licence, Copyright (c) "
                 "ppy Pty Ltd <contact@ppy.sh> and contributors.")
    lines.append("- **osu! Game Resources** (`ppy.osu.Game.Resources`, © ppy Pty Ltd): CC BY-NC 4.0 for most content (https://github.com/ppy/osu-resources). Used non-commercially; kan!'s own art and sounds replace them as they are made (`brand/`). The osu! name and logo are ppy's trademarks.")
    lines.append("- **Fonts** in the osu! resources, each under its own licence: Inter and Noto (SIL Open Font License); Torus, Torus-Alternate and Venera (their upstream licence files state that distribution requires a commercial licence).")
    lines.append("")
    lines.append("Full text of the base project's licence (fork `LICENCE`):")
    lines.append("")
    lines.append("```")
    lines.append(licence_text)
    lines.append("```")
    lines.append("")
    lines.append("## 2. BASS audio library (un4seen): special terms")
    lines.append("")
    lines.append("osu!framework's audio backend uses **BASS / BASSMIX / BASSFX** "
                 "and, on Windows, **BASSWASAPI** (© 1999-2026 Un4seen "
                 "Developments). BASS is **not** open source: it is "
                 "free for non-commercial use and requires a commercial licence otherwise. "
                 "kan!'s status (donation-funded) is analysed in "
                 "`game/docs/bass-licensing.md`; the outcome of that analysis must be "
                 "resolved before any donation-driven release (ADR-0010 §5). "
                 "See https://www.un4seen.com/ for the authoritative terms.")
    lines.append("")
    lines.append("## 3. NuGet package closure (osu.Desktop)")
    lines.append("")
    lines.append(f"{len(rows)} packages at generation time:")
    lines.append("")
    lines.append("| Package | Version | Licence | Project |")
    lines.append("|---|---|---|---|")
    for pkg_id, version, license_id, link in rows:
        lines.append(f"| `{pkg_id}` | {version} | {license_id} | {link} |")
    lines.append("")

    if warnings or hard_warnings:
        lines.append("## 4. Warnings from the generator")
        lines.append("")
        for w in hard_warnings:
            lines.append(f"- ⚠⚠ {w}")
        for w in warnings:
            lines.append(f"- ⚠ {w}")
        lines.append("")

    lines.append("## 5. Native / platform components")
    lines.append("")
    lines.append("Shipped as native binaries next to the executable; section 3 lists "
                 "the managed wrapper packages, and each package carries its own "
                 "licence text.")
    lines.append("")
    lines.append("- **FFmpeg 4.x** (`libavcodec` 58, `libavformat` 58, `libavutil` 56, "
                 "`libswscale` 5): redistributed in every kan! zip via "
                 "`ppy.osu.Framework.NativeLibs` (osu!framework's native "
                 "libraries). Licence: **LGPL 2.1 or later** (build-dependent). "
                 "The binaries stay separate libraries that are loaded "
                 "dynamically, and their licence terms travel with them; "
                 "ADR-0013 of the kan-rhythm monorepo records the inherited "
                 "LGPL set.")
    lines.append("- **SDL2 and SDL3** (Simple DirectMedia Layer): Zlib licence, via "
                 "`ppy.SDL2-CS` and `ppy.SDL3-CS`.")
    lines.append("- **libveldrid-spirv** (SPIRV-Cross): MIT licence, via `ppy.Veldrid.SPIRV`.")
    lines.append("- **libstbi** (stb image decoding): public domain / MIT, via `StbiSharp`.")
    lines.append("- **librealm-wrappers** (Realm database core): Apache-2.0, via `Realm`.")
    lines.append("- **e_sqlite3** (SQLite): public domain, via `SQLite` and `SQLitePCLRaw`.")
    lines.append("- **BASS / BASSMIX / BASSFX / BASSWASAPI**: see section 2 (un4seen commercial terms).")
    lines.append("- **DiscordRPC** (`DiscordRichPresence`, MIT): a Discord RPC "
                 "wrapper. kan! keeps Discord presence **off** (fork F-20), so no "
                 "Discord integration is active.")
    lines.append("- **kan!'s native bridge** (`kan_forge_native`): miniaudio (MIT-0), "
                 "the Basic Pitch C++ port and its ICASSP 2022 weights (Apache-2.0, "
                 "Copyright 2022 Spotify AB), GGML (MIT) and BSRoformer.cpp (MIT). "
                 "Record of record with versions and changes: "
                 "`libkan/THIRD_PARTY_NOTICES.md` in the kan-rhythm monorepo.")
    lines.append("")
    lines.append("## 6. Separation model (downloaded at first use)")
    lines.append("")
    lines.append("Splitting a song into its parts downloads one BS-RoFormer 6-stem "
                 "separation model (about 200 MB) to `models/` on first use; the "
                 "model is not shipped in the zip.")
    lines.append("")
    lines.append("- **Default: the f16 grade of `studio409/BSRoformer-SW-6Stem-GGUF`** "
                 "(Hugging Face), a GGUF conversion of "
                 "`elicwhite/bs-roformer-sw-6stem-onnx`: **MIT licence**.")
    lines.append("- **Opt-in: the q8 grade of `FractalSurfer/BS-RoFormer-SW-6stem`** "
                 "(only with `KAN_ROFORMER_GRADE=q8`): its model card declares "
                 "**no licence**, so kan! never selects it on its own.")
    lines.append("- `KAN_MODEL_URL` downloads from an own host instead; "
                 "`KAN_ROFORMER_GGUF` points at a local file (no download).")
    lines.append("")
    lines.append("This notices file is generated, not exhaustive by legal opinion: if any "
                 "entry looks wrong, fix the generator or the package reference and "
                 "regenerate. Tracking issue surface: `game/docs/fork-divergence.md`.")
    lines.append("")

    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text("\n".join(lines), encoding="utf-8")
    try:
        shown = output.resolve().relative_to(REPO_ROOT)
    except ValueError:
        shown = output.resolve()
    print(f"wrote {shown} ({len(rows)} packages, "
          f"{len(hard_warnings)} hard warnings, {len(warnings)} warnings)")
    for w in hard_warnings:
        print(f"hard warning: {w}", file=sys.stderr)
    for w in warnings:
        print(f"warning: {w}", file=sys.stderr)
    return 0


if __name__ == "__main__":
    sys.exit(main())
