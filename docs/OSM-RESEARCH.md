# OpenStreetMap data in creative tools: research for VL.OpenStreetMap

Collected 2026-11-15 to design [DESIGN.md](DESIGN.md). Two parts: how established tools give
artists and designers OSM data, and what was measured here. Claims marked *[unverified]* could
not be confirmed from a primary source.

## How other tools do it

| Tool | Area | Filter | Outputs | Tags | Online / offline |
|---|---|---|---|---|---|
| **QGIS QuickOSM** | In (place name), Around (place + distance, default 1000 m), Extent (canvas or layer), raw query with `{{bbox}}` | Key and value (empty = all), presets ("Urban"), raw Overpass QL; a "build query" mode shows the QL without running it | Four: points, lines, multilinestrings, multipolygons (a fifth, other relations, from files) | Columns, chosen per layer | Live Overpass (timeout default 25 s), and .osm/.pbf files |
| **Gismo** (Grasshopper) | Location + radius (default 100 m, capped near 0.5°) | Required keys, or a keys helper with presets | One geometry type per run | Parallel key and value lists | Live, cached in an `osm_files` folder; a `_runIt` toggle |
| **Elk / Elk2** (Grasshopper) | A downloaded .osm file | One key and one value per component | Points or curves | Filter only | File only; heights only from `height` |
| **Heron** (Grasshopper) | A boundary curve | A search term | Nodes, ways, relations on separate branches | *[unverified]* | Overpass and file |
| **Caribou** (Grasshopper) | Downloaded .osm files | A picker for any OSM key | A tree per data type | Any | File; parsing asynchronous "so Grasshopper remains responsive" |
| **Houdini Labs** | An OSM file | OSM Filter: Buildings, Roads, Other toggles | Curves and polygons; points optional | **All tags become attributes** | File; heights from `height` or `building:levels` × level height |
| **BlenderGIS** | The view extent or an object; or a file | Tags (default `building`, `highway`) | One mesh per type and tag | Vertex groups or properties | Live (no timeout set); height `height`, else levels × 3 m, else 20 m ± random |
| **TouchDesigner** | no native OSM component found; the community route is QGIS → Houdini → TD | | | | |
| **osmtogeojson** | | | One mixed FeatureCollection; closed ways become polygons by the *Polygon Features* rules | Nested, or flat key-value | Library |

What users praise and complain about:
- Elk's single key/value filter produced "no output data" threads.
- Gismo users were confused that "polylines" still gave closed forest areas, and had to list keys
  by hand on large areas.
- BlenderGIS warns that "separate objects" "can be very slow".
- Caribou is praised for staying responsive.

What they all share, and the design follows:
- A preset for buildings and roads.
- Key/value with "any".
- Heights from `height`, then `building:levels`.
- A file path for offline work.

What none of them does, and the design adds: putting the ODbL attribution in the output.

**Polygon rules** (https://wiki.openstreetmap.org/wiki/Overpass_turbo/Polygon_Features), which
the author calls "surely quite incomplete". A closed way is an area when any of these holds, unless
it has `area=no`:
- it has `area=*` other than `no`;
- it has `building`, `landuse`, `amenity`, `leisure`, `shop`, `tourism`, `boundary`, `place` and
  similar keys with any value but `no`;
- it has a whitelisted value: `highway` services, rest_area, escape or elevator; `barrier` wall,
  hedge or city_wall; `railway` platform or station; `waterway` riverbank or dock;
- it has `natural`, except coastline, cliff or tree_row;
- it has `man_made`, except embankment or pipeline;
- it has `aeroway`, except taxiway.

## The Overpass usage policy

From https://wiki.openstreetmap.org/wiki/Overpass_API and
https://dev.overpass-api.de/overpass-doc/en/preface/commons.html:

- **Volume:** fewer than 10,000 queries and about 1 GB a day for occasional use; an application's
  users count together.
- **Identification:** send a User-Agent or Referer that identifies the application.
- **Concurrency:** no parallel scripts.
- **Busy server:** a request waits up to 15 s for a slot, then gets **429**; after a 429, wait
  30 s. A query too large for its declared limits gets **504**.
- **Limits:** default 180 s and 512 MiB when undeclared.
- **Reliability:** the main server is overloaded; "do not expect high reliability".
- **Public endpoints:** `overpass-api.de`, `overpass.private.coffee`,
  `maps.mail.ru/osm/tools/overpass`, and regional ones (`overpass.osm.ch`, `ovp.dresel.systems`).
- **Attribution:** "© OpenStreetMap contributors" with a link to openstreetmap.org/copyright
  (https://osmfoundation.org/wiki/Licence/Attribution_Guidelines).

## Measured here, 2026-11-15

A probe (`scratchpad\osm-probe`, not committed) asked overpass-api.de for buildings, roads and
amenities in a box around Tokyo Station (35.6795-35.6830 N, 139.7650-139.7690 E), with a
User-Agent naming the project.

- **First request:** `502 Gateway Timeout` - "This Overpass API server is currently at capacity
  ... Retry your request using exponential backoff". The next request succeeded and took
  **33.5 s** for 381 KB.
- **OsmSharp 7.0.0-pre028** read all of it: 2142 nodes, 746 ways, 7 relations, with every tag,
  multilingual names included.
- **OsmSharp.Geo `ToFeatureSource`** returned **only the 86 tagged nodes** on the stream as
  Overpass orders it (ways before nodes). Sorted to nodes, ways, relations, it returned 780
  features: 86 points, 650 LineStrings, 44 LinearRings and **no Polygon**. 38 buildings were
  left as lines, every relation was dropped, and the 8 closed highways stayed lines (correctly).
  Hence: OsmSharp to read, our own geometry builder.
- **The 7 relations:**
  - Six are `type=multipolygon` buildings with outer members only. Tokyo Station's Marunouchi
    building (relation 4856156) has **35 outer ways**.
  - One is `type=building`: JP Tower, relation 11955761, with 1 outline way and 3 part ways.
    The outline way carries no tags in the response, so a reader that skips building relations
    loses the building.
- **Licences:**
  - OsmSharp: MIT (Copyright 2017 Ben Abelshausen; repository active, last push 2026-10).
  - protobuf-net: Apache-2.0.
  - OsmSharp.Geo targets NetTopologySuite 2.5+ and NetTopologySuite.Features 2.1, so it is
    compatible with the family.
