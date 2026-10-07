using System.Buffers.Binary;
using System.Text.Json;
namespace PoteHunter;
internal static class GameMapChecks
{
    internal static byte[] Tile(uint format=0x31545844)
    {
        int size=format==0x31545844?8:16;var data=new byte[128+4096*size];
        void U(int at,uint value)=>BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(at,4),value);
        U(0,0x20534444);U(4,124);U(12,256);U(16,256);U(76,32);U(80,4);U(84,format);
        for(int i=0;i<4096;i++){int at=128+i*size;if(size==16){for(int j=0;j<8;j++)data[at+j]=255;at+=8;}data[at]=0;data[at+1]=248;} // red RGB565
        return data;
    }
    internal static void Run()
    {
        if(!GameMapLayout.ClientHashSupported(GameMapLayout.VerifiedClientHash) ||
           !GameMapLayout.ClientHashSupported(GameMapLayout.VerifiedOctober6ClientHash))
            throw new Exception("A verified map client was rejected");
        foreach(string? hash in new[]{null,"",new string('0',64),GameMapLayout.VerifiedOctober6ClientHash[..^1],
                    "0"+GameMapLayout.VerifiedOctober6ClientHash[1..]})
            if(GameMapLayout.ClientHashSupported(hash))throw new Exception("An unverified map hash was accepted");
        foreach(uint format in new uint[]{0x31545844,0x33545844,0x35545844})
        {
            using var image=DdsMapTile.Decode(Tile(format));
            if(image.GetPixel(0,0)!=Color.FromArgb(255,255,0,0) || image.GetPixel(255,255)!=Color.FromArgb(255,255,0,0))throw new Exception("DDS block order/color/alpha failed");
        }
        var raw=new byte[128+256*256*4];
        void U(int at,uint value)=>BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(at,4),value);
        U(0,0x20534444);U(4,124);U(12,256);U(16,256);U(20,1024);U(76,32);U(80,65);U(88,32);U(92,0xff0000);U(96,0xff00);U(100,0xff);U(104,0xff000000);raw[128]=255;raw[131]=255;
        using(var image=DdsMapTile.Decode(raw))if(image.GetPixel(0,0)!=Color.FromArgb(255,0,0,255))throw new Exception("DDS BGRA layout failed");
        void Rejected(byte[] bytes){try{using var image=DdsMapTile.Decode(bytes);throw new Exception("Invalid DDS accepted");}catch(InvalidDataException){}}
        Rejected(Tile()[..130]);var invalid=Tile();invalid[84]=0;Rejected(invalid);invalid=Tile();invalid[16]=1;Rejected(invalid);
        // DXT1 c0 <= c1 uses transparent palette entry three.
        var transparent=Tile();transparent[128]=0;transparent[129]=0;transparent[130]=255;transparent[131]=255;for(int i=132;i<136;i++)transparent[i]=255;
        using(var image=DdsMapTile.Decode(transparent))if(image.GetPixel(0,0).A!=0)throw new Exception("DDS transparency failed");
        var bounds=GameMapLayout.Bounds(8)!.Value;
        if(bounds.Pixel(new(315,3780))!=new PointF(0,0) || bounds.Pixel(new(3780,315))!=new PointF(512,512) || bounds.Pixel(bounds.Center)!=new PointF(256,256))throw new Exception("Game map axes/offset failed");
        if(GameMapLayout.Bounds(15)!=null || GameMapLayout.Bounds(999)!=null)throw new Exception("Unverified projection accepted");
        foreach(var sample in new (int Zone,Vec World,PointF Pixel)[]{(1,new(3465,0),new(464,466)),(2,new(3422,3438),new(460,52)),(4,new(2463,2383),new(444,83)),(5,new(3120,3104),new(459,55)),(16,new(1116,1111),new(117,405)),(3,new(1000,1000),new(138,372))})
        {
            var projected=GameMapLayout.Bounds(sample.Zone)!.Value.Pixel(sample.World);
            if(Math.Abs(projected.X-sample.Pixel.X)>.001 || Math.Abs(projected.Y-sample.Pixel.Y)>.001)throw new Exception("Client affine projection regression: "+sample.Zone);
        }
        var root=Path.Combine(Path.GetTempPath(),"PPB-map-"+Guid.NewGuid().ToString("N"));
        try
        {
            var directory=Path.Combine(root,"TEXTURE","Interface","Zone8");Directory.CreateDirectory(directory);
            for(int i=1;i<=4;i++)File.WriteAllBytes(Path.Combine(directory,$"LargeMap{i:00}.dds"),Tile());
            using(var image=GameMapLayout.Load(root,8))if(image==null || image.Size!=new Size(512,512))throw new Exception("Map assembly failed");
            File.Delete(Path.Combine(directory,"LargeMap04.dds"));using(var image=GameMapLayout.Load(root,8))if(image!=null)throw new Exception("Partial map accepted");
            string fakeClient=Path.Combine(root,"Client.exe");File.WriteAllText(fakeClient,"unverified client");if(GameMapLayout.ClientSupported(fakeClient))throw new Exception("Unverified client accepted");
            if(GameMapLayout.ClientSupported(Path.Combine(root,"missing-client.exe")))throw new Exception("Missing client accepted");
        }
        finally{Directory.Delete(root,true);}
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"game-map-checks.json"),JsonSerializer.Serialize(new{Passed=true,Checks=new[]{"both verified map builds accepted; altered, truncated and unknown hashes rejected","DXT1/3/5 and BGRA decoding","corrupt/truncated/unsupported tiles rejected","alpha and block order","north-up map axes and zone offsets","all four tiles required","unsupported zone and missing/unverified client rejected"}}));
    }
}
