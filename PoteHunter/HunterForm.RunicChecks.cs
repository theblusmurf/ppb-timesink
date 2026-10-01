using System.Runtime.InteropServices;

namespace PoteHunter;

public sealed partial class HunterForm
{
    void CheckRunicStripOverlay(LootTrackerSnapshot sample)
    {
        lootTrackerDesign.SelectedIndex=3;lootTrackerScale.Value=135;CurrentOptions().Save();
        var saved=Options.Read();
        if(saved.LootTrackerDesign!=3 || saved.LootTrackerScalePercent!=135 || saved.LootTrackerDesignVersion!=1)
            throw new Exception("Runic Strip design/size did not persist.");
        lootTrackerDesign.SelectedIndex=1;CurrentOptions().Save();
        if(Options.Read().LootTrackerDesign!=1)throw new Exception("Upgrade migration overwrote a subsequent design choice.");
        File.WriteAllText(Options.PathName,"{\"LootTrackerDesign\":0,\"LootTrackerScalePercent\":999,\"LootTrackerOverlayX\":37,\"LootTrackerOverlayY\":48}");
        var migrated=Options.Read();
        if(migrated.LootTrackerDesign!=3 || migrated.LootTrackerScalePercent!=200 || migrated.LootTrackerOverlayX!=37 || migrated.LootTrackerOverlayY!=48)
            throw new Exception("Runic Strip migration lost overlay position or size bounds.");
        migrated.LootTrackerDesign=2;migrated.Save();
        if(Options.Read().LootTrackerDesign!=2)throw new Exception("Migration did not preserve the next chosen design.");
        lootTrackerDesign.SelectedIndex=3;lootTrackerScale.Value=100;CurrentOptions().Save();
        working=true;
        try
        {
            lootTrackerScale.Value=150;
            if(Options.Read().LootTrackerScalePercent!=150)throw new Exception("Size changed during hunting was not saved.");
        }
        finally {working=false;}
        lootTrackerScale.Value=100;

        var renders=new List<object>();
        foreach(int percent in new[]{50,100,135,200})
        {
            using var bitmap=RunicStripRenderer.Render(sample,percent);
            if(bitmap.Size!=RunicStripRenderer.SizeAt(percent))throw new Exception("Runic Strip dimensions did not scale together.");
            int transparent=0,visible=0,partial=0;
            for(int y=0;y<bitmap.Height;y++)for(int x=0;x<bitmap.Width;x++)
            {
                int alpha=bitmap.GetPixel(x,y).A;
                if(alpha==0)transparent++;else visible++;
                if(alpha>0 && alpha<255)partial++;
            }
            if(transparent<bitmap.Width*bitmap.Height*.65 || visible<100 || partial<100 || bitmap.GetPixel(0,0).A!=0 || bitmap.GetPixel(bitmap.Width/2,bitmap.Height-1).A!=0)
                throw new Exception("Runic Strip has a background or lacks antialiased transparent text.");
            bitmap.Save(Path.Combine(AppContext.BaseDirectory,$"runic-strip-{percent}.png"));
            renders.Add(new{Percent=percent,bitmap.Width,bitmap.Height,TransparentPixels=transparent,VisiblePixels=visible,PartialAlphaPixels=partial});
        }
        using(var large=RunicStripRenderer.Render(sample with{TrackedLoot=sample.TrackedLoot.Select(item=>item with{Count=long.MaxValue}).ToArray()},100))
            large.Save(Path.Combine(AppContext.BaseDirectory,"runic-strip-large-amounts.png"));

        using var native=new LootTrackerOverlay(()=>sample){Location=new Point(-20000,-20000)};
        _=native.Handle;
        IntPtr foreground=GetForegroundWindow();
        for(int cycle=0;cycle<4;cycle++)
        {
            native.SetDesign(3);native.SetScale(cycle%2==0?50:200);native.PresentRunicStrip();
            if(!native.HasNonActivatingStyles || native.Size!=RunicStripRenderer.SizeAt(native.ScalePercent))
                throw new Exception("Runic Strip lost passive styles or scaled size.");
            native.SetDesign(cycle%3);
        }
        if(native.LayeredPresentationCount<4 || foreground!=GetForegroundWindow())
            throw new Exception("Native transparent composition failed or activated another window.");
        native.SetDesign(3);native.SetScale(-1);if(native.ScalePercent!=50)throw new Exception("Minimum Runic Strip scale was not bounded.");
        native.SetScale(999);if(native.ScalePercent!=200)throw new Exception("Maximum Runic Strip scale was not bounded.");
        native.FitToArea(new Size(800,400));
        if(native.Width>800 || native.Height>400 || native.ScalePercent!=200 || native.EffectiveScalePercent>=200)
            throw new Exception("Screen fitting cropped the strip or discarded the requested size.");
        native.PresentRunicStrip();
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"runic-strip-ui-checks.json"),System.Text.Json.JsonSerializer.Serialize(new
        {
            Passed=true,NativePresentations=native.LayeredPresentationCount,Renders=renders,
            Checks=new[]{"transparent background with partial-alpha text","50/100/135/200 percent rendering","full large amounts","saved design and size including during hunting","one-time upgrade selection with position retained","subsequent design preserved","native layered composition","opaque/transparent switching","nonactivating styles and foreground preserved","scale bounds and smaller-screen fit"}
        },new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));
        lootTrackerDesign.SelectedIndex=3;lootTrackerScale.Value=100;
    }

    [DllImport("user32.dll")]static extern IntPtr GetForegroundWindow();
}
