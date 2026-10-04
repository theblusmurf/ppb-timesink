using System.Security.Cryptography;

namespace PoteHunter;

// Read-only local assets. Projection verified from the client large-map player
// renderer, not inferred from the artwork or used as a collision/navigation mesh.
internal static class GameMapLayout
{
    internal const string VerifiedClientHash="f3a39ea1f3687aca418aba17df295043618014b681eca4ff1db33d4837943c90";
    internal readonly record struct Extent(double MinX,double MinY,double MaxX,double MaxY)
    {
        internal Vec Center=>new((MinX+MaxX)/2,(MinY+MaxY)/2);
        internal PointF Pixel(Vec position)=>new((float)((position.X-MinX)/(MaxX-MinX)*512),(float)((MaxY-position.Y)/(MaxY-MinY)*512));
    }
    // u = offsetX + x*512/spanX; v = offsetY + 512 - y*512/spanY.
    static Extent Offset(double spanX,double spanY,double offsetX,double offsetY)
        =>new(-offsetX*spanX/512,offsetY*spanY/512,(512-offsetX)*spanX/512,(512+offsetY)*spanY/512);
    internal static Extent? Bounds(int zone)=>zone switch
    {
        1=>Offset(3465,3465,-48,-46), 2=>Offset(3422,3438,-52,52),
        4=>Offset(2463,2383,-68,83), 5=>Offset(3120,3104,-53,55),
        8 or 12=>new(315,315,3780,3780), 9=>new(630,630,3780,3780),
        16 or 17 or 18=>Offset(1116,1111,-395,405),
        // The small battle map has its own affine projection and crop.
        3 or 10=>new(1000-138/1.0923,1462/1.09-512/1.09,1000+(512-138)/1.0923,1462/1.09),
        _=>null
    };
    internal static bool ClientSupported(string path)
    {
        try{using var stream=File.OpenRead(path);return Convert.ToHexStringLower(SHA256.HashData(stream))==VerifiedClientHash;}
        catch(IOException){return false;}catch(UnauthorizedAccessException){return false;}
    }
    internal static Bitmap? Load(string clientDirectory,int zone)
    {
        if(Bounds(zone)==null)return null;
        var directory=Path.Combine(clientDirectory,"TEXTURE","Interface","Zone"+zone);
        var files=Enumerable.Range(1,4).Select(i=>Path.Combine(directory,$"LargeMap{i:00}.dds")).ToArray();
        // All four tiles must decode. Never show a partial or unrelated map.
        Bitmap? result=null;
        try
        {
            result=new Bitmap(512,512);
            using var g=Graphics.FromImage(result);
            for(int i=0;i<4;i++)
            {
                using var tile=DdsMapTile.Decode(File.ReadAllBytes(files[i]));
                g.DrawImageUnscaled(tile,i%2*256,i/2*256);
            }
            return result;
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or OverflowException)
        {result?.Dispose();return null;}
    }
}
