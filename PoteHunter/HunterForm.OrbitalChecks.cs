namespace PoteHunter;

public sealed partial class HunterForm
{
    void CheckOrbitalUi(TabControl tabs)
    {
        if(!offlinePreviewMode || connected || working) throw new InvalidOperationException("Orbital checks require the disconnected offline fixture.");
        var previousSize=Size; var previousPage=tabs.SelectedTab;
        var folds=orbitalRailBody!.Controls.OfType<CollapsibleSection>().ToArray();
        var expanded=folds.Select(f=>f.Expanded).ToArray();
        T Find<T>(string name) where T:Control => Controls.Find(name,true).OfType<T>().Single();
        void Layout(){PerformLayout();Application.DoEvents();}
        void Capture(string name)
        {
            Layout(); using var bitmap=new Bitmap(Width,Height);
            DrawToBitmap(bitmap,new Rectangle(Point.Empty,Size)); bitmap.Save(Path.Combine(AppContext.BaseDirectory,name));
        }
        try
        {
            var rail=Find<TableLayoutPanel>("orbitalOperationRail");
            var main=Find<TableLayoutPanel>("orbitalPageContent");
            var nav=Find<FlowLayoutPanel>("fieldNavigation");
            foreach(var size in new[]{new Size(1480,1000),MinimumSize})
            {
                Size=size;
                foreach(string section in new[]{"Overview","Hunt","Routes","Recovery","Settings"})
                {
                    Find<Button>("fieldNav"+section).PerformClick(); Layout();
                    var railBounds=RectangleToClient(rail.RectangleToScreen(rail.ClientRectangle));
                    var mainBounds=RectangleToClient(main.RectangleToScreen(main.ClientRectangle));
                    if(!rail.Visible || railBounds.Right>mainBounds.Left || rail.Width<320 || rail.Height<350)
                        throw new Exception("The operation rail is missing, overlaps a page or clips at "+size+" / "+section);
                    foreach(var action in new Control[]{connect,start,stop,Find<ComboBox>("overviewMode"),Find<TextBox>("overviewTargetFilter")})
                    {
                        var rect=RectangleToClient(action.RectangleToScreen(action.ClientRectangle));
                        if(!action.Visible || rect.Left<railBounds.Left || rect.Right>railBounds.Right || action.Width<70)
                            throw new Exception("An operation moved outside the left rail: "+action.Name);
                    }
                    if(PointToClient(nav.PointToScreen(Point.Empty)).Y < mainBounds.Bottom)
                        throw new Exception("Bottom navigation overlaps the current page.");
                    if(section=="Overview")
                    {
                        var telemetry=Find<TableLayoutPanel>("orbitalTelemetry");
                        var targetHeading=telemetry.RectangleToClient(Find<Label>("orbitalTargetHeading").RectangleToScreen(Find<Label>("orbitalTargetHeading").ClientRectangle));
                        foreach(string name in new[]{"orbitalActiveTime","orbitalTrackedKills"})
                        {
                            var value=Find<Label>(name);var valueBounds=telemetry.RectangleToClient(value.RectangleToScreen(value.ClientRectangle));
                            if(!telemetry.ClientRectangle.Contains(valueBounds) || valueBounds.Bottom>targetHeading.Top)
                                throw new Exception("A telemetry value clips into the target roster at "+size+": "+name);
                        }
                        if(Find<OrbitalScanner>("overviewRouteMap").Height<320*DeviceDpi/96)
                            throw new Exception("The route scanner collapsed below its readable canvas height.");
                    }
                }
            }
            Size=new Size(1480,1000);Find<Button>("fieldNavOverview").PerformClick();
            foreach(var fold in folds)fold.Expanded=false;
            Find<Panel>("orbitalRailScroll").AutoScrollPosition=Point.Empty;refreshOverview?.Invoke();Layout();
            if(Find<Label>("orbitalPageTitle").Text!="The Endless Pursuit")throw new Exception("The requested philosophical Overview headline was lost.");
            var scanner=Find<OrbitalScanner>("overviewRouteMap");
            foreach(string name in new[]{"overviewAnchor","overviewMapLegend","overviewRouteTarget"})
                if(Find<Label>(name).Width<Find<TableLayoutPanel>("overviewMapColumn").ClientSize.Width-12)
                    throw new Exception("A scanner note retained a stale width constraint: "+name);
            var railScroll=Find<Panel>("orbitalRailScroll");
            foreach(string name in new[]{"overviewResetLoot","overviewResetTimer"})
            {
                var reset=Find<Button>(name);railScroll.ScrollControlIntoView(reset);Layout();
                var resetBounds=railScroll.RectangleToClient(reset.RectangleToScreen(reset.ClientRectangle));
                if(!railScroll.ClientRectangle.Contains(resetBounds))throw new Exception("A session reset action is unreachable by scrolling: "+name);
            }
            railScroll.AutoScrollPosition=Point.Empty;Layout();
            if(scanner.PlayerKnown || scanner.RouteCount!=0 || !scanner.ReadingStatus.StartsWith("Connect"))
                throw new Exception("The disconnected scanner invented live coordinates or routes.");
            if(Find<Label>("orbitalSessionGold").Text!="—" || Find<Label>("orbitalGoldRate").Text!="— gold / active hour")
                throw new Exception("Unknown wallet values were presented as known telemetry.");
            Capture("orbital-ops-overview.png");
            Find<CollapsibleSection>("overviewFoldSkills").Expanded=true;Capture("orbital-ops-expanded-controls.png");
            Find<CollapsibleSection>("overviewFoldSkills").Expanded=false;
            Find<Button>("fieldNavRoutes").PerformClick();Capture("orbital-ops-routes.png");
            Find<Button>("fieldNavOverview").PerformClick();Size=MinimumSize;
            tabs.SelectedTab!.AutoScrollPosition=Point.Empty;Capture("orbital-ops-minimum.png");
            var cargo=Find<Label>("overviewResourceGold");tabs.SelectedTab.ScrollControlIntoView(cargo);Layout();
            if(!tabs.SelectedTab.ClientRectangle.Contains(tabs.SelectedTab.RectangleToClient(cargo.RectangleToScreen(cargo.ClientRectangle))))
                throw new Exception("The cargo manifest is unreachable in the scrollable minimum Overview.");
            tabs.SelectedTab.AutoScrollPosition=Point.Empty;
            // A synthetic scanner fixture is separate from actual application-state screenshots.
            using var fixture=new OrbitalScanner{Size=new Size(640,520)};
            var primary=new SavedNavigationRoute(8,new Vec(35,60),.8,[new(0,0),new(15,20),new(24,43),new(35,60)],DateTime.UnixEpoch);
            var alternative=new SavedNavigationRoute(8,new Vec(55,18),1.5,[new(0,0),new(22,4),new(43,9),new(55,18)],DateTime.UnixEpoch);
            fixture.SetReadings(true,8,[(0,primary),(1,alternative)],new Vec(15,20),.6,[new(28,33),new(40,43)],10,primary.Anchor,35);
            if(!fixture.PlayerKnown || fixture.RouteCount!=2)throw new Exception("Known current-zone scanner readings were discarded.");
            using(var image=new Bitmap(fixture.Width,fixture.Height))
            {fixture.DrawToBitmap(image,new Rectangle(Point.Empty,fixture.Size));image.Save(Path.Combine(AppContext.BaseDirectory,"orbital-scanner-synthetic.png"));}
            fixture.SetReadings(true,8,[(0,primary),(1,primary),(2,primary)],primary.Anchor,.6,[],10,primary.Anchor,35);
            using(var image=new Bitmap(fixture.Width,fixture.Height))
            {
                fixture.DrawToBitmap(image,new Rectangle(Point.Empty,fixture.Size));
                image.Save(Path.Combine(AppContext.BaseDirectory,"orbital-scanner-coincident.png"));
            }
            if(fixture.TabStop || fixture.LastLabelBounds.Count!=4 || fixture.LastLabelBounds
                .SelectMany((a,i)=>fixture.LastLabelBounds.Skip(i+1).Select(b=>(a,b))).Any(pair=>pair.a.IntersectsWith(pair.b)))
                throw new Exception("Coincident scanner anchors overlap labels or the passive canvas entered tab order.");
            fixture.SetReadings(true,9,[(0,primary),(1,alternative)],null,0,[],10,null,35);
            if(fixture.PlayerKnown || fixture.RouteCount!=0 || !fixture.ReadingStatus.Contains("unavailable"))
                throw new Exception("Cross-zone routes or unavailable player readings leaked into the scanner.");
            fixture.SetReadings(false,null,[(0,primary)],new Vec(12,12),0,[],10,null,35);
            if(fixture.PlayerKnown || fixture.RouteCount!=0)throw new Exception("Disconnect retained a live scanner marker.");
            CheckOrbitalAcceptedPoll(fixture);
            using(var gold=new Bitmap(28,28))
            {
                using var g=Graphics.FromImage(gold);g.Clear(Color.Transparent);CrownfireControls.Glyph(g,"Gold",new RectangleF(0,0,28,28),ImperialTheme.Accent);
                if(gold.GetPixel(14,14).ToArgb()!=Color.FromArgb(218,167,66).ToArgb())throw new Exception("The currency glyph inherited the lime UI accent.");
            }
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"orbital-ui-checks.json"),System.Text.Json.JsonSerializer.Serialize(new
            {Passed=true,Checks=new[]{"left rail persists across all five destinations at default and minimum size","bottom navigation clear of content","readable minimum canvas with reachable cargo and reset actions","telemetry values do not overlap target roster","unknown wallet and disconnected player remain unknown","current-zone route filtering","accepted-poll position and transition/identity/staleness guards","coincident anchor labels remain distinct","passive scanner excluded from tab order","original gold glyph colors","native offscreen Overview, expanded controls, Routes and minimum screenshots"}}));
        }
        finally
        {
            for(int i=0;i<folds.Length;i++)folds[i].Expanded=expanded[i];
            Size=previousSize;tabs.SelectedTab=previousPage;refreshOverview?.Invoke();Layout();
        }
    }

    void CheckOrbitalAcceptedPoll(OrbitalScanner fixture)
    {
        var old=(connected,navigationZone,guardSelfId,navigationPosition,recognitionSelf,recognitionZone,recognitionSeen,navigation3DPlayerSeen);
        try
        {
            var self=new Entity(100,1,"Orbital accepted-poll fixture",new(8,12),5,Model:"PC_MAN.GCMDS");
            connected=true;navigationZone=recognitionZone=8;guardSelfId=self.Id;
            recognitionSelf=self;recognitionSeen=Environment.TickCount64;
            navigationPosition=new(900,900);navigation3DPlayerSeen=Environment.TickCount64;
            UpdateOrbitalScanner(fixture);
            if(!fixture.PlayerKnown || !fixture.PlayerPosition!.Value.Equals(self.Position))
                throw new Exception("The scanner preferred a mutable position over the accepted poll.");
            // Tick refreshes its 3D timestamp before it rejects a zone transition.
            navigationZone=12;navigation3DPlayerSeen=Environment.TickCount64;UpdateOrbitalScanner(fixture);
            if(fixture.PlayerKnown)throw new Exception("A rejected zone-transition poll appeared as a live scanner player.");
            navigationZone=8;recognitionSeen=Environment.TickCount64-3001;UpdateOrbitalScanner(fixture);
            if(fixture.PlayerKnown)throw new Exception("An expired accepted poll retained a live scanner player.");
            recognitionSeen=Environment.TickCount64;guardSelfId=77;UpdateOrbitalScanner(fixture);
            if(fixture.PlayerKnown)throw new Exception("An old player identity retained a live scanner marker.");
        }
        finally
        {
            (connected,navigationZone,guardSelfId,navigationPosition,recognitionSelf,recognitionZone,recognitionSeen,navigation3DPlayerSeen)=old;
        }
    }
}
