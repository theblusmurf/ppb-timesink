using System.Buffers.Binary;
using System.Numerics;
using System.Text;

namespace PoteHunter;

// Reads verified local assets only. These reduced visual meshes never influence movement.
internal static class MapSceneReader
{
    internal const int MaximumSceneBytes=64*1024*1024;
    internal const int MaximumTerrainRecords=4096;
    internal const int TerrainStart=276;
    internal const int TerrainRecordBytes=16908;
    internal const double TilePitchRaw=31507;
    internal const double CellStepRaw=492.307586669921875;
    const int SourceGrid=65;
    const float MaximumHeightRaw=1000000;
    static readonly byte[] Header=Encoding.ASCII.GetBytes("ZallA-3D Scene Data File Build#");
    static readonly byte[] AlternateKey=[0x63,0x6a,0x73];

    internal static int SourceZone(int zone)=>zone switch
    {
        1 or 2 or 3 or 4 or 5 or 8 or 9 or 12 or 15 or 16=>zone,
        6=>3,17 or 18=>16,
        _=>throw new InvalidDataException("This zone has no verified local 3D scene.")
    };

    internal static MapScene3D Load(string clientDirectory,int zone,CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        int sourceZone=SourceZone(zone);
        string directory=Path.GetFullPath(clientDirectory);
        if(!GameMapLayout.ClientSupported(Path.Combine(directory,"Client.exe")))
            throw new InvalidDataException("The client build has not been verified for local 3D maps.");
        cancellationToken.ThrowIfCancellationRequested();
        string source=$"Zone{sourceZone}.z3s";
        byte[] raw=ReadAsset(Path.Combine(directory,source),cancellationToken);
        ReadOnlyMemory<byte> decoded=sourceZone==9?DecodeScene(raw,true,cancellationToken):raw;
        var terrain=ReadTerrain(decoded,cancellationToken);
        var objects=MapObjectReader.Load(decoded,directory,cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return new(zone,source,terrain,objects.Meshes,objects.Objects,
            $"{terrain.Length:N0} terrain patches · {objects.Status}");
    }

    static byte[] ReadAsset(string path,CancellationToken cancellationToken)
    {
        using var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
        if(file.Length<TerrainStart || file.Length>MaximumSceneBytes)
            throw new InvalidDataException("The local scene file is outside the supported size limits.");
        var bytes=new byte[checked((int)file.Length)];
        for(int at=0;at<bytes.Length;)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int read=file.Read(bytes,at,Math.Min(65536,bytes.Length-at));
            if(read==0)throw new InvalidDataException("The local scene file is truncated.");
            at+=read;
        }
        cancellationToken.ThrowIfCancellationRequested();
        if(file.ReadByte()!=-1)throw new InvalidDataException("The local scene file changed while reading.");
        return bytes;
    }

    static void ValidateHeader(ReadOnlySpan<byte> scene)
    {
        if(scene.Length<TerrainStart || scene.Length>MaximumSceneBytes ||
            !scene[..Header.Length].SequenceEqual(Header) ||
            (scene[Header.Length]!=(byte)'1' && scene[Header.Length]!=(byte)'2') || scene[Header.Length+1]!=0)
            throw new InvalidDataException("The local scene header or file size is unsupported.");
    }

    internal static byte[] DecodeScene(ReadOnlyMemory<byte> source,bool alternate,CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateHeader(source.Span);
        byte[] decoded=source.ToArray();
        // Confirmed alternate loader 0x6f7390: bytes 256..EOF, subtract cyclic
        // 0/1/2 then XOR c/j/s. Header stays unchanged; no format guesses.
        if(alternate)for(int at=256;at<decoded.Length;at++)
        {
            if((at&65535)==0)cancellationToken.ThrowIfCancellationRequested();
            int phase=(at-256)%3;
            decoded[at]=(byte)(((decoded[at]-phase)&255)^AlternateKey[phase]);
        }
        cancellationToken.ThrowIfCancellationRequested();
        return decoded;
    }

    internal static MapTerrainPatch[] ReadTerrain(ReadOnlyMemory<byte> decoded,CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var scene=decoded.Span;ValidateHeader(scene);
        uint count=BinaryPrimitives.ReadUInt32LittleEndian(scene.Slice(272,4));
        if(count==0 || count>MaximumTerrainRecords || (long)TerrainStart+(long)count*TerrainRecordBytes>scene.Length)
            throw new InvalidDataException("The local scene terrain count is invalid or truncated.");
        var patches=new List<MapTerrainPatch>(144);
        var identities=new HashSet<(int X,int Z)>();
        for(int record=0;record<count;record++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int at=checked(TerrainStart+record*TerrainRecordBytes);
            int x=BinaryPrimitives.ReadInt32LittleEndian(scene.Slice(at,4));
            int z=BinaryPrimitives.ReadInt32LittleEndian(scene.Slice(at+4,4));
            // Check every source height, including unsampled values; malformed
            // terrain cannot hide between the reduced preview's stride-four rows.
            var heights=scene.Slice(at+8,SourceGrid*SourceGrid*4);
            for(int row=0;row<SourceGrid;row++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                for(int col=0;col<SourceGrid;col++)
                {
                    float height=BinaryPrimitives.ReadSingleLittleEndian(heights.Slice((row*SourceGrid+col)*4,4));
                    if(!float.IsFinite(height) || Math.Abs(height)>MaximumHeightRaw)
                        throw new InvalidDataException("The local scene contains an invalid terrain height.");
                }
            }
            // The confirmed client loader accepts only 0..11 on both axes.
            if(x<0 || x>11 || z<0 || z>11)continue;
            if(!identities.Add((x,z)))throw new InvalidDataException("The local scene contains duplicate terrain tiles.");
            var vertices=new Vector3[MapSceneGeometry.Grid*MapSceneGeometry.Grid];
            for(int row=0;row<MapSceneGeometry.Grid;row++)for(int col=0;col<MapSceneGeometry.Grid;col++)
            {
                int sourceRow=row*4,sourceCol=col*4;
                float height=BinaryPrimitives.ReadSingleLittleEndian(heights.Slice((sourceRow*SourceGrid+sourceCol)*4,4));
                vertices[row*MapSceneGeometry.Grid+col]=new(
                    (float)((x*TilePitchRaw+sourceCol*CellStepRaw)/100),
                    (float)((double)height/100),
                    (float)((z*TilePitchRaw+sourceRow*CellStepRaw)/100));
            }
            patches.Add(new(x,z,vertices));
        }
        cancellationToken.ThrowIfCancellationRequested();
        if(patches.Count==0)throw new InvalidDataException("The local scene contains no supported terrain tiles.");
        return patches.ToArray();
    }
}
