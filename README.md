# VL.OpenStreetMap

OpenStreetMap data for vvvv gamma, as [NetTopologySuite](https://github.com/NetTopologySuite/NetTopologySuite) features:

* Buildings, roads, water, parks, shops: ask the [Overpass API](https://wiki.openstreetmap.org/wiki/Overpass_API) by preset, by tag, or around a place
* Or read a downloaded `.osm` / `.osm.pbf` file, for working offline
* Real polygons: buildings with courtyards, areas made of many ways, closed roads kept as lines
* Every OSM tag on every feature, and building heights in metres
* Each answer saved on disk, so a patch that worked once keeps working offline

This is map **data**, not map pictures: VL.Mapsui's `OpenStreetMap` layer shows the tiles, this package gives you what is behind them, to measure, filter and draw your own way.

For use with vvvv, the visual live-programming environment for .NET: http://vvvv.org

## Getting started
- Install as [described here](https://thegraybook.vvvv.org/reference/hde/managing-nugets.html) via commandline:

    `nuget install VL.OpenStreetMap -pre`

- Usage examples and more information are included in the pack and can be found via the [Help Browser](https://thegraybook.vvvv.org/reference/hde/findinghelp.html). Start with *Explanation Overview of available nodes*, then *HowTo Get buildings around a place*.
- What changed in each version: [Releases](https://github.com/rednotfound/VL.OpenStreetMap/releases)

## Using OpenStreetMap data
- Nothing is fetched until you press **Download**. The public Overpass server is shared and often busy: ask for small areas, and switch the **Server** pin when one does not answer.
- OpenStreetMap data is © OpenStreetMap contributors, under the [ODbL](https://www.openstreetmap.org/copyright). Show the credit wherever you show the data: the **Attribution** node gives it.

## Related packages
- [VL.NetTopologySuite](https://github.com/rednotfound/VL.NetTopologySuite): measure, combine and draw the features
- [VL.Mapsui](https://github.com/rednotfound/VL.Mapsui): put them on a map
- [VL.NetTopologySuite.GeoJSON](https://github.com/rednotfound/VL.NetTopologySuite.GeoJSON): save them as GeoJSON
- [VL.PROJ](https://github.com/rednotfound/VL.PROJ): move them to another coordinate system

## Contributing
- Report issues on [GitHub](https://github.com/rednotfound/VL.OpenStreetMap/issues)
- How the package is designed and why: [the design](https://github.com/rednotfound/VL.OpenStreetMap/blob/main/docs/DESIGN.md)
- When making a pull-request, please make sure to read the general [guidelines on contributing to vvvv libraries](https://thegraybook.vvvv.org/reference/extending/contributing.html)

## Credits
Based on [OsmSharp](https://github.com/OsmSharp/core) (MIT), with protobuf-net (Apache-2.0), and [NetTopologySuite](https://github.com/NetTopologySuite/NetTopologySuite) (BSD-3-Clause). The polygon rules are [osm-polygon-features](https://github.com/tyrasd/osm-polygon-features), as overpass-turbo and osmtogeojson apply them. Map data © [OpenStreetMap contributors](https://www.openstreetmap.org/copyright).

## License
[MIT](https://github.com/rednotfound/VL.OpenStreetMap/blob/main/LICENSE). The sample data keeps its own terms: see [THIRD-PARTY-NOTICES](https://github.com/rednotfound/VL.OpenStreetMap/blob/main/THIRD-PARTY-NOTICES.md).
