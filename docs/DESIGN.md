# VL.OpenStreetMap: design

**VL.OpenStreetMap** gets OpenStreetMap **data** into vvvv gamma: buildings, roads, water, shops,
as NetTopologySuite features with every OSM tag as an attribute. It reads them live from the
Overpass API or from a downloaded `.osm` / `.osm.pbf` file. Its first purpose is **real data to
test and teach with**, in a few nodes.

Not map tiles. VL.Mapsui's `OpenStreetMap` node (category `Mapsui.Layers`) shows the tiles: a
*picture* of the map. This package gives the *data behind the picture*, which can be measured,
filtered, intersected and drawn any way you like, on top of those tiles or anywhere else.

Started 2026-11-15. Everything below is a **proposal** until the maintainer confirms it. The
research and the measurements behind it are in [OSM-RESEARCH.md](OSM-RESEARCH.md).

---

## Why a package of its own

| Sibling | Why not there |
|---|---|
| VL.NetTopologySuite | Geometry only, no network and no maps; every sibling depends on it, so it must stay small. |
| VL.NetTopologySuite.GeoJSON | A file format. Overpass speaks OSM's own model (nodes, ways, relations), not GeoJSON. |
| VL.Mapsui | Draws. Where data comes from is not its business. |
| VL.Overworld | The course; it has no nodes. It will use this package. |

**Depends on VL.NetTopologySuite only** (and on NuGet libraries), like every sibling. It does not
depend on the GeoJSON package: its features go straight into `ToGeoJSON`, so "fetch once, save a
GeoJSON file, teach offline from it" is a wire, not a dependency. Examples that use several
packages together live in VL.Overworld, which declares them all.

## The engine: OsmSharp to read, our own code to build geometry

Measured 2026-11-15 on a real Overpass response around Tokyo Station (381 KB: 2142 nodes, 746
ways, 7 relations):

- **OsmSharp 7.0.0-pre028 reads OSM correctly**: every element, every tag, multilingual names,
  `height=50`. It also reads `.osm.pbf`, which nothing else in .NET does as well. MIT licence;
  its dependency protobuf-net is Apache-2.0. Both arrive as NuGet dependencies, not bundled.
- **OsmSharp.Geo cannot build the geometry.** Of 780 features it made 86 points, 650 lines,
  44 `LinearRing`s and **no polygon**: buildings came out as lines or bare rings, and the 7
  relations (multipolygons, the buildings with courtyards) were dropped. It also returns only
  points unless the stream is first sorted nodes, ways, relations.

So: **OsmSharp parses** (Overpass is asked for OSM XML, so one parser serves Overpass and files),
and **this package builds the geometry**, with NetTopologySuite:

1. A tagged node is a **Point**. An untagged node is only a vertex of a way and is not a feature.
2. An open way is a **LineString**.
3. A closed way is a **Polygon** when the tags say it is an area, a LineString otherwise.
   The rules are overpass-turbo's published *Polygon Features*, the de-facto standard
   (osmtogeojson and overpass-turbo use them): `area=no` is never an area; `area=yes`
   always; keys such as `building`, `landuse`, `amenity`, `leisure`, `natural` (except
   `coastline`, `cliff`, `tree_row`), and whitelisted values such as `highway=services` are
   areas. A closed `highway=residential` (a ring road) stays a line.
4. A relation with `type=multipolygon` or `type=boundary` is a **Polygon or MultiPolygon with
   holes**: its `outer` and `inner` way members are joined into rings (NTS `LineMerger`), and the
   rings assembled (NTS `Polygonizer`), then validated; an invalid result goes through the
   family's `MakeValid` rule rather than being dropped. A relation whose tags are only
   `type=multipolygon` takes its tags from its outer way (the old tagging style).
5. A relation with `type=building` (Simple 3D Buildings) becomes **its `outline` member**, as a
   polygon carrying the relation's tags; its `part` members (the 3D pieces) are left for later.
   This matters: in the measured area JP Tower is such a relation, and its outline way carries
   **no tags of its own**, so skipping the relation would make the building vanish.
6. Other relations (routes, turn restrictions) are **not features** in version one; the status
   says how many were skipped.

Every feature carries **all of its OSM tags as attributes**, plus `@id` (`way/123456`) and
`@type`. Geometry is **SRID 4326, x = longitude, y = latitude**, as VL.Mapsui and VL.PROJ expect.

## Nodes: a proposal, category `OpenStreetMap`

Names follow the family's rule: after what the user gets, VL's shape, three inputs as the target.

### Getting data

| Node | Inputs | Outputs | Notes |
|---|---|---|---|
| **`Overpass`** (process node) | **Query** (the Overpass QL text), **Download** (a bang), **Server** (default `https://overpass-api.de/api/interpreter`) | **Features**, **Status**, **Busy** | See *The network* below. |
| **`OverpassQuery (Tag)`** | **Area** (a geometry in lon/lat), **Key** (`building`), **Value** (empty = any) | **Query** | Writes the QL for you. |
| **`OverpassQuery (Preset)`** | **Area**, **Preset** (Buildings, Roads, Railways, Water, Green, Amenities, Everything) | **Query** | The presets every tool surveyed offers (QuickOSM "Urban", Houdini's OSM Filter, BlenderGIS's defaults). |
| **`OverpassQuery (Around)`** | **Position** (lon/lat), **Radius** (metres), **Key**, **Value** | **Query** | "Everything within 300 m of here", the most common way an area is chosen (Gismo, QuickOSM "Around"). |
| **`ReadOSM`** (process node) | **Path** (`.osm` or `.osm.pbf`), **Area** (optional: keep only what lies in it), **Read** (a bang) | **Features**, **Status**, **Busy** | Offline and reliable in a classroom. A whole country's `.pbf` is large: Area keeps what is needed, and reading runs in the background. |

- **Area** is any NTS geometry: its bounding box is what Overpass is asked for. So a `Rectangle`,
  a `Circle`, a drawn `GeometryEditor` shape or a buffered point all work, in the units Overpass
  wants (degrees, SRID 4326).
- **The query is a visible string.** Every `OverpassQuery` node's output can be read in an IOBox,
  so a user learns Overpass QL by looking, and can edit it and feed their own text to `Overpass`
  (QuickOSM's "build query" idea). (overpass-turbo's `{{bbox}}` placeholder is not supported: `Overpass`
  has no area of its own; a raw query names its box.)
- **A size limit:** an Area larger than 0.25 degrees in either direction (about 25 km) is refused
  with a status that says so, instead of sending a query the server will reject after minutes
  (Gismo caps at 0.5°, the Overpass docs ask for planet extracts for big areas).

### Using the features

| Node | Inputs | Outputs | Notes |
|---|---|---|---|
| **`Split (By Type)`** | **Features** | **Points**, **Lines**, **Polygons** | One list out of `Overpass` keeps the dataflow simple; this splits it when a layer wants one kind. QuickOSM's four outputs and Gismo's one-type-per-run both drew complaints. |
| **`Height`** | **Feature**, **Level Height** (default 3 m) | **Height** (m), **Min Height** (m), **Estimated** | `height` parsed as metres (`12`, `12 m`, `12.5`; feet converted), else `building:levels` × Level Height (Estimated = true), else NaN. Never a random number. |
| **`ToGeometry`** | **Features** | **Geometry** | Every feature's geometry as one GeometryCollection, to draw in one IOBox or measure together. Added while writing the help: nothing in VL.NetTopologySuite turns a spread of features into one geometry. |
| **`Attribution`** | | **Text** (`© OpenStreetMap contributors`), **Link** | The credit the ODbL requires wherever the data is shown; wire it to VL.Mapsui's `Attribution` or draw it. None of the tools surveyed surfaces it. |

A single tag is read with VL.NetTopologySuite's existing **`GetAttribute`**: no new node.

### Later, each when something needs it

- Place names (`Area (Place)` through Nominatim, which has its own usage policy).
- Route relations as MultiLineStrings (bus lines, hiking routes).
- Writing OSM. Editing OpenStreetMap is not this package's business.

## The network: polite, cached, never by surprise

Measured on 2026-11-15: the main public Overpass server answered **502 "at capacity"** to the
first request and took **33 s** for the second. The Overpass wiki says the same ("do not expect
high reliability"). So:

- **Nothing is fetched until Download is pressed.** A patch that opens does not hit the server,
  in the spirit of VL.Mapsui's fetching nodes, which start with Enabled off.
- **Every result is cached on disk** (`%LOCALAPPDATA%\VL.OpenStreetMap\overpass\`, keyed by the
  query text and the server). When a query has a cached answer, `Overpass` gives it at once, with
  no network and the status saying how old it is. Download fetches anew and replaces it. A patch
  that worked once keeps working offline and in a classroom.
- **One request at a time** in the whole process, as the usage policy asks; a second waits.
- **The query carries a timeout** (`[timeout:25]`, QuickOSM's default), editable in the text.
- **Errors are a status, never an exception:** 429 "too many requests: wait 30 s" (and it waits
  that long before the next attempt), 504 "too large or server busy: shrink the area", 502 "server
  at capacity: try again, or another Server", no network "offline: showing the cached result".
- **A User-Agent names the package and its version**, as the policy asks (VL.Mapsui does the same
  for tiles).
- **The server is a pin.** Free public alternatives exist (overpass.private.coffee, the Mail.ru
  instance, regional ones); when the main one is busy the user can switch.

The policy's limits (fewer than 10,000 queries and about 1 GB a day for one-off use, far less
for an application's users together) are far above a person pressing Download.

## Implementation notes

- **Process nodes for anything that waits**: `Overpass` and `ReadOSM` run their work on a task and
  hand the result back on the main thread when it is done, keeping the last good result while
  busy, as VL.NetTopologySuite.GeoJSON's `GeoJSONReader (Reactive)` does. A static node
  evaluated sixty times a second must never touch the network or the disk.
- **Feature lists are built once per result**, so downstream caches see one reference until the
  next download (the family's change-detection rule: identity, not equality).
- **The geometry builder is internal and has no network**, so it is tested on saved OSM XML: the
  Tokyo Station response from the measurement, plus hand-written cases (a courtyard building, an
  old-style multipolygon, a closed ring road, an `area=no` fence, a broken relation).
- Copied from the siblings: `[assembly: ImportAsIs]`, the build / pack / Test-VLPackage /
  Test-VLPatch / Compile-HelpPatches / Test-Install tools, `NodeDocsTests`, the README template,
  `docs/GUIDE.md`, the launcher that closes only its own vvvv.
- Dependencies: VL.NetTopologySuite (0.0.8-alpha: empty geometries rather than nulls),
  NetTopologySuite 2.6.0, NetTopologySuite.Features 2.1.0, OsmSharp 7.0.0-pre028. Every NuGet
  dependency also goes into `deps\`, the family's rule 2.

## Help patches (version one)

- `Explanation Overview of available nodes`
- `HowTo Get buildings around a place` - `OverpassQuery (Preset)` → `Overpass` → drawn, with
  `Attribution`.
- `HowTo Ask Overpass for a tag` - `OverpassQuery (Tag)`, the query text visible and editable.
- `HowTo Read an OSM file` - a small `.osm` file shipped with the help (ODbL, attributed).
- `HowTo Split features by type` and `HowTo Get the height of a building`.

**As built (2026-11-15):** a cached Overpass answer cannot ship with the help (the cache is keyed
by query and server in the user's own folder), so the two Overpass patches say "press Download"
until it is pressed once, and work offline after that. The three file patches read the shipped
Tokyo Station sample and show data the moment they open.

## First acceptance target

In vvvv, from an empty patch: buildings within 300 m of Tokyo Station appear as filled polygons
with heights from their tags. **Tokyo Station's Marunouchi building is one polygon**: in OSM it is
a multipolygon relation of 35 outer ways (relation 4856156), which OsmSharp.Geo dropped in the
measurement, so it is the case that proves the ring joining. **JP Tower is there too**, through
its `type=building` relation's outline. Pressing Download again is served
from the cache, and unplugging the network leaves the patch working. (The measured area has no
building with a courtyard; the hole case is covered by a hand-written test until a real one is
chosen.)

## Open questions for the maintainer

1. **Names.** `Overpass`, `OverpassQuery (Tag)`, `OverpassQuery (Preset)`, `OverpassQuery
   (Around)`, `ReadOSM`, `Split (By Type)`, `Height`, `Attribution`. Anything that reads oddly?
2. **The size limit** (0.25° per side): enough for a city district, small enough to stay polite.
3. **Shipped sample data:** a small `.osm` extract (a few hundred buildings around one place) for
   the offline help. Which place? Tokyo Station matches the family's other examples.
