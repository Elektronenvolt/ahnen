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
  - Levels stay anchored to the root until **Set as root** is used. Separate families are labeled
    independently. Complex pedigrees can span levels; layout warnings preserve every recorded
    relationship rather than rewriting the data. Figures do not imply age.
  - Chart types: **Hourglass** (ancestors + descendants, default), **Relatives**, **Ancestors**,
    **Descendants**. Click a person to center the chart on them; Shift-click only shows details.
  - **Show all people** clears the selection and opens a temporary 2D overview including disconnected
    families and isolated people. Selecting a person returns to the preferred chart type; Shift-click
    still opens details without changing the view. In 3D, the button clears relationship highlighting.
    Zoom, display settings, and the stored root are unchanged. Use **Fit to view** separately to fit
    the whole overview on screen.
  - Settings (⚙, remembered in the browser): colours (none / by sex / by generation),
    vertical/horizontal orientation, show IDs, show sex symbols.
  - Detail panel with all data plus the derived relationship to the root person
    (e.g. *great-uncle / Urgroßonkel*); set any person as root.
- **Edit page** (`/edit`) — master-detail editor: searchable person list, names/facts/photo/relationship
  sections, auto-save with undo toast, smart date input, photo upload, and safe delete with impact
  preview.
- **Languages:** English and German (switcher in the header). Data is always stored untranslated in
  canonical GEDCOM form.

## Running locally

Requirements: [.NET 10 SDK](https://dotnet.microsoft.com/download) and [Node.js](https://nodejs.org)
(Node is only needed to build the TypeScript tree module).

```bash
cd Ahnen.Web
dotnet run
```

Open the printed URL (e.g. http://localhost:5205). On first start a small sample family is seeded.

### Data storage

All data lives in one folder — **backing up your tree = copying one folder**:

```
Ahnen.Web/data/
├── familytree.json       # all persons & families (GEDCOM 7-mapped, see below)
├── familytree.json.bak   # automatic backup of the previous save
└── photos/               # uploaded photos + thumbnails (webp)
```

The location can be changed with the `DataDirectory` setting in `appsettings.json` or the
`FAMILYTREE_DATA_DIR` environment variable. Writes are atomic (temp file + rename) and every save
first keeps the previous version as `.bak`.

Photos are re-encoded on upload (strips EXIF/payloads), downscaled to max 1600 px and stored as
webp together with a 160×160 thumbnail. The JSON only stores the relative path.

## Tests

```bash
dotnet test
# deterministic client layout and relationship tests
npm --prefix Ahnen.Web test
```

## Deployment

```bash
# self-contained publish (no .NET runtime needed on the target)
dotnet publish Ahnen.Web -c Release -r linux-x64 --self-contained -o publish

# or with Docker
docker build -t ahnen .
docker run -p 8080:8080 -v ahnen-data:/data ahnen
```

The Docker volume `/data` holds the JSON file and photos.

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
Ahnen.Web/
├── Models/        GEDCOM 7-aligned domain model + GedcomDate parser
├── Services/      FamilyTreeStore (JSON persistence, CRUD, undo, validation),
│                  RelationshipService (derives kinship labels),
│                  ChartDataService (Topola JSON mapping), PhotoService (SkiaSharp)
├── Components/    Blazor pages: Tree (/) and Edit (/edit)
├── ClientSrc/     tree.ts — Topola + d3-zoom, tree3d.ts — Three.js renderer,
│                  tree3d-layout.ts — pure family graph layout and neighborhood selection (esbuild)
├── ClientTests/   Node test-runner layout regressions (using the existing esbuild dependency)
├── Resources/     en/de string resources
└── seed/          sample family used on first start
```

All licenses are free: Apache-2.0 (Topola), ISC/BSD (d3), MIT (Three.js, esbuild, SkiaSharp, .NET).
