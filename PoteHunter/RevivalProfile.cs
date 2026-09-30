using System.Text.Json;
using System.Text.Json.Serialization;

namespace PoteHunter;

internal sealed record RevivalStep(RepairPatch Marker,RepairPatch? Button,int X,int Y)
{
    [JsonIgnore] public Point Point=>new(X,Y);
    public static RevivalStep From(RecognitionSelection selection)=>new(selection.Marker,selection.Button,selection.Click.X,selection.Click.Y);
    public bool Valid(Size size,bool confirmation)=>Marker!=null && Marker.Valid(size) && new Rectangle(Point.Empty,size).Contains(Point) &&
        (confirmation?Button!=null && Button.Valid(size) && Button.Bounds.Contains(Point) && !Marker.Bounds.IntersectsWith(Button.Bounds):Button==null);
    public bool Matches(Bitmap image,bool button,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        bool matches=Marker.Matches(image) && (!button || Button!=null && Button.Matches(image));
        token.ThrowIfCancellationRequested();return matches;
    }
}

internal sealed record RevivalProfile(int Version,string ClientHash,int Width,int Height,RevivalStep? Opening,RevivalStep Confirm)
{
    public static string PathName=>Path.Combine(AppContext.BaseDirectory,"revival-profile.json");
    public void Validate(string hash,Size size)
    {
        if(Version!=1 || string.IsNullOrWhiteSpace(ClientHash) || !string.Equals(ClientHash,hash,StringComparison.OrdinalIgnoreCase) || Width!=size.Width || Height!=size.Height)
            throw new InvalidOperationException("Revival setup belongs to a different client build or window size. Run Custom revival setup again or choose Use automatic.");
        if(Confirm==null || !Confirm.Valid(size,true) || Opening!=null && !Opening.Valid(size,false))
            throw new InvalidOperationException("Revival setup has invalid recognition areas or click positions. Configure revival again.");
    }
    bool SizeMatches(Bitmap image)=>image.Width==Width && image.Height==Height;
    public VisualControl? Find(Bitmap image,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return SizeMatches(image) && Confirm.Matches(image,true,token)
            ?new(Confirm.Point,1,Confirm.Button!.Bounds,Confirm.Marker.Bounds,Custom:true):null;
    }
    public bool CanOpen(Bitmap image,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return SizeMatches(image) && Opening!=null && Opening.Matches(image,false,token) && !Confirm.Matches(image,false,token);
    }
    public bool CanConfirm(Bitmap image,VisualControl control,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        // Hover can change the button's fill. The independent dialog text,
        // selected position, and window size still have to match immediately.
        return SizeMatches(image) && control.Custom && control.Point==Confirm.Point && control.Button==Confirm.Button!.Bounds &&
            control.Marker==Confirm.Marker.Bounds && Confirm.Matches(image,false,token);
    }
    public void Save(string? path=null)
    {
        Validate(ClientHash,new(Width,Height));string file=path??PathName;
        File.WriteAllText(file+".tmp",JsonSerializer.Serialize(this));File.Move(file+".tmp",file,true);
    }
    public static RevivalProfile? Load(string hash,Size size,string? path=null)
    {
        string file=path??PathName;if(!File.Exists(file))return null;
        if(new FileInfo(file).Length>2_000_000)throw new InvalidOperationException("Revival setup is too large. Configure revival again or choose Use automatic.");
        RevivalProfile? profile;
        try{profile=JsonSerializer.Deserialize<RevivalProfile>(File.ReadAllText(file));}
        catch(JsonException){throw new InvalidOperationException("Revival setup could not be read. Configure revival again or choose Use automatic.");}
        if(profile==null)throw new InvalidOperationException("Revival setup is empty. Configure revival again or choose Use automatic.");
        profile.Validate(hash,size);return profile;
    }
    public static string? UseAutomatic(string? path=null)
    {
        string file=path??PathName;if(!File.Exists(file))return null;
        string backup=file+"."+DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff")+"-"+Guid.NewGuid().ToString("N")+".bak";
        File.Move(file,backup);return backup;
    }
}
