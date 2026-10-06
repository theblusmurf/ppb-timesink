using System.Text.Json;

namespace PoteHunter;

public sealed partial class HunterForm
{
    void CheckNavigation3DUi(TabControl tabs)
    {
        if(!offlinePreviewMode||connected||working)throw new InvalidOperationException("3D UI checks require a disconnected offline fixture.");
        var original=CurrentOptions();var originalJson=JsonSerializer.Serialize(original);
        tabs.SelectedTab=navigationPage;PerformLayout();Application.DoEvents();
        if(navigationCanvas.IsDisposed||savedNavigationSlot.IsDisposed||startNavigationRecording.IsDisposed||saveNavigationRoute.IsDisposed)
            throw new InvalidOperationException("Original route controls were disposed during navigation layout replacement.");
        foreach(var control in new Control[]{savedNavigationSlot,startNavigationRecording,saveNavigationRoute,useAlternativeHuntRoutes,
            assignUnassignedNavigationRoutes,clearSavedNavigationRoute,clearAllSavedNavigationRoutes,showNavigationOverlay,showRouteOverlay,
            automaticRouting,guideTreasureChests,resetLootTracker,resetLootTimer})
            if(!navigationPage.Contains(control)&&!tabs.TabPages.Cast<TabPage>().Single(p=>p.Name=="overlaysPage").Contains(control))
                throw new InvalidOperationException("Original navigation or overlay action was lost: "+control.Text);
        CheckSavedRoutePanelLayout();
        foreach(var section in navigationPage.Controls.Find("navigationLayerSidebar",true).Single().Controls.OfType<CollapsibleSection>())
        {
            section.Expanded=true;PerformLayout();Application.DoEvents();
            if(!section.Header.Visible||!section.Content.Visible||section.Width<240)throw new InvalidOperationException("Navigation section cannot expand: "+section.Name);
            section.Expanded=section.Name is "navigation3DLayers" or "navigationSavedRoutes";
        }
        // Settings from older releases default to the new presentation, without changing their route or combat choices.
        var legacy=JsonSerializer.Deserialize<Options>("{\"Target\":\"Mimic\",\"HuntRadius\":35,\"AutoReviveAfterDeath\":true}")!;
        if(!legacy.Navigation3D||legacy.Navigation3DMapOpacity!=80||legacy.Target!="Mimic"||!legacy.AutoReviveAfterDeath)
            throw new InvalidOperationException("Older settings do not migrate to the default 3D presentation.");
        legacy.Navigation3DMapOpacity=-1;if(legacy.Navigation3DMapOpacity!=0)throw new InvalidOperationException("Map opacity lower boundary.");
        legacy.Navigation3DMapOpacity=101;if(legacy.Navigation3DMapOpacity!=100)throw new InvalidOperationException("Map opacity upper boundary.");
        navigation3DUpdating=true;
        navigation3DPreferred=true;navigation3DFallback=false;navigation3DMap.Checked=true;navigationMapOpacity.Value=60;
        navigation3DTerrain.Checked=true;navigation3DObjects.Checked=false;navigation3DRoutes.Checked=false;navigation3DAnchors.Checked=true;
        navigation3DUpdating=false;ApplyNavigation3DLayers();
        var saved=WithNavigation3DSettings(Options.Read());saved.Save();var restored=Options.Read();
        if(!restored.Navigation3D||!restored.Navigation3DMap||restored.Navigation3DMapOpacity!=60||restored.Navigation3DObjects||restored.Navigation3DRoutes||!restored.Navigation3DAnchors)
            throw new InvalidOperationException("Navigation layers and transparency did not persist.");
        var normalized=WithNavigation3DSettings(original);
        if(JsonSerializer.Serialize(normalized)!=JsonSerializer.Serialize(CurrentOptions()))
            throw new InvalidOperationException("Presentation controls changed a combat, recovery, route or loot option.");
        // A native fixture is displayed only offscreen, without connecting, registering hotkeys or emitting input.
        navigation3DScene=Navigation3DRenderChecks.SyntheticScene(false);navigation3DView.SetScene(navigation3DScene);
        navigation3DView.SetRoutes([]);navigation3DView.SetMarkers([]);navigation3DView.FitMap();UpdateNavigation3DVisibility();
        PerformLayout();Application.DoEvents();
        if(!navigation3DView.Failed)
        {
            if(!navigation3DView.Visible||navigationCanvas.Visible||navigationPlayerButton.Enabled)
                throw new InvalidOperationException("3D fixture visibility or disconnected player availability is incorrect.");
            using var frame=navigation3DView.CaptureFrame();
            frame?.Save(Path.Combine(AppContext.BaseDirectory,"navigation3d-fixture.png"));
        }
        using(var preview=new Bitmap(Width,Height)){DrawToBitmap(preview,new Rectangle(Point.Empty,Size));preview.Save(Path.Combine(AppContext.BaseDirectory,"navigation3d-ui-preview.png"));}
        // Test the same mode selection handlers that users invoke; no route recording or gameplay takes place.
        navigation2DButton.PerformClick();Application.DoEvents();
        if(!navigationCanvas.Visible||navigation3DView.Visible||navigationMapChoice.Enabled||Navigation3DVisible)
            throw new InvalidOperationException("Original 2D view is inaccessible after switching modes.");
        navigation3DPreferred=true;navigation3DFallback=true;UpdateNavigation3DVisibility();
        if(!navigationCanvas.Visible||navigation3DView.Visible)throw new InvalidOperationException("Unavailable 3D did not fall back to 2D.");
        if(!navigation3DView.Failed&&!navigationMapChoice.Enabled)throw new InvalidOperationException("A missing scene prevents selecting another supported map.");
        CheckNavigationRouteHotkeys();CheckOverlay3DUi();CheckPlayerRecognitionUi();
        CheckSentinelRadarUi();
        var before=JsonSerializer.Deserialize<Options>(originalJson)!;
        navigation3DUpdating=true;navigation3DPreferred=before.Navigation3D;navigation3DMap.Checked=before.Navigation3DMap;
        navigation3DTerrain.Checked=before.Navigation3DTerrain;navigation3DObjects.Checked=before.Navigation3DObjects;
        navigation3DRoutes.Checked=before.Navigation3DRoutes;navigation3DAnchors.Checked=before.Navigation3DAnchors;navigationMapOpacity.Value=before.Navigation3DMapOpacity;
        navigation3DUpdating=false;before.Save();ApplyNavigation3DLayers();
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"navigation3d-ui-checks.json"),JsonSerializer.Serialize(new
        {Passed=true,HardwareInputEmitted=false,NativeRenderer=navigation3DView.Failed?"Unavailable; 2D fallback verified":"Available; offscreen frame captured",
            Checks=new[]{"old settings migration and opacity bounds","display-only settings persistence","existing controls preserved and sections expand","2D switch and unavailable 3D fallback","disconnected player hidden","existing Home/End recording guards","minimum-size native layout"}}));
    }

    void CheckSavedRoutePanelLayout()
    {
        if(!offlinePreviewMode||connected||working)
            throw new InvalidOperationException("Route panel checks require a disconnected offline fixture.");
        var side=(ScrollableControl)navigationPage.Controls.Find("navigationLayerSidebar",true).Single();
        var section=(CollapsibleSection)navigationPage.Controls.Find("navigationSavedRoutes",true).Single();
        var originalSize=Size;var originalScroll=side.AutoScrollPosition;
        var originalExpanded=section.Expanded;
        var selectorReference=savedNavigationSlot;var startReference=startNavigationRecording;
        var selectorParent=savedNavigationSlot.Parent;var startParent=startNavigationRecording.Parent;
        var originalComboFont=savedNavigationSlot.Font;int originalItemHeight=savedNavigationSlot.ItemHeight;
        int normalComboHeight=savedNavigationSlot.Height;
        using var enlargedComboFont=new Font(originalComboFont.FontFamily,16f,originalComboFont.Style,GraphicsUnit.Point);
        string originalTarget=navigation3DTarget.Text,originalRecording=navigationRecordingStatus.Text,
            originalRoutes=savedNavigationRoutesStatus.Text;
        var runs=new List<object>();var errors=new List<string>();
        var jsonOptions=new JsonSerializerOptions{WriteIndented=true};
        object Rect(Rectangle r)=>new{r.X,r.Y,r.Width,r.Height,r.Right,r.Bottom};
        void Layout(){PerformLayout();side.PerformLayout();section.PerformLayout();section.Content.PerformLayout();Application.DoEvents();}
        try
        {
            section.Expanded=true;
            // Long, synthetic text exercises the same wrapping as a populated
            // farming setup; it neither edits routes nor changes their targets.
            navigation3DTarget.Text="Targets · Mimic, Pulkhan, Tribal, Towers";
            navigationRecordingStatus.Text="Ready · Home starts";
            savedNavigationRoutesStatus.Text="Primary: Z8 · 817 pts · TrailToon · R10\nAlt 1: Z8 · 581 pts · TrailToon · R20\nAlt 2: Z8 · 618 pts · TrailToon · R20\n3 existing route(s) unassigned";
            section.Expanded=false;Layout();
            if(section.Content.Visible||savedNavigationSlot.Visible||startNavigationRecording.Visible)
                errors.Add("Collapsed Saved routes leaves its route controls visible");
            section.Expanded=true;Layout();
            if(!section.Content.Visible||!section.Content.Contains(selectorReference)||!section.Content.Contains(startReference)||
                savedNavigationSlot.Parent!=selectorParent||startNavigationRecording.Parent!=startParent||
                selectorReference.IsDisposed||startReference.IsDisposed)
                errors.Add("Expanding Saved routes replaced or lost its original selector/Start controls");
            foreach(var sample in new[]{(Name:"normal",Size:new Size(1480,1000),Enlarged:false),
                (Name:"minimum",Size:MinimumSize,Enlarged:false),(Name:"native-height-growth",Size:MinimumSize,Enlarged:true)})
            {
                savedNavigationSlot.Font=sample.Enlarged?enlargedComboFont:originalComboFont;
                // The themed selector is owner-drawn. Grow its item height as
                // well as its font to model native theme/DPI height changes.
                savedNavigationSlot.ItemHeight=sample.Enlarged?36:originalItemHeight;
                Size=sample.Size;side.AutoScrollPosition=Point.Empty;Layout();
                if(!sample.Enlarged)normalComboHeight=savedNavigationSlot.Height;
                else if(savedNavigationSlot.Height<=normalComboHeight)
                    errors.Add(sample.Name+" · growth fixture did not increase the native combo height");
                side.ScrollControlIntoView(section);Layout();
                var body=section.Content;
                var rows=body.GetRowHeights();var columns=body.GetColumnWidths();
                var controls=body.Controls.Cast<Control>().Where(c=>c.Visible).ToArray();
                var cells=new List<object>();
                foreach(var control in controls)
                {
                    var position=body.GetPositionFromControl(control);
                    int rowHeight=position.Row>=0&&position.Row<rows.Length?rows[position.Row]:0;
                    int rowTop=body.Padding.Top+rows.Take(Math.Max(0,position.Row)).Sum();
                    var row=new Rectangle(body.Padding.Left,rowTop,columns.Sum(),rowHeight);
                    string id=control.Name.Length>0?control.Name:control.GetType().Name+" · "+control.Text;
                    var problems=new List<string>();
                    if(rowHeight<control.Height+control.Margin.Vertical)
                        problems.Add($"row {position.Row} is {rowHeight}px; actual control plus margins needs {control.Height+control.Margin.Vertical}px");
                    if(control.Top-control.Margin.Top<row.Top||control.Bottom+control.Margin.Bottom>row.Bottom)
                        problems.Add("actual control escapes its allocated row");
                    if(control.Left<body.Padding.Left||control.Right>body.ClientSize.Width-body.Padding.Right)
                        problems.Add("control escapes the body width");
                    if(control is Label label&&label.AutoSize)
                    {
                        int textHeight=label.GetPreferredSize(new Size(label.Width,0)).Height;
                        if(label.Height<textHeight||label.AutoEllipsis)problems.Add("wrapping label hides content");
                    }
                    if(control==useAlternativeHuntRoutes)
                    {
                        int captionWidth=TextRenderer.MeasureText(control.Text,control.Font,new Size(int.MaxValue,int.MaxValue),
                            TextFormatFlags.SingleLine|TextFormatFlags.NoPadding|TextFormatFlags.NoPrefix).Width;
                        if(captionWidth>control.Width-47)problems.Add($"switch caption needs {captionWidth}px; only {control.Width-47}px is visible");
                    }
                    side.ScrollControlIntoView(control);Layout();
                    var viewport=side.RectangleToScreen(side.ClientRectangle);
                    var visible=control.RectangleToScreen(control.ClientRectangle);
                    // WinForms' client rectangle already excludes its native
                    // scroll bars, so do not subtract their width a second time.
                    if(!viewport.Contains(visible))problems.Add("control cannot be fully reached by the sidebar scroll bar");
                    cells.Add(new{Name=id,control.Text,Row=position.Row,RowHeight=rowHeight,
                        Bounds=Rect(control.Bounds),RowBounds=Rect(row),Preferred=Rect(new Rectangle(Point.Empty,control.PreferredSize)),
                        Viewport=Rect(viewport),ScrolledBounds=Rect(visible),Problems=problems.ToArray()});
                    errors.AddRange(problems.Select(p=>sample.Name+" · "+id+": "+p));
                }
                for(int i=0;i<controls.Length;i++)for(int j=i+1;j<controls.Length;j++)
                    if(controls[i].Bounds.IntersectsWith(controls[j].Bounds))
                        errors.Add(sample.Name+" · sibling overlap: "+controls[i].Text+" / "+controls[j].Text);
                if(savedNavigationSlot.Height<savedNavigationSlot.PreferredHeight)
                    errors.Add(sample.Name+" · route selection does not reserve its native preferred height");
                if(savedNavigationSlot.Parent!=body)
                {
                    var comboRow=savedNavigationSlot.Parent!;
                    if(!comboRow.ClientRectangle.Contains(savedNavigationSlot.Bounds)||comboRow.Height<savedNavigationSlot.PreferredHeight)
                        errors.Add(sample.Name+" · route combo escapes its reserved wrapper");
                    side.ScrollControlIntoView(savedNavigationSlot);Layout();
                    if(!side.RectangleToScreen(side.ClientRectangle).Contains(savedNavigationSlot.RectangleToScreen(savedNavigationSlot.ClientRectangle)))
                        errors.Add(sample.Name+" · native route combo cannot be fully reached by scrolling");
                }
                if(controls.OfType<Label>().SingleOrDefault(c=>c.Name=="navigationAlternativesHelp") is Label help&&
                    (help.Text.Length==0||help.AutoEllipsis||help.Height<help.GetPreferredSize(new Size(help.Width,0)).Height))
                    errors.Add(sample.Name+" · alternative-route explanation is clipped or empty");
                if(navigation3DTarget.Height<=navigation3DTarget.Font.Height||savedNavigationRoutesStatus.Height<=savedNavigationRoutesStatus.Font.Height)
                    errors.Add(sample.Name+" · long target or saved-route summary did not wrap");
                side.ScrollControlIntoView(section);Layout();
                using(var panel=new Bitmap(section.Width,section.Height))
                {
                    section.DrawToBitmap(panel,new Rectangle(Point.Empty,section.Size));
                    panel.Save(Path.Combine(AppContext.BaseDirectory,"saved-routes-panel-"+sample.Name+".png"));
                }
                using(var page=new Bitmap(Width,Height))
                {
                    DrawToBitmap(page,new Rectangle(Point.Empty,Size));
                    page.Save(Path.Combine(AppContext.BaseDirectory,"saved-routes-page-"+sample.Name+".png"));
                }
                runs.Add(new{sample.Name,Form=Rect(new Rectangle(Point.Empty,Size)),Body=Rect(body.ClientRectangle),
                    Section=Rect(section.Bounds),ComboFontPoints=savedNavigationSlot.Font.SizeInPoints,
                    ComboItemHeight=savedNavigationSlot.ItemHeight,ComboNativeHeight=savedNavigationSlot.Height,
                    ComboPreferredHeight=savedNavigationSlot.PreferredHeight,
                    RowHeights=rows,ColumnWidths=columns,Cells=cells});
            }
            savedNavigationSlot.Font=originalComboFont;savedNavigationSlot.ItemHeight=originalItemHeight;
            section.Expanded=false;Layout();section.Expanded=true;Layout();
            if(!ReferenceEquals(savedNavigationSlot,selectorReference)||!ReferenceEquals(startNavigationRecording,startReference)||
                !section.Content.Contains(selectorReference)||!section.Content.Contains(startReference)||
                savedNavigationSlot.Parent!=selectorParent||startNavigationRecording.Parent!=startParent)
                errors.Add("Saved routes restore did not preserve its original selector/Start instances");
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"saved-routes-layout-checks.json"),JsonSerializer.Serialize(new
            {Passed=errors.Count==0,HardwareInputEmitted=false,LiveClientConnected=false,SyntheticRouteDataOnly=true,
                Checks=new[]{"native combo height reserved","each table row encloses its actual child and margins","no sibling overlap",
                    "populated target and multiline route summaries wrap without ellipsis","complete alternatives caption",
                    "all rows reachable by scrolling at normal/minimum form sizes","native font/item-height growth reserves a larger wrapper",
                    "collapse/expand/restore preserves original selector and Start controls"},
                Errors=errors,Runs=runs},jsonOptions));
            if(errors.Count>0)throw new InvalidOperationException("Saved routes layout: "+string.Join("; ",errors));
        }
        finally
        {
            navigation3DTarget.Text=originalTarget;navigationRecordingStatus.Text=originalRecording;savedNavigationRoutesStatus.Text=originalRoutes;
            savedNavigationSlot.Font=originalComboFont;savedNavigationSlot.ItemHeight=originalItemHeight;
            section.Expanded=originalExpanded;Size=originalSize;
            side.AutoScrollPosition=new Point(-originalScroll.X,-originalScroll.Y);Layout();
        }
    }
}
