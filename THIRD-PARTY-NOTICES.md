# Third-party notices

VL.OpenStreetMap is MIT-licensed (see LICENSE).

## Data shipped with the package

| file | what | licence | what it asks of you |
|---|---|---|---|
| `help/Assets/tokyo-station.osm` (also `test/fixtures/`) | An Overpass answer, fetched 2026-11-15: buildings, roads and amenities in the box 35.6795-35.6830 N, 139.7650-139.7690 E around Tokyo Station - 2142 nodes, 746 ways, 7 relations | **ODbL 1.0**, © OpenStreetMap contributors | Credit "© OpenStreetMap contributors" wherever the data is shown (the help patches draw the Attribution node). A database derived from it must be shared under the ODbL too. |

OpenStreetMap's terms: https://www.openstreetmap.org/copyright, and the attribution guidelines,
https://osmfoundation.org/wiki/Licence/Attribution_Guidelines.

## Libraries

None is bundled: NuGet installs each with the package, under its own licence.

| library | licence |
|---|---|
| [OsmSharp](https://github.com/OsmSharp/core) 7.0.0-pre028 | MIT, Copyright (c) 2017 Ben Abelshausen |
| [protobuf-net](https://github.com/protobuf-net/protobuf-net) (through OsmSharp) | Apache-2.0 |
| [NetTopologySuite](https://github.com/NetTopologySuite/NetTopologySuite), NetTopologySuite.Features | BSD-3-Clause |
| [VL.NetTopologySuite](https://github.com/rednotfound/VL.NetTopologySuite) | MIT |

The polygon rules in `src/VL.OpenStreetMap/PolygonRules.cs` follow the list published at
https://github.com/tyrasd/osm-polygon-features (no code is copied: the 27 rules are restated).
