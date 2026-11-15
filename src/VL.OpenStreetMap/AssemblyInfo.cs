using System.Runtime.CompilerServices;
using VL.Core.Import;

[assembly: InternalsVisibleTo("VL.OpenStreetMap.Tests")]

// Without this every public static method is demoted to a raw .NET reflection node, which the
// NodeBrowser hides; the package loads, compiles and packs with zero warnings regardless. Nine
// VL.GIS releases shipped that way (family rule 1, docs/RULES.md in VL.NetTopologySuite).
// Category = namespace minus this prefix: VL.OpenStreetMap gives OpenStreetMap.
[assembly: ImportAsIs(Namespace = "VL")]
