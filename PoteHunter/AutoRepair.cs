using System.Text.Json;
using System.Text.Json.Serialization;

namespace PoteHunter;

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
}

internal sealed record RepairProfile(int Version,string ClientHash,int Width,int Height,
    RepairPatch Inventory,RepairPatch Hammer,RepairPatch Prompt,RepairPatch Confirm)
{
    public static string PathName=>Path.Combine(AppContext.BaseDirectory,"repair-profile.json");
    public void Validate(string hash,Size size)
    {
        if(Version!=1 || !string.Equals(ClientHash,hash,StringComparison.OrdinalIgnoreCase) || Width!=size.Width || Height!=size.Height)
            throw new InvalidOperationException("Repair setup belongs to a different client build or window size. Configure repair again.");
        if(new[]{Inventory,Hammer,Prompt,Confirm}.Any(p=>p==null || !p.Valid(size)) ||
            Inventory.Bounds.IntersectsWith(Hammer.Bounds) || Prompt.Bounds.IntersectsWith(Confirm.Bounds))
            throw new InvalidOperationException("Repair setup has invalid or overlapping recognition areas. Configure repair again.");
    }
    public void Save(string? path=null)
    {
        Validate(ClientHash,new(Width,Height));
        string file=path??PathName;
        File.WriteAllText(file+".tmp",JsonSerializer.Serialize(this));File.Move(file+".tmp",file,true);
    }
    public static RepairProfile Load(string hash,Size size,string? path=null)
    {
        string file=path??PathName;
        if(!File.Exists(file))throw new InvalidOperationException("Choose Configure repair in Setup before enabling auto repair.");
        if(new FileInfo(file).Length>2_000_000)throw new InvalidOperationException("Repair setup is invalid; configure repair again.");
        RepairProfile? profile;
        try {profile=JsonSerializer.Deserialize<RepairProfile>(File.ReadAllText(file));}
        catch(JsonException) {throw new InvalidOperationException("Repair setup could not be read; configure repair again.");}
        if(profile==null)throw new InvalidOperationException("Repair setup is empty; configure repair again.");
        profile.Validate(hash,size);return profile;
    }
}

internal readonly record struct RepairObservation(bool Inventory,bool Hammer,bool Prompt,bool Confirm);
internal enum RepairAction { OpenInventory, Hammer, Confirm, CloseInventory }

internal interface IRepairSurface
{
    Task<RepairObservation> Observe(CancellationToken token);
    Task Perform(RepairAction action,CancellationToken token);
    Task Delay(CancellationToken token);
}

internal static class AutoRepair
{
    public static async Task Run(IRepairSurface surface,CancellationToken token)
    {
        async Task WaitFor(Func<RepairObservation,bool> test,string failure)
        {
            using var phase=CancellationTokenSource.CreateLinkedTokenSource(token);
            phase.CancelAfter(TimeSpan.FromSeconds(12));
            try
            {
                for(int attempt=0;attempt<25;attempt++)
                {
                    phase.Token.ThrowIfCancellationRequested();
                    if(test(await surface.Observe(phase.Token)))return;
                    await surface.Delay(phase.Token);
                }
            }
            catch(OperationCanceledException) when(!token.IsCancellationRequested && phase.IsCancellationRequested)
            {throw new InvalidOperationException(failure);}
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
