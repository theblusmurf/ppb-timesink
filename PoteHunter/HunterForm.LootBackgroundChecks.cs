namespace PoteHunter;

public sealed partial class HunterForm
{
    void CheckLootBackground(LootTrackerSnapshot sample)
    {
        lootTrackerDesign.SelectedIndex=1;lootTrackerBackgroundOpacity.Value=65;CurrentOptions().Save();
        if(Options.Read().LootTrackerBackgroundOpacityPercent!=65)throw new Exception("Loot backdrop opacity did not persist.");
        working=true;
        try
        {
            lootTrackerBackgroundOpacity.Value=25;
            if(Options.Read().LootTrackerBackgroundOpacityPercent!=25)throw new Exception("Live backdrop edits were not saved.");
        }
        finally {working=false;}
        File.WriteAllText(Options.PathName,"{\"LootTrackerDesignVersion\":2,\"LootTrackerBackgroundOpacityPercent\":999}");
        if(Options.Read().LootTrackerBackgroundOpacityPercent!=100)throw new Exception("Backdrop maximum was not bounded.");
        File.WriteAllText(Options.PathName,"{\"LootTrackerDesignVersion\":2,\"LootTrackerBackgroundOpacityPercent\":-1}");
        if(Options.Read().LootTrackerBackgroundOpacityPercent!=0)throw new Exception("Backdrop minimum was not bounded.");
        File.WriteAllText(Options.PathName,"{\"LootTrackerDesignVersion\":2}");
        if(Options.Read().LootTrackerBackgroundOpacityPercent!=40)throw new Exception("Older settings did not get the default backdrop.");
        var renders=new List<object>();
        foreach(int design in new[]{1,3})foreach(int scale in new[]{50,100,200})
        {
            Bitmap Render(int opacity)=>design==1?RunicFoldRenderer.Render(sample,scale,opacity):RunicStripRenderer.Render(sample,scale,opacity);
            Size logical=design==1?RunicFoldRenderer.LogicalSize:RunicStripRenderer.LogicalSize;
            int x=(int)Math.Round(logical.Width/2d*scale/100),y=(int)Math.Round(50d*scale/100);
            using var clear=Render(0);
            if(clear.GetPixel(x,y).A!=0)throw new Exception("Backdrop alpha probe intersects foreground art.");
            foreach(int opacity in new[]{0,40,100})
            {
                using var bitmap=Render(opacity);
                int expected=(int)Math.Round(opacity*255d/100);
                if(bitmap.Size!=clear.Size || bitmap.GetPixel(x,y).A!=expected || bitmap.GetPixel(0,0).A!=0)
                    throw new Exception("Backdrop opacity, dimensions or rounded transparent margin failed.");
                int foreground=0;
                for(int row=0;row<clear.Height;row++)for(int col=0;col<clear.Width;col++)
                {
                    Color pixel=clear.GetPixel(col,row);
                    if(pixel.A!=255)continue;
                    foreground++;
                    Color composed=bitmap.GetPixel(col,row);
                    // GDI+ rounds premultiplied color channels at overlapping edges.
                    // Allow two channel levels; real whole-overlay fading fails this.
                    if(composed.A!=255 || Math.Abs(composed.R-pixel.R)>2 || Math.Abs(composed.G-pixel.G)>2 || Math.Abs(composed.B-pixel.B)>2)
                        throw new Exception($"Backdrop foreground mismatch: design={design}, scale={scale}, opacity={opacity}, x={col}, y={row}, clear={pixel}, withBackground={composed}.");
                }
                if(foreground<(scale==50?10:100))throw new Exception($"Insufficient foreground pixels checked: design={design}, scale={scale}, pixels={foreground}.");
                if(scale==100)bitmap.Save(Path.Combine(AppContext.BaseDirectory,$"loot-background-design-{design}-{opacity}.png"));
                renders.Add(new{Design=design,Scale=scale,Opacity=opacity,BackgroundAlpha=expected,PreservedForegroundPixels=foreground,ColorQuantizationTolerance=2});
            }
        }
        using(var gallery=new Bitmap(760,930))
        using(var graphics=Graphics.FromImage(gallery))
        using(var heading=new Font("Segoe UI",12))
        {
            graphics.Clear(Color.FromArgb(48,62,44));
            using var tile=new SolidBrush(Color.FromArgb(76,88,60));
            for(int y=0;y<gallery.Height;y+=24)for(int x=0;x<gallery.Width;x+=24)
                if((x/24+y/24)%2==0)graphics.FillRectangle(tile,x,y,24,24);
            int row=0;
            foreach(int opacity in new[]{0,40,100})
            {
                graphics.DrawString($"Background opacity {opacity}%",heading,Brushes.Ivory,new PointF(12,row*310+6));
                using var preview=RunicFoldRenderer.Render(sample,100,opacity);
                graphics.DrawImageUnscaled(preview,10,row*310+28);row++;
            }
            gallery.Save(Path.Combine(AppContext.BaseDirectory,"loot-background-preview.png"));
        }
        using var native=new LootTrackerOverlay(()=>sample){Location=new Point(-20000,-20000)};
        _=native.Handle;native.FitToArea(Screen.FromPoint(native.Location).WorkingArea.Size);
        IntPtr foregroundWindow=GetForegroundWindow();
        foreach(int design in new[]{1,3,2,1})
        {
            native.SetDesign(design);
            foreach(int opacity in new[]{0,40,100})
            {
                native.SetBackgroundOpacity(opacity);
                if(native.IsTransparentDesign)native.PresentTransparentOverlay();
                if(native.BackgroundOpacityPercent!=opacity || !native.HasNonActivatingStyles)throw new Exception("Native backdrop changes lost settings or passive styles.");
            }
        }
        native.SetBackgroundOpacity(-1);if(native.BackgroundOpacityPercent!=0)throw new Exception("Native minimum backdrop failed.");
        native.SetBackgroundOpacity(999);if(native.BackgroundOpacityPercent!=100)throw new Exception("Native maximum backdrop failed.");
        if(foregroundWindow!=GetForegroundWindow() || native.LayeredPresentationCount<9)throw new Exception("Backdrop native presentation failed or changed focus.");
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"loot-background-ui-checks.json"),System.Text.Json.JsonSerializer.Serialize(new
        {
            Passed=true,NativePresentations=native.LayeredPresentationCount,Renders=renders,
            Checks=new[]{"0/40/100 percent backdrop alpha","foreground alpha preserved and colors within two-level GDI rounding","transparent rounded margins","50/100/200 percent scale","older settings default and persisted bounds","live edits saved","native design switching and focus preserved"}
        },new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));
        lootTrackerDesign.SelectedIndex=1;lootTrackerBackgroundOpacity.Value=40;CurrentOptions().Save();
    }
}
