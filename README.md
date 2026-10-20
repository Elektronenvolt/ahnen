# Ahnen 🌳

A local-first family tree web application built with **.NET 10 Blazor** (Interactive Server) and a
TypeScript visualization layer based on [family-chart](https://github.com/donatso/family-chart) (MIT, d3).

- **Family tree page** (`/`) — Ancestry-style interactive chart: person cards with photos, marriage
  lines, zoom/pan (wheel, pinch, toolbar), fit-to-view, automatic re-render on window/panel resize
  (never overlapping), re-root on any person, and a detail panel that shows all data plus the derived
  relationship to the root person (e.g. *great-uncle / Urgroßonkel*).
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
│                  ChartDataService (family-chart mapping), PhotoService (SkiaSharp)
├── Components/    Blazor pages: Tree (/) and Edit (/edit)
├── ClientSrc/     tree.ts — thin TypeScript wrapper around family-chart (built with esbuild)
├── Resources/     en/de string resources
└── seed/          sample family used on first start
```

All licenses are free: MIT/ISC (family-chart, d3, esbuild), MIT (SkiaSharp), MIT (.NET).
