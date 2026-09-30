using System.Collections.Concurrent;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace PoteHunter;

internal sealed record VisualControl(Point Point,double Scale,Rectangle Button,Rectangle Marker,bool Custom=false);
internal sealed record RepairVisuals(VisualControl? Hammer,VisualControl? Confirm);

// Adapted from Domitus's paired inventory/dialog recognizer. Templates are
// cached, searches are cancellable, and no world/input calls run on the worker.
internal static class RecoveryVision
{
    internal sealed class Pixels
    {
        public readonly int Width,Height;
        readonly byte[] gray;
        public Pixels(Bitmap image,CancellationToken token)
        {
            Width=image.Width;Height=image.Height;gray=new byte[Width*Height];
            using var copy=new Bitmap(Width,Height,PixelFormat.Format32bppArgb);
            using(var g=Graphics.FromImage(copy))g.DrawImageUnscaled(image,0,0);
            var data=copy.LockBits(new(0,0,Width,Height),ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
            try
            {
                var bytes=new byte[data.Stride*Height];Marshal.Copy(data.Scan0,bytes,0,bytes.Length);
                for(int y=0;y<Height;y++)
                {
                    if(y%32==0)token.ThrowIfCancellationRequested();
                    for(int x=0;x<Width;x++){int i=y*data.Stride+x*4;gray[y*Width+x]=(byte)((bytes[i]+bytes[i+1]+bytes[i+2])/3);}
                }
            }
            finally{copy.UnlockBits(data);}
            var raw=(byte[])gray.Clone();
            for(int y=1;y<Height-1;y++)
            {
                if(y%32==0)token.ThrowIfCancellationRequested();
                for(int x=1;x<Width-1;x++)
                {
                    int i=y*Width+x;
                    gray[i]=(byte)((raw[i]*4+(raw[i-1]+raw[i+1]+raw[i-Width]+raw[i+Width])*2+
                        raw[i-Width-1]+raw[i-Width+1]+raw[i+Width-1]+raw[i+Width+1])/16);
                }
            }
        }
        public int At(int x,int y)=>gray[y*Width+x];
    }
    sealed class Template
    {
        public readonly Pixels Pixels;
        public readonly (int X,int Y,int Gray)[] Samples;
        public Template(string name,double scale)
        {
            using var original=Resource(name);
            using var resized=new Bitmap(original,new Size((int)Math.Round(original.Width*scale),(int)Math.Round(original.Height*scale)));
            Pixels=new(resized,default);
            var samples=new List<(int X,int Y,int Gray)>();
            for(int y=1;y<Pixels.Height-1;y+=2)for(int x=1;x<Pixels.Width-1;x+=2)samples.Add((x,y,Pixels.At(x,y)));
            Samples=samples.OrderByDescending(p=>Math.Abs(p.Gray-Pixels.At(p.X-1,p.Y))+Math.Abs(p.Gray-Pixels.At(p.X,p.Y-1))).ToArray();
        }
    }
    static readonly ConcurrentDictionary<(string,int),Template> templates=new();
    internal static Bitmap Resource(string name)
    {
        using var stream=typeof(RecoveryVision).Assembly.GetManifestResourceStream("PoteHunter."+name+".png")
            ?? throw new InvalidOperationException("Missing visual recovery template: "+name);
        using var loaded=new Bitmap(stream);return new Bitmap(loaded);
    }
    static Template Get(string name,double scale)=>templates.GetOrAdd((name,(int)Math.Round(scale*100)),key=>new(key.Item1,key.Item2/100d));
    static IEnumerable<double> Scales(Size size,double referenceHeight)=>new[]{size.Height/referenceHeight,1d,.75,1.25,1.5,2d}
        .Where(s=>s>=.6 && s<=2.5).Select(s=>Math.Round(s,2)).Distinct();

    static bool Fits(Pixels frame,Template template,int x,int y,bool text=false)
    {
        if(x<0 || y<0 || x+template.Pixels.Width>frame.Width || y+template.Pixels.Height>frame.Height)return false;
        if(text)
        {
            double a=0,b=0,aa=0,bb=0,ab=0,n=template.Samples.Length;
            foreach(var p in template.Samples){double f=frame.At(x+p.X,y+p.Y),t=p.Gray;a+=f;b+=t;aa+=f*f;bb+=t*t;ab+=f*t;}
            double variance=(aa-a*a/n)*(bb-b*b/n);
            return variance>1 && (ab-a*b/n)/Math.Sqrt(variance)>.72;
        }
        double sum=0,bright=0;int count=0,brightCount=0,low=255,high=0;
        foreach(var p in template.Samples)
        {
            int actual=frame.At(x+p.X,y+p.Y);low=Math.Min(low,actual);high=Math.Max(high,actual);
            int difference=Math.Abs(actual-p.Gray);sum+=difference;count++;
            if(p.Gray>160){bright+=difference;brightCount++;}
            if(count==4 && sum/count>55 || count==16 && sum/count>40)return false;
            if(count==16 && high-low<8)return false;
        }
        return sum/count<22 && (brightCount==0 || bright/brightCount<35) && Fits(frame,template,x,y,true);
    }
    static bool SameLocation(VisualControl first,VisualControl second)=>Math.Abs(first.Point.X-second.Point.X)<=20 && Math.Abs(first.Point.Y-second.Point.Y)<=20;
    static VisualControl? FindPair(Pixels frame,Size size,bool prompt,CancellationToken token)
    {
        string anchorName=prompt?"RepairButtons":"RepairHammer",markerName=prompt?"RepairQuestion":"RepairInventory";
        int dx=prompt?-155:-284,dy=prompt?-27:-586,cx=prompt?34:13,cy=prompt?16:24;
        foreach(double scale in Scales(size,1080))
        {
            var anchor=Get(anchorName,scale);var marker=Get(markerName,scale);VisualControl? found=null;double best=double.MaxValue;
            int sx=(int)Math.Round(dx*scale),sy=(int)Math.Round(dy*scale);
            // Both controls must fit; this also avoids scanning impossible rows.
            for(int y=Math.Max(0,-sy);y<=Math.Min(frame.Height-anchor.Pixels.Height,frame.Height-marker.Pixels.Height-sy);y++)
            {
                if(y%8==0)token.ThrowIfCancellationRequested();
                for(int x=Math.Max(0,-sx);x<=Math.Min(frame.Width-anchor.Pixels.Width,frame.Width-marker.Pixels.Width-sx);x++)
                {
                    if(!Fits(frame,anchor,x,y))continue;
                    Rectangle? verified=null;
                    for(int oy=-2;oy<=2 && verified==null;oy++)for(int ox=-2;ox<=2 && verified==null;ox++)
                        if(Fits(frame,marker,x+sx+ox,y+sy+oy,true))verified=new(x+sx+ox,y+sy+oy,marker.Pixels.Width,marker.Pixels.Height);
                    if(verified==null)continue;
                    var match=new VisualControl(new(x+(int)Math.Round(cx*scale),y+(int)Math.Round(cy*scale)),scale,
                        new(x,y,anchor.Pixels.Width,anchor.Pixels.Height),verified.Value);
                    if(found!=null && !SameLocation(found,match))return null;
                    double score=anchor.Samples.Average(p=>Math.Abs(frame.At(x+p.X,y+p.Y)-p.Gray));
                    if(score<best){found=match;best=score;}
                }
            }
            if(found!=null)return found;
        }
        return null;
    }
    internal static RepairVisuals Repair(Bitmap image,CancellationToken token)
    {
        var pixels=new Pixels(image,token);
        return new(FindPair(pixels,image.Size,false,token),FindPair(pixels,image.Size,true,token));
    }
    internal static bool RepairMarker(Bitmap image,VisualControl control,bool prompt,CancellationToken token)
    {
        var pixels=new Pixels(image,token);
        return Fits(pixels,Get(prompt?"RepairQuestion":"RepairInventory",control.Scale),control.Marker.X,control.Marker.Y,true) &&
            (prompt || FindPair(pixels,image.Size,true,token)==null);
    }

    internal static VisualControl? Revive(Bitmap image,CancellationToken token)
    {
        var frame=new Pixels(image,token);
        foreach(double scale in Scales(image.Size,1440))
        {
            var template=Get("ReviveButton",scale);VisualControl? found=null;double best=double.MaxValue;
            int width=template.Pixels.Width,height=template.Pixels.Height;
            int cx=image.Width/2-width/2,cy=image.Height/2;
            for(int y=Math.Max(0,cy-(int)(55*scale));y<Math.Min(image.Height-height,cy+(int)(70*scale));y++)
            {
                token.ThrowIfCancellationRequested();
                for(int x=Math.Max(0,cx-(int)(55*scale));x<Math.Min(image.Width-width,cx+(int)(55*scale));x++)
                {
                    if(!Fits(frame,template,x,y))continue;
                    double score=template.Samples.Average(p=>Math.Abs(frame.At(x+p.X,y+p.Y)-p.Gray));
                    if(score>=12)continue;
                    var match=new VisualControl(new(x+width/2,y+height/2),scale,new(x,y,width,height),new(x,y,width,height));
                    if(found!=null && !SameLocation(found,match))return null;
                    if(score<best){found=match;best=score;}
                }
            }
            if(found!=null)return found;
        }
        return null;
    }
    internal static bool ReviveStillPresent(Bitmap image,VisualControl match,CancellationToken token)
        =>Fits(new Pixels(image,token),Get("ReviveButton",match.Scale),match.Button.X,match.Button.Y);
}
