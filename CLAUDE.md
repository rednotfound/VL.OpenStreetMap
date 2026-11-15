# CLAUDE.md

Guidance for Claude Code working in this repository.

## Project overview

**VL.OpenStreetMap** gets OpenStreetMap **data** into vvvv gamma - buildings, roads, water, places -
as NetTopologySuite features, from the Overpass API or a `.osm` / `.osm.pbf` file. Not map tiles
(VL.Mapsui's `OpenStreetMap` layer is the picture; this is the data behind it). Started 2026-11-15
for real data to test and teach with. **Read [docs/DESIGN.md](docs/DESIGN.md) first**; the research
and measurements behind it are in [docs/OSM-RESEARCH.md](docs/OSM-RESEARCH.md).

A member of the VL.NetTopologySuite family (`D:\2026_Projects\vl-nettopologysuite` is its centre,
whose CLAUDE.md and `docs/RULES.md` carry the family's rules). It depends on VL.NetTopologySuite and
on nothing else of ours. Memory for the whole family lives in that repository's memory folder.

## How it works

- **OsmSharp reads, this package builds the geometry.** OsmSharp 7.0.0-pre028 parses OSM XML (what
  Overpass is asked for) and `.osm.pbf`. OsmSharp.Geo is NOT used: measured 2026-11-15 it made no
  polygon at all from a real Overpass answer. `OsmGeometry.cs` applies the published polygon rules
  (`PolygonRules.cs`, the 27 rules of osm-polygon-features, restated rule for rule), joins
  multipolygon members into rings with NTS's LineMerger, and turns a `type=building` relation into
  its outline (JP Tower vanished otherwise: its outline way has no tags).
- **Overpass is polite and cached.** Nothing is fetched until Download; one request at a time in the
  process; a User-Agent naming the package; every answer saved under
  `%LOCALAPPDATA%\VL.OpenStreetMap\overpass\` and shown at once, offline, the next time the same
  query is asked. Errors are a status, never an exception. The main server is busy (a 502 and a
  33 s answer on the first two requests, 2026-11-15).
- **Process nodes run on a worker thread** (`Background.cs`): a node runs sixty times a second and
  may never wait for the network or a file. The features stay one Spread per result.

## Rules carried from the family (read the originals in vl-nettopologysuite)

- `[assembly: ImportAsIs(Namespace = "VL")]` is mandatory (rule 1). Every NuGet dependency is also a
  `<NugetDependency>` in `VL.OpenStreetMap.vl` and is installed into `deps\` by `build.ps1` (rule 2).
- Tooltips are short: `///` is what a user sees on hover; reasoning goes in `//` above it.
  `NodeDocsTests` enforces it.
- **Commit, do not push.** The maintainer decides every push and does every nuget.org upload.
- **vvvv:** never `Stop-Process`; the launcher (`tools\Open-HelpPatch.ps1`) writes its pid to
  `%TEMP%\vl-openstreetmap-vvvv.pid` and closes only that, with WM_CLOSE; never launch while a
  foreign vvvv runs; never leave vvvv running unattended. Run `tools\Normalize-HelpPatches.ps1`
  after any GUI session, and `git status help/` before touching a `.vl`: the maintainer arranges
  patches by hand.
- Help patches follow VL.NetTopologySuite's `docs/HELP-PATCH-STYLE.md`: a 20pt heading, short `<`
  notes, a cell per featured node, input and result drawn together, data flowing down.
- The README is the short vvvv-library template; links absolute (it is the nuget.org page).

## Commands

```powershell
.\build.ps1                     # vvvv closed: build + stage dist\ + install dependencies into deps\
dotnet test test\VL.OpenStreetMap.Tests\VL.OpenStreetMap.Tests.csproj
.\tools\Test-VLPackage.ps1
.\tools\Test-VLPatch.ps1
.\pack.ps1
.\tools\Compile-HelpPatches.ps1  # vvvvc on every help patch, then READS the generated C#
.\tools\Open-HelpPatch.ps1 "Get buildings"
```

## Verification

| | state |
|---|---|
| `dotnet test` | 35 tests, no network; the Tokyo Station answer (test/fixtures) plus hand-written cases; the polygon rules negative-tested (5 tests red with them off) |
| `Test-VLPatch`, `Test-VLPackage` | pass: 6 help patches, Help.xml |
| `Compile-HelpPatches` (`vvvvc`) | all 6 compile; every node of ours resolved and its renamed pins wired, read from the generated C# |
| the vvvv GUI | 2026-11-15: Read an OSM file (783 features, 86 points / 651 lines / 46 polygons), Split by type, Height (50 m from `height=50`) seen working. Not yet: the two Overpass patches with Download pressed, ReadOSM on a .pbf |
