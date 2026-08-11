# Plugin Architecture

Designed 2026-08-11. Status: Phases 1–4 implemented on `feature/plugin-architecture` (YouTube
provider shipped; KaraFun still to come). The rotation engine is ported, registered,
plugin-extensible (`plans/rotation-strategies.md`), and wired into SingerQueueService:
`RotateQueueAsync` runs on performance end (PlaybackService delegates) and `AddUserAsync`
applies strategy-aware join placement. Rotation is ALWAYS on — the old
MoveSingerToBottomAfterPerformance setting was removed because default fifo + drop-to-end is
the same behavior, and fifo gained a LeavesQueue drop position (finished singer leaves the
queue; the ApplyOrder sanitizer permits dropping only the finished singer). Config is
venue-scoped:
`VenueSettings.QueueRotation` (nullable `QueueRotationConfig`; EF reads pre-existing rows as
null, callers fall back to defaults) lives in the venue's JSON settings column via a nested
`OwnsOne` (empty migration `AddQueueRotationVenueSetting`). Edited through the Singer Queue
dialog (`SingerQueueDialog` hosting `QueueRotationSettingsEditor`) opened from the venue edit
dialog — rotation edits ride the venue model, so venue Cancel/Save applies to them too.
Last remaining piece: the KaraFun provider plugin.

## Goals

- Drop-in plugins: a folder under `plugins/`, picked up at app restart. No hot load/unload.
- Extension points for v1: **media search providers** (YouTube, KaraFun ship this way) and
  **singer queue rotation modes** (engine ported from `origin/feature/singer-rotations`).
- Outside developers compile against a small, stable SDK — not the app's internals.

## Non-goals

- Hot reload / unload (no collectible ALCs — restart is the reload mechanism).
- Sandboxing. A plugin dll is arbitrary full-trust code in the host process; enablement and
  `apiVersion` are UX/compat gates, not security. Docs must say this plainly.
- Plugin-provided UI (Razor components). Settings UI is host-rendered from the manifest schema.

## Decisions (workshopped 2026-08-11)

| Decision | Choice |
|---|---|
| Contract assembly | New `KHost.Plugins.Sdk` project; `KHost.Abstractions` references it |
| Registration | Host reflects over the entry assembly for known extension interfaces; plugins never touch the `IServiceCollection` |
| Newly discovered plugins | Disabled until explicitly enabled (nothing executes just by being copied in) |
| Plugins folder | `AppContext.BaseDirectory/plugins` (beside `cache/`; deleting `bin/` in dev wipes installed plugins, same as the DB) |

## Layout

```
plugins/
  khost.youtube/
    manifest.json
    KHost.Plugin.YouTube.dll
    <plugin's own NuGet dependency dlls>
```

Folder names are cosmetic (pick something readable); the manifest `id` is authoritative.

## Manifest (`manifest.json`)

```json
{
  "id": "88f42c92-f15a-443c-afa0-0983f05fdbb4",
  "name": "YouTube Search",
  "version": "1.2.0",
  "author": "riddlemd",
  "description": "Search and queue YouTube karaoke videos",
  "entryAssembly": "KHost.Plugin.YouTube.dll",
  "apiVersion": 1,
  "settings": [
    { "key": "ApiKey", "type": "string", "label": "YouTube API Key", "secret": true }
  ]
}
```

- `id`: a GUID, generated once when the plugin is created and never changed (typed `Guid` in
  the manifest model, so a malformed id fails parsing → Errored). Duplicate id across
  folders → later one Errored.
- `apiVersion`: integer, checked against the host's supported range. Bumped only on breaking
  SDK changes. Mismatch → plugin listed as Incompatible, never loaded.
- `settings`: declarative schema (`key`, `type` = string|int|bool, `label`, `secret`,
  optional `default`). Host renders the form; plugin never persists its own config.
- What a plugin *provides* is NOT declared — the host discovers it by scanning the entry
  assembly for public non-abstract types implementing SDK extension interfaces. The manifest
  and the code can't disagree.
- The Sdk ships `manifest.schema.json`; manifests reference it via `$schema` so authors get
  editor IntelliSense and validation for free.
- Anti-drift validation at load: `entryAssembly` must exist (else Errored); a manifest
  `version` that differs from the assembly's version logs a warning but still loads. If drift
  becomes a real annoyance, v2 can add an Sdk MSBuild target that generates the manifest from
  csproj properties — authors keep one source of truth, the host format never changes.

## KHost.Plugins.Sdk

New project, no project references, netstandard-ish surface (target `net10.0` is fine).
Contents move OUT of `KHost.Abstractions` into the Sdk (namespace change is an accepted
internal break); `Abstractions` adds a project reference to the Sdk:

- `IMediaProvider`, `MediaSearchEntity`, `MediaProviderAction`
- `IQueueRotationMode`, `IQueueRotationStrategy`, `QueueRotationContext` + the singer-facing
  models it needs (port from the stale branch — see below)
- `IPlugin` (host-provided context, injected into plugin types): `T? GetSetting<T>(string key)`
  plus `TSettings BindSettings<TSettings>()` for typed access — the manifest stays the source
  of truth for the settings *form*; the typed class is the plugin-code-side view of the same
  keys (camelCase-insensitive match, property initializers as last-resort defaults)
- Manifest model (`PluginManifest`, `PluginSettingDefinition`)

Rule of thumb: the Sdk holds exactly what a plugin author needs to compile, nothing else.
Publishable to NuGet later as `KHost.Plugins.Sdk`.

## Loading

`PluginLoader` (Domain), runs in `Program.cs` before `builder.Build()`:

1. Scan `plugins/*/manifest.json`; parse → `DiscoveredPlugin { Manifest, Directory, Status }`.
2. Statuses: `Disabled`, `Incompatible` (apiVersion), `Errored` (bad manifest / missing dll /
   scan or ctor failure), `Loaded`.
3. For each *enabled + compatible* plugin: create a `PluginLoadContext : AssemblyLoadContext`
   (non-collectible) with an `AssemblyDependencyResolver` on the entry dll.
   `Load()` returns `null` for anything resolvable from the default context — critically the
   Sdk assembly — so contract types are reference-equal across the boundary; the resolver
   supplies the plugin's private deps from its own folder (plugins may carry conflicting
   NuGet versions safely).
4. Reflect over exported types; for each type implementing a known extension interface,
   register: `services.AddSingleton<IMediaProvider>(sp => (IMediaProvider)ActivatorUtilities
   .CreateInstance(sp, type, new PluginSettings(pluginId, cache)))`. Plugin ctors may take
   host services available via DI plus `IPlugin`.
5. Any exception in 1–4 → mark that plugin Errored with the message and continue.
   **The app always starts.**

The extension-interface list is a single registry (interface → registration action) so a new
extension point is one entry + one Sdk interface.

## State

`plugins.json` in the JSON cache (`ICacheService`), app-level like the queue:

```json
{
  "enabledPluginIds": ["88f42c92-f15a-443c-afa0-0983f05fdbb4"],
  "settings": {
    "88f42c92-f15a-443c-afa0-0983f05fdbb4": { "apiKey": "..." }
  }
}
```

## Settings UI

New settings page: **Plugins**. Lists every discovered plugin with name, version, author,
status badge (Enabled / Disabled / Errored+message / Incompatible), an enable toggle, and a
host-rendered settings form from the manifest schema (`secret` renders as password input).
Toggling or editing writes `plugins.json` and shows a "restart required" banner — settings
values could be live-read later, but v1 keeps one rule: restart applies everything.

## Resilience at call sites

- `MediaSearchService`: wrap each provider's `SearchAsync` in try/catch + per-provider timeout
  (~10s); a dead provider logs and contributes zero results instead of failing the search.
- Rotation factory already falls back to `fifo` for unknown ids; keep that — covers a
  disabled/removed plugin whose mode id is still referenced by saved config.

## First-party plugins

Plugins are fundamentally NOT part of the core repo or solution. Each lives in its own
sibling repo/solution (e.g. `~/Developer/riddlemd/KHost.Plugin.YouTube`) referencing
`KHost.Plugins.Sdk` by sibling-checkout project reference until the Sdk ships on NuGet.
A conditional AfterBuild target in the plugin repo drops its output into a sibling KHost
checkout's runtime `plugins/` folder for the dev loop; end users install by copying the
build output into `plugins/` like any third-party plugin. KHost's build has no plugin
copy targets and its tests know nothing about plugins.

## Rotation engine port (prerequisite for the second extension point)

Ported 2026-08-11 from `origin/feature/singer-rotations` (no merge base with master — ported
by hand):

- Sdk: `IQueueRotationStrategy`/`IQueueRotationMode` (Services/QueueRotation) and
  `QueueRotationContext`/`QueueRotationConfig`/`RotationSinger`/`DropPositionMode`
  (Models/QueueRotation). `RotationSinger` is a host-built snapshot (LastSangOn/CheckinOn/
  TippedOn, GroupIds) — the branch's extra `KHostUser` fields never landed on master, so the
  host populates snapshots instead of leaking its user model into the Sdk.
- Abstractions: `IQueueRotationStrategyFactory`, `IQueueRotationStateService`.
- Domain: 9 modes + 8 modifiers + factory + state service under `Services/QueueRotation/`.
  Factory dedupes mode ids first-wins — built-ins register before plugins, so a plugin can't
  hijack (or crash) a taken id. `IPerformanceService.ReadBySingerIdAsync` gained an optional
  `startDate` for the songs-sung-tonight count.
- Modifiers stay host-owned config (not pluggable) in v1.
- NOT yet wired into SingerQueueService, and no rotation settings UI — the engine runs only
  when something calls `IQueueRotationStrategyFactory.Resolve(config).ApplyAsync(context)`.

## Phases

1. **Sdk + loader + enabled list** — new project, type moves, `PluginLoader`, ALC, statuses,
   `plugins.json`. App runs with zero plugins present.
2. **Plugins settings page** — discovery list, toggles, generic settings form, restart banner.
3. **First provider plugin (YouTube)** — proves the SDK ergonomics end to end; KaraFun follows.
4. **Rotation engine port** — hand-port from stale branch into Domain + Sdk; wire the second
   extension point.
