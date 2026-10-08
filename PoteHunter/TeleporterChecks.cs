using System.Text.Json;

namespace PoteHunter;

internal static class TeleporterChecks
{
    static void Require(bool condition,string message){if(!condition)throw new Exception("Teleporter: "+message);}
    static void Invalid(Action action,string message)
    {
        bool rejected=false;try{action();}catch(InvalidOperationException){rejected=true;}
        Require(rejected,message);
    }
    static Bitmap Fixture(bool popup,bool wrongDestination=false,bool mapVisible=true)
    {
        var image=new Bitmap(960,640);using var g=Graphics.FromImage(image);g.Clear(Color.FromArgb(35,40,45));
        using var font=new Font("Segoe UI",16,FontStyle.Bold);
        if(mapVisible)
        {
            g.DrawString(wrongDestination?"OTHER PLACE":"TEST LANDING",font,Brushes.White,35,35);
            foreach(int x in new[]{120,240,360})
            {
                g.FillEllipse(Brushes.RoyalBlue,x,110,36,36);
                g.DrawLine(Pens.White,x+8,128,x+28,128);g.DrawLine(Pens.White,x+18,118,x+18,138);
            }
        }
        else g.DrawString("WORLD AFTER MAP CLOSED",font,Brushes.White,35,500);
        if(popup)
        {
            g.FillRectangle(Brushes.DarkSlateGray,320,200,350,170);
            g.DrawString("Move to Location",font,Brushes.White,350,220);
            g.FillRectangle(Brushes.DimGray,440,300,90,40);
            g.DrawString("OK",font,Brushes.White,465,304);
        }
        return image;
    }
    static TeleporterProfile Profile(Bitmap before,Bitmap popup)=>new(1,"fixture-client",960,640,"Test landing",
        "Fixture",7,"Tribal",0,new(8,new(10,10),4),new(8,new(100,200),7),
        new(RepairPatch.Capture(before,new(32,32,200,35)),RepairPatch.Capture(before,new(118,108,40,40)),138,128),
        new(RepairPatch.Capture(popup,new(345,215,270,40)),RepairPatch.Capture(popup,new(462,302,48,32)),486,318));
    static TeleporterObservation Departure(TeleporterProfile profile)=>new(profile.ClientHash,123,(nint)456,new(960,640),
        new(100,7,"Fixture",profile.Departure.Position,profile.Departure.Height,Generation:3,Model:"PC_FIXTURE.GCMDS"),new(90,100),8,true);
    static TeleporterObservation Landing(TeleporterProfile profile,TeleporterObservation departure)=>departure with
    {Character=departure.Character! with{Address=300,Generation=4,Position=profile.Landing.Position,Height=profile.Landing.Height}};
    static TeleporterTransaction Confirmed(TeleporterProfile profile,TeleporterObservation departure)
    {
        var transaction=new TeleporterTransaction(profile,departure,0);
        transaction.BeforeSelection(departure,1);transaction.SelectionSent(departure,2);
        transaction.BeforeConfirmation(departure,3);transaction.ConfirmationSent(4);return transaction;
    }

    public static Task Run()
    {
        using var before=Fixture(false);using var popup=Fixture(true,mapVisible:false);using var wrong=Fixture(false,true);
        var profile=Profile(before,popup);profile.Validate(profile.ClientHash,before.Size);
        var selection=profile.FindSelection(before,default);var confirmation=profile.FindConfirmation(popup,default);
        Require(selection!=null && confirmation!=null && profile.FindConfirmation(before,default)==null && profile.FindSelection(popup,default)==null,
            "stage recognition confused the map marker and generic Move to Location dialog");
        Require(!profile.Select.Marker.Matches(popup) && profile.CanClickConfirmation(popup,confirmation!,default),
            "closing the destination map prevented independent Move to Location / OK recognition");
        using(var popupOverMap=Fixture(true))
            Require(profile.Select.Matches(popupOverMap,true,default) && profile.FindSelection(popupOverMap,default)==null &&
                !profile.CanClickSelection(popupOverMap,selection!,default),"a pre-existing confirmation dialog authorized a new destination selection");
        Require(profile.FindSelection(wrong,default)==null && !profile.CanClickSelection(wrong,selection!,default),
            "a different destination label authorized the saved blue marker");
        using(var hovered=(Bitmap)before.Clone())
        {
            using(var g=Graphics.FromImage(hovered))g.FillRectangle(Brushes.Gray,profile.Select.Button!.Bounds);
            Require(profile.FindSelection(hovered,default)==null && profile.CanClickSelection(hovered,selection!,default),
                "destination hover did not retain its independent captured destination marker");
            using(var g=Graphics.FromImage(hovered))g.FillRectangle(Brushes.Black,profile.Select.Marker.Bounds);
            Require(!profile.CanClickSelection(hovered,selection!,default),"selection clicked after the independent destination marker disappeared");
        }
        using(var hovered=(Bitmap)popup.Clone())
        {
            using(var g=Graphics.FromImage(hovered))g.FillRectangle(Brushes.Gray,profile.Confirm.Button!.Bounds);
            Require(profile.FindConfirmation(hovered,default)==null && profile.CanClickConfirmation(hovered,confirmation!,default),
                "OK hover discarded the independently recognized dialog marker");
            using(var g=Graphics.FromImage(hovered))g.FillRectangle(Brushes.Black,profile.Confirm.Marker.Bounds);
            Require(!profile.CanClickConfirmation(hovered,confirmation!,default),"missing Move to Location marker still authorized OK");
        }
        Require(!profile.CanClickSelection(before,selection! with{Point=new(258,128)},default) &&
            !profile.CanClickSelection(before,selection! with{Button=new(238,108,40,40)},default) &&
            !profile.CanClickConfirmation(popup,confirmation! with{Custom=false},default) &&
            !profile.CanClickConfirmation(popup,confirmation! with{Scale=2},default) &&
            !profile.CanClickConfirmation(popup,selection!,default),"a different control or blue destination was accepted");
        using(var resized=new Bitmap(before,480,320))
            Require(profile.FindSelection(resized,default)==null && !profile.CanClickSelection(resized,selection!,default),"resized map used stale click coordinates");
        using(var moved=new Bitmap(960,640))
        {
            using(var g=Graphics.FromImage(moved))g.DrawImageUnscaled(popup,20,20);
            Require(profile.FindConfirmation(moved,default)==null && !profile.CanClickConfirmation(moved,confirmation!,default),"moved dialog accepted stale OK");
        }
        using(var cancelled=new CancellationTokenSource())
        {
            cancelled.Cancel();bool stopped=false;
            try{profile.FindSelection(before,cancelled.Token);}catch(OperationCanceledException){stopped=true;}
            Require(stopped,"cancelled recognition kept running");
        }
        Invalid(()=>profile.Validate("different-client",before.Size),"another client build accepted captures");
        Invalid(()=>(profile with{Version=2}).Validate(profile.ClientHash,before.Size),"unsupported profile version accepted");
        Invalid(()=>(profile with{DestinationSlot=3}).Validate(profile.ClientHash,before.Size),"invalid destination slot accepted");
        Invalid(()=>(profile with{CharacterId=0x80000007}).Validate(profile.ClientHash,before.Size),"monster UID accepted as local character");
        Invalid(()=>(profile with{Landing=profile.Landing with{Zone=9}}).Validate(profile.ClientHash,before.Size),"cross-zone teleport accepted");
        Invalid(()=>(profile with{Departure=profile.Departure with{Position=new(double.NaN,0)}}).Validate(profile.ClientHash,before.Size),"nonfinite departure accepted");
        Invalid(()=>(profile with{Landing=profile.Landing with{Height=double.PositiveInfinity}}).Validate(profile.ClientHash,before.Size),"nonfinite landing height accepted");
        Invalid(()=>(profile with{Landing=profile.Landing with{Position=new(20,10)}}).Validate(profile.ClientHash,before.Size),"ten-unit route discontinuity became a teleport");
        Invalid(()=>(profile with{Select=profile.Select with{X=5000}}).Validate(profile.ClientHash,before.Size),"out-of-window map click accepted");
        Invalid(()=>(profile with{Confirm=profile.Confirm with{Button=null}}).Validate(profile.ClientHash,before.Size),"OK without captured button accepted");
        Invalid(()=>(profile with{Select=profile.Select with{Marker=profile.Select.Button!}}).Validate(profile.ClientHash,before.Size),"overlapping destination marker/button accepted");
        Storage(profile,before,popup);
        Calibration(profile,before,popup);
        Policy(profile);
        Journey(profile);
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"teleporter-checks.json"),JsonSerializer.Serialize(new
        {
            Passed=true,HardwareInputEmitted=false,SyntheticFixtures=true,
            Checks=new[]{"destination and generic dialog stage recognition","map may close before independently recognized confirmation","pre-existing dialog blocks selection",
                "wrong destination rejected","hover independent markers","exact saved control identity",
                "strict client/window size and bounds","same-zone only finite separated geometry","target/slot registry isolation and latest merge",
                "stale selected scope rejected","sixteen-link/file-size bounds","invalid/corrupt/duplicate registry preservation","failed atomic replace keeps all scopes",
                "walking/map transition/body recreation before first capture allowed only for same character","coherent first image and departure reading required",
                "strict captured departure body/map/position through confirmation","living same-character same-map landing permits manual body recreation",
                "one selection and one confirmation","identity recreation only after sent OK at captured landing","wrong player/client/window/map/location rejected",
                "known living HP only","focus/cancel stop","fifteen-second deadline and reversed clock stop","ordinary identity rule remains strict",
                "continuous separately recorded walking legs","no route jump or implied reverse connector","landing tail skips teleport","reversed/off-route/incompatible walking plans rejected"}
        },new JsonSerializerOptions{WriteIndented=true}));
        return Task.CompletedTask;
    }

    static void Storage(TeleporterProfile profile,Bitmap before,Bitmap popup)
    {
        string directory=Path.Combine(Path.GetTempPath(),"PoteHunter-teleporter-checks-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);string file=Path.Combine(directory,"teleporters.json");
        try
        {
            Require(TeleporterProfile.Load(profile.ClientHash,before.Size,"Tribal",0,file)==null,"missing registry invented a profile");
            profile.Save(file);(profile with{TargetSelection="Mimic"}).Save(file);(profile with{DestinationSlot=1}).Save(file);
            Require(TeleporterProfile.Load(profile.ClientHash,before.Size," tribal ",0,file)?.FindSelection(before,default)!=null &&
                TeleporterProfile.Load(profile.ClientHash,before.Size,"Mimic",0,file)?.FindConfirmation(popup,default)!=null &&
                TeleporterProfile.Load(profile.ClientHash,before.Size,"Tribal",1,file)!=null &&
                TeleporterProfile.Load(profile.ClientHash,before.Size,"",0,file)==null,"target selection or route slot scopes leaked");
            Invalid(()=>TeleporterProfile.Load("new-build",before.Size,"Tribal",0,file),"stale matching client scope silently fell back");
            Invalid(()=>TeleporterProfile.Load(profile.ClientHash,new(800,600),"Tribal",0,file),"stale matching window scope silently fell back");
            Require(TeleporterProfile.Load("new-build",before.Size,"Not saved",0,file)==null,"unrelated client-bound scope blocked an absent target");
            var staleLoaded=TeleporterProfile.Load(profile.ClientHash,before.Size,"Tribal",0,file)!;
            (profile with{TargetSelection="Towers"}).Save(file);
            (staleLoaded with{Destination="Updated landing"}).Save(file);
            Require(TeleporterProfile.Load(profile.ClientHash,before.Size,"Towers",0,file)!=null &&
                TeleporterProfile.Load(profile.ClientHash,before.Size,"Tribal",0,file)?.Destination=="Updated landing" &&
                TeleporterProfile.Load(profile.ClientHash,before.Size,"Tribal",1,file)!=null,"stale loaded profile overwrote another recent scope");
            byte[] original=File.ReadAllBytes(file);
            Invalid(()=>(profile with{Confirm=profile.Confirm with{X=-1}}).Save(file),"invalid partial setup replaced registry");
            Require(original.SequenceEqual(File.ReadAllBytes(file)),"invalid setup changed registry bytes");
            bool writeFailed=false;
            using(var occupied=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.Read))
                try{(profile with{Destination="Cannot replace"}).Save(file);}catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){writeFailed=true;}
            Require(writeFailed && original.SequenceEqual(File.ReadAllBytes(file)) && Directory.GetFiles(directory,"*.tmp").Length==0,
                "failed atomic replacement changed existing registry or left a partial file");
            for(int i=4;i<TeleporterProfile.MaximumLinks;i++)(profile with{TargetSelection="Fixture target "+i}).Save(file);
            original=File.ReadAllBytes(file);
            Invalid(()=>(profile with{TargetSelection="Seventeenth"}).Save(file),"unbounded link registry accepted a seventeenth scope");
            Require(original.SequenceEqual(File.ReadAllBytes(file)),"link-count rejection changed registry");
            foreach(string corrupt in new[]{"{broken","null","{}","{\"Version\":2,\"Links\":[]}",JsonSerializer.Serialize(new TeleporterProfile.Registry(1,[profile,profile]))})
            {
                File.WriteAllText(file,corrupt);
                Invalid(()=>TeleporterProfile.Load(profile.ClientHash,before.Size,"Tribal",0,file),"invalid or duplicate registry accepted");
                Invalid(()=>profile.Save(file),"invalid registry was overwritten");
                Require(File.ReadAllText(file)==corrupt,"corrupt registry bytes were lost");
            }
            using(var large=new FileStream(file,FileMode.Create,FileAccess.Write))large.SetLength(TeleporterProfile.MaximumRegistryBytes+1L);
            Invalid(()=>TeleporterProfile.Load(profile.ClientHash,before.Size,"Tribal",0,file),"oversized registry accepted");
            Invalid(()=>profile.Save(file),"oversized registry overwritten");
        }
        finally{foreach(string child in Directory.GetFiles(directory))File.Delete(child);Directory.Delete(directory);}
    }

    static void Calibration(TeleporterProfile profile,Bitmap before,Bitmap popup)
    {
        var departure=Departure(profile);var original=departure.Character!;
        var initial=new TeleporterSetupSample(original,departure.Zone,departure.Health);
        var walked=initial with{Body=original with{Position=new(45,65),Height=10}};
        var recreated=walked with{Body=walked.Body with{Address=300,Generation=4},Zone=9};
        TeleporterSetupPolicy.RequireBeforeDeparture(original,walked);
        TeleporterSetupPolicy.RequireBeforeDeparture(original,recreated);
        Require(TeleporterSetupPolicy.SameIdentity(original,recreated.Body) && !LocalCharacter.Same(original,recreated.Body),
            "walking or same-character body recreation before the first capture required the wizard-start body");

        var invalidSamples=new[]
        {
            recreated with{Body=recreated.Body with{Id=8}},
            recreated with{Body=recreated.Body with{Name="Other character"}},
            recreated with{Body=recreated.Body with{Model="PC_OTHER.GCMDS"}},
            recreated with{Body=recreated.Body with{Position=new(double.NaN,65)}},
            recreated with{Body=recreated.Body with{Height=double.PositiveInfinity}},
            recreated with{Health=default},recreated with{Health=new(0,100)},
            recreated with{Zone=0},recreated with{Zone=19}
        };
        foreach(var invalid in invalidSamples)
            Invalid(()=>TeleporterSetupPolicy.RequireBeforeDeparture(original,invalid),
                "pre-capture walking accepted a different character, unreadable/dead HP, or invalid scene");

        // The departure comes from the living reading bracketing this image,
        // after any walk from the place where the wizard was first opened.
        var captured=recreated;
        TeleporterSetupPolicy.RequireCaptureStable(original,captured,captured);
        var captureBoundary=captured with{Body=captured.Body with{Position=captured.Body.Position+new Vec(.5,0),Height=captured.Body.Height+.5}};
        TeleporterSetupPolicy.RequireCaptureStable(original,captured,captureBoundary);
        foreach(var changed in invalidSamples.Concat(new[]
        {
            captured with{Body=captured.Body with{Position=captured.Body.Position+new Vec(.501,0)}},
            captured with{Body=captured.Body with{Height=captured.Body.Height+.501}},
            captured with{Body=captured.Body with{Address=400}},
            captured with{Body=captured.Body with{Generation=5}},
            captured with{Zone=8}
        }))
        {
            Invalid(()=>TeleporterSetupPolicy.RequireCaptureStable(original,captured,changed),
                "changed post-image reading was attached to an incoherent capture");
            Invalid(()=>TeleporterSetupPolicy.RequireCaptureStable(original,changed,captured),
                "changed pre-image reading was attached to an incoherent capture");
        }

        var departureBoundary=captured with{Body=captured.Body with{Position=captured.Body.Position+new Vec(3,0),Height=captured.Body.Height+3}};
        TeleporterSetupPolicy.RequireDeparture(captured,departureBoundary);
        foreach(var changed in invalidSamples.Concat(new[]
        {
            captured with{Body=captured.Body with{Position=captured.Body.Position+new Vec(3.001,0)}},
            captured with{Body=captured.Body with{Height=captured.Body.Height+3.001}},
            captured with{Body=captured.Body with{Address=400}},
            captured with{Body=captured.Body with{Generation=5}},
            captured with{Zone=8}
        }))
        {
            Invalid(()=>TeleporterSetupPolicy.RequireDeparture(captured,changed),
                "captured departure moved or changed body/map before confirmation");
            Invalid(()=>TeleporterSetupPolicy.RequireDeparture(changed,captured),
                "invalid captured departure baseline authorized a later reading");
        }

        var landed=captured with{Body=captured.Body with{Address=900,Generation=6,Position=new(100,200),Height=17}};
        TeleporterSetupPolicy.RequireLanding(captured,landed);
        TeleporterSetupPolicy.RequireCaptureStable(captured.Body,landed,landed);
        foreach(var changed in invalidSamples.Concat(new[]{landed with{Zone=8}}))
            Invalid(()=>TeleporterSetupPolicy.RequireLanding(captured,changed),
                "manual landing admitted the wrong character/map, invalid position, or unknown/dead HP");
        foreach(var invalid in invalidSamples)
            Invalid(()=>TeleporterSetupPolicy.RequireLanding(invalid,landed),"invalid departure baseline admitted a manual landing");

        // The synthetic execution recognises the intended destination first;
        // its later confirmation only needs the saved dialog, with no map.
        var transaction=new TeleporterTransaction(profile,departure,0);
        Require(profile.FindSelection(before,default)!=null,"calibrated destination was not recognized before its confirmation");
        transaction.BeforeSelection(departure,1);transaction.SelectionSent(departure,2);
        Require(profile.FindConfirmation(popup,default)!=null && !profile.Select.Marker.Matches(popup),
            "closed-map confirmation still depended on the earlier destination text");
        transaction.BeforeConfirmation(departure,3);transaction.ConfirmationSent(4);
        Require(transaction.ObserveLanding(Landing(profile,departure),5),"closed-map confirmation failed the recognized destination-to-landing sequence");
    }

    static void Policy(TeleporterProfile profile)
    {
        var departure=Departure(profile);var landing=Landing(profile,departure);
        var complete=Confirmed(profile,departure);
        Require(!complete.ObserveLanding(departure,5) && complete.ObserveLanding(landing,6) &&
            complete.Stage==TeleporterStage.Complete && complete.AcceptedCharacter==landing.Character,"one confirmed teleport did not accept expected recreated body");
        Require(!LocalCharacter.Same(departure.Character!,landing.Character!),"ordinary body identity guard was loosened for teleport");
        Require(complete.VerifySettledLanding(landing,7)==landing.Character,"stable living landing was rejected");
        foreach(var changed in new[]{landing with{Health=default},landing with{Health=new(0,100)},landing with{ProcessId=999},
            landing with{Window=(nint)999},landing with{Character=landing.Character! with{Generation=999}},landing with{Zone=9}})
        {
            var settled=Confirmed(profile,departure);settled.ObserveLanding(landing,6);
            Invalid(()=>settled.VerifySettledLanding(changed,7),"changed settled landing accepted before adoption");
        }
        var expired=Confirmed(profile,departure);expired.ObserveLanding(landing,15003);
        Invalid(()=>expired.VerifySettledLanding(landing,15004),"landing settling extended the original phase deadline");
        Invalid(()=>new TeleporterTransaction(profile,landing,0),"landing identity was accepted before teleport began");
        var pending=new TeleporterTransaction(profile,departure,0);
        Invalid(()=>pending.CheckDeparture(landing,1),"generation changed before confirmation");
        foreach(var changed in new[]
        {
            departure with{ClientHash="different-client"},departure with{ProcessId=999},departure with{Window=(nint)999},
            departure with{WindowSize=new(800,600)},departure with{Focused=false},departure with{Cancelled=true},
            departure with{Health=default},departure with{Health=new(0,100)},departure with{Character=null}
        })
        {
            var selectionStopped=new TeleporterTransaction(profile,departure,0);
            Invalid(()=>selectionStopped.BeforeSelection(changed,1),"changed or unreadable context authorized selection before confirmation");
            Require(selectionStopped.Stage==TeleporterStage.Stopped && !selectionStopped.SelectionAttempted,
                "failed context check left a selection attempt usable");
            var confirmationStopped=new TeleporterTransaction(profile,departure,0);
            confirmationStopped.BeforeSelection(departure,1);confirmationStopped.SelectionSent(departure,2);
            Invalid(()=>confirmationStopped.BeforeConfirmation(changed,3),"changed or unreadable context authorized confirmation");
            Require(confirmationStopped.Stage==TeleporterStage.Stopped && !confirmationStopped.ConfirmationAttempted,
                "failed context check left a confirmation attempt usable");
        }
        var skipped=new TeleporterTransaction(profile,departure,0);
        Invalid(()=>skipped.BeforeConfirmation(departure,1),"generic OK was accepted without recognized destination selection");
        var noClick=new TeleporterTransaction(profile,departure,0);
        noClick.BeforeSelection(departure,1);noClick.SelectionSent(departure,2);noClick.BeforeConfirmation(departure,3);
        Invalid(()=>noClick.ObserveLanding(landing,4),"attempted but unsent OK admitted new identity");
        var twice=new TeleporterTransaction(profile,departure,0);twice.BeforeSelection(departure,1);
        Invalid(()=>twice.BeforeSelection(departure,2),"duplicate selection was authorized");
        twice=Confirmed(profile,departure);Invalid(()=>twice.BeforeConfirmation(departure,5),"duplicate confirmation was authorized");
        twice=Confirmed(profile,departure);Invalid(()=>twice.ConfirmationSent(5),"duplicate sent confirmation was acknowledged");
        foreach(var changed in new[]
        {
            landing with{Character=landing.Character! with{Id=8}},
            landing with{Character=landing.Character! with{Name="Other character"}},
            landing with{Character=landing.Character! with{Model="PC_OTHER.GCMDS"}},
            landing with{Character=landing.Character! with{Position=new(300,300)}},
            landing with{Character=landing.Character! with{Height=profile.Landing.Height+TeleporterProfile.HeightTolerance+.01}},
            departure with{Character=departure.Character! with{Generation=4}},
            departure with{Character=departure.Character! with{Position=new(50,50)}},
            landing with{ClientHash="different-client"},landing with{ProcessId=999},landing with{Window=(nint)999},
            landing with{WindowSize=new(800,600)},landing with{Zone=9},landing with{Health=new(0,100)},
            landing with{Focused=false},landing with{Cancelled=true},landing with{Character=null},
            landing with{Character=landing.Character! with{Position=new(double.NaN,200)}}
        })
        {
            var stopped=Confirmed(profile,departure);
            Invalid(()=>stopped.ObserveLanding(changed,5),"wrong landing/identity/health/focus observation was accepted");
            Require(stopped.Stage==TeleporterStage.Stopped && stopped.AcceptedCharacter==null,"invalid observation left teleport transaction usable");
            Invalid(()=>stopped.ObserveLanding(landing,6),"stopped transaction resumed on a later valid sample");
        }
        foreach(var invalid in new[]{departure with{Health=default},departure with{Health=new(0,100)},departure with{Focused=false},
            departure with{Cancelled=true},departure with{ProcessId=0},departure with{Window=0},departure with{Zone=9}})
            Invalid(()=>new TeleporterTransaction(profile,invalid,0),"teleport started with invalid initial state");
        var deadline=Confirmed(profile,departure);
        var unreadable=Confirmed(profile,departure);
        Require(!unreadable.ObserveLanding(landing with{Health=default},5)&&unreadable.AcceptedCharacter==null,"unknown landing HP admitted movement");
        unreadable.ObserveUnavailable(departure with{Character=null,Health=default},6);
        Require(unreadable.ObserveLanding(landing,7),"bounded released scene gap prevented a later known living landing");
        var gapDeadline=Confirmed(profile,departure);
        gapDeadline.ObserveUnavailable(departure with{Character=null},15003);
        Invalid(()=>gapDeadline.ObserveUnavailable(departure with{Character=null},15004),"unavailable scene extended the original landing deadline");
        var gapFocus=Confirmed(profile,departure);
        Invalid(()=>gapFocus.ObserveUnavailable(departure with{Character=null,Focused=false},5),"unreadable scene bypassed focus stop");
        var gapClient=Confirmed(profile,departure);
        Invalid(()=>gapClient.ObserveUnavailable(departure with{Character=null,ProcessId=999},5),"unreadable scene followed a replacement client");
        Invalid(()=>new TeleporterTransaction(profile,departure,0).ObserveUnavailable(departure with{Character=null},1),"unreadable scene bypassed pre-confirmation identity checks");
        Require(!deadline.ObserveLanding(departure,15003),"landing deadline elapsed too early");
        Invalid(()=>deadline.ObserveLanding(landing,15004),"landing accepted at or beyond fifteen-second deadline");
        var mapDeadline=new TeleporterTransaction(profile,departure,0);
        Invalid(()=>mapDeadline.BeforeSelection(departure,15000),"map selection waited forever");
        var dialogDeadline=new TeleporterTransaction(profile,departure,0);
        dialogDeadline.BeforeSelection(departure,1);dialogDeadline.SelectionSent(departure,2);
        Invalid(()=>dialogDeadline.BeforeConfirmation(departure,15002),"confirmation waited forever");
        var reversed=Confirmed(profile,departure);reversed.ObserveLanding(departure,10);
        Invalid(()=>reversed.ObserveLanding(landing,9),"reversed transaction clock was accepted");
        var boundary=landing with{Character=landing.Character! with{Position=profile.Landing.Position+new Vec(TeleporterProfile.LandingRadius,0)}};
        Require(Confirmed(profile,departure).ObserveLanding(boundary,5),"captured landing radius boundary failed");
        var outside=boundary with{Character=boundary.Character! with{Position=boundary.Character!.Position+new Vec(.001,0)}};
        Invalid(()=>Confirmed(profile,departure).ObserveLanding(outside,5),"position outside captured landing radius accepted");
        var unchangedLanding=landing with{Character=landing.Character! with{Address=departure.Character!.Address,Generation=departure.Character.Generation}};
        Require(Confirmed(profile,departure).ObserveLanding(unchangedLanding,5),"teleport required recreation when original identity still lived");
    }

    static void Journey(TeleporterProfile profile)
    {
        var link=profile with{Departure=new(8,new(20,0),4),Landing=new(8,new(80,0),4)};
        var route=new SavedNavigationRoute(8,new(100,0),.7,
            Enumerable.Range(0,51).Select(i=>new Vec(100-i*2,0)).ToArray(),DateTime.UnixEpoch,"Fixture",4,35,3,true,true);
        var journey=TeleporterJourney.Create(route,link,3);
        Require(RecoveryTravel.Recorded(journey.DepartureLeg) && RecoveryTravel.Recorded(journey.LandingLeg) &&
            journey.DepartureLeg.Anchor==link.Departure.Position && journey.DepartureLeg.RevivalOrigin==route.RevivalOrigin &&
            journey.LandingLeg.Anchor==route.Anchor && journey.LandingLeg.RevivalOrigin==link.Landing.Position &&
            journey.LandingLeg.Heading==route.Heading && journey.LandingLeg.HuntRadius==route.HuntRadius &&
            journey.LandingLeg.RepairAfterDeath==route.RepairAfterDeath,"actual journey split lost continuous origins, final facing, or destination settings");
        Require(journey.DepartureLeg.Points.All(p=>p.X<=20) && journey.LandingLeg.Points.All(p=>p.X>=80),
            "portal gap was included as a walking waypoint");
        var approach=RecoveryTravel.Plan([journey.DepartureLeg],journey.DepartureLeg,new(0,0),false,3);
        var continuation=RecoveryTravel.Plan([journey.LandingLeg],journey.LandingLeg,new(80,0),false,3);
        Require(approach.Destination==link.Departure.Position && approach.Points.All(p=>p.X<=20) &&
            continuation.Destination==route.Anchor && continuation.Points.All(p=>p.X>=80) &&
            approach.Points.Zip(approach.Points.Skip(1)).All(p=>(p.First-p.Second).Length<=8.01) &&
            continuation.Points.Zip(continuation.Points.Skip(1)).All(p=>(p.First-p.Second).Length<=8.01),
            "ordinary walking plan jumped between portal ends or backtracked through the omitted gap");
        foreach(double x in new[]{0d,10,19,20})Require(journey.NeedsTeleport(new(x,0),3),"departure walking corridor did not require teleport");
        foreach(double x in new[]{80d,90,100})Require(!journey.NeedsTeleport(new(x,0),3),"current character on landing tail would repeat teleport");
        Require(!journey.NeedsTeleport(new(50,0),3) && !journey.NeedsTeleport(new(10,4),3),"missing/off-route corridor silently selected teleport");
        void Rejected(Action action,string message)
        {
            bool rejected=false;try{action();}catch(RouteUnavailableException){rejected=true;}
            Require(rejected,message);
        }
        Rejected(()=>RecoveryTravel.Plan([journey.DepartureLeg,journey.LandingLeg],journey.LandingLeg,new(10,0),false,3),
            "walking planner invented a shared origin or reverse connection between portal legs");
        Rejected(()=>TeleporterJourney.Create(route with{Anchor=new(0,0),Points=route.Points.Reverse().ToArray()},link,3),
            "reversed recorded route accepted portal departure after landing");
        Rejected(()=>TeleporterJourney.Create(route,link with{Departure=link.Departure with{Position=new(20,4)}},10),
            "departure outside captured source radius accepted a route connector");
        Rejected(()=>TeleporterJourney.Create(route,link with{Landing=link.Landing with{Position=new(80,6)}},10),
            "landing outside captured destination radius accepted a route connector");
        var atSource=TeleporterJourney.Create(route,link with{Departure=link.Departure with{Position=new(0,0)}},3);
        var atAnchor=TeleporterJourney.Create(route,link with{Landing=link.Landing with{Position=route.Anchor}},3);
        Require(atSource.DepartureLeg.Points.Length==1 && TeleporterJourney.PlanLeg(atSource.DepartureLeg,new(0,0),3).Destination==new Vec(0,0) &&
            atAnchor.LandingLeg.Points.Length==1 && TeleporterJourney.PlanLeg(atAnchor.LandingLeg,route.Anchor,3).Destination==route.Anchor,
            "already-present portal endpoints required a fictional walking leg");
        Rejected(()=>TeleporterJourney.PlanLeg(atSource.DepartureLeg,new(4,0),3),"single endpoint bypassed corridor protection");
        Rejected(()=>TeleporterJourney.Create(route with{Zone=9},link,3),"wrong map route accepted");
        Rejected(()=>TeleporterJourney.Create(route with{Character="Other"},link,3),"wrong character route accepted");
        Rejected(()=>TeleporterJourney.Create(route with{Points=[route.Anchor,new(0,0)]},link,3),"ordinary discontinuous route became a portal jump");
        Rejected(()=>TeleporterJourney.Create(route,link,double.NaN),"nonfinite route corridor accepted");
        Rejected(()=>TeleporterJourney.Create(route,link,0),"empty route corridor accepted");
    }
}
