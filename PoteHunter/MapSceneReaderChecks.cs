using System.Buffers.Binary;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PoteHunter;

internal static class MapSceneReaderChecks
{
    static byte[] Scene(params (int X,int Z,Func<int,int,float> Height)[] patches)
    {
        var bytes=new byte[MapSceneReader.TerrainStart+patches.Length*MapSceneReader.TerrainRecordBytes];
        Encoding.ASCII.GetBytes("ZallA-3D Scene Data File Build#2").CopyTo(bytes,0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(272,4),(uint)patches.Length);
        for(int i=0;i<patches.Length;i++)
        {
            int at=MapSceneReader.TerrainStart+i*MapSceneReader.TerrainRecordBytes;
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(at,4),patches[i].X);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(at+4,4),patches[i].Z);
            for(int row=0;row<65;row++)for(int col=0;col<65;col++)
                BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(at+8+(row*65+col)*4,4),patches[i].Height(row,col));
        }
        return bytes;
    }
    static void Require(bool value,string message){if(!value)throw new Exception(message);}
    static void Rejected(Action action,string name)
    {
        try{action();}catch(InvalidDataException){return;}
        throw new Exception("Invalid 3D scene accepted: "+name);
    }
    static void Cancelled(Action action)
    {
        try{action();}catch(OperationCanceledException){return;}
        throw new Exception("Cancelled 3D map work continued.");
    }
    internal static void Run()
    {
        var none=CancellationToken.None;
        var source=Scene((2,3,(r,c)=>r*65+c));
        var patches=MapSceneReader.ReadTerrain(source,none);
        Require(patches.Length==1 && patches[0].TileX==2 && patches[0].TileZ==3,"Terrain tile identity changed.");
        var vertices=patches[0].Vertices;
        Require(vertices.Length==289 && vertices[0]==new Vector3((float)(2*31507d/100),0,(float)(3*31507d/100)),"Terrain origin or grid count failed.");
        Require(vertices[1].X>vertices[0].X && vertices[1].Z==vertices[0].Z && vertices[17].Z>vertices[0].Z && vertices[17].X==vertices[0].X,"Terrain row/column axes changed.");
        Require(vertices[1].Y==(float)(4d/100) && vertices[17].Y==(float)(260d/100) && vertices[^1].Y==(float)(4224d/100),"Terrain stride-four or height scale failed.");
        var raw=Scene((-1,0,(_,_)=>0),(12,0,(_,_)=>0),(0,12,(_,_)=>0),(1,1,(_,_)=>200));
        Require(MapSceneReader.ReadTerrain(raw,none).Length==1,"Client terrain bounds were not preserved.");
        Rejected(()=>MapSceneReader.ReadTerrain(source[..^1],none),"truncated heights");
        Rejected(()=>MapSceneReader.ReadTerrain(new byte[275],none),"truncated header");
        var invalid=(byte[])source.Clone();invalid[0]=0;
        Rejected(()=>MapSceneReader.ReadTerrain(invalid,none),"header");
        invalid=(byte[])source.Clone();BinaryPrimitives.WriteUInt32LittleEndian(invalid.AsSpan(272,4),4097);
        Rejected(()=>MapSceneReader.ReadTerrain(invalid,none),"huge count");
        Rejected(()=>MapSceneReader.ReadTerrain(Scene(),none),"zero count");
        Rejected(()=>MapSceneReader.ReadTerrain(Scene((12,12,(_,_)=>0)),none),"no accepted terrain");
        Rejected(()=>MapSceneReader.ReadTerrain(Scene((0,0,(_,_)=>0),(0,0,(_,_)=>0)),none),"duplicate accepted tile");
        foreach(float value in new[]{float.NaN,float.PositiveInfinity,float.NegativeInfinity,1000001f})
            Rejected(()=>MapSceneReader.ReadTerrain(Scene((0,0,(r,c)=>r==1&&c==1?value:0)),none),"unsampled invalid height");
        foreach(int zone in new[]{0,7,10,11,13,14,100,999})Rejected(()=>MapSceneReader.SourceZone(zone),"unsupported zone "+zone);
        Require(MapSceneReader.SourceZone(6)==3 && MapSceneReader.SourceZone(17)==16 && MapSceneReader.SourceZone(18)==16,"Unproved scene aliases introduced.");
        // Encode the exact inverse of the statically proved Zone 9 transform.
        byte[] encoded=(byte[])source.Clone();byte[] key=[0x63,0x6a,0x73];
        for(int at=256;at<encoded.Length;at++){int phase=(at-256)%3;encoded[at]=(byte)((source[at]^key[phase])+phase);}
        Require(MapSceneReader.DecodeScene(encoded,true,none).AsSpan().SequenceEqual(source),"Zone 9 byte transform changed.");
        Require(MapSceneReader.DecodeScene(source,false,none).AsSpan().SequenceEqual(source),"Ordinary scene changed during decoding.");
        Require(encoded.AsSpan(0,256).SequenceEqual(source.AsSpan(0,256)),"Zone 9 header was transformed.");
        using var cancelled=new CancellationTokenSource();cancelled.Cancel();
        Cancelled(()=>MapSceneReader.ReadTerrain(source,cancelled.Token));
        Cancelled(()=>MapSceneReader.DecodeScene(encoded,true,cancelled.Token));
        Cancelled(()=>MapSceneReader.Load("",8,cancelled.Token));

        var surface=MapSceneReader.ReadTerrain(Scene((0,0,(r,c)=>r*50+c*25+r*c*31.25f)),none);
        var scene=new MapScene3D(8,"synthetic",surface,[],[],"");
        var v=surface[0].Vertices;
        Vec Point(double u,double w)=>new(v[0].X+(v[1].X-v[0].X)*u,v[0].Z+(v[17].Z-v[0].Z)*w);
        Require(Math.Abs(MapSceneGeometry.Height(scene,Point(.25,.25))!.Value-.75)<1e-6,"First preview triangle height failed.");
        Require(Math.Abs(MapSceneGeometry.Height(scene,Point(.75,.75))!.Value-4.75)<1e-6,"Second preview triangle height failed.");
        Require(MapSceneGeometry.Height(scene,new(v[^1].X,v[^1].Z))==v[^1].Y,"Exact patch endpoint height failed.");
        Require(MapSceneGeometry.Height(scene,new(-1,-1))==null && MapSceneGeometry.Height(scene,new(double.NaN,0))==null,"Missing terrain height was invented.");
        string temp=Path.Combine(Path.GetTempPath(),"PPB-3d-reader-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            File.WriteAllText(Path.Combine(temp,"Client.exe"),"unverified build");
            File.WriteAllBytes(Path.Combine(temp,"Zone8.z3s"),source);
            Rejected(()=>MapSceneReader.Load(temp,8,none),"unverified client build");
        }
        finally{Directory.Delete(temp,true);}
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"map-scene-reader-checks.json"),JsonSerializer.Serialize(new
        {
            Passed=true,Checks=new[]{"verified zone whitelist and exact scene aliases","bounded header/count/height parsing",
                "all source heights checked including unsampled cells","client accepted tile bounds and duplicate rejection",
                "stride-four original X/Y/Z coordinates","proved Zone9 byte transform","cancellation and unverified build rejection",
                "both preview triangle height formulas and unavailable terrain"}
        }));
    }

    // Explicit developer/local check only. No installed assets are required by CI.
    // exportDirectory is the private research scene folder containing preview manifests.
    internal static void RunLocal(string clientDirectory,string exportDirectory)
    {
        Require(GameMapLayout.ClientSupported(Path.Combine(clientDirectory,"Client.exe")),"Local reader check requires the verified client.");
        var manifests=Directory.EnumerateFiles(exportDirectory,"manifest.json",SearchOption.AllDirectories)
            .Where(p=>Path.GetFileName(Path.GetDirectoryName(p))!.EndsWith("terrain-glb-preview",StringComparison.Ordinal));
        var results=new List<object>();var seen=new HashSet<int>();
        foreach(string manifest in manifests)
        {
            using var entries=JsonDocument.Parse(File.ReadAllBytes(manifest));
            foreach(var entry in entries.RootElement.EnumerateArray())
            {
                if(!entry.TryGetProperty("zone",out var zoneProperty) || zoneProperty.ValueKind!=JsonValueKind.Number || !zoneProperty.TryGetInt32(out int zone) || !seen.Add(zone))continue;
                int sourceZone=MapSceneReader.SourceZone(zone);
                byte[] source=File.ReadAllBytes(Path.Combine(clientDirectory,$"Zone{sourceZone}.z3s"));
                var decoded=MapSceneReader.DecodeScene(source,sourceZone==9,CancellationToken.None);
                var terrain=MapSceneReader.ReadTerrain(decoded,CancellationToken.None);
                string path=entry.GetProperty("path").GetString()!;
                byte[] glb=File.ReadAllBytes(path);
                Require(Convert.ToHexStringLower(SHA256.HashData(glb))==entry.GetProperty("sha256").GetString(),"Private terrain reference hash mismatch.");
                var expected=ReadPositions(glb);
                Require(expected.Length==terrain.Length*289,"Local accepted terrain patch count differs from independent export.");
                int at=0;
                foreach(var patch in terrain)foreach(var vertex in patch.Vertices)
                    Require(vertex==expected[at++],"Local terrain vertex differs from independent export: "+zone);
                results.Add(new{Zone=zone,Patches=terrain.Length,Vertices=at,AllVertexCoordinatesExact=true,
                    SourceSha256=Convert.ToHexStringLower(SHA256.HashData(source)),ReferenceSha256=Convert.ToHexStringLower(SHA256.HashData(glb))});
            }
        }
        Require(seen.SetEquals(new[]{1,2,3,4,5,8,9,12,15,16}),"The local terrain reference set is incomplete.");
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"map-scene-local-checks.json"),JsonSerializer.Serialize(new{Passed=true,Scenes=results}));
    }

    static Vector3[] ReadPositions(byte[] glb)
    {
        Require(glb.Length>=28 && BinaryPrimitives.ReadUInt32LittleEndian(glb)==0x46546c67 &&
            BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(4))==2 && BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(8))==glb.Length,"Invalid private GLB header.");
        int jsonLength=checked((int)BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(12)));
        Require(jsonLength>=0 && (long)jsonLength+28<=glb.Length && BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(16))==0x4e4f534a,"Invalid private GLB JSON chunk.");
        using var doc=JsonDocument.Parse(glb.AsMemory(20,jsonLength));
        int binLength=checked((int)BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(20+jsonLength)));
        Require((long)binLength+jsonLength+28==glb.Length && BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(24+jsonLength))==0x004e4942,"Invalid private GLB binary chunk.");
        int index=doc.RootElement.GetProperty("meshes")[0].GetProperty("primitives")[0].GetProperty("attributes").GetProperty("POSITION").GetInt32();
        var accessor=doc.RootElement.GetProperty("accessors")[index];
        Require(accessor.GetProperty("componentType").GetInt32()==5126 && accessor.GetProperty("type").GetString()=="VEC3","Unexpected private GLB positions.");
        var view=doc.RootElement.GetProperty("bufferViews")[accessor.GetProperty("bufferView").GetInt32()];
        int count=accessor.GetProperty("count").GetInt32(),offset=view.GetProperty("byteOffset").GetInt32();
        if(accessor.TryGetProperty("byteOffset",out var extra))offset+=extra.GetInt32();
        Require(count>=0 && offset>=0 && (long)offset+(long)count*12<=binLength,"Private GLB positions exceed buffer.");
        var positions=new Vector3[count];
        for(int i=0;i<count;i++)
        {
            int at=checked(28+jsonLength+offset+i*12);
            positions[i]=new(BinaryPrimitives.ReadSingleLittleEndian(glb.AsSpan(at,4)),
                BinaryPrimitives.ReadSingleLittleEndian(glb.AsSpan(at+4,4)),BinaryPrimitives.ReadSingleLittleEndian(glb.AsSpan(at+8,4)));
        }
        return positions;
    }
}
