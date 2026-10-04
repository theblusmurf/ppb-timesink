using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;

namespace PoteHunter;

// Reads only the installed scene's validated primary House and compact Object
// tables. Geometry is a visual aid, not a collision or navigation model.
internal static class MapObjectReader
{
    const int MaxSceneBytes=96*1024*1024,MaxModelBytes=64*1024*1024;
    const int MaxGroups=4096,MaxPlacements=100000,MaxChildren=100000,MaxVegetation=2000000;
    const int MaxVertices=2000000,MaxIndices=6000000,MaxSceneVertices=4000000,MaxSceneIndices=12000000;
    const float MaxRawCoordinate=100000000,MaxWorldCoordinate=1000000;
    static readonly Regex FirstReference=new(@"[A-Za-z0-9_\\():!. -]{3,}\.[Rr]3[Ss](?=\x00)",RegexOptions.CultureInvariant,TimeSpan.FromSeconds(1));
    sealed record Placement(string Folder,string Name,Matrix4x4 Transform,bool Valid);
    sealed record Tables(List<Placement> Placements,int Children,int Vegetation,int Secondary);
    enum ModelState { Loaded,Missing,Invalid,Unsafe,Budget }
    sealed record ModelResult(int Mesh,ModelState State,Vector3 Min=default,Vector3 Max=default);

    internal static (MapObjectMesh[] Meshes,MapObjectPlacement[] Objects,string Status) Load(
        ReadOnlyMemory<byte> decodedScene,string clientDirectory,CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Tables tables;
        try { tables=ReadTables(decodedScene.Span,cancellationToken); }
        catch(Exception ex) when(ex is InvalidDataException or RegexMatchTimeoutException or OverflowException)
        { return ([],[],"Static objects unavailable: "+(ex is RegexMatchTimeoutException?"object-table search limit reached":ex.Message)); }

        string root;
        try { root=Path.GetFullPath(clientDirectory); }
        catch(Exception ex) when(ex is ArgumentException or NotSupportedException or PathTooLongException)
        { return ([],[],"Static objects unavailable: invalid client asset directory"); }
        var meshes=new List<MapObjectMesh>();var objects=new List<MapObjectPlacement>();
        var cache=new Dictionary<string,ModelResult>(StringComparer.OrdinalIgnoreCase);
        int vertices=0,indices=0,missing=0,invalidModels=0,invalidTransforms=0,unsafeNames=0,budget=0,empty=0;
        foreach(var item in tables.Placements)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if(item.Name.Length==0){empty++;continue;}
            if(!item.Valid){invalidTransforms++;continue;}
            string key=item.Folder+"/"+item.Name;
            if(!cache.TryGetValue(key,out var model))
            {
                model=ReadModel(root,item.Folder,item.Name,vertices,indices,meshes,cancellationToken);
                cache.Add(key,model);
                if(model.State==ModelState.Loaded){vertices+=meshes[model.Mesh].Vertices.Length;indices+=meshes[model.Mesh].Indices.Length;}
            }
            if(model.State!=ModelState.Loaded)
            {
                switch(model.State){case ModelState.Missing:missing++;break;case ModelState.Invalid:invalidModels++;break;case ModelState.Unsafe:unsafeNames++;break;case ModelState.Budget:budget++;break;}
                continue;
            }
            // A finite affine matrix can still contain a corrupt, enormous scale.
            // Check every model-bound corner without expanding all instances.
            if(!BoundedInstance(model.Min,model.Max,item.Transform)){invalidTransforms++;continue;}
            objects.Add(new(model.Mesh,item.Transform));
        }
        string status=$"{objects.Count:N0} static objects · {meshes.Count:N0} models";
        var omissions=new List<string>();
        if(missing>0)omissions.Add($"{missing:N0} placements with missing folder-specific models");
        if(invalidModels>0)omissions.Add($"{invalidModels:N0} placements with invalid models");
        if(invalidTransforms>0)omissions.Add($"{invalidTransforms:N0} invalid placements");
        if(unsafeNames>0)omissions.Add($"{unsafeNames:N0} unsafe references");
        if(budget>0)omissions.Add($"{budget:N0} placements exceeding geometry budget");
        if(empty>0)omissions.Add($"{empty:N0} placeholders");
        if(tables.Children>0)omissions.Add($"{tables.Children:N0} compound child records");
        if(tables.Vegetation>0)omissions.Add($"{tables.Vegetation:N0} vegetation records");
        if(tables.Secondary>0)omissions.Add($"{tables.Secondary:N0} secondary references");
        if(omissions.Count>0)status+="; omitted: "+string.Join(", ",omissions);
        return (meshes.ToArray(),objects.ToArray(),status+"; untextured visual geometry");
    }

    static Tables ReadTables(ReadOnlySpan<byte> data,CancellationToken token)
    {
        if(data.Length<276||data.Length>MaxSceneBytes)throw new InvalidDataException("scene size outside supported limits");
        var header=data[..64];
        var h1="ZallA-3D Scene Data File Build#1"u8;var h2="ZallA-3D Scene Data File Build#2"u8;
        bool build1=header.StartsWith(h1)&&header[h1.Length]==0;
        if(!build1&&!(header.StartsWith(h2)&&header[h2.Length]==0))throw new InvalidDataException("unsupported scene header");
        uint patches=BinaryPrimitives.ReadUInt32LittleEndian(data[272..276]);
        if(patches>4096)throw new InvalidDataException("terrain count outside supported limits");
        long terrainEnd=276+(long)patches*16908;
        if(terrainEnd>data.Length)throw new InvalidDataException("truncated terrain before object tables");
        // The intervening material section varies by scene. The private extractor
        // validates this same candidate relationship: the first reference starts
        // after group-count(4), group header(12), matrix(64), and identity(4).
        // Never accept a candidate from a name alone: the complete following
        // table chain must pass bounded structural validation.
        int scanStart=(int)terrainEnd,scanLength=Math.Min(data.Length-scanStart,2*1024*1024),attempts=0;
        string search=Encoding.Latin1.GetString(data.Slice(scanStart,scanLength));
        for(var match=FirstReference.Match(search);match.Success;match=match.NextMatch())
        {
            token.ThrowIfCancellationRequested();
            if(++attempts>64)break;
            int candidate=scanStart+match.Index-84;
            if(candidate<scanStart)continue;
            try { return ParseChain(data,candidate,build1?3:4,token); }
            catch(InvalidDataException) { }
        }
        throw new InvalidDataException("no complete supported static-object table chain");
    }

    static Tables ParseChain(ReadOnlySpan<byte> data,int offset,int referenceSlots,CancellationToken token)
    {
        var c=new Cursor(data,offset);var placements=new List<Placement>();int secondary=0,children=0,vegetation=0;
        int groups=c.Count(MaxGroups,"House groups");
        if(groups==0)throw new InvalidDataException("candidate has no House groups");
        for(int group=0;group<groups;group++)
        {
            token.ThrowIfCancellationRequested();c.Grid();int count=c.Count(MaxPlacements,"House placements");
            CheckTotal(placements.Count,count,MaxPlacements,"placements");
            c.RequireProduct(count,68+referenceSlots*256);
            for(int i=0;i<count;i++)
            {
                if((i&255)==0)token.ThrowIfCancellationRequested();
                var matrix=ReadMatrix(c.Take(64));c.Take(4);string name=Reference(c.Take(256));
                for(int n=1;n<referenceSlots;n++)if(Reference(c.Take(256)).Length>0)secondary++;
                placements.Add(new("House",name,ToMap(matrix),ValidMatrix(matrix)));
            }
        }
        int definitions=c.Count(MaxGroups,"compound definitions");
        for(int definition=0;definition<definitions;definition++)
        {
            token.ThrowIfCancellationRequested();c.Take(4*256);
            foreach(int stride in new[]{326,332})
            {
                int count=c.Count(MaxChildren,"compound children");CheckTotal(children,count,MaxChildren,"compound children");children+=count;
                c.RequireProduct(count,stride);
                for(int i=0;i<count;i++)
                {
                    if((i&255)==0)token.ThrowIfCancellationRequested();var record=c.Take(stride);
                    if(Reference(record[..256]).Length==0)throw new InvalidDataException("empty compound child reference");
                    // Child hierarchy/visibility is unproved; validate its bounded
                    // record shape but never substitute it for a static placement.
                    var m=ReadMatrix(record.Slice(260,64));if(!Finite(m))throw new InvalidDataException("nonfinite compound child matrix");
                }
            }
        }
        int vegetationGroups=c.Count(MaxGroups,"vegetation groups");
        for(int group=0;group<vegetationGroups;group++)
        {
            token.ThrowIfCancellationRequested();c.Grid();int count=c.Count(MaxVegetation,"vegetation records");
            CheckTotal(vegetation,count,MaxVegetation,"vegetation records");vegetation+=count;c.RequireProduct(count,3);c.Take(count*3);
        }
        groups=c.Count(MaxGroups,"Object groups");
        for(int group=0;group<groups;group++)
        {
            token.ThrowIfCancellationRequested();c.Grid();int count=c.Count(MaxPlacements,"Object placements");
            CheckTotal(placements.Count,count,MaxPlacements,"placements");c.RequireProduct(count,326);
            for(int i=0;i<count;i++)
            {
                if((i&255)==0)token.ThrowIfCancellationRequested();var record=c.Take(326);string name=Reference(record.Slice(4,256));
                var matrix=ReadMatrix(record.Slice(260,64));placements.Add(new("Object",name,ToMap(matrix),ValidMatrix(matrix)));
            }
        }
        if(placements.Count==0)throw new InvalidDataException("empty candidate placement chain");
        return new(placements,children,vegetation,secondary);
    }

    static ModelResult ReadModel(string root,string folder,string name,int vertices,int indices,List<MapObjectMesh> meshes,CancellationToken token)
    {
        if(!SafeReference(name))return new(-1,ModelState.Unsafe);
        try
        {
            string objectRoot=Path.GetFullPath(Path.Combine(root,"OBJECTS")),directory=Path.GetFullPath(Path.Combine(objectRoot,folder));
            string path=Path.GetFullPath(Path.Combine(directory,name));
            if(!path.StartsWith(directory+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))return new(-1,ModelState.Unsafe);
            // A basename check alone would still follow a junction or symlink out
            // of the installation. Refuse reparse components in the asset branch.
            foreach(string component in new[]{root,objectRoot,directory,path})
                if((File.GetAttributes(component)&FileAttributes.ReparsePoint)!=0)return new(-1,ModelState.Unsafe);
            using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);
            if(stream.Length<12||stream.Length>MaxModelBytes)return new(-1,ModelState.Invalid);
            byte[] raw=new byte[(int)stream.Length];int at=0;
            while(at<raw.Length){token.ThrowIfCancellationRequested();int got=stream.Read(raw,at,Math.Min(65536,raw.Length-at));if(got==0)throw new InvalidDataException("truncated model");at+=got;}
            if(stream.Length!=raw.Length)throw new InvalidDataException("model changed during read");
            var mesh=DecodeModel(raw,folder+"/"+name,token);
            if(mesh.Vertices.Length>MaxSceneVertices-vertices||mesh.Indices.Length>MaxSceneIndices-indices)return new(-1,ModelState.Budget);
            var min=new Vector3(float.PositiveInfinity);var max=new Vector3(float.NegativeInfinity);
            foreach(var point in mesh.Vertices){min=Vector3.Min(min,point);max=Vector3.Max(max,point);}
            int id=meshes.Count;meshes.Add(mesh);return new(id,ModelState.Loaded,min,max);
        }
        catch(FileNotFoundException){return new(-1,ModelState.Missing);}
        catch(DirectoryNotFoundException){return new(-1,ModelState.Missing);}
        catch(Exception ex) when(ex is InvalidDataException or IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {return new(-1,ModelState.Invalid);}
    }

    internal static MapObjectMesh DecodeModel(ReadOnlySpan<byte> raw,string name,CancellationToken token=default)
    {
        if(raw.Length<12||raw.Length>MaxModelBytes)throw new InvalidDataException("model size outside supported limits");
        var c=new Cursor(raw,0);int groups=c.Count(MaxGroups,"submeshes"),textures=c.Count(MaxGroups,"texture references");
        if(c.U()!=0)throw new InvalidDataException("unsupported model header");c.RequireProduct(textures,256);c.Take(textures*256);
        var vertices=new List<Vector3>();var indices=new List<int>();
        for(int group=0;group<groups;group++)
        {
            token.ThrowIfCancellationRequested();c.Take(256);uint material=c.U();
            if(textures>0&&material>=textures)throw new InvalidDataException("model material outside reference table");
            int count=c.Count(MaxVertices,"model vertices"),triangles=c.Count(MaxIndices/3,"model triangles");
            CheckTotal(vertices.Count,count,MaxVertices,"model vertices");CheckTotal(indices.Count,triangles*3,MaxIndices,"model indices");
            c.RequireProduct(count,36);int start=vertices.Count;
            for(int i=0;i<count;i++)
            {
                if((i&4095)==0)token.ThrowIfCancellationRequested();var v=c.Take(36);
                var point=new Vector3(F(v,0),F(v,4),F(v,8));
                if(!Finite(point)||Math.Abs(point.X)>=MaxRawCoordinate||Math.Abs(point.Y)>=MaxRawCoordinate||Math.Abs(point.Z)>=MaxRawCoordinate)throw new InvalidDataException("nonfinite or unreasonable model vertex");
                vertices.Add(point/100f);
            }
            c.RequireProduct(triangles,6);
            for(int i=0;i<triangles*3;i++)
            {
                if((i&8191)==0)token.ThrowIfCancellationRequested();int index=BinaryPrimitives.ReadUInt16LittleEndian(c.Take(2));
                if(index>=count)throw new InvalidDataException("model index outside vertex array");indices.Add(start+index);
            }
            if(triangles==0)vertices.RemoveRange(start,count);
        }
        if(c.Remaining!=0)throw new InvalidDataException("unrecognized model trailing bytes");
        if(vertices.Count==0||indices.Count==0)throw new InvalidDataException("empty model geometry");
        return new(name,vertices.ToArray(),indices.ToArray());
    }

    static string Reference(ReadOnlySpan<byte> bytes)
    {
        int end=bytes.IndexOf((byte)0);if(end<0)throw new InvalidDataException("unterminated object reference");
        string name=Encoding.Latin1.GetString(bytes[..end]);
        if(name.Length>0&&!name.EndsWith(".r3s",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("unsupported object reference");
        return name;
    }
    static bool SafeReference(string name)=>name.Length is >0 and <=255&&Path.GetFileName(name)==name&&
        !Path.IsPathRooted(name)&&name.IndexOfAny(Path.GetInvalidFileNameChars())<0&&!name.Contains('/')&&!name.Contains('\\')&&
        name.All(c=>c is >=' ' and <='~')&&name.EndsWith(".r3s",StringComparison.OrdinalIgnoreCase);
    static float F(ReadOnlySpan<byte> bytes,int at)=>BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(at,4)));
    static Matrix4x4 ReadMatrix(ReadOnlySpan<byte> b)=>new(F(b,0),F(b,4),F(b,8),F(b,12),F(b,16),F(b,20),F(b,24),F(b,28),F(b,32),F(b,36),F(b,40),F(b,44),F(b,48),F(b,52),F(b,56),F(b,60));
    static Matrix4x4 ToMap(Matrix4x4 matrix){matrix.M41/=100f;matrix.M42/=100f;matrix.M43/=100f;return matrix;}
    static bool Finite(Vector3 p)=>float.IsFinite(p.X)&&float.IsFinite(p.Y)&&float.IsFinite(p.Z);
    static bool Finite(Matrix4x4 m)=>float.IsFinite(m.M11)&&float.IsFinite(m.M12)&&float.IsFinite(m.M13)&&float.IsFinite(m.M14)&&float.IsFinite(m.M21)&&float.IsFinite(m.M22)&&float.IsFinite(m.M23)&&float.IsFinite(m.M24)&&float.IsFinite(m.M31)&&float.IsFinite(m.M32)&&float.IsFinite(m.M33)&&float.IsFinite(m.M34)&&float.IsFinite(m.M41)&&float.IsFinite(m.M42)&&float.IsFinite(m.M43)&&float.IsFinite(m.M44);
    static bool ValidMatrix(Matrix4x4 m)=>Finite(m)&&Math.Abs(m.M14)<=.001f&&Math.Abs(m.M24)<=.001f&&Math.Abs(m.M34)<=.001f&&Math.Abs(m.M44-1)<=.001f&&
        new[]{m.M11,m.M12,m.M13,m.M21,m.M22,m.M23,m.M31,m.M32,m.M33}.All(v=>Math.Abs(v)<=10000)&&
        new[]{m.M11,m.M12,m.M13,m.M21,m.M22,m.M23,m.M31,m.M32,m.M33}.Any(v=>v!=0)&&
        Math.Abs(m.M41)<=MaxRawCoordinate&&Math.Abs(m.M42)<=MaxRawCoordinate&&Math.Abs(m.M43)<=MaxRawCoordinate;
    static bool BoundedInstance(Vector3 min,Vector3 max,Matrix4x4 matrix)
    {
        for(int corner=0;corner<8;corner++)
        {
            var p=Vector3.Transform(new((corner&1)==0?min.X:max.X,(corner&2)==0?min.Y:max.Y,(corner&4)==0?min.Z:max.Z),matrix);
            if(!Finite(p)||Math.Abs(p.X)>MaxWorldCoordinate||Math.Abs(p.Y)>MaxWorldCoordinate||Math.Abs(p.Z)>MaxWorldCoordinate)return false;
        }
        return true;
    }
    static void CheckTotal(int old,int count,int limit,string label){if(count>limit-old)throw new InvalidDataException(label+" exceed supported budget");}
    ref struct Cursor(ReadOnlySpan<byte> data,int offset)
    {
        readonly ReadOnlySpan<byte> data=data;int offset=offset;
        internal int Remaining=>data.Length-offset;
        internal ReadOnlySpan<byte> Take(int count){if(count<0||count>Remaining)throw new InvalidDataException("truncated object table/model");var result=data.Slice(offset,count);offset+=count;return result;}
        internal uint U()=>BinaryPrimitives.ReadUInt32LittleEndian(Take(4));
        internal int Count(int limit,string label){uint n=U();if(n>limit)throw new InvalidDataException(label+" exceed supported limits");return (int)n;}
        internal void RequireProduct(int count,int stride){if((long)count*stride>Remaining)throw new InvalidDataException("truncated object record array");}
        internal void Grid(){if(U()>4096||U()>4096)throw new InvalidDataException("unreasonable object-group coordinates");}
    }
}
