using System.Buffers.Binary;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PoteHunter;

internal static class MapObjectReaderChecks
{
    // Explicit local-only diagnostic entry point. CI runs Run() with synthetic
    // assets; it must never require or package an installed game's geometry.
    internal static void RunLocal(string clientDirectory,string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);var results=new List<object>();
        foreach(int zone in new[]{1,2,3,4,5,8,9,12,15,16})
        {
            var scene=MapSceneReader.Load(clientDirectory,zone,default);
            var samples=new List<object>();
            foreach(var placement in scene.Objects.Take(3))
            {
                var mesh=scene.Meshes[placement.Mesh];var first=Vector3.Transform(mesh.Vertices[0],placement.Transform);
                var last=Vector3.Transform(mesh.Vertices[^1],placement.Transform);
                samples.Add(new{mesh.Name,FirstWorld=new[]{first.X,first.Y,first.Z},LastWorld=new[]{last.X,last.Y,last.Z}});
            }
            using var source=File.OpenRead(Path.Combine(clientDirectory,scene.Source));
            results.Add(new{Zone=zone,scene.Source,SourceSha256=Convert.ToHexStringLower(SHA256.HashData(source)),
                Meshes=scene.Meshes.Length,Objects=scene.Objects.Length,Vertices=scene.Meshes.Sum(m=>m.Vertices.Length),
                Indices=scene.Meshes.Sum(m=>m.Indices.Length),scene.Status,Samples=samples});
        }
        File.WriteAllText(Path.Combine(outputDirectory,"map-object-local-checks.json"),JsonSerializer.Serialize(new{ReadOnlyInstalledAssets=true,Results=results},new JsonSerializerOptions{WriteIndented=true}));
    }
    static byte[] Model()
    {
        using var bytes=new MemoryStream();using var w=new BinaryWriter(bytes);
        w.Write(2u);w.Write(0u);w.Write(0u);
        foreach(var points in new[]{new[]{new Vector3(100,200,300),new Vector3(200,200,300),new Vector3(100,300,300)},new[]{new Vector3(-100,0,0),new Vector3(0,100,0),new Vector3(0,0,100)}})
        {
            w.Write(new byte[256]);w.Write(0u);w.Write(3u);w.Write(1u);
            foreach(var p in points){w.Write(p.X);w.Write(p.Y);w.Write(p.Z);w.Write(new byte[24]);}
            w.Write((ushort)2);w.Write((ushort)0);w.Write((ushort)1);
        }
        return bytes.ToArray();
    }
    static void Name(BinaryWriter w,string name)
    {
        var b=Encoding.ASCII.GetBytes(name);w.Write(b);w.Write(new byte[256-b.Length]);
    }
    static void Matrix(BinaryWriter w,Matrix4x4 m)
    {
        foreach(var value in new[]{m.M11,m.M12,m.M13,m.M14,m.M21,m.M22,m.M23,m.M24,m.M31,m.M32,m.M33,m.M34,m.M41,m.M42,m.M43,m.M44})w.Write(value);
    }
    static byte[] Scene(bool build1=false)
    {
        using var bytes=new MemoryStream();using var w=new BinaryWriter(bytes);
        var h=new byte[256];Encoding.ASCII.GetBytes("ZallA-3D Scene Data File Build#"+(build1?"1":"2")).CopyTo(h,0);w.Write(h);
        w.Write(new byte[16]);w.Write(1u);w.Write(new byte[16908]);
        w.Write(1u);w.Write(0u);w.Write(0u);w.Write(4u);
        var rotated=new Matrix4x4(0,0,-1,0,0,1,0,0,1,0,0,0,1000,2000,3000,1);
        var bad=Matrix4x4.Identity;bad.M11=float.NaN;
        var enormous=Matrix4x4.Identity;enormous.M13=4.37e27f;
        foreach(var m in new[]{rotated,Matrix4x4.Identity,bad,enormous})
        {
            Matrix(w,m);w.Write(1u);Name(w,"fixture.r3s");for(int n=1;n<(build1?3:4);n++)Name(w,n==1?"secondary.r3s":"");
        }
        w.Write(1u);w.Write(new byte[1024]);
        foreach(int stride in new[]{326,332}){w.Write(1u);Name(w,"fixture.r3s");w.Write(5u);Matrix(w,Matrix4x4.Identity);w.Write(new byte[stride-324]);}
        w.Write(1u);w.Write(0u);w.Write(0u);w.Write(2u);w.Write(new byte[6]);
        w.Write(1u);w.Write(0u);w.Write(0u);w.Write(4u);
        foreach(string name in new[]{"fixture.r3s","house-only.r3s","../escape.r3s",""})
        {w.Write(1u);Name(w,name);Matrix(w,name.Length==0?default:Matrix4x4.Identity);w.Write((ushort)0);}
        return bytes.ToArray();
    }
    static void RejectModel(byte[] bytes)
    {
        try{MapObjectReader.DecodeModel(bytes,"synthetic");throw new Exception("Invalid object model was accepted");}
        catch(InvalidDataException){}
    }
    internal static void Run()
    {
        var model=Model();var mesh=MapObjectReader.DecodeModel(model,"synthetic");
        if(mesh.Vertices.Length!=6||!mesh.Indices.SequenceEqual(new[]{2,0,1,5,3,4})||mesh.Vertices[0]!=new Vector3(1,2,3))throw new Exception("Object submesh merge/index/unit regression");
        RejectModel(model[..^1]);RejectModel([..model,0]);
        var invalid=(byte[])model.Clone();BinaryPrimitives.WriteUInt32LittleEndian(invalid.AsSpan(272,4),uint.MaxValue);RejectModel(invalid);
        invalid=(byte[])model.Clone();BinaryPrimitives.WriteUInt16LittleEndian(invalid.AsSpan(388,2),65535);RejectModel(invalid);
        invalid=(byte[])model.Clone();BinaryPrimitives.WriteInt32LittleEndian(invalid.AsSpan(280,4),BitConverter.SingleToInt32Bits(float.NaN));RejectModel(invalid);
        invalid=(byte[])model.Clone();BinaryPrimitives.WriteUInt32LittleEndian(invalid.AsSpan(8,4),1);RejectModel(invalid);
        var root=Path.Combine(Path.GetTempPath(),"PPB-object-reader-"+Guid.NewGuid().ToString("N"));
        try
        {
            var house=Path.Combine(root,"OBJECTS","House");var objects=Path.Combine(root,"OBJECTS","Object");Directory.CreateDirectory(house);Directory.CreateDirectory(objects);
            File.WriteAllBytes(Path.Combine(house,"fixture.r3s"),model);File.WriteAllBytes(Path.Combine(objects,"fixture.r3s"),model);File.WriteAllBytes(Path.Combine(house,"house-only.r3s"),model);
            foreach(bool build1 in new[]{false,true})
            {
                var result=MapObjectReader.Load(Scene(build1),root,default);
                if(result.Meshes.Length!=2||result.Objects.Length!=3||result.Objects[0].Mesh!=result.Objects[1].Mesh||result.Objects[2].Mesh==result.Objects[0].Mesh)throw new Exception("Folder-specific object cache/table regression: "+result.Status);
                var world=Vector3.Transform(result.Meshes[result.Objects[0].Mesh].Vertices[0],result.Objects[0].Transform);
                if(world!=new Vector3(13,22,29))throw new Exception("Row-vector placement matrix/double-scaling/reflection regression");
                foreach(string reason in new[]{"1 placements with missing folder-specific models","2 invalid placements","1 unsafe references","1 placeholders","2 compound child records","2 vegetation records","4 secondary references"})
                    if(!result.Status.Contains(reason,StringComparison.Ordinal))throw new Exception("Object omission status regression: "+reason+" / "+result.Status);
            }
            var broken=Scene();BinaryPrimitives.WriteUInt32LittleEndian(broken.AsSpan(272,4),uint.MaxValue);
            if(MapObjectReader.Load(broken,root,default).Objects.Length!=0)throw new Exception("Unbounded scene terrain count accepted");
            if(MapObjectReader.Load(Scene()[..^1],root,default).Objects.Length!=0)throw new Exception("Truncated object chain accepted");
            invalid=(byte[])model.Clone();BinaryPrimitives.WriteUInt16LittleEndian(invalid.AsSpan(388,2),65535);File.WriteAllBytes(Path.Combine(house,"fixture.r3s"),invalid);
            var rejected=MapObjectReader.Load(Scene(),root,default);
            if(rejected.Objects.Length!=1||!rejected.Status.Contains("2 placements with invalid models",StringComparison.Ordinal))throw new Exception("Invalid models were not omitted independently");
            using var stop=new CancellationTokenSource();stop.Cancel();
            try{MapObjectReader.Load(Scene(),root,stop.Token);throw new Exception("Cancelled object load completed");}catch(OperationCanceledException){}
        }
        finally{if(Directory.Exists(root))Directory.Delete(root,true);}
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"map-object-reader-checks.json"),JsonSerializer.Serialize(new{Passed=true,Checks=new[]{"Build1/2 complete table chains","row-vector matrices and /100 units","merged submesh indices","folder-specific cache without fallback","missing/invalid/unsafe/sentinel omissions","corrupt/truncated/oversized models and tables","compound and vegetation omitted","cancellation"}}));
    }
}
