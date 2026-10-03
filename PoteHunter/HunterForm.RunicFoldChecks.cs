namespace PoteHunter;

public sealed partial class HunterForm
{
    void CheckRunicFoldOverlay(LootTrackerSnapshot sample)
    {
        // Verify the approved files survived embedding and single-file packaging.
        var expectedIcons=new (string Name,int Width,int Height,string Hash)[]{
            ("Silvin",23,18,"45c6a2e57111112c2a0f139fddd4438b0485eec7c04f0adc088e92b54d7322d5"),
            ("Mithril",20,20,"ff51979ed5cd920bbf6b60568a924b6ebef754b364c54e6ace4d51673baa2a23"),
            ("Iternium",21,18,"5e3daced862ca9f5611339eebb62daca60db5c6c2e3e8dcc83314379f322eaab"),
            ("Fehu",25,25,"d3f7547701e06562f2774af3612bd0d7481fb81cf05c0ced55f5223dc8ba2e81"),
            ("Diamond",19,17,"7cb40a9da6670a5ccd658fab7064c4b9b4319459e8b1f0eca99868e091fa1910")};
        foreach(var icon in expectedIcons)
        {
            using var stream=typeof(GameLootIcons).Assembly.GetManifestResourceStream($"PoteHunter.LootIcons.{icon.Name}.png")
                ?? throw new Exception($"Bundled loot image missing: {icon.Name}.");
            string hash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream)).ToLowerInvariant();
            stream.Position=0;using var image=Image.FromStream(stream);
            if(hash!=icon.Hash || image.Width!=icon.Width || image.Height!=icon.Height)
                throw new Exception($"Bundled loot image differs from the approved asset: {icon.Name}.");
        }
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
            int transparent=0,partial=0,passivePixels=0;
            for(int y=0;y<bitmap.Height;y++)for(int x=0;x<bitmap.Width;x++)
            {
                var logical=new PointF(x*100f/percent,y*100f/percent);
                if(RectangleF.Inflate(LootOverlayResetButtons.LootBounds(1,RunicFoldRenderer.LogicalSize),2,2).Contains(logical) ||
                   RectangleF.Inflate(LootOverlayResetButtons.TimerBounds(1,RunicFoldRenderer.LogicalSize),2,2).Contains(logical))continue;
                passivePixels++;int alpha=bitmap.GetPixel(x,y).A;if(alpha==0)transparent++;if(alpha>0 && alpha<255)partial++;
            }
            if(transparent<passivePixels*.65 || partial<100 || bitmap.GetPixel(0,0).A!=0)
                throw new Exception("Runic Fold has an opaque background or lost antialiasing.");
            bitmap.Save(Path.Combine(AppContext.BaseDirectory,$"runic-fold-{percent}.png"));
            renders.Add(new{Percent=percent,bitmap.Width,bitmap.Height,TransparentPixels=transparent,PartialAlphaPixels=partial});
        }
        using(var large=RunicFoldRenderer.Render(sample with{Elapsed=TimeSpan.FromHours(1000),RateElapsed=TimeSpan.FromHours(999),TrackedLoot=sample.TrackedLoot.Select(item=>item with{Count=long.MaxValue}).ToArray()},100))
            large.Save(Path.Combine(AppContext.BaseDirectory,"runic-fold-large-amounts.png"));
        using(var empty=RunicFoldRenderer.Render(sample with{TrackedLoot=[],HourlyLoot=[],Sources=[],Elapsed=TimeSpan.Zero,RateElapsed=TimeSpan.Zero},100))
            empty.Save(Path.Combine(AppContext.BaseDirectory,"runic-fold-empty.png"));
        using var native=new LootTrackerOverlay(()=>sample){Location=new Point(-20000,-20000)};
        _=native.Handle;
        native.FitToArea(Screen.FromPoint(native.Location).WorkingArea.Size);
        IntPtr foreground=GetForegroundWindow();
        for(int cycle=0;cycle<4;cycle++)
        {
            native.SetDesign(1);native.SetScale(cycle%2==0?50:200);native.PresentTransparentOverlay();
            if(!native.HasNonActivatingStyles || native.Size!=RunicFoldRenderer.SizeAt(native.EffectiveScalePercent))
                throw new Exception($"Runic Fold lost passive styles or native scaled dimensions: passive={native.HasNonActivatingStyles}, actual={native.Size}, expected={RunicFoldRenderer.SizeAt(native.EffectiveScalePercent)}, requested={native.ScalePercent}, fitted={native.EffectiveScalePercent}, DPI={native.DeviceDpi}.");
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
            Passed=true,EmbeddedIcons=expectedIcons.Select(icon=>new{icon.Name,icon.Width,icon.Height,icon.Hash}),NativePresentations=native.LayeredPresentationCount,Renders=renders,
            Checks=new[]{"transparent pixels and partial-alpha frame/text","50/100/135/200 percent dimensions","full large counts and long sessions","empty snapshot","saved size and position migration","subsequent design preserved","native composition across opaque and transparent designs","foreground preserved","screen fit including style changes"}
        },new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));
        lootTrackerDesign.SelectedIndex=1;lootTrackerScale.Value=100;
    }
}
