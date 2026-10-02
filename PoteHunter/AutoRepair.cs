using System.Text.Json;
using System.Text.Json.Serialization;

namespace PoteHunter;

internal sealed record RepairIconMatch(bool Matched,string Method,double? Correlation=null,
    double? GradientCorrelation=null,double? MeanError=null);

internal sealed record RepairPatch(int X,int Y,int Width,int Height,byte[] Rgb)
{
    [JsonIgnore] public Rectangle Bounds=>new(X,Y,Width,Height);
    [JsonIgnore] public Point Center=>new(X+Width/2,Y+Height/2);
    public bool Valid(Size size)=>Width is >=12 and <=400 && Height is >=8 and <=200 && X>=0 && Y>=0 &&
        (long)X+Width<=size.Width && (long)Y+Height<=size.Height && Rgb!=null && Rgb.Length==Width*Height*3 && Informative(Rgb);

    static bool Informative(byte[] data)
    {
        var light=new int[data.Length/3];
        for(int i=0;i<light.Length;i++)light[i]=(data[i*3]+data[i*3+1]+data[i*3+2])/3;
        double mean=light.Average();
        // Reject blank patches; a plain panel background cannot identify a control.
        return light.Select(b=>(b-mean)*(b-mean)).Average()>180 && light.Max()-light.Min()>45;
    }

    public static RepairPatch Capture(Bitmap image,Rectangle bounds)
    {
        if(bounds.Width is <12 or >400 || bounds.Height is <8 or >200 || !new Rectangle(Point.Empty,image.Size).Contains(bounds))
            throw new InvalidOperationException("Select a 12–400 pixel wide, 8–200 pixel high area inside the game image.");
        var data=new byte[bounds.Width*bounds.Height*3];int offset=0;
        for(int y=bounds.Top;y<bounds.Bottom;y++)for(int x=bounds.Left;x<bounds.Right;x++)
        {
            var pixel=image.GetPixel(x,y);data[offset++]=pixel.R;data[offset++]=pixel.G;data[offset++]=pixel.B;
        }
        var patch=new RepairPatch(bounds.X,bounds.Y,bounds.Width,bounds.Height,data);
        if(!patch.Valid(image.Size))throw new InvalidOperationException("That area is too plain. Select distinctive static text or an icon.");
        return patch;
    }

    public bool Matches(Bitmap image)
    {
        if(!new Rectangle(Point.Empty,image.Size).Contains(Bounds) || Rgb.Length!=Width*Height*3)return false;
        long difference=0;int changed=0,offset=0;
        for(int y=Y;y<Y+Height;y++)for(int x=X;x<X+Width;x++)
        {
            var p=image.GetPixel(x,y);
            int error=Math.Abs(p.R-Rgb[offset++])+Math.Abs(p.G-Rgb[offset++])+Math.Abs(p.B-Rgb[offset++]);
            difference+=error;if(error>90)changed++;
        }
        return difference<=Width*Height*3L*8 && changed<=Width*Height*.04;
    }

    // Repair dialogs are translucent: the world behind their static white
    // text changes after revival. Match the foreground glyphs at the saved
    // coordinates, requiring both coverage and precision so blank/bright
    // backgrounds or a different question cannot stand in for the text.
    public bool MatchesText(Bitmap image)
    {
        if(Matches(image))return true;
        if(!new Rectangle(Point.Empty,image.Size).Contains(Bounds) || Rgb.Length!=Width*Height*3)return false;
        static bool Ink(int r,int g,int b)=>Math.Min(r,Math.Min(g,b))>=190 &&
            Math.Max(r,Math.Max(g,b))-Math.Min(r,Math.Min(g,b))<=30;
        int expected=0,actual=0,shared=0,offset=0;
        for(int y=Y;y<Y+Height;y++)for(int x=X;x<X+Width;x++)
        {
            bool reference=Ink(Rgb[offset],Rgb[offset+1],Rgb[offset+2]);offset+=3;
            var pixel=image.GetPixel(x,y);bool observed=Ink(pixel.R,pixel.G,pixel.B);
            if(reference)expected++;if(observed)actual++;if(reference&&observed)shared++;
        }
        return expected>=20 && expected<=Width*Height*.35 && actual>=20 && actual<=Width*Height*.35 &&
            shared>=expected*.95 && shared>=actual*.95;
    }

    // Small translucent icons retain their shape under a bounded color cast.
    // Remove only a uniform per-channel offset; keep the original error and
    // changed-pixel limits so missing, moved or different icons still fail.
    public bool MatchesControl(Bitmap image)
    {
        if(Matches(image))return true;
        if(!new Rectangle(Point.Empty,image.Size).Contains(Bounds) || Rgb.Length!=Width*Height*3)return false;
        long red=0,green=0,blue=0;int offset=0;
        for(int y=Y;y<Y+Height;y++)for(int x=X;x<X+Width;x++)
        {
            var pixel=image.GetPixel(x,y);
            red+=pixel.R-Rgb[offset++];green+=pixel.G-Rgb[offset++];blue+=pixel.B-Rgb[offset++];
        }
        int count=Width*Height;
        int dr=(int)Math.Round(red/(double)count),dg=(int)Math.Round(green/(double)count),db=(int)Math.Round(blue/(double)count);
        if(Math.Abs(dr)>32 || Math.Abs(dg)>32 || Math.Abs(db)>32)return false;
        long difference=0;int changed=0;offset=0;
        for(int y=Y;y<Y+Height;y++)for(int x=X;x<X+Width;x++)
        {
            var pixel=image.GetPixel(x,y);
            int error=Math.Abs(pixel.R-Math.Clamp(Rgb[offset++]+dr,0,255))+
                Math.Abs(pixel.G-Math.Clamp(Rgb[offset++]+dg,0,255))+
                Math.Abs(pixel.B-Math.Clamp(Rgb[offset++]+db,0,255));
            difference+=error;if(error>90)changed++;
        }
        return difference<=count*3L*8 && changed<=count*.04;
    }

    // The saved hammer selection may include a translucent panel edge and
    // world pixels. Only this small icon gets a structural fallback, at its
    // exact saved position. Inventory and confirmation remain separate gates.
    public RepairIconMatch InspectIcon(Bitmap image)
    {
        if(MatchesControl(image))return new(true,"Pixels");
        if(Width is <16 or >96 || Height is <16 or >96 ||
            !new Rectangle(Point.Empty,image.Size).Contains(Bounds) || Rgb.Length!=Width*Height*3)
            return new(false,"Unsupported icon bounds");
        int insetX=Width/5,insetY=Math.Max(1,Height/12);
        int width=Width-2*insetX,height=Height-2*insetY,count=width*height;
        if(count<144)return new(false,"Insufficient icon detail");
        var expected=new double[count];var actual=new double[count];
        double colorDifference=0;var offsets=new double[3];
        for(int y=0;y<height;y++)for(int x=0;x<width;x++)
        {
            int i=y*width+x,source=((y+insetY)*Width+x+insetX)*3;
            var p=image.GetPixel(X+x+insetX,Y+y+insetY);
            expected[i]=(Rgb[source]+Rgb[source+1]+Rgb[source+2])/3d;
            actual[i]=(p.R+p.G+p.B)/3d;
            offsets[0]+=p.R-Rgb[source];offsets[1]+=p.G-Rgb[source+1];offsets[2]+=p.B-Rgb[source+2];
        }
        for(int c=0;c<3;c++)offsets[c]/=count;
        for(int y=0;y<height;y++)for(int x=0;x<width;x++)
        {
            int source=((y+insetY)*Width+x+insetX)*3;
            var p=image.GetPixel(X+x+insetX,Y+y+insetY);
            colorDifference+=Math.Abs(p.R-Rgb[source]-offsets[0])+Math.Abs(p.G-Rgb[source+1]-offsets[1])+Math.Abs(p.B-Rgb[source+2]-offsets[2]);
        }
        static double Correlation(double[] a,double[] b,out double varianceA,out double varianceB)
        {
            double meanA=a.Average(),meanB=b.Average(),aa=0,bb=0,ab=0;
            for(int i=0;i<a.Length;i++){double x=a[i]-meanA,y=b[i]-meanB;aa+=x*x;bb+=y*y;ab+=x*y;}
            varianceA=aa/a.Length;varianceB=bb/a.Length;
            return aa>0 && bb>0?ab/Math.Sqrt(aa*bb):0;
        }
        double correlation=Correlation(expected,actual,out double varianceExpected,out double varianceActual);
        int edges=(width-1)*height+(height-1)*width,index=0;
        var expectedEdges=new double[edges];var actualEdges=new double[edges];
        for(int y=0;y<height;y++)for(int x=0;x<width;x++)
        {
            int i=y*width+x;
            if(x+1<width){expectedEdges[index]=expected[i+1]-expected[i];actualEdges[index++]=actual[i+1]-actual[i];}
            if(y+1<height){expectedEdges[index]=expected[i+width]-expected[i];actualEdges[index++]=actual[i+width]-actual[i];}
        }
        double gradient=Correlation(expectedEdges,actualEdges,out double edgeExpected,out double edgeActual);
        double meanError=colorDifference/(count*3);
        bool matched=offsets.All(d=>Math.Abs(d)<=32) && varianceExpected>=144 && varianceActual>=144 &&
            edgeExpected>=64 && edgeActual>=64 && correlation>=.92 && gradient>=.92 && meanError<=12;
        return new(matched,"Icon structure",correlation,gradient,meanError);
    }
}

internal sealed record RepairProfile(int Version,string ClientHash,int Width,int Height,
    RepairPatch Inventory,RepairPatch Hammer,RepairPatch Prompt,RepairPatch Confirm)
{
    public static string PathName=>Path.Combine(AppContext.BaseDirectory,"repair-profile.json");
    public void Validate(Size size)
    {
        // ClientHash records where the UI patches were captured; executable updates
        // do not invalidate unchanged pixels. Keep version, geometry and patch checks.
        if(Version!=1)throw new InvalidOperationException("Repair setup format is unsupported. Configure repair again.");
        if(Width!=size.Width || Height!=size.Height)
            throw new InvalidOperationException("The game window size changed. Configure repair again for this size.");
        if(new[]{Inventory,Hammer,Prompt,Confirm}.Any(p=>p==null || !p.Valid(size)) ||
            Inventory.Bounds.IntersectsWith(Hammer.Bounds) || Prompt.Bounds.IntersectsWith(Confirm.Bounds))
            throw new InvalidOperationException("Repair setup has invalid or overlapping recognition areas. Configure repair again.");
    }
    public void Save(string? path=null)
    {
        Validate(new(Width,Height));
        string file=path??PathName;
        File.WriteAllText(file+".tmp",JsonSerializer.Serialize(this));File.Move(file+".tmp",file,true);
    }
    public static RepairProfile Load(Size size,string? path=null)
    {
        string file=path??PathName;
        if(!File.Exists(file))throw new InvalidOperationException("Choose Configure repair in Setup before enabling auto repair.");
        if(new FileInfo(file).Length>2_000_000)throw new InvalidOperationException("Repair setup is invalid; configure repair again.");
        RepairProfile? profile;
        try {profile=JsonSerializer.Deserialize<RepairProfile>(File.ReadAllText(file));}
        catch(JsonException) {throw new InvalidOperationException("Repair setup could not be read; configure repair again.");}
        if(profile==null)throw new InvalidOperationException("Repair setup is empty; configure repair again.");
        profile.Validate(size);return profile;
    }
}

internal readonly record struct RepairObservation(bool Inventory,bool Hammer,bool Prompt,bool Confirm);
internal enum RepairAction { OpenInventory, Hammer, Confirm, CloseInventory }

internal interface IRepairSurface
{
    long Now=>Environment.TickCount64;
    Task<RepairObservation> Observe(CancellationToken token);
    Task Perform(RepairAction action,CancellationToken token);
    Task Delay(CancellationToken token);
}

internal static class AutoRepair
{
    internal const int PhaseTimeoutMilliseconds=12000;
    public static async Task Run(IRepairSurface surface,CancellationToken token)
    {
        async Task WaitFor(Func<RepairObservation,bool> test,string failure)
        {
            using var phase=CancellationTokenSource.CreateLinkedTokenSource(token);
            phase.CancelAfter(PhaseTimeoutMilliseconds);
            long started=surface.Now;
            RepairObservation last=default;
            try
            {
                while(surface.Now-started<PhaseTimeoutMilliseconds)
                {
                    phase.Token.ThrowIfCancellationRequested();
                    last=await surface.Observe(phase.Token);
                    if(test(last))return;
                    await surface.Delay(phase.Token);
                }
            }
            catch(OperationCanceledException) when(!token.IsCancellationRequested && phase.IsCancellationRequested)
            { /* Report the same last-observed stage for either deadline. */ }
            TraceLog.Record("repair recognition timed out",new{Reason=failure,ElapsedMilliseconds=surface.Now-started,LastObserved=last});
            throw new InvalidOperationException(failure);
        }
        token.ThrowIfCancellationRequested();
        var first=await surface.Observe(token);
        if(first.Prompt)throw new InvalidOperationException("A repair dialog was already open. Close it before starting repair.");
        if(!first.Inventory)
        {
            await surface.Perform(RepairAction.OpenInventory,token);
            await WaitFor(s=>s.Inventory&&!s.Prompt,"Inventory was not recognized. Repair stopped; check its layout and setup.");
        }
        await WaitFor(s=>s.Inventory&&s.Hammer&&!s.Prompt,"The repair hammer and inventory were not recognized.");
        await surface.Perform(RepairAction.Hammer,token);
        await WaitFor(s=>s.Prompt&&s.Confirm,"The repair confirmation was not recognized. No confirmation was clicked.");
        // Each click is attempted once. A timeout never repeats a confirmation.
        await surface.Perform(RepairAction.Confirm,token);
        await WaitFor(s=>!s.Prompt&&s.Inventory,"Repair confirmation did not close. Repair stopped without retrying Yes.");
        await surface.Perform(RepairAction.CloseInventory,token);
        await WaitFor(s=>!s.Inventory&&!s.Prompt,"Inventory did not close after repair. Return to anchor was paused.");
    }
}
