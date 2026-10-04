using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace PoteHunter;

// Only translate a saved inventory when its title AND hammer agree on one
// offset. No scaling, profile mutation, or relocation of the Yes dialog.
internal static class RepairInventoryLocation
{
    internal static RepairPatch Shift(RepairPatch patch,Point offset)=>patch with{X=patch.X+offset.X,Y=patch.Y+offset.Y};
    internal static Point? Find(Bitmap image,RepairProfile profile,CancellationToken token)
    {
        var title=profile.Inventory;
        var ink=new List<Point>();
        static bool White(int r,int g,int b)=>Math.Min(r,Math.Min(g,b))>=190 && Math.Max(r,Math.Max(g,b))-Math.Min(r,Math.Min(g,b))<=30;
        for(int y=0;y<title.Height;y++)for(int x=0;x<title.Width;x++)
        {
            int i=(y*title.Width+x)*3;
            if(White(title.Rgb[i],title.Rgb[i+1],title.Rgb[i+2]))ink.Add(new(x,y));
        }
        if(ink.Count<20)return null;
        var anchors=Enumerable.Range(0,8).Select(i=>ink[i*(ink.Count-1)/7]).ToArray();
        using var copy=new Bitmap(image.Width,image.Height,PixelFormat.Format32bppArgb);
        using(var g=Graphics.FromImage(copy))g.DrawImageUnscaled(image,0,0);
        var data=copy.LockBits(new(Point.Empty,copy.Size),ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
        byte[] bytes=new byte[data.Stride*data.Height];int stride=data.Stride;
        try{Marshal.Copy(data.Scan0,bytes,0,bytes.Length);}finally{copy.UnlockBits(data);}
        int left=Math.Max(0,title.X-384),right=Math.Min(image.Width-title.Width,title.X+384);
        int top=Math.Max(0,title.Y-256),bottom=Math.Min(image.Height-title.Height,title.Y+256);
        Point? found=null;
        for(int y=top;y<=bottom;y++)
        {
            token.ThrowIfCancellationRequested();
            for(int x=left;x<=right;x++)
            {
                bool possible=true;
                foreach(var p in anchors)
                {
                    int i=(y+p.Y)*stride+(x+p.X)*4;
                    if(!White(bytes[i+2],bytes[i+1],bytes[i])){possible=false;break;}
                }
                if(!possible)continue;
                var offset=new Point(x-title.X,y-title.Y);
                if(!Shift(title,offset).MatchesText(image) || !Shift(profile.Hammer,offset).InspectIcon(image).Matched)continue;
                if(found is {} previous && previous!=offset)return null; // ambiguous paired panels
                found=offset;
            }
        }
        return found;
    }
}
