# Ahnen 🌳

A local-first family tree web application built with **.NET 10 Blazor** (Interactive Server) and a
TypeScript visualization layer based on [Topola](https://github.com/PeWu/topola) (Apache-2.0), d3,
and Three.js (MIT).

- **Family tree page** (`/`) — interactive Topola chart: person boxes with photos, spouse pairs,
  multiple marriages, expand/collapse of branches, animated transitions, zoom/pan (wheel, drag,
  toolbar), fit-to-view and automatic re-fit on window/panel resize. An optional **3D view** shows
  people on generation terraces: earlier levels are higher/rearward, later levels lower/frontward.
  Level headings count upward toward ancestors (**G+1**, **G+2**) and downward toward descendants
  (**G-1**, **G-2**), with the root at **G0**. An English/German legend complements the colors. Partners share
  family junctions; arrows point toward children. Hover or select someone to highlight their
  parents, partners, children, and connecting links without rearranging the tree.
  Orbit, pan, zoom, and fit-to-view remain available.
  Person figures, cards, photos, and card text are doubled in size for readability, with
  extra spacing only where needed to keep items apart. **Fit to view** still fits the complete
  3D tree and may therefore zoom out farther; headings and relationship badges retain their size.
  Given names and surnames on 3D cards use an additional 50% larger font; other card text is unchanged.
  Cards are centered left-to-right on their platforms and remain in front of their person figures.
  - Levels stay anchored to the root until **Set as root** is used. Separate families are labeled
    independently. Complex pedigrees can span levels; layout warnings preserve every recorded
    relationship rather than rewriting the data. Figures do not imply age.
  - Chart types: **Hourglass** (ancestors + descendants, default), **Relatives**, **Ancestors**,
    **Descendants**. Click a person to center the chart on them; Shift-click only shows details.
  - **Show all people** clears the selection and opens a temporary 2D overview including disconnected
    families and isolated people. Each person appears once; couples sit side by side, a person married
    several times is drawn once with the spouses on both sides in marriage order, and every marriage has
    its own junction with right-angle links to the children (married solid, divorced/separated dashed,
    unmarried dotted, adopted children dashed). Generation lanes carry the same `G+n / G0 / G-n` labels
    as the 3D view, and separate families are packed side by side to fit the window. Large families are
    split into blocks that repeat the parents as faint dotted *ghost* cards (also used when a marriage
    joins two branches). Zoomed out, cards turn into readable chips and then tiles; hover a person to
    highlight their parents, partners and children; the minimap (bottom left) shows the visible area
    and pans on click/drag. Selecting a person returns to the preferred chart type; Shift-click
    still opens details without changing the view. In 3D, the button clears relationship highlighting.
    Zoom, display settings, and the stored root are unchanged. Use **Fit to view** separately to fit
    the whole overview on screen.
  - In the person charts the focus person is outlined in every repeated card (once per marriage) and
    ended partnerships are drawn dashed; **Generations shown** (⚙) limits ancestors and descendants
    around the focus person.
  - Settings (⚙, remembered in the browser): colours (none / by sex / by generation),
    vertical/horizontal orientation, show IDs, show sex symbols.
  - Detail panel with all data plus the derived relationship to the root person
    (e.g. *great-uncle / Urgroßonkel*); set any person as root.
- **Edit page** (`/edit`) — master-detail editor: searchable person list, names/facts/photo/relationship
  sections, auto-save with undo toast, smart date input, photo upload, and safe delete with impact
  preview. Interface text and control padding are enlarged by 25% for readability, including
  the editor, person lists, dialogs, header, person search, and 2D/3D tree controls;
  fields and actions wrap to fit smaller screens.
  Person-search suggestions use the same enlarged text; select with the mouse or
  Up/Down and Enter, and dismiss with Escape.
- **2D chart readability:** chart text and boxes are enlarged by an additional 20% at the same
  zoom level in all person charts and **Show all people** (including chips, tiles, and generation
  labels). **Fit to view** still fits the complete chart and may therefore zoom out further.
  This additional scaling does not affect the toolbar, detail panel, editor, or 3D view.
- **Languages:** English and German (switcher in the header). Data is always stored untranslated in
  canonical GEDCOM form.

## Running locally

Requirements: [.NET 10 SDK](https://dotnet.microsoft.com/download) and [Node.js](https://nodejs.org)
(Node is only needed to build the TypeScript tree module).

```bash
cd Ahnen.Web
dotnet run
```

Open the printed URL (e.g. http://localhost:5205). The configured `data/familytree.json` contains
a supplied 200-person test tree. The application loads the selected file directly; there is no
first-run seeding or automatic replacement of saved trees.

### Web data storage

All data lives in one folder

```
Ahnen.Web/data/
├── familytree.json       # supplied test dataset; selected by appsettings.json
├── familytree.json.bak   # automatic backup of the previous save
├── ahnen.json            # optional empty dataset (select explicitly)
└── photos/               # uploaded photos + thumbnails (webp)
```

The folder is always `data/` under the application's content root (`Ahnen.Web/data/` locally,
`data/` beside the published application, `/app/data` in Docker). Photos remain in `data/photos/`.
Writes are atomic (temp file + rename), and every save keeps the previous version as `.bak`.

Select a file by **filename only** in `appsettings.json`:

```json
"DataFileName": "familytree.json"
```

Any valid filename is allowed; absolute paths, subdirectories, and traversal are not. There is no
implicit filename fallback. The optional `data/ahnen.json` contains `{}`; select it with
`"DataFileName": "ahnen.json"` to start with an empty, editable tree. Changing the setting does not
copy data between files. Back up these files before editing; the supplied test tree is also mutable
when selected.

If the selected file is missing, the application still starts with no tree data, logs an **Error**,
and disables editing, including creation of new people. Missing/blank or invalid filename
configuration also produces a logged error and the same read-only state. No replacement data file is
created. Correct `DataFileName` or supply the file in `data/`, then restart the application.
Malformed JSON and other file read errors remain explicit errors rather than being treated as
missing data.

**Configuration migration:** `DataFilePath`, `DataDirectory`, and `FAMILYTREE_DATA_DIR` are no longer
used. Move existing trees and photos into the application's `data/` folder and configure only the
filename. Do not publish supplied data files over saved trees or replace a used `ahnen.json` with
the supplied empty file. Keep deployment backups or mount persistent storage at the fixed folder.

### Test dataset

`Ahnen.Web/data/familytree.json` is the supplied deterministic 200-person test tree with
married and single people, childless couples, one to six children per family, single parents,
repeated marriages, divorced/separated and unmarried partnerships, adopted children, marriages
between branches, disconnected families, and isolated people. It showcases the person charts,
**Show all people** overview, and **3D view**. The generator is test-data tooling only and is never
called at application startup. To generate a separate test dataset:

```bash
cd Ahnen.Web
node tools/generate-sample-tree.mjs data/example.json 42 200
# then select "DataFileName": "example.json" and restart
```

The generator's default output is `data/familytree.json`; running it without arguments overwrites
that file. Only regenerate the supplied dataset if it has no personal changes to preserve.

Photos are re-encoded on upload (strips EXIF/payloads), downscaled to max 1600 px and stored as
webp together with a 160×160 thumbnail. The JSON only stores the relative path.

### Application logs

The **Logs** section at the bottom of every page starts collapsed and can be expanded with the
keyboard or mouse. It shows timestamp, level, category, event ID, message, and exception details.
Only level labels are severity-colored: Warning is orange, Error and Critical are red,
and Information, Debug, and Trace retain the default text color. All other entry text is unchanged.
While expanded, it refreshes approximately once per second. The latest 500 entries are kept in
process memory, including startup errors, and are cleared on restart.

The viewer is an additional standard .NET logging provider, using the existing `Logging:LogLevel`
configuration. Existing logging targets are unchanged; provider-specific filters can differ.
Viewer-specific filtering can be set with `Logging:ApplicationLogs:LogLevel`. Logs are process-wide,
not per browser, and are visible to every application user. Do not include secrets in log messages.

## Tests

```bash
# shared-library tests only (no web or Node.js build required)
dotnet test Ahnen.Core.Tests/Ahnen.Core.Tests.csproj
# all .NET tests
dotnet test Ahnen.sln
# deterministic client layout and relationship tests
npm --prefix Ahnen.Web test
```

`Ahnen.Core.Tests` covers the shared domain model, dates, validation, persistence, editing state,
relationships, photos, logging, helpers, and resources independently of the web host.
`Ahnen.Tests` covers web configuration, chart mapping, sample datasets, and Blazor log rendering.
All projects target `net10.0`; building and testing the solution does not require Windows.

## Deployment

```bash
# self-contained publish (no .NET runtime needed on the target)
dotnet publish Ahnen.Web -c Release -r linux-x64 --self-contained -o publish

# or with Docker
docker build -t ahnen .
docker run -p 8080:8080 -v ahnen-data:/app/data ahnen
```

The Docker volume `/app/data` holds the JSON files and photos. Remount any existing volume previously
used at `/data` at `/app/data`; the application no longer uses `/data`. An empty bind mount hides
the packaged files, so explicitly supply your selected JSON before editing. Until then, the app
starts read-only and reports the missing file in Logs.

## Editing personal names

The name-type selector opens a separately stored name, rather than changing the type of the
current entry. A type with no recorded name opens a blank draft; selecting it alone saves nothing.
Typing any name component saves that entry after a short debounce. Changes to names are saved
independently of facts, so an invalid pending date does not prevent a name save.

Field labels follow the selected type in English and German. **More name details** exposes optional
nickname, prefix, and suffix fields for every type; collapsing it never discards those values.
Name text is stored verbatim, including spacing and capitalization, regardless of the UI language.

Use the entry selector to edit duplicate names of the same type, or **Add another name of this type**
to start a new draft. Records are never merged or reordered: the first recorded name remains the
display name. **Remove this name** removes only the selected entry and always retains at least one
record. **Saved / Undo** confirms successful persistence and restores the previous save using the
app's one-step undo. A later change elsewhere replaces that undo opportunity.

Pending name edits are flushed when switching types, entries, or people and before fact saves or
leaving the editor. Save failures appear inline, retain the unsaved edits, and do not show **Saved**.
If stored names have changed in the meantime, reload them explicitly rather than overwrite another
edit; the reload button discards local name changes.

## GEDCOM 7 compatibility

**Binding rule: in any design conflict, GEDCOM compatibility overrules.** The JSON schema is a 1:1
projection of the GEDCOM 7 conceptual model, so a lossless `.ged` import/export can be added without
any data migration. Only GEDCOM-legal enum values are ever persisted (validated on every save), and
non-standard events use GEDCOM's generic `EVEN` + `TYPE` (e.g. separation).

Relationships like *brother*, *nephew* or *cousin* are **never stored** — only the two structural
GEDCOM link types (partner, parent-child) are, and everything else is derived at display time.

### Field mapping

| JSON field | GEDCOM 7 |
|---|---|
| `schemaVersion` | app metadata (omitted on export) |
| `rootPersonId` | extension tag `_ROOT` |
| `persons[].id` | `@I…@` xref of `INDI` |
| `persons[].names[].type` | `NAME.TYPE` (BIRTH, MARRIED, AKA, MAIDEN, …) |
| `persons[].names[].given` | `NAME` / `GIVN` |
| `persons[].names[].surname` | `NAME` / `SURN` |
| `persons[].names[].prefix` | `NPFX` |
| `persons[].names[].suffix` | `NSFX` |
| `persons[].names[].nickname` | `NICK` |
| `persons[].sex` | `SEX` (M, F, X, U) |
| `persons[].events[].type` | individual event tag (`BIRT`, `DEAT`, `BURI`, `CREM`, `BAPM`, `CHR`, `RESI`, `OCCU`, `EDUC`, `EMIG`, `IMMI`, `NATU`, `EVEN`) |
| `persons[].events[].value` | event payload / `EVEN.TYPE` text |
| `persons[].events[].date` | `DATE` (canonical GEDCOM date incl. `ABT`/`BEF`/`AFT`) |
| `persons[].events[].place` | `PLAC` |
| `persons[].address.lines` | `ADDR` / `ADR1`, `ADR2` |
| `persons[].address.city` | `CITY` |
| `persons[].address.postalCode` | `POST` |
| `persons[].address.country` | `CTRY` |
| `persons[].notes[]` | `NOTE` |
| `persons[].media[].file` | `OBJE` / `FILE` (relative path) |
| `persons[].media[].title` | `TITL` |
| `families[].id` | `@F…@` xref of `FAM` |
| `families[].partnerIds[]` | `HUSB` / `WIFE` (mapped by sex) |
| `families[].events[].type` | family event tag (`MARR`, `DIV`, `ANUL`, `DIVF`, `ENGA`, `MARB`, `MARC`, `MARL`, `EVEN`) |
| `families[].events[].typeDetail` | `EVEN.TYPE` text (e.g. `Separation`) |
| `families[].events[].date` | `DATE` |
| `families[].events[].place` | `PLAC` |
| `families[].childRefs[].personId` | `CHIL` |
| `families[].childRefs[].pedigree` | `INDI.FAMC.PEDI` (BIRTH, ADOPTED, FOSTER, SEALING, OTHER) |

A unit test (`GedcomMappingTests`) asserts that every schema field keeps a documented row in this
table, so exportability cannot silently break.

## Architecture

```
Ahnen.Core/        shared net10.0 library (no ASP.NET dependency), used by Web and available to future hosts
├── Models/        GEDCOM 7-aligned domain model + GedcomDate parser
├── Services/      DataStore (JSON persistence, CRUD, undo, validation; storage location is
│                  supplied by the host via DataStoreOptions; a failed save rolls back state),
│                  RelationshipService (derives kinship labels), PhotoService (SkiaSharp),
│                  ApplicationLogBuffer / ApplicationLogProvider (live log history)
├── Helpers/       small shared extensions (ancestry walk, marriage event, text join, photo paths)
├── Editing/       NameEditorState (UI-independent name draft state)
└── Resources/     en/de string resources

Ahnen.Web/
├── Services/      ChartDataService (Topola JSON mapping), WebDataStoreOptions (data/ under content root)
├── Components/    Blazor pages: Tree (/) and Edit (/edit)
├── ClientSrc/     tree.ts — Topola + d3-zoom, tree3d.ts — Three.js renderer,
│                  tree3d-layout.ts — pure family graph layout and neighborhood selection,
│                  tree-overview-layout.ts — pure family-unit layout of the 2D overview,
│                  tree-overview.ts / tree-minimap.ts — overview rendering, minimap,
│                  tree-depth.ts — generation limit, shared.ts — shared types (esbuild)
├── ClientTests/   Node test-runner layout regressions (using the existing esbuild dependency)
├── tools/         generate-sample-tree.mjs — deterministic sample data generator
└── data/          selected JSON tree, supplied test/empty datasets, photos, and backups

Ahnen.Core.Tests/  independent shared-library tests; temporary-store fixture source also linked by Ahnen.Tests
Ahnen.Tests/       web-host and integration tests
```

All licenses are free: Apache-2.0 (Topola), ISC/BSD (d3), MIT (Three.js, esbuild, SkiaSharp, .NET).
