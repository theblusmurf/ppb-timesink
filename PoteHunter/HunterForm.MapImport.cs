using System.Drawing;
using System.Drawing.Imaging;
using System.Text.Json;

namespace PoteHunter;

public sealed partial class HunterForm
{
    async Task ImportMapToolWorld()
    {
        if(working||busy||clientRecoveryRunning||navigation.Recording)return;
        if(!connected||!world.ConnectionVerified)
        {
            message="Connect to the game in the zone you want to map before importing calibrated geometry.";
            return;
        }
        using var dialog=new OpenFileDialog
        {
            Filter="MapTool calibrated world (*.json)|*.json|JSON files (*.json)|*.json|All files (*.*)|*.*",
            CheckFileExists=true,
            Multiselect=false,
            Title="Select a calibrated MapTool WorldGeometry file"
        };
        if(dialog.ShowDialog(this)!=DialogResult.OK)return;

        int zone=world.ActiveZone();
        string clientHash=world.ClientHash;
        var player=world.LocalPlayer();Vec playerPosition=player.Position;
        MapToolWorldRenderer.RasterResult raster;
        busy=true;importMapToolWorld.Enabled=false;
        try
        {
            message="Rendering calibrated zone terrain preview…";
            raster=await Task.Run(()=>MapToolWorldRenderer.Render(dialog.FileName,clientHash,zone));
        }
        catch(Exception ex)when(ex is not OutOfMemoryException and not AccessViolationException)
        {
            message="Map import rejected: "+ex.Message;
            TraceLog.Record("MapTool world import rejected",new{Path=dialog.FileName,Zone=zone,ClientSha256=clientHash,Error=ex.Message});
            return;
        }
        finally{busy=false;importMapToolWorld.Enabled=true;}

        using(raster)
        using(var preview=CreateMapAlignmentPreview(raster,zone,playerPosition,out bool markerInside,out CheckBox confirmed,out Button accept))
        {
            accept.Enabled=markerInside&&confirmed.Checked;
            confirmed.CheckedChanged+=(_,_)=>accept.Enabled=markerInside&&confirmed.Checked;
            if(preview.ShowDialog(this)!=DialogResult.OK)return;
            try
            {
                if(working||busy||clientRecoveryRunning||navigation.Recording||!connected||!world.ConnectionVerified||world.ActiveZone()!=zone||world.ClientHash!=clientHash||world.LocalPlayer().Id!=player.Id)throw new InvalidOperationException("The connected character, zone or client changed during review. Import again in the correct zone.");
                SaveMapToolRaster(zone,raster);
                zoneMapBackground.Reload();
                importedCollisionKey="";RefreshImportedCollisionObstacles();
                navigationCanvas.Invalidate();navigationOverlay?.Invalidate();
                message=$"Imported Zone {zone} map and {raster.CollisionObstacles.Count:N0} collision blockers for route planning.";
                TraceLog.Record("MapTool zone map imported",new
                {
                    Zone=zone,
                    SourceZone=raster.SourceZoneId,
                    ClientSha256=raster.ClientSha256,
                    GeometrySha256=raster.SourceFileSha256,
                    Bounds=new{raster.MinX,raster.MinY,raster.MaxX,raster.MaxY},
                    raster.TriangleCount,
                    raster.StaticObjectCount,
                    CollisionBlockerCount=raster.CollisionObstacles.Count,
                    VisualAlignmentConfirmed=true,
                    Output=Path.Combine(zoneMapBackground.Root,$"{zone}.json")
                });
            }
            catch(Exception ex)when(ex is not OutOfMemoryException and not AccessViolationException)
            {
                message="Could not save the zone map: "+ex.Message;
                TraceLog.Record("MapTool zone map save failed",new{Zone=zone,Error=ex.Message});
            }
        }
    }

    static Form CreateMapAlignmentPreview(MapToolWorldRenderer.RasterResult raster,int zone,Vec player,
        out bool markerInside,out CheckBox confirmation,out Button accept)
    {
        double spanX=raster.MaxX-raster.MinX,spanY=raster.MaxY-raster.MinY;
        bool isInside=player.Finite&&player.X>=raster.MinX&&player.X<=raster.MaxX&&player.Y>=raster.MinY&&player.Y<=raster.MaxY;
        markerInside=isInside;
        var preview=new Form
        {
            Text=$"Review Zone {zone} map alignment",
            StartPosition=FormStartPosition.CenterParent,
            MinimumSize=new Size(640,520),
            Size=new Size(980,780),
            BackColor=Color.FromArgb(18,24,32),
            ForeColor=Color.Gainsboro,
            ShowInTaskbar=false
        };
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,RowCount=3,ColumnCount=1,Padding=new Padding(10)};
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var instructions=new Label
        {
            AutoSize=true,
            MaximumSize=new Size(900,0),
            ForeColor=isInside?Color.FromArgb(190,220,232):Color.Salmon,
            Text=isInside
                ?$"MapTool source {raster.SourceZoneId} · client hash verified · {raster.TriangleCount:N0} terrain triangles, {raster.StaticObjectCount:N0} visible object footprints, and {raster.CollisionObstacles.Count:N0} decoded collision blockers for route planning. Red dot is your live character at ({player.X:F1}, {player.Y:F1}). Compare it with the same location in-game before accepting. Coordinate convention: {raster.CoordinateConvention}"
                :$"The live character at ({player.X:F1}, {player.Y:F1}) is outside this map's calibrated bounds; alignment cannot be confirmed. Map X: {raster.MinX:F1}–{raster.MaxX:F1}; map Y: {raster.MinY:F1}–{raster.MaxY:F1}."
        };
        var picture=new PictureBox{Dock=DockStyle.Fill,BackColor=Color.FromArgb(10,16,22),SizeMode=PictureBoxSizeMode.Zoom,Image=raster.Image};
        picture.Paint+=(_,e)=>
        {
            if(!isInside||picture.ClientSize.Width<=0||picture.ClientSize.Height<=0)return;
            double imageScale=Math.Min(picture.ClientSize.Width/(double)raster.Image.Width,picture.ClientSize.Height/(double)raster.Image.Height);
            double imageWidth=raster.Image.Width*imageScale,imageHeight=raster.Image.Height*imageScale;
            double offsetX=(picture.ClientSize.Width-imageWidth)/2,offsetY=(picture.ClientSize.Height-imageHeight)/2;
            double mapScale=Math.Min((imageWidth-2)/spanX,(imageHeight-2)/spanY);
            double drawnWidth=spanX*mapScale,drawnHeight=spanY*mapScale;
            double mapOffsetX=offsetX+(imageWidth-drawnWidth)/2,mapOffsetY=offsetY+(imageHeight-drawnHeight)/2;
            float x=(float)(mapOffsetX+(player.X-raster.MinX)*mapScale);
            float y=(float)(mapOffsetY+(raster.MaxY-player.Y)*mapScale);
            using var halo=new SolidBrush(Color.FromArgb(190,255,255,255));
            using var marker=new SolidBrush(Color.Red);
            using var outline=new Pen(Color.White,2);
            e.Graphics.FillEllipse(halo,x-9,y-9,18,18);
            e.Graphics.FillEllipse(marker,x-5,y-5,10,10);
            e.Graphics.DrawEllipse(outline,x-6,y-6,12,12);
        };
        var confirmationControl=new CheckBox
        {
            Name="confirmMapAlignment",
            AutoSize=true,
            Enabled=markerInside,
            Text="I compared the red marker with this in-game zone and confirm the map is aligned.",
            Margin=new Padding(4,8,4,8)
        };
        confirmation=confirmationControl;
        accept=new Button{Text="Use map and collision blockers",Name="useMapToolMap",AutoSize=true,Enabled=false};
        var cancel=new Button{Text="Cancel",DialogResult=DialogResult.Cancel,AutoSize=true};
        var buttons=new FlowLayoutPanel{Dock=DockStyle.Fill,AutoSize=true,WrapContents=true};
        buttons.Controls.AddRange([confirmationControl,accept,cancel]);
        accept.Click+=(_,_)=>{if(confirmationControl.Checked)preview.DialogResult=DialogResult.OK;};
        if(raster.Warnings.Count>0)instructions.Text+="\n"+string.Join("\n",raster.Warnings);
        layout.Controls.Add(instructions,0,0);layout.Controls.Add(picture,0,1);layout.Controls.Add(buttons,0,2);
        preview.Controls.Add(layout);preview.CancelButton=cancel;
        return preview;
    }

    void SaveMapToolRaster(int zone,MapToolWorldRenderer.RasterResult raster)
    {
        Directory.CreateDirectory(zoneMapBackground.Root);
        string imageName=$"zone-{zone}-{raster.SourceFileSha256[..12]}.png";
        string imagePath=Path.GetFullPath(Path.Combine(zoneMapBackground.Root,imageName));
        string manifestPath=Path.GetFullPath(Path.Combine(zoneMapBackground.Root,$"{zone}.json"));
        string root=Path.GetFullPath(zoneMapBackground.Root).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
        if(!imagePath.StartsWith(root,StringComparison.OrdinalIgnoreCase)||!manifestPath.StartsWith(root,StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Map output escaped the per-user maps folder.");
        if(!File.Exists(imagePath))
        {
            string imageTemporary=imagePath+".tmp";
            raster.Image.Save(imageTemporary,ImageFormat.Png);
            File.Move(imageTemporary,imagePath,false);
        }
        if(File.Exists(manifestPath))
        {
            string backup=manifestPath+".backup-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff");
            File.Copy(manifestPath,backup,false);
        }
        var manifest=new
        {
            Zone=zone,
            Image=imageName,
            MinX=raster.MinX,
            MinY=raster.MinY,
            MaxX=raster.MaxX,
            MaxY=raster.MaxY,
            Source="MapTool WorldGeometry",
            SourceZoneId=raster.SourceZoneId,
            ClientSha256=raster.ClientSha256,
            WorldGeometrySha256=raster.SourceFileSha256,
            CoordinateConvention="PoteHunter X/Y <- calibrated MapTool X/Z; image top is MaxY",
            VisualAlignmentConfirmed=true,
            VisualAlignmentConfirmedUtc=DateTime.UtcNow,
            CollisionObstacles=raster.CollisionObstacles
        };
        string manifestTemporary=manifestPath+".tmp";
        File.WriteAllText(manifestTemporary,JsonSerializer.Serialize(manifest,new JsonSerializerOptions{WriteIndented=true}));
        File.Move(manifestTemporary,manifestPath,true);
    }
}
