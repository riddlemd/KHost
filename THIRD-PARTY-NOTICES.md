# Third-Party Notices

KHost itself is licensed under the PolyForm Shield License 1.0.0 (see `LICENSE`).
It incorporates and/or distributes the third-party components listed below, each
of which remains under its own license and copyright. The licenses below permit
inclusion in a source-available and/or commercial product; nothing here changes
the terms of the PolyForm Shield License covering KHost's own code.

> Full verbatim texts of the licenses that require inclusion in binary
> distributions are bundled under [`licenses/`](licenses): Apache-2.0, GNU
> LGPL v2.1, and SIL OFL 1.1. MIT and BSD-3-Clause are reproduced inline in
> [§4](#4-common-license-texts). For a formal release, regenerate the inventory
> with a tool such as `dotnet-project-licenses` to confirm exact license
> identifiers and versions.

---

## 1. Components distributed with KHost

These ship inside a KHost build (host and/or screen app, or the browser assets).

### Permissive (MIT / BSD / Apache-2.0 / OFL)

| Component | Used in | License | Copyright / Author |
|---|---|---|---|
| FFMpegCore | host, screen | MIT | Malte Rosenbjerg and contributors |
| Avalonia (Desktop, Themes.Fluent) | screen | MIT | The AvaloniaUI Project / .NET Foundation |
| Avalonia.Fonts.Inter — Inter typeface | screen | SIL Open Font License 1.1 | Rasmus Andersson (font); Avalonia (packaging, MIT) |
| SkiaSharp / HarfBuzzSharp (via Avalonia) | screen | MIT (Skia: BSD-3-Clause) | Microsoft / Google |
| Silk.NET.OpenAL.Soft.Native — binding | screen | MIT | .NET Foundation / Silk.NET contributors |
| Konscious.Security.Cryptography.Argon2 | host | MIT | Keef Aragon |
| Entity Framework Core + Microsoft.Data.Sqlite | host | MIT | Microsoft / .NET Foundation |
| Microsoft.Extensions.* (DI, Logging, Http, Options, ServiceDiscovery, Resilience) | host, screen | MIT | Microsoft / .NET Foundation |
| Microsoft.AspNetCore.SignalR.Client | screen | MIT | Microsoft / .NET Foundation |
| Polly (via Microsoft.Extensions.Http.Resilience) | host | BSD-3-Clause | App vNext |
| SQLitePCLRaw (native SQLite provider) | host | Apache-2.0 | Eric Sink / SourceGear |
| SQLite engine | host | Public Domain | D. Richard Hipp and contributors |
| Serilog, Serilog.AspNetCore, Serilog.Extensions.Logging, Serilog.Sinks.File | host, screen | Apache-2.0 | Serilog Contributors |
| OpenTelemetry .NET (SDK, exporters, instrumentation) | host | Apache-2.0 | The OpenTelemetry Authors |
| Bootstrap Icons (`wwwroot/css/bootstrap-icons.css`, `wwwroot/css/fonts/bootstrap-icons.woff*`) | host UI (browser) | MIT | The Bootstrap Authors |
| SortableJS (`wwwroot/js/Sortable.min.js`) | host UI (browser) | MIT | All contributors to SortableJS |

License texts:
- MIT and BSD-3-Clause are reproduced in [§4](#4-common-license-texts).
- Apache-2.0: [`licenses/Apache-2.0.txt`](licenses/Apache-2.0.txt)
  (https://www.apache.org/licenses/LICENSE-2.0) — retain each project's `NOTICE`
  file where provided.
- SIL Open Font License 1.1: [`licenses/SIL-OFL-1.1.txt`](licenses/SIL-OFL-1.1.txt)
  (https://openfontlicense.org) — the Inter font may be bundled and used freely
  (including commercially); it may not be sold on its own, and its
  license/copyright must travel with the font files.

### Weak copyleft — requires the compliance steps below

| Component | Used in | License | Copyright / Author |
|---|---|---|---|
| OpenAL Soft (native `soft_oal` / `OpenAL32` library, bundled by `Silk.NET.OpenAL.Soft.Native`) | screen | GNU LGPL v2.1 | Chris Robinson (kcat) and contributors |

**OpenAL Soft (LGPL-2.1) compliance.** KHost satisfies the LGPL by linking OpenAL
Soft **dynamically** as a standalone, replaceable native library loaded at runtime
(via Silk.NET) — it is never statically linked into KHost code. To remain
compliant when distributing a KHost build you must:
1. Keep OpenAL Soft as a separate library file the user can replace with their own
   compatible build.
2. Include a copy of the GNU LGPL v2.1 ([`licenses/LGPL-2.1.txt`](licenses/LGPL-2.1.txt);
   https://www.gnu.org/licenses/old-licenses/lgpl-2.1.txt) and this notice.
3. Not remove OpenAL Soft's copyright notices.

This obligation applies only to OpenAL Soft itself; it does **not** require
disclosing KHost's own source code, and is compatible with the PolyForm Shield
License and with selling commercial licenses to KHost.

---

## 2. FFmpeg (NOT distributed with KHost)

KHost uses **FFmpeg** (and `ffprobe`) for media decoding and metadata, invoked as a
**separate process** over standard streams. FFmpeg is **not** bundled, mirrored, or
distributed as part of KHost. It is obtained by the end user:

- supplied by the user (on `PATH` or via the `FFMPEG_PATH` configuration), or
- downloaded by KHost **at the user's direction from the upstream/official source**
  (the user obtains their own copy; KHost does not host or redistribute the binary).

FFmpeg is licensed under the **LGPL-2.1+** or, for many prebuilt distributions,
the **GPL-2.0+/GPL-3.0+**, depending on how the binary was compiled. Because KHost
calls FFmpeg only as an independent program at arm's length, FFmpeg's copyleft
terms do not extend to KHost's own code.

Guidance:
- Prefer an **LGPL** build of FFmpeg where available.
- Never use or steer users to a `--enable-nonfree` build (those are not
  redistributable).
- If KHost is ever changed to **bundle or self-host** an FFmpeg binary, that build's
  full license obligations (including, for a GPL build, providing the corresponding
  source) attach to the distributor — keep FFmpeg user-obtained to avoid this.

See https://ffmpeg.org/legal.html for details.

---

## 3. Build, test, and tooling dependencies (NOT distributed)

The following are used only to build, test, or develop KHost and are **not** shipped
in any KHost build, so they impose no distribution obligations: xUnit (Apache-2.0),
xunit.runner.visualstudio (Apache-2.0), NSubstitute (BSD-3-Clause),
coverlet.collector (MIT), Microsoft.NET.Test.Sdk (MIT), and the npm tooling
`sass` (MIT), `concurrently` (MIT), and `chokidar` (MIT).

---

## 4. Common license texts

### The MIT License

```
Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

### The 3-Clause BSD License

```
Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met:

1. Redistributions of source code must retain the above copyright notice, this
   list of conditions and the following disclaimer.
2. Redistributions in binary form must reproduce the above copyright notice,
   this list of conditions and the following disclaimer in the documentation
   and/or other materials provided with the distribution.
3. Neither the name of the copyright holder nor the names of its contributors
   may be used to endorse or promote products derived from this software
   without specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND
ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE
FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL
DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR
SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER
CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY,
OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
```

Full verbatim texts of Apache-2.0, GNU LGPL v2.1, and SIL OFL 1.1 are bundled
under [`licenses/`](licenses) and are included with binary distributions.

---

*This file is informational, reflects KHost's dependencies as of this revision,
and is not legal advice. Verify license identifiers for any revenue-bearing
release.*
