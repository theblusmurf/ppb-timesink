using System.Numerics;

namespace PoteHunter;

// Original game coordinates: X east, Z map north, Y elevation, all in map units.
// Rendering reflects Z once; these visual assets never change navigation policy.
internal sealed record MapTerrainPatch(int TileX,int TileZ,Vector3[] Vertices);
internal sealed record MapObjectMesh(string Name,Vector3[] Vertices,int[] Indices);
internal sealed record MapObjectPlacement(int Mesh,Matrix4x4 Transform);
internal sealed record MapScene3D(int Zone,string Source,MapTerrainPatch[] Terrain,
    MapObjectMesh[] Meshes,MapObjectPlacement[] Objects,string Status);
internal sealed record MapRoute3D(int Slot,SavedNavigationRoute Route);
internal sealed record MapMarker3D(string Name,Vec Position,double Height,Color Color,double Heading=0);

internal static class MapSceneGeometry
{
    internal const int Grid=17;
    internal const double TilePitch=315.07;
    internal const double CellStep=4.923075866699219;
    internal static double? Height(MapScene3D? scene,Vec point)
    {
        if(scene==null||!point.Finite)return null;
        foreach(var patch in scene.Terrain)
        {
            var v=patch.Vertices;if(v.Length!=Grid*Grid)continue;
            double minX=v[0].X,minZ=v[0].Z,maxX=v[^1].X,maxZ=v[^1].Z;
            if(point.X<minX-1e-5||point.X>maxX+1e-5||point.Y<minZ-1e-5||point.Y>maxZ+1e-5)continue;
            int col=0,row=0;
            while(col<15&&point.X>v[col+1].X)col++;
            while(row<15&&point.Y>v[(row+1)*Grid].Z)row++;
            double u=Math.Clamp((point.X-v[col].X)/(v[col+1].X-v[col].X),0,1);
            double w=Math.Clamp((point.Y-v[row*Grid].Z)/(v[(row+1)*Grid].Z-v[row*Grid].Z),0,1);
            double a=v[row*Grid+col].Y,b=v[row*Grid+col+1].Y,c=v[(row+1)*Grid+col].Y,d=v[(row+1)*Grid+col+1].Y;
            return u+w<=1?a+(b-a)*u+(c-a)*w:d+(c-d)*(1-u)+(b-d)*(1-w);
        }
        return null;
    }
}
