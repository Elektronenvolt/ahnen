# Ahnen: native Windows desktop application

## Implementation progress (2026-10-29)

Implementation continues on `feature/windows-desktop-implementation`. This plan was
originally preserved on `feature/native-windows-desktop` and is restored here for reference.
Shared Core extraction and the WPF/MVVM shell are implemented. The first functional native
2D view now uses C# layout, batched WPF drawing, indexed picking, background cancellation,
search/details, root/focus/selection separation, chart modes, generation limits, collapse,
settings, zoom/pan/fit, overview/minimap and bounded native WebP thumbnails.

The user's newer storage requirement **supersedes the original per-user data proposal below**:
`appsettings.json` selects a filename, and the desktop document/configuration always live
beside the executable. Preferences remain per-user. Existing personal trees are not migrated
or overwritten automatically at runtime; publishing supplied files into a used folder still
requires care and backups.

Desktop regression tests cover layout invariants through 10,000 synthetic people, view-model
navigation/settings/root-save failures, and the actual WPF window rendering the supplied
200-person file without binding errors. A native bitmap-render probe exercises a 10,000-person
scene; its timings are diagnostic, not an accepted interactive performance budget.
A local Debug/.NET 10 Windows test run measured approximately 209 ms for a synthetic
10,000-person/9,999-family layout, 27 ms for its first 1,000 x 600 overview bitmap render,
and 20 ms for a focused viewport bitmap render. These isolated probes do not measure
sustained interactive FPS, startup/saving throughput, GPU diversity or high-DPI behavior.
Full 2D visual parity, crossing reduction/complex pedigree layout refinement, representative
GPU/high-DPI profiling and quantitative performance acceptance remain follow-up work.
The native editor, new 3D renderer/prototype gate, validated import, single-writer lifecycle
protection, and release packaging are not completed by this 2D increment.

## Recommendation and confirmed scope

Use **WPF on modern .NET 10, with native XAML controls and MVVM**, in a new
`Ahnen.Desktop` project added to `Ahnen.sln`. Extract reusable C# code into a
`net10.0` class library named `Ahnen.Core`, referenced by both `Ahnen.Web` and
`Ahnen.Desktop`. Keep the existing web application working.

Use `CommunityToolkit.Mvvm` for desktop observable state and commands. Evaluate
**HelixToolkit.Wpf.SharpDX v3** as the preferred native 3D renderer, subject to a
Windows prototype gate. It supplies a DirectX 11 scene/rendering layer; it does
not supply genealogical layout or guarantee acceptable performance at scale.
Keep the new layout and scene data independent of its rendering types.

The user explicitly chose a fully native rewrite rather than Blazor Hybrid.
The desktop application will have no BlazorWebView, browser runtime, JavaScript
UI, local HTTP server, or server circuit.

### Storage clarification: no application SQL

The current application does not use a SQL database. `DataStore` reads and writes
a JSON document using `System.Text.Json` and filesystem operations; photos are
separate files. Neither project currently references a database provider or ORM.

The desktop plan retains this JSON-and-photos storage model. It does not introduce
SQL, SQLite, SQL Server, Entity Framework or a database migration. The SQL tool
used during planning only records task status and dependencies in the assistant's
session workspace; it is not application code, does not contain genealogy data,
and creates no database dependency in the repository or desktop application.

Confirmed requirements:

- Modern .NET is allowed; legacy .NET Framework 4.x is not required.
- A separate desktop project must be created inside this repository and solution.
- Shared code belongs in a library usable by both applications.
- Preserve the web application.
- Create a new 3D family-tree view: readable cards/photos, links, selection,
  orbit, pan, and zoom. Do not port or preserve the current 3D design.
- Design for documents containing up to 10,000 people.
- Desktop data is separate from web data, with explicit copy/import of an
  existing JSON-and-photos folder.
- This document is the approved implementation plan, not an implemented desktop
  application.

## Project assessment

The assessment covers the solution/project definitions, domain and application
services, UI components and state, non-3D client visualization architecture,
resources/styles, test structure, configuration, deployment, and documentation.
Personal genealogy records were not inspected. The current 3D implementation
was excluded from the technology assessment; only its integration boundaries
and dependencies of the existing 2D view were considered.

| Area | Evidence and current behavior | Desktop implication |
|---|---|---|
| Hosting | `Ahnen.Web/Ahnen.Web.csproj:1-46`, `Ahnen.Web/Program.cs:1-62`, `Ahnen.Web/Components/App.razor:1-21`: .NET 10, Blazor Interactive Server, DI singletons, HTTP media endpoint and culture cookie | Keep the runtime generation; replace the host and UI rather than downgrade the application to .NET Framework |
| Domain | `Ahnen.Web/Models/FamilyTree.cs:1-154`, `Models/GedcomEnums.cs:1-71`, `Models/GedcomDate.cs:1-156` | Strong C# reuse: people, ordered names, families, pedigree links, events, canonical dates and relative media paths |
| Data and commands | `Ahnen.Web/Services/DataStore.cs:12-144,149-372`: JSON storage, validation, relationship commands, change notifications, deep-copy queries and one-step undo | Reuse behavior, but replace `IWebHostEnvironment` coupling with explicit host-supplied storage settings |
| Save semantics | `DataStore.cs:64-109,176-208`: temporary-file replacement and previous-save backup; name writes use candidate state and rollback, while generic mutations alter state before saving | Establish a consistent failure contract during extraction: failed desktop saves must not appear successful or leave published state out of sync with disk; retain pending edits |
| Genealogy rules | `Services/GedcomValidator.cs:11-66`, `RelationshipService.cs:10-155`, `RelationshipLabeler.cs:13-123`, `FamilyStatus.cs:11-67`, `Helpers/FamilyTreeExtensions.cs:6-43` | Share validation, ancestry rules, relationship derivation, family status and localized kinship labels; do not duplicate them in view models |
| Editor | `Components/Pages/Edit.razor:1-711`, `NameEditor.razor:1-313`, `NameEditorState.cs:6-117`, `PersonPicker.razor:1-86` | Rewrite Razor controls and lifecycle handling in XAML/MVVM. Reuse the UI-independent name draft/baseline state and domain commands |
| Existing editing contract | `Edit.razor:324-480,648-711`, `NameEditor.razor:133-313` | Preserve independent debounced name saves, ordered/duplicate name entries, invalid-date drafts, conflict checks, flush-on-navigation and one-step undo; add an explicit desktop close workflow |
| Photos | `Services/PhotoService.cs:12-83`, `Helpers/PhotoPaths.cs:3-13` | Reuse stream-based SkiaSharp processing and relative paths; replace browser uploads with Windows dialogs. Decode WebP for WPF using SkiaSharp rather than assume Windows has a WebP codec |
| 2D charts | `ClientSrc/tree.ts:1-486`, `tree-overview-layout.ts:1-506`, `tree-overview.ts:1-178`, `tree-depth.ts:1-62`, `tree-minimap.ts:1-93` | Topola/d3/SVG/DOM code cannot directly render as native WPF. Preserve its behavior with new C# layout and native drawing |
| Chart projection | `Services/ChartDataService.cs:8-127` | This is a Topola-specific adapter, including one preferred parent-family slot and husband/wife display slots. Keep it web-specific; never use its lossy projection as the new desktop 3D source |
| Hidden 2D dependency | `tree-overview-layout.ts:1-2`, `tree-overview.ts:10` | Existing 2D uses graph helpers in a file named `tree3d-layout.ts`. Do not delete it or break the web overview merely because desktop 3D is new |
| Selection and preferences | `Components/Pages/Tree.razor:214-533`, `ClientSrc/tree.ts:35-125,392-486` | Preserve distinctions between stored root, focus and selected person. Replace browser localStorage with desktop preferences without altering genealogy JSON |
| Localization and readability | `Resources/SharedResources.resx:7-70`, matching German resource, `Services/RelationshipLabeler.cs`, `wwwroot/app.css:1-100`, `README.md:44-59` | Reuse resource values and labels; implement native binding/refresh and preserve intentionally enlarged text, keyboard access and readable layouts |
| Diagnostics | `Services/ApplicationLogBuffer.cs:1-33`, `ApplicationLogProvider.cs:1-24`, `Components/Layout/ApplicationLogs.razor:1-91` | Share the logging provider/buffer; provide a native collapsible log viewer with the existing capacity and severity presentation |
| Tests | `Ahnen.Tests/Ahnen.Tests.csproj:1-34`, `TestStore.cs:10-45`, `DataFileConfigurationTests.cs:33-140`, `NamePersistenceTests.cs:13-105`, `ClientTests/tree-overview-layout.test.mjs:1-100` | Existing xUnit coverage protects reusable behavior; client tests protect web layouts. Add desktop-specific tests and independent 10,000-person fixtures |
| Deployment | `Dockerfile:1-27`, `Ahnen.Web/Ahnen.Web.csproj:16-23`, `README.md:77-127,164-181` | Retain web/Docker deployment. Desktop needs Windows publishing and packaging, writable data outside the installation directory, and safe import/update behavior |
| Interchange | `README.md:204-239`, `GEDCOMX-COMPATIBILITY.md:1-13` | Preserve the current schema and supported GEDCOM semantics. A GEDCOM-oriented model is not an implemented `.ged` import/export feature or full GEDCOM X support |

### Reuse versus rewrite

**Reuse in C#:** domain models and date parsing; structural helpers; validation;
relationship calculation and label generation; family status rules; JSON
storage and mutation semantics after host decoupling; photo processing; logging;
UI-independent name-editor state; English/German resources.

**Rewrite natively:** editor, person picker, detail panel, navigation, dialogs,
search UI, notifications, settings, log viewer, 2D drawing/layout integration and
all desktop 3D. Razor markup, CSS and DOM-based TypeScript are not native UI reuse.

**Retain in the web project:** ASP.NET startup/middleware and culture endpoint;
Razor pages; Topola adapter; TypeScript build and client tests; existing web 3D;
Linux-specific hosting/deployment details.

The desktop is a new presentation layer, not a new genealogy data model.

## Technology comparison

| Option | Fit for this project | Decision |
|---|---|---|
| WPF + .NET 10 + MVVM | Mature Windows data binding, forms, keyboard support, custom 2D rendering, and built-in/third-party native 3D integration; reusable C# core | Recommended for the requested native rewrite |
| WinUI 3 + Windows App SDK | Viable native Windows alternative with modern Fluent controls. HelixToolkit also has a WinUI renderer, so 3D is possible; it still requires the same Razor/TypeScript rewrite | Not selected: the current requirements do not establish an advantage that outweighs WPF's desktop visualization fit |
| Avalonia | Strong candidate if macOS/Linux desktop becomes a requirement; would also permit local UI work on macOS. Rendering backend/platform support needs its own assessment | Not selected for the Windows-only product requirement |
| Windows Forms | Native, straightforward forms and supported .NET host; less attractive for this application's rich diagram, styling, layout and MVVM composition | Not preferred |
| WPF + Blazor Hybrid | Highest potential reuse of the current editor and 2D chart; no server required, but retains an embedded-web UI/runtime | Considered and explicitly rejected by the user in favor of fully native UI |
| .NET MAUI | Useful for a broader mobile/cross-platform requirement, but adds a layer without resolving this native desktop graph/3D problem | Not preferred |
| Legacy .NET Framework 4.x | Would move away from the existing `net10.0` code and require compatibility work without a stated legacy requirement | Do not use |

### 3D recommendation and limitations

Start with `HelixToolkit.Wpf.SharpDX` v3, not the similarly named
`HelixToolkit.Wpf` package based on WPF's built-in Media3D renderer.
HelixToolkit offers a higher-level native scene graph suitable for evaluating
camera controls, picking, meshes, lines and card/label rendering without writing
raw DirectX integration.

Important caveats:

- HelixToolkit is MIT-licensed, but its SharpDX dependency is archived.
  Active HelixToolkit development does not remove that underlying maintenance risk.
- The inspected WPF SharpDX project targets `net8.0-windows`,
  `net6.0-windows` and `net48`, rather than explicitly targeting .NET 10.
  A compatible package asset is not proof that the full desktop stack has been
  validated on .NET 10. Verify the selected released package on Windows.
- Built-in WPF 3D remains an option for simpler scenes, but do not assume one
  visual/label/photo per person will scale to 10,000 people in either renderer.
- Layout, readable text, picking, batching, visibility and texture management
  are application work, not capabilities solved by choosing a 3D package.
- Keep scene/layout output as ordinary C# data. If the prototype fails on
  supportability or rendering requirements, stop and select a different native
  renderer before implementing the complete visualization. Do not silently
  replace native rendering with a browser.

## Proposed solution structure

```text
Ahnen.sln
Ahnen.Core/                 net10.0; no ASP.NET or WPF dependency
  Models/
  Helpers/
  Services/                 store, validation, genealogy, photos, logging
  Editing/                  reusable NameEditorState
  Resources/                shared English/German strings and marker type
Ahnen.Web/                  existing web host and UI; references Ahnen.Core
Ahnen.Desktop/              net10.0-windows; UseWPF; references Ahnen.Core
  Views/
  ViewModels/
  Visualization/            new C# 2D/3D layout and rendering
  Services/                 Windows dialogs, preferences, import, lifecycle
Ahnen.Tests/                existing core/web behavior tests, references adjusted
Ahnen.Desktop.Tests/        Windows-specific view-model/layout/integration tests
```

Keep the extraction to one shared production library initially; do not introduce
separate domain/application/infrastructure libraries without an actual need.
Use explicit `Microsoft.Extensions.*` dependencies where the current Web SDK
provided implicit usings or framework references. Retain SkiaSharp processing;
keep Linux-specific native asset configuration in the web host and verify
Windows native asset deployment for the desktop.

The desktop must not reference the executable web project. Tests that exercise
Topola projection can still reference the web project; core-only tests can
reference the library. Adjust fake environments, resource discovery and schema
reflection checks after moving types. Avoid using `typeof(Program).Assembly`
to locate shared desktop routes/resources.

## Storage, threading and lifecycle decisions

- The web host retains `data/` under its content root and filename-only
  `DataFileName` validation. Preserve its missing-file/read-only behavior and
  existing configuration tests.
- The desktop supplies a writable root such as `%LOCALAPPDATA%\Ahnen\data`.
  Application settings and logs/cache stay separate from genealogy records.
- First use presents explicit copy/import of an existing document and its photos;
  no automatic migration, sample seeding, overwrite or opening the web's live data.
  Preserve the selected filename, schema, IDs and relative `photos/` paths.
- Validate the candidate import and required media paths before activating it.
  Surface malformed JSON, unsupported schema, missing source media and destination
  conflicts explicitly. Never show a successful import after a partial copy.
- Preserve prior-save `.bak`, temporary-file replacement and one-step document undo.
  Explain that document undo does not currently recover photo files already deleted;
  do not promise full media undo without separate design work.
- Prevent simultaneous desktop writers to the same desktop document, including a
  second app instance. No shared live web/desktop editing or synchronization is planned.
- Use an ordered save workflow and versioned/cancellable layout work. Do not run
  full-document cloning, validation, serialization or layout on the UI thread
  for 10,000-person documents. Publish coherent snapshots and marshal UI updates
  to the WPF dispatcher.
- Flush pending valid edits on navigation and close. If invalid or unsaved edits
  remain, require an explicit discard or cancel the transition; save failures keep
  drafts and block a false success/close.
- Preserve stored GEDCOM date strings; a standard DatePicker alone is insufficient
  for partial or approximate dates.

## Native visualization requirements

### 2D

Preserve hourglass, relatives, ancestors, descendants, generation limits,
expand/collapse, color and orientation settings, photos, root/focus/selection,
search navigation, zoom/pan/fit and all-people overview/minimap.
Retain relationship/status/pedigree styling and disconnected components.
Repeated cards are display instances referencing stable person IDs, not
duplicated records.

Use pure C# layout results and a batched drawing approach such as
`DrawingVisual`/`DrawingContext`, with spatial hit testing and bounded bitmap
caches. Use ordinary accessible controls for search, details and commands.
Do not implement the whole 10,000-person canvas as thousands of heavyweight
templated controls or assume `VirtualizingStackPanel` virtualizes a graph.
Use existing TypeScript tests as behavioral examples and fixtures, not a claim
that Topola can simply be dropped into WPF.

### New 3D

Design a new scene from complete Core people/families/pedigree links, independent
of the old renderer and its layout. Include readable person cards/photos, family
junctions, links, picking, selected-person details, camera controls and fit.
Do not prescribe the old generation-terrace design.

For 10,000-person documents, prototype batching/instancing where supported,
distance-based detail, visibility culling and bounded thumbnail/texture caches.
At overview scales, detailed text/photos may be reduced; the full document and
relationships must remain represented, and selecting/searching a person must
restore readable detail. Provide an accessible person-list/detail alternative.
Visual generation conflicts or pedigree collapse must never rewrite stored data.

## Todos and dependencies

1. **Extract shared behavior** (`extract-core`): create `Ahnen.Core`, move reusable
   types/resources, decouple storage paths from ASP.NET, retain host-specific
   adapters and expand save-failure/snapshot coverage. Update web references and
   existing tests without changing the web storage location/schema.
2. **Create native application shell** (`desktop-shell`, depends on
   `extract-core`): add `Ahnen.Desktop` to the solution, WPF/MVVM startup, DI,
   native navigation, localization, preferences and logging.
3. **Prove rendering approach** (`rendering-spike`, depends on `desktop-shell`):
   validate released HelixToolkit assets on .NET 10/Windows and prototype native
   2D and new 3D with synthetic 200/1,000/10,000-person documents. Exercise label
   readability, picking, camera controls, photo memory, layout cancellation,
   high-DPI and integrated-GPU behavior. Record measured limits and agree on
   quantitative acceptance criteria before full visualization implementation.
4. **Implement desktop document lifecycle** (`desktop-storage`, depends on
   `desktop-shell`): per-user paths, explicit validated copy/import, ordered
   persistence, backups, single-writer protection and close/navigation save gates.
5. **Implement native editor and details** (`native-editor`, depends on
   `desktop-storage`): search/list, names, facts, canonical dates, relationships,
   photos, safe delete, diagnostics and one-step undo; reuse Core rules and state.
6. **Implement native 2D charts** (`native-2d`, depends on `rendering-spike` and
   `desktop-storage`): C# layouts and native renderer with parity for the current
   2D behaviors and scale-aware interaction.
7. **Implement new native 3D** (`native-3d`, depends on `rendering-spike` and
   `desktop-storage`): implement the newly designed scene and renderer, not a
   port of the current 3D view.
8. **Integrate and verify behavior** (`desktop-integration`, depends on
   `native-editor`, `native-2d`, `native-3d`): coordinate selected/focus/root IDs
   across views, dirty state, cultures, errors and cancellation; complete native
   and shared regression coverage.
9. **Package and document** (`desktop-release`, depends on
   `desktop-integration`): Windows self-contained x64 publish and installer,
   clean-machine/native dependency checks, update/data safety, licensing notices,
   desktop instructions and Windows build/test path, while retaining web/Docker.

The renderer prototype is a decision gate, not evidence that 10,000-person
interactive performance is already achieved. A different renderer requires an
explicit updated decision before proceeding.

## Verification plan

- Preserve existing `dotnet test`, `npm --prefix Ahnen.Web test` and
  `npm --prefix Ahnen.Web run check` coverage; verify web build/publish after extraction.
- Add Core tests for host-independent paths, unchanged JSON round trips,
  failed writes, backups, stale names, command sequencing and coherent snapshots.
- Add desktop tests for view models, draft flushing, invalid inputs, native
  photo conversion, localization, root/selection separation and cross-view updates.
- Test imports using synthetic/copied fixtures only, including missing media,
  malformed JSON, schema rejection, cancellation and destination conflicts.
- Test pure 2D/3D layout invariants: stable IDs, every expected person/family/link
  represented, disconnected people, multiple partnerships, adoption, pedigree
  collapse, no accidental record duplication and deterministic results.
- Measure 10,000-person behavior on representative Windows hardware, including
  startup, layout, saves, interaction and repeated-view-switch memory use.
  Capture results and set acceptance thresholds at the prototype gate rather
  than claim unmeasured frame rates or memory budgets.
- Perform Windows integration checks for keyboard navigation, high-DPI scaling,
  accessible alternatives, close/save failures, unsupported graphics and offline use.
- Verify the installer does not include real genealogy data, overwrite existing
  desktop documents or require administrator access to save user data.
- Resume implementation on Windows. WPF UI, graphics, native dependencies and
  installer verification require a Windows machine/VM or Windows CI.
  Cross-compilation alone does not verify the desktop.

## Boundaries

No mobile/cross-platform desktop release, live web/desktop synchronization,
database migration, GEDCOM/GEDCOM X importer/exporter, multi-document workspace,
full undo history, game engine, or unrelated web redesign is included.
Do not delete or redesign the web application's current 3D view as part of this
desktop work. New 3D visual design details remain a prototype deliverable.

## Technology sources

- [Microsoft: WPF overview](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/overview/)
  confirms modern .NET support, Windows-only execution, binding and 2D/3D graphics.
- [Microsoft: .NET support policy](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core)
  identifies .NET 10 as an LTS release; the repository already uses it.
- [Microsoft: Blazor Hybrid](https://learn.microsoft.com/en-us/aspnet/core/blazor/hybrid/?view=aspnetcore-10.0)
  documents the considered embedded-web alternative and its native .NET execution model.
- [Microsoft: WPF Blazor tutorial](https://learn.microsoft.com/en-us/aspnet/core/blazor/hybrid/tutorials/wpf?view=aspnetcore-10.0)
  documents the .NET 10 composition-control change; old WebView2 airspace advice
  is not a reason to reject the hybrid option. The user chose native UI instead.
- [HelixToolkit upstream](https://github.com/helix-toolkit/helix-toolkit)
  documents DirectX 11, WPF/WinUI renderers, MIT licensing and v3 development.
- [HelixToolkit WPF SharpDX project](https://github.com/helix-toolkit/helix-toolkit/blob/develop3/Source/HelixToolkit.Wpf.SharpDX/HelixToolkit.Wpf.SharpDX.csproj)
  provides the inspected target frameworks and package dependencies.
- [HelixToolkit v3.1.2 release](https://github.com/helix-toolkit/helix-toolkit/releases/tag/v3.1.2)
  was the latest release returned by the upstream API during assessment.
- [SharpDX upstream](https://github.com/sharpdx/SharpDX)
  is archived, confirmed by its repository metadata.
- [Microsoft: WinUI quickstart](https://learn.microsoft.com/en-us/windows/apps/get-started/start-here)
  documents the alternative modern Windows application stack.
