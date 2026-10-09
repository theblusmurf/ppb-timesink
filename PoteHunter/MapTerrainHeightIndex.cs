using System.Runtime.CompilerServices;

namespace PoteHunter;

// Scene meshes are immutable after loading. Weak ownership releases the index
// when the existing bounded scene cache and viewports release that scene.
internal static class MapTerrainHeightIndex
{
    const double Bin = MapSceneGeometry.CellStep * 16;
    static readonly ConditionalWeakTable<MapScene3D, Index> indexes = new();
    sealed class Index
    {
        internal readonly Dictionary<(long X, long Y), MapTerrainPatch[]> Cells = new();
        internal bool LinearFallback;
        internal Index(MapScene3D scene)
        {
            var building = new Dictionary<(long, long), List<MapTerrainPatch>>();
            foreach(var patch in scene.Terrain)
            {
                var v = patch.Vertices; if(v.Length != MapSceneGeometry.Grid * MapSceneGeometry.Grid)continue;
                double minX = v[0].X - 1e-5, minY = v[0].Z - 1e-5, maxX = v[^1].X + 1e-5, maxY = v[^1].Z + 1e-5;
                if(!Coordinates(minX, minY, out var first) || !Coordinates(maxX, maxY, out var last) ||
                   first.X > last.X || first.Y > last.Y || (decimal)last.X - first.X > 3 || (decimal)last.Y - first.Y > 3)
                { LinearFallback = true; break; }
                for(long x = first.X; x <= last.X; x++)for(long y = first.Y; y <= last.Y; y++)
                {
                    if(!building.TryGetValue((x,y), out var list))building[(x,y)] = list = [];
                    list.Add(patch); // Original order preserves shared-edge/overlap behavior.
                }
            }
            if(!LinearFallback)foreach(var entry in building)Cells[entry.Key] = entry.Value.ToArray();
        }
    }
    static bool Coordinates(double x, double y, out (long X, long Y) cell)
    {
        x = Math.Floor(x / Bin); y = Math.Floor(y / Bin); cell = default;
        if(!double.IsFinite(x) || !double.IsFinite(y) || Math.Abs(x) >= long.MaxValue || Math.Abs(y) >= long.MaxValue)return false;
        cell = ((long)x, (long)y); return true;
    }
    internal static IReadOnlyList<MapTerrainPatch> Candidates(MapScene3D scene, Vec point)
    {
        var index = indexes.GetValue(scene, value => new Index(value));
        if(index.LinearFallback)return scene.Terrain;
        return Coordinates(point.X, point.Y, out var cell) && index.Cells.TryGetValue(cell, out var candidates) ? candidates : [];
    }
}
