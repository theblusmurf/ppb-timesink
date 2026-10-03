namespace PoteHunter;

public sealed partial class HunterForm
{
    void CheckRunicFoldOverlay(LootTrackerSnapshot sample)
    {
        // Upgrade an existing size/position, then ensure a later user choice wins.
        File.WriteAllText(Options.PathName,"{\"LootTrackerDesign\":3,\"LootTrackerDesignVersion\":1,\"LootTrackerScalePercent\":135,\"LootTrackerOverlayX\":37,\"LootTrackerOverlayY\":48}");
        var upgraded=Options.Read();
        if(upgraded.LootTrackerDesign!=1 || upgraded.LootTrackerDesignVersion!=2 || upgraded.LootTrackerScalePercent!=135 || upgraded.LootTrackerOverlayX!=37 || upgraded.LootTrackerOverlayY!=48)
            throw new Exception("Runic Fold upgrade lost saved position/size or did not select the requested design.");
        upgraded.LootTrackerDesign=3;upgraded.Save();
        if(Options.Read().LootTrackerDesign!=3)throw new Exception("Runic Fold migration overwrote a subsequent selection.");
        lootTrackerDesign.SelectedIndex=1;lootTrackerScale.Value=135;CurrentOptions().Save();
        if(Options.Read().LootTrackerDesign!=1 || Options.Read().LootTrackerScalePercent!=135)
            throw new Exception("Runic Fold choice and size did not persist.");
        var renders=new List<object>();
        foreach(int percent in new[]{50,100,135,200})
        {
            using var bitmap=RunicFoldRenderer.Render(sample,percent);
            if(bitmap.Size!=RunicFoldRenderer.SizeAt(percent))throw new Exception("Runic Fold size failed to scale.");
            int transparent=0,partial=0;
            for(int y=0;y<bitmap.Height;y++)for(int x=0;x<bitmap.Width;x++)
            {int alpha=bitmap.GetPixel(x,y).A;if(alpha==0)transparent++;if(alpha>0 && alpha<255)partial++;}
            if(transparent<bitmap.Width*bitmap.Height*.65 || partial<100 || bitmap.GetPixel(0,0).A!=0)
                throw new Exception("Runic Fold has an opaque background or lost antialiasing.");
            bitmap.Save(Path.Combine(AppContext.BaseDirectory,$"runic-fold-{percent}.png"));
            renders.Add(new{Percent=percent,bitmap.Width,bitmap.Height,TransparentPixels=transparent,PartialAlphaPixels=partial});
        }
        using(var large=RunicFoldRenderer.Render(sample with{Elapsed=TimeSpan.FromHours(1000),RateElapsed=TimeSpan.FromHours(999),TrackedLoot=sample.TrackedLoot.Select(item=>item with{Count=long.MaxValue}).ToArray()},100))
            large.Save(Path.Combine(AppContext.BaseDirectory,"runic-fold-large-amounts.png"));
        using(var empty=RunicFoldRenderer.Render(sample with{TrackedLoot=[],HourlyLoot=[],Sources=[],Elapsed=TimeSpan.Zero,RateElapsed=TimeSpan.Zero},100))
            empty.Save(Path.Combine(AppContext.BaseDirectory,"runic-fold-empty.png"));
        using var native=new LootTrackerOverlay(()=>sample){Location=new Point(-20000,-20000)};
        _=native.Handle;IntPtr foreground=GetForegroundWindow();
        for(int cycle=0;cycle<4;cycle++)
        {
            native.SetDesign(1);native.SetScale(cycle%2==0?50:200);native.PresentTransparentOverlay();
            if(!native.HasNonActivatingStyles || native.Size!=RunicFoldRenderer.SizeAt(native.EffectiveScalePercent))
                throw new Exception("Runic Fold lost passive styles or native scaled dimensions.");
            native.SetDesign(cycle%2==0?2:3);
        }
        native.SetDesign(1);native.SetScale(200);native.FitToArea(new Size(800,400));native.PresentTransparentOverlay();
        if(native.Width>800 || native.Height>400 || native.ScalePercent!=200 || native.EffectiveScalePercent>=200)
            throw new Exception("Runic Fold screen fitting cropped the frame or changed the saved scale.");
        native.SetDesign(3);native.PresentTransparentOverlay();
        if(native.Width>800 || native.Height>400 || native.LayeredPresentationCount<6 || foreground!=GetForegroundWindow())
            throw new Exception("Transparent design switching failed to refit or stole focus.");
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"runic-fold-ui-checks.json"),System.Text.Json.JsonSerializer.Serialize(new
        {
            Passed=true,NativePresentations=native.LayeredPresentationCount,Renders=renders,
            Checks=new[]{"transparent pixels and partial-alpha frame/text","50/100/135/200 percent dimensions","full large counts and long sessions","empty snapshot","saved size and position migration","subsequent design preserved","native composition across opaque and transparent designs","foreground preserved","screen fit including style changes"}
        },new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));
        lootTrackerDesign.SelectedIndex=1;lootTrackerScale.Value=100;
    }
}
