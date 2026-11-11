# THIRD PARTY NOTICES: kan! client

Generated 2026-11-11 by `game/tools/gen-third-party-notices.py` (monorepo kan-rhythm). Do not edit by hand: regenerate instead.

The kan! client is built on the osu!lazer fork (github.com/KairoPhos13/kan, itself a fork of `ppy/osu`). This file lists every redistributed third-party component and its licence, and is shown on the in-game about screen. Licence *texts* of individual packages ship inside the corresponding NuGet package (`~/.nuget/packages/<id>/<version>/`); extract any of them with:

    unzip -p ~/.nuget/packages/<id>/<version>/<id>.<version>.nupkg <path-inside> 

## 1. Base project

- **osu!lazer** (`ppy/osu`): MIT Licence, Copyright (c) ppy Pty Ltd <contact@ppy.sh> and contributors. kan! is a rebranded fork (ADR-0011); upstream copyright headers are preserved in every adapted file.
- **osu!framework** (`ppy/osu-framework`): MIT Licence, Copyright (c) ppy Pty Ltd <contact@ppy.sh> and contributors.
- **osu! Game Resources** (`ppy.osu.Game.Resources`, © ppy Pty Ltd): CC BY-NC 4.0 for most content (https://github.com/ppy/osu-resources). Used non-commercially; kan!'s own art and sounds replace them as they are made (`brand/`). The osu! name and logo are ppy's trademarks.
- **Fonts** in the osu! resources, each under its own licence: Inter and Noto (SIL Open Font License); Torus, Torus-Alternate and Venera (their upstream licence files state that distribution requires a commercial licence).

Full text of the base project's licence (fork `LICENCE`):

```
Copyright (c) 2025 ppy Pty Ltd <contact@ppy.sh>.

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in
all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
THE SOFTWARE.
```

## 2. BASS audio library (un4seen): special terms

osu!framework's audio backend uses **BASS / BASSMIX / BASSFX** and, on Windows, **BASSWASAPI** (© 1999-2026 Un4seen Developments). BASS is **not** open source: it is free for non-commercial use and requires a commercial licence otherwise. kan!'s status (donation-funded) is analysed in `game/docs/bass-licensing.md`; the outcome of that analysis must be resolved before any donation-driven release (ADR-0010 §5). See https://www.un4seen.com/ for the authoritative terms.

## 3. NuGet package closure (osu.Desktop)

140 packages at generation time:

| Package | Version | Licence | Project |
|---|---|---|---|
| `AutoMapper` | 13.0.1 | MIT | https://automapper.org/ |
| `DiffPlex` | 1.9.0 | Apache-2.0 | https://github.com/mmanela/diffplex/ |
| `DiscordRichPresence` | 1.5.0.51 | MIT | https://github.com/Lachee/discord-rpc-csharp |
| `FFmpeg.AutoGen` | 4.3.0.1 | LGPL (see licence file in package) | https://github.com/Ruslan-B/FFmpeg.AutoGen |
| `Fody` | 6.9.1 | MIT (see licence file in package) | https://github.com/Fody/Fody |
| `HidSharpCore` | 1.3.0 | (unknown) |  (bca4aee1c349) |
| `HtmlAgilityPack` | 1.13.0 | MIT | http://html-agility-pack.net/ |
| `Humanizer` | 3.0.10 | MIT | https://github.com/Humanizr/Humanizer |
| `Humanizer.Core` | 3.0.10 | MIT | https://github.com/Humanizr/Humanizer |
| `Humanizer.Core.af` | 3.0.10 | MIT | https://github.com/Humanizr/Humanizer |
| `Humanizer.Core.ar` | 3.0.10 | MIT | https://github.com/Humanizr/Humanizer |
| `Humanizer.Core.az` | 3.0.10 | MIT | https://github.com/Humanizr/Humanizer |
| `Humanizer.Core.bg` | 3.0.10 | MIT | https://github.com/Humanizr/Humanizer |
| `Humanizer.Core.bn` | 3.0.10 | MIT | https://github.com/Humanizr/Humanizer |
| `Humanizer.Core.ca` | 3.0.10 | MIT | https://github.com/Humanizr/Humanizer |
| `Humanizer.Core.cs` | 3.0.10 | MIT | https://github.com/Humanizr/Humanizer |
| `Humanizer.Core.da` | 3.0.10 | MIT | https://github.com/Humanizr/Humanizer |
| `Humanizer.Core.de` | 3.0.10 | MIT | https://github.com/Humanizr/Humanizer |
| `Humanizer.Core.el` | 3.0.10 | MIT | https://github.com/Humanizr/Humanizer |
| `Humanizer.Core.es` | 3.0.10 | MIT | https://github.com/Humanizr/Humanizer |
| `Humanizer.Core.fa` | 3.0.10 | MIT | https://github.com/Humanizr/Humanizer |
| `Humanizer.Core.fi` | 3.0.10 | MIT | https://github.com/Humanizr/Humanizer |
| `Humanizer.Core.fil` | 3.0.10 | MIT | https://github.com/Humanizr/Humanizer |
| `Humanizer.Core.fr` | 3.0.10 | MIT | https://github.com/Humanizr/Humanizer |
| `Humanizer.Core.he` | 3.0.10 | MIT | https://github.com/Humanizr/Humanizer |
| `Humanizer.Core.hr` | 3.0.10 | MIT | https://github.com/Humanizr/Humanizer |
| `Humanizer.Core.hu` | 3.0.10 | MIT | https://github.com/Humanizr/Humanizer |
| `Humanizer.Core.hy` | 3.0.10 | MIT | https://github.com/Humanizr/Humanizer |
| `Humanizer.Core.id` | 3.0.10 | MIT | https://github.com/Humanizr/Humanizer |
| `Humanizer.Core.is` | 3.0.10 | MIT | https://github.com/Humanizr/Humanizer |
| `Humanizer.Core.it` | 3.0.10 | MIT | https://github.com/Humanizr/Humanizer |
| `Humanizer.Core.ja` | 3.0.10 | MIT | https://github.com/Humanizr/Humanizer |
| `Humanizer.Core.ko` | 3.0.10 | MIT | https://github.com/Humanizr/Humanizer |
| `Humanizer.Core.ku` | 3.0.10 | MIT | https://github.com/Humanizr/Humanizer |
| `Humanizer.Core.lb` | 3.0.10 | MIT | https://github.com/Humanizr/Humanizer |
| `Humanizer.Core.lt` | 3.0.10 | MIT | https://github.com/Humanizr/Humanizer |
| `Humanizer.Core.lv` | 3.0.10 | MIT | https://github.com/Humanizr/Humanizer |
| `Humanizer.Core.ms` | 3.0.10 | MIT | https://github.com/Humanizr/Humanizer |
| `Humanizer.Core.mt` | 3.0.10 | MIT | https://github.com/Humanizr/Humanizer |
| `Humanizer.Core.nb` | 3.0.10 | MIT | https://github.com/Humanizr/Humanizer |
| `Humanizer.Core.nl` | 3.0.10 | MIT | https://github.com/Humanizr/Humanizer |
| `Humanizer.Core.pl` | 3.0.10 | MIT | https://github.com/Humanizr/Humanizer |
| `Humanizer.Core.pt` | 3.0.10 | MIT | https://github.com/Humanizr/Humanizer |
| `Humanizer.Core.pt-BR` | 3.0.10 | MIT | https://github.com/Humanizr/Humanizer |
| `Humanizer.Core.ro` | 3.0.10 | MIT | https://github.com/Humanizr/Humanizer |
| `Humanizer.Core.ru` | 3.0.10 | MIT | https://github.com/Humanizr/Humanizer |
| `Humanizer.Core.sk` | 3.0.10 | MIT | https://github.com/Humanizr/Humanizer |
| `Humanizer.Core.sl` | 3.0.10 | MIT | https://github.com/Humanizr/Humanizer |
| `Humanizer.Core.sr` | 3.0.10 | MIT | https://github.com/Humanizr/Humanizer |
| `Humanizer.Core.sr-Latn` | 3.0.10 | MIT | https://github.com/Humanizr/Humanizer |
| `Humanizer.Core.sv` | 3.0.10 | MIT | https://github.com/Humanizr/Humanizer |
| `Humanizer.Core.th` | 3.0.10 | MIT | https://github.com/Humanizr/Humanizer |
| `Humanizer.Core.tr` | 3.0.10 | MIT | https://github.com/Humanizr/Humanizer |
| `Humanizer.Core.uk` | 3.0.10 | MIT | https://github.com/Humanizr/Humanizer |
| `Humanizer.Core.uz-Cyrl-UZ` | 3.0.10 | MIT | https://github.com/Humanizr/Humanizer |
| `Humanizer.Core.uz-Latn-UZ` | 3.0.10 | MIT | https://github.com/Humanizr/Humanizer |
| `Humanizer.Core.vi` | 3.0.10 | MIT | https://github.com/Humanizr/Humanizer |
| `Humanizer.Core.zh-CN` | 3.0.10 | MIT | https://github.com/Humanizr/Humanizer |
| `Humanizer.Core.zh-Hans` | 3.0.10 | MIT | https://github.com/Humanizr/Humanizer |
| `Humanizer.Core.zh-Hant` | 3.0.10 | MIT | https://github.com/Humanizr/Humanizer |
| `JetBrains.Annotations` | 2026.2.0 | MIT | https://www.jetbrains.com/help/resharper/Code_Analysis__Code_Annotations.html |
| `Markdig` | 0.23.0 | BSD-2-Clause | https://github.com/lunet-io/markdig |
| `MessagePack` | 3.1.12 | MIT | https://github.com/MessagePack-CSharp/MessagePack-CSharp |
| `MessagePack.Annotations` | 3.1.12 | MIT | https://github.com/MessagePack-CSharp/MessagePack-CSharp |
| `MessagePackAnalyzer` | 3.1.12 | MIT | https://github.com/MessagePack-CSharp/MessagePack-CSharp |
| `Microsoft.AspNetCore.Connections.Abstractions` | 10.0.13 | MIT | https://asp.net/ |
| `Microsoft.AspNetCore.Http.Connections.Client` | 10.0.13 | MIT | https://asp.net/ |
| `Microsoft.AspNetCore.Http.Connections.Common` | 10.0.13 | MIT | https://asp.net/ |
| `Microsoft.AspNetCore.SignalR.Client` | 10.0.13 | MIT | https://asp.net/ |
| `Microsoft.AspNetCore.SignalR.Client.Core` | 10.0.13 | MIT | https://asp.net/ |
| `Microsoft.AspNetCore.SignalR.Common` | 10.0.13 | MIT | https://asp.net/ |
| `Microsoft.AspNetCore.SignalR.Protocols.Json` | 10.0.13 | MIT | https://asp.net/ |
| `Microsoft.AspNetCore.SignalR.Protocols.MessagePack` | 10.0.13 | MIT | https://asp.net/ |
| `Microsoft.AspNetCore.SignalR.Protocols.NewtonsoftJson` | 10.0.13 | MIT | https://asp.net/ |
| `Microsoft.CodeAnalysis.BannedApiAnalyzers` | 3.3.4 | MIT | https://github.com/dotnet/roslyn-analyzers |
| `Microsoft.Data.Sqlite.Core` | 10.0.12 | MIT | https://docs.microsoft.com/dotnet/standard/data/sqlite/ |
| `Microsoft.Diagnostics.NETCore.Client` | 0.2.61701 | MIT | https://github.com/dotnet/diagnostics |
| `Microsoft.Diagnostics.Runtime` | 2.0.161401 | MIT | https://github.com/Microsoft/clrmd |
| `Microsoft.Extensions.Configuration.Abstractions` | 10.0.12 | MIT | https://dot.net/ |
| `Microsoft.Extensions.DependencyInjection` | 10.0.13 | MIT | https://dot.net/ |
| `Microsoft.Extensions.DependencyInjection.Abstractions` | 10.0.13 | MIT | https://dot.net/ |
| `Microsoft.Extensions.Features` | 10.0.13 | MIT | https://asp.net/ |
| `Microsoft.Extensions.Logging` | 10.0.13 | MIT | https://dot.net/ |
| `Microsoft.Extensions.Logging.Abstractions` | 10.0.13 | MIT | https://dot.net/ |
| `Microsoft.Extensions.ObjectPool` | 10.0.11 | MIT | https://asp.net/ |
| `Microsoft.Extensions.Options` | 10.0.13 | MIT | https://dot.net/ |
| `Microsoft.Extensions.Primitives` | 10.0.13 | MIT | https://dot.net/ |
| `Microsoft.NET.StringTools` | 17.11.4 | MIT | http://go.microsoft.com/fwlink/?LinkId=624683 |
| `Microsoft.NETCore.Platforms` | 1.1.0 | Microsoft .NET library licence (see licenceUrl) | https://dot.net/ |
| `Microsoft.Toolkit.HighPerformance` | 7.1.2 | MIT | https://github.com/CommunityToolkit/WindowsCommunityToolkit |
| `MongoDB.Bson` | 2.21.0 | Apache-2.0 (see licence file in package) | https://www.mongodb.com/docs/drivers/csharp/ |
| `NETStandard.Library` | 1.6.1 | Microsoft .NET library licence (see licenceUrl) | https://dot.net/ |
| `Newtonsoft.Json` | 13.0.4 | MIT | https://www.newtonsoft.com/json |
| `NUnit` | 4.6.1 | MIT | https://nunit.org/ |
| `OpenTabletDriver` | 0.6.7 | LGPL-3.0-or-later | https://www.github.com/OpenTabletDriver/OpenTabletDriver (736003ed72c8) |
| `OpenTabletDriver.Configurations` | 0.6.7 | LGPL-3.0-or-later | https://www.github.com/OpenTabletDriver/OpenTabletDriver (736003ed72c8) |
| `OpenTabletDriver.Native` | 0.6.7 | LGPL-3.0-or-later | https://www.github.com/OpenTabletDriver/OpenTabletDriver (736003ed72c8) |
| `OpenTabletDriver.Plugin` | 0.6.7 | LGPL-3.0-or-later | https://www.github.com/OpenTabletDriver/OpenTabletDriver (736003ed72c8) |
| `PolySharp` | 1.10.0 | MIT | https://github.com/Sergio0694/PolySharp/ |
| `ppy.managed-midi` | 1.10.2 | see https://github.com/atsushieno/managed-midi/LICENSE | https://github.com/atsushieno/managed-midi |
| `ppy.ManagedBass` | 2022.1216.0 | MIT (see licence file in package) | https://github.com/ManagedBass/ManagedBass/ |
| `ppy.ManagedBass.Fx` | 2022.1216.0 | MIT (see licence file in package) | https://github.com/ManagedBass/ManagedBass |
| `ppy.ManagedBass.Mix` | 2022.1216.0 | MIT (see licence file in package) | https://github.com/ManagedBass/ManagedBass |
| `ppy.ManagedBass.Wasapi` | 2022.1216.0 | MIT (see licence file in package) | https://github.com/ManagedBass/ManagedBass |
| `ppy.osu.Framework` | 2026.1005.0 | MIT | https://github.com/ppy/osu-framework |
| `ppy.osu.Framework.NativeLibs` | 2025.806.0-nativelibs | MIT | https://github.com/ppy/osu-framework |
| `ppy.osu.Framework.SourceGeneration` | 2025.1121.1 | MIT | https://github.com/ppy/osu-framework |
| `ppy.osu.Game.Resources` | 2026.1008.0 | see https://github.com/ppy/osu-resources/blob/master/LICENCE.md | https://github.com/ppy/osu-resources |
| `ppy.osuTK.NS20` | 1.0.211 | (unknown) | https://github.com/ppy/opentk/tree/netstandard |
| `ppy.SDL2-CS` | 1.0.741-alpha | (unknown) | https://github.com/ppy/SDL2-CS |
| `ppy.SDL3-CS` | 2026.1002.1 | MIT | https://github.com/ppy/SDL3-CS |
| `ppy.Veldrid` | 4.9.69-ga405fe8484 | see https://raw.githubusercontent.com/ppy/veldrid/master/LICENSE | https://github.com/ppy/veldrid (a405fe8484fa) |
| `ppy.Veldrid.MetalBindings` | 4.9.69-ga405fe8484 | see https://raw.githubusercontent.com/ppy/veldrid/master/LICENSE | https://github.com/ppy/veldrid (a405fe8484fa) |
| `ppy.Veldrid.OpenGLBindings` | 4.9.69-ga405fe8484 | see https://raw.githubusercontent.com/ppy/veldrid/master/LICENSE | https://github.com/ppy/veldrid (a405fe8484fa) |
| `ppy.Veldrid.SPIRV` | 1.0.15-g5911265ec3 | see https://raw.githubusercontent.com/mellinoe/veldrid-spirv/master/LICENSE | https://github.com/mellinoe/veldrid-spirv (5911265ec37f) |
| `ppy.Vk` | 1.0.26 | (unknown) | https://github.com/mellinoe/vk (c3a41a7e14f3) |
| `Realm` | 20.1.0 | Apache-2.0 | https://github.com/realm/realm-dotnet |
| `Remotion.Linq` | 2.2.0 | see http://opensource.org/licenses/Apache-2.0 | http://relinq.codeplex.com |
| `Sentry` | 6.11.0 | MIT | https://sentry.io/ |
| `SharpCompress` | 0.50.4 | MIT | https://github.com/adamhathcock/sharpcompress |
| `SharpFNT` | 2.0.0 | MIT (see licence file in package) | https://github.com/AuroraBertaOldham/SharpFNT |
| `SharpGen.Runtime` | 2.0.0-beta.13 | MIT | https://github.com/SharpGenTools/SharpGenTools |
| `SharpGen.Runtime.COM` | 2.0.0-beta.13 | MIT | https://github.com/SharpGenTools/SharpGenTools |
| `SixLabors.ImageSharp` | 3.1.11 | Apache-2.0 (see licence file in package) | https://github.com/SixLabors/ImageSharp |
| `SQLite` | 3.53.4 | (custom; see licence file in package) | https://sqlite.org/ |
| `SQLitePCLRaw.bundle_e_sqlite3` | 3.0.5 | Apache-2.0 | https://github.com/ericsink/SQLitePCL.raw |
| `SQLitePCLRaw.config.e_sqlite3` | 3.0.5 | Apache-2.0 | https://github.com/ericsink/SQLitePCL.raw (96043b8cff32) |
| `SQLitePCLRaw.core` | 3.0.5 | Apache-2.0 | https://github.com/ericsink/SQLitePCL.raw (ed046114d5a3) |
| `SQLitePCLRaw.provider.e_sqlite3` | 3.0.5 | Apache-2.0 | https://github.com/ericsink/SQLitePCL.raw (ed046114d5a3) |
| `StbiSharp` | 1.2.2 | BSD (see licence file in package) | https://github.com/tom94/stbi-sharp (47ed8ac5776a) |
| `System.ComponentModel.Annotations` | 5.0.0 | MIT | https://github.com/dotnet/runtime |
| `System.IO.Packaging` | 10.0.12 | MIT | https://dot.net/ |
| `System.Numerics.Tensors` | 10.0.11 | MIT | https://dot.net/ |
| `TagLibSharp` | 2.3.0 | LGPL-2.1-only | https://github.com/mono/taglib-sharp |
| `Velopack` | 1.2.0 | MIT | https://github.com/velopack/velopack |
| `Vortice.D3DCompiler` | 2.4.2 | MIT | https://github.com/amerkoleci/Vortice.Windows (2feee0fc9c67) |
| `Vortice.Direct3D11` | 2.4.2 | MIT | https://github.com/amerkoleci/Vortice.Windows (2feee0fc9c67) |
| `Vortice.DirectX` | 2.4.2 | MIT | https://github.com/amerkoleci/Vortice.Windows (2feee0fc9c67) |
| `Vortice.DXGI` | 2.4.2 | MIT | https://github.com/amerkoleci/Vortice.Windows (2feee0fc9c67) |
| `Vortice.Mathematics` | 1.4.25 | MIT | https://github.com/amerkoleci/Vortice.Mathematics |

## 4. Warnings from the generator

- ⚠⚠ FFmpeg.AutoGen 4.3.0.1: license 'LGPL (see licence file in package)': GPL-family licence inherited from upstream osu!lazer. Kept as separate assemblies/libraries with dynamic linking (ADR-0013); re-review before any paid release (CONTRIBUTING.md §6).
- ⚠⚠ OpenTabletDriver 0.6.7: license 'LGPL-3.0-or-later': GPL-family licence inherited from upstream osu!lazer. Kept as separate assemblies/libraries with dynamic linking (ADR-0013); re-review before any paid release (CONTRIBUTING.md §6).
- ⚠⚠ OpenTabletDriver.Configurations 0.6.7: license 'LGPL-3.0-or-later': GPL-family licence inherited from upstream osu!lazer. Kept as separate assemblies/libraries with dynamic linking (ADR-0013); re-review before any paid release (CONTRIBUTING.md §6).
- ⚠⚠ OpenTabletDriver.Native 0.6.7: license 'LGPL-3.0-or-later': GPL-family licence inherited from upstream osu!lazer. Kept as separate assemblies/libraries with dynamic linking (ADR-0013); re-review before any paid release (CONTRIBUTING.md §6).
- ⚠⚠ OpenTabletDriver.Plugin 0.6.7: license 'LGPL-3.0-or-later': GPL-family licence inherited from upstream osu!lazer. Kept as separate assemblies/libraries with dynamic linking (ADR-0013); re-review before any paid release (CONTRIBUTING.md §6).
- ⚠⚠ TagLibSharp 2.3.0: license 'LGPL-2.1-only': GPL-family licence inherited from upstream osu!lazer. Kept as separate assemblies/libraries with dynamic linking (ADR-0013); re-review before any paid release (CONTRIBUTING.md §6).
- ⚠ HidSharpCore 1.3.0: license '(unknown)' is not on the permissive allow-list: verify manually (CONTRIBUTING.md §6)
- ⚠ ppy.managed-midi 1.10.2: license 'see https://github.com/atsushieno/managed-midi/LICENSE' is not on the permissive allow-list: verify manually (CONTRIBUTING.md §6)
- ⚠ ppy.osu.Game.Resources 2026.1008.0: license 'see https://github.com/ppy/osu-resources/blob/master/LICENCE.md' is not on the permissive allow-list: verify manually (CONTRIBUTING.md §6)
- ⚠ ppy.osuTK.NS20 1.0.211: license '(unknown)' is not on the permissive allow-list: verify manually (CONTRIBUTING.md §6)
- ⚠ ppy.SDL2-CS 1.0.741-alpha: license '(unknown)' is not on the permissive allow-list: verify manually (CONTRIBUTING.md §6)
- ⚠ ppy.Veldrid 4.9.69-ga405fe8484: license 'see https://raw.githubusercontent.com/ppy/veldrid/master/LICENSE' is not on the permissive allow-list: verify manually (CONTRIBUTING.md §6)
- ⚠ ppy.Veldrid.MetalBindings 4.9.69-ga405fe8484: license 'see https://raw.githubusercontent.com/ppy/veldrid/master/LICENSE' is not on the permissive allow-list: verify manually (CONTRIBUTING.md §6)
- ⚠ ppy.Veldrid.OpenGLBindings 4.9.69-ga405fe8484: license 'see https://raw.githubusercontent.com/ppy/veldrid/master/LICENSE' is not on the permissive allow-list: verify manually (CONTRIBUTING.md §6)
- ⚠ ppy.Veldrid.SPIRV 1.0.15-g5911265ec3: license 'see https://raw.githubusercontent.com/mellinoe/veldrid-spirv/master/LICENSE' is not on the permissive allow-list: verify manually (CONTRIBUTING.md §6)
- ⚠ ppy.Vk 1.0.26: license '(unknown)' is not on the permissive allow-list: verify manually (CONTRIBUTING.md §6)
- ⚠ SQLite 3.53.4: license '(custom; see licence file in package)' is not on the permissive allow-list: verify manually (CONTRIBUTING.md §6)

## 5. Native / platform components

Shipped as native binaries next to the executable; section 3 lists the managed wrapper packages, and each package carries its own licence text.

- **FFmpeg 4.x** (`libavcodec` 58, `libavformat` 58, `libavutil` 56, `libswscale` 5): redistributed in every kan! zip via `ppy.osu.Framework.NativeLibs` (osu!framework's native libraries). Licence: **LGPL 2.1 or later** (build-dependent). The binaries stay separate libraries that are loaded dynamically, and their licence terms travel with them; ADR-0013 of the kan-rhythm monorepo records the inherited LGPL set.
- **SDL2 and SDL3** (Simple DirectMedia Layer): Zlib licence, via `ppy.SDL2-CS` and `ppy.SDL3-CS`.
- **libveldrid-spirv** (SPIRV-Cross): MIT licence, via `ppy.Veldrid.SPIRV`.
- **libstbi** (stb image decoding): public domain / MIT, via `StbiSharp`.
- **librealm-wrappers** (Realm database core): Apache-2.0, via `Realm`.
- **e_sqlite3** (SQLite): public domain, via `SQLite` and `SQLitePCLRaw`.
- **BASS / BASSMIX / BASSFX / BASSWASAPI**: see section 2 (un4seen commercial terms).
- **DiscordRPC** (`DiscordRichPresence`, MIT): a Discord RPC wrapper. kan! keeps Discord presence **off** (fork F-20), so no Discord integration is active.
- **kan!'s native bridge** (`kan_forge_native`): miniaudio (MIT-0), the Basic Pitch C++ port and its ICASSP 2022 weights (Apache-2.0, Copyright 2022 Spotify AB), GGML (MIT) and BSRoformer.cpp (MIT). Record of record with versions and changes: `libkan/THIRD_PARTY_NOTICES.md` in the kan-rhythm monorepo.

## 6. Separation model (downloaded at first use)

Splitting a song into its parts downloads one BS-RoFormer 6-stem separation model (about 200 MB) to `models/` on first use; the model is not shipped in the zip.

- **Default: the f16 grade of `studio409/BSRoformer-SW-6Stem-GGUF`** (Hugging Face), a GGUF conversion of `elicwhite/bs-roformer-sw-6stem-onnx`: **MIT licence**.
- **Opt-in: the q8 grade of `FractalSurfer/BS-RoFormer-SW-6stem`** (only with `KAN_ROFORMER_GRADE=q8`): its model card declares **no licence**, so kan! never selects it on its own.
- `KAN_MODEL_URL` downloads from an own host instead; `KAN_ROFORMER_GGUF` points at a local file (no download).

This notices file is generated, not exhaustive by legal opinion: if any entry looks wrong, fix the generator or the package reference and regenerate. Tracking issue surface: `game/docs/fork-divergence.md`.
