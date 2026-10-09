using System.Numerics;

namespace PoteHunter;

internal sealed class PassiveRouteBounds
{
    bool dirty = true, known;
    Vector3 minimum, maximum;
    internal int Builds { get; private set; }
    internal void Invalidate() => dirty = true;
    internal bool Get(MapScene3D? scene, MapRoute3D[] routes, Vec? player, out Vector3 min, out Vector3 max)
    {
        if(dirty)
        {
            dirty = false; known = false; Builds++;
            foreach(var route in routes)
            {
                foreach(var point in route.Route.Points)Include(point);
                Include(route.Route.Anchor);
            }
            void Include(Vec point)
            {
                if(!point.Finite || MapSceneGeometry.Height(scene, point) is not double height)return;
                var value = Navigation3DView.Reflect(new((float)point.X, (float)height, (float)point.Y));
                if(!known) { minimum = maximum = value; known = true; }
                else { minimum = Vector3.Min(minimum, value); maximum = Vector3.Max(maximum, value); }
            }
        }
        min = minimum; max = maximum; bool found = known;
        if(player is Vec p && p.Finite && MapSceneGeometry.Height(scene, p) is double elevation)
        {
            var value = Navigation3DView.Reflect(new((float)p.X, (float)elevation, (float)p.Y));
            if(!found) { min = max = value; found = true; }
            else { min = Vector3.Min(min, value); max = Vector3.Max(max, value); }
        }
        return found;
    }
}
