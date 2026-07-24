# Vapor Keys — Rider / ReSharper plugin

Gives Rider **autocomplete, validation, and inlay hints** for Vapor data keys, driven by the text
manifests that Unity emits (see `Assets/Vapor/Keys/Definitions/Generated/*.keys.tsv`). Because the
manifests are plain text, adding new keys never requires a Unity recompile — the IDE picks them up
from the files.

> **Status: implemented against the 2026.1 SDK docs, pending a clean build.**
> - `ManifestStore.cs` / `KeyContextResolver.cs` — complete, plain C#, SDK-independent.
> - `IdeIntegration.cs` (reference/validation), `Completion.cs`, `Zone.cs` — written to the confirmed
>   2026.1 API surface. The resolve internals in `KeyManifestReference` (EmptyResolveResult.Instance,
>   EmptySymbolTable.INSTANCE, the ResolveResultWithInfo ctor, GetAccessContext) are the spots most likely
>   to still need a one-line adjustment against your exact build — if the build stops there, the error text
>   names the fix.
>
> **Scope of the current pass:** string-mode autocomplete + string/uint validation. uint-mode *insertion*
> (insert the hash literal, match/display by name), inlay hints, and `[DataKey]`-attribute detection for
> plain-primitive params are deliberate follow-ups.

---

## What the Unity side produces (already implemented)

Running **Vapor ▸ Keys ▸ Generate Data Keys** (or auto, on data-asset import if
**Vapor ▸ Keys ▸ Auto-Generate Key Manifests** is on) writes, under
`Assets/Vapor/Keys/Definitions/Generated/`:

| File | Meaning |
|------|---------|
| `<ScriptName>.keys.tsv` | `DisplayName<TAB>Key(uint)` — one per data type (e.g. `AttributeKeys.keys.tsv`). |
| `GameplayTags.keys.tsv` | Every key name + its dotted prefixes (tag hierarchy nodes). |
| `keys.index.tsv` | `dataTypeFullName<TAB>dataTypeSimpleName<TAB>category<TAB>scriptName<TAB>relativePath`. |

Lines starting with `#` are headers/comments; blank lines are ignored.

## How the plugin maps a call site to a key set (auto-detect by type)

| Code context | Key set |
|---|---|
| argument to `DataRegistry<T>.Get/TryGet(...)` | index row whose `dataTypeSimpleName == T` |
| value converted to `GameplayTag` (ctor / implicit op / assignment / argument) | `GameplayTags.keys.tsv` |
| member marked `[DataKey(typeof(X))]` / `[DataKey("Category")]` | index row for that type / category; no row → keys under that **name prefix** (`[DataKey("Attribute")]` → `Attribute.*`, real data keys only, no synthetic tag nodes) |
| (v2) `KeyDropdownValue` targets | resolved by nearby type context |

For each context the plugin offers the display names. Insertion mode follows the **target type**:

- target is `string` → insert `"Display Name"`.
- target is `uint` → insert the **hash literal with a name comment** (e.g.
  `2847362910u /* Attribute.Armor.Magic */`), completing by name. The comment keeps call sites readable
  in diffs and reviews, not just in-IDE; re-completing an annotated literal replaces the stale name
  comment (prose comments like `/* TODO */` are left alone).

Unknown keys resolve to a **warning** (not an error) so a momentarily stale manifest doesn't red-wall
the file; a quick-fix can run the regenerate command.

---

## Build & install

1. Install any modern .NET SDK (the project targets **net472** — that is what the Rider SDK's build assets
   are designed for; `Microsoft.NETFramework.ReferenceAssemblies` lets the dotnet CLI compile it without
   Visual Studio. Do NOT retarget to net8/net9: it still compiles via AssetTargetFallback, but Rider's
   in-IDE resolve then half-breaks semantic highlighting in this solution permanently).
2. Set the SDK version in `src/VaporKeysPlugin.csproj` to your **exact** Rider build (see below).
3. Run **`build.bat`** (double-click, or `build.bat Debug` for a debug build). It builds the solution **and
   packages `VaporKeysPlugin.zip`** — the Rider-installable plugin, laid out as Rider expects
   (`VaporKeysPlugin/META-INF/plugin.xml` + `VaporKeysPlugin/dotnet/VaporKeysPlugin.dll`).
4. Rider ▸ Settings ▸ Plugins ▸ ⚙ ▸ **Install Plugin from Disk…** ▸ pick
   **`VaporKeysPlugin.zip`** (next to `build.bat`) ▸ restart. (Rider needs the zip, not a bare DLL.)

The plugin locates the manifests via the opened solution directory
(`<solution>/Assets/Vapor/Keys/Definitions/Generated/`). For `project-idle` the solution root is
`project-idle.sln`, the parent of both Vapor folders — so it resolves correctly.

**Packaging note.** "Install Plugin from Disk" of a bare `.dll` is the ReSharper (Visual Studio) flow
and the quickest local loop. A distributable **Rider** plugin is normally a `.zip` produced by the
JetBrains Gradle plugin (a Kotlin front-end shell around this .NET back-end) with a `META-INF/plugin.xml`
descriptor. For a purely local, back-end-only feature like this you can start with the dll/dev-instance
loop; reach for the Gradle packaging only when you want to share it. See the JetBrains sample plugin for
the current packaging layout.

## Validate against your SDK

The reference-provider / completion / inlay APIs in `IdeIntegration.cs` are written against the
*shape* of the ReSharper SDK but **names change across versions**. Before relying on them:

1. Find your Rider version (Rider ▸ About). Set `<JetBrainsRiderSdkVersion>` in the csproj to it.
2. Open the compiled SDK assemblies (or the
   [resharper-unity](https://github.com/JetBrains/resharper-unity) and
   [sample plugin](https://github.com/JetBrains/resharper-rider-plugin) repos) and confirm the exact
   names for: `IReferenceFactory` / `ReferenceProviderFactory`, `ReferenceBase<T>`,
   `ResolveResultWithInfo`, the C# completion `ItemsProvider` base + `LookupItem` factory, and the
   inlay-hint provider base.
3. Fix any renamed symbols. The **logic** (what to attach to, what to look up, what to insert) is
   correct and lives in small methods that call into `ManifestStore` — that part won't change.

## Why `ManifestStore` matters

`Resolve()` and completion run constantly. Reading a file from disk in those callbacks (as the naive
approach does) causes editor hitches. `ManifestStore` parses once and reloads only when a manifest's
write-time changes, so the hot path is a dictionary lookup. Keep all file access behind it.
