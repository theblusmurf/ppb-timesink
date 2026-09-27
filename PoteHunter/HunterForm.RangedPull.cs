namespace PoteHunter;

public sealed partial class HunterForm
{
    sealed class RangedTargetOutOfRangeException : Exception
    {
        public RangedTargetOutOfRangeException() : base("Ranged tag target left shooting range.") { }
    }

    internal static int CheckRangedPullUi()
    {
        // Preview construction omits the live connection/global-hotkey Shown handler.
        ApplicationConfiguration.Initialize();
        var existing = File.Exists(Options.PathName) ? File.ReadAllBytes(Options.PathName) : null;
        try
        {
            using var form = new HunterForm(offlinePreview:true);
            try
            {
                form.ranged.Checked = false;
                form.InvokeOnClick(form.ranged,EventArgs.Empty);
                form.rangedPullEnabled.Checked = true;
                form.experimentalInternalTargeting.Checked = true;
                form.rangedPullCount.Value = 5;
                form.rangedMinimumNearby.Value = 5;
                form.rangedGatherRadius.Value = 2;
                form.rangedMeleeAttackRange.Value = 2;
                form.rangedVerticalAimOffset.Value = 1;
                form.showNavigationOverlay.Checked = true;
        form.navigationOverlaySize.Value = 325;
        form.navigationViewRadius.Value = 175;
        form.guideTreasureChests.Checked = true;
        form.showTreasureChestMarkers.Checked = true;
        form.returnToHuntLocation.Checked = true;
        form.attackPotions.Checked=true;form.defensePotions.Checked=true;
                var expected = form.CurrentOptions(); expected.Save();
                var restored = Options.Read();
                if(!restored.Ranged || !restored.RangedPullEnabled || !restored.ExperimentalInternalTargeting || restored.RangedPullCount!=5 || restored.RangedMinimumNearby!=5 || restored.RangedGatherRadius!=2 || restored.RangedMeleeAttackRange!=2 || restored.RangedVerticalAimOffset!=1 ||
                    restored.RangedTagMilliseconds!=expected.RangedTagMilliseconds || restored.RangedGatherTimeoutSeconds!=expected.RangedGatherTimeoutSeconds ||
                    restored.AutoRestoreMana!=expected.AutoRestoreMana || restored.ManaBelowPercent!=expected.ManaBelowPercent || restored.ManaDelaySeconds!=expected.ManaDelaySeconds ||
             !restored.ShowNavigationOverlay || restored.NavigationOverlaySize!=325 || restored.NavigationViewRadius!=175 ||
             !restored.GuideTreasureChests ||
             !restored.ShowTreasureChestMarkers ||
             !restored.ReturnToHuntLocationAfterGamekeeper ||
             !restored.UseAttackPotions || !restored.UseDefensePotions)
                    throw new Exception("Ranged pack controls did not persist through settings save/load.");
                // Deterministic preview data makes monster/chest rendering visible
                // without connecting to a client or emitting game input.
                var known=new Entity(0x10001,0x80000001,"Lv. 1 Preview monster",new(40,25),0);
                var unknown=new Entity(0x10002,0x80000002,"Lv. 1 Unknown health",new(-45,30),0);
                var dead=new Entity(0x10003,0x80000003,"Lv. 1 Dead monster",new(-35,-50),0);
                var distant=new Entity(0x10004,0x80000004,"Lv. 1 Distant monster",new(180,0),0);
                var chest=new Entity(0x10005,0x80000bbf,"Treasure Box",new(80,-30),0,Model:"MON_luckybag.GCMDS");
                form.entities=[known,unknown,dead,distant,chest];
                form.latestHealth=new(){{known.Id,new(100,100)},{dead.Id,new(0,100)},{distant.Id,new(100,100)}};
                form.encounter.Begin();form.encounter.MarkAttack(known,new(100,100));
                form.FitNavigationRadiusToLoaded();
                if(form.navigationViewRadius.Value!=190)throw new Exception("Fit loaded did not include the furthest living monster with its view margin.");
                form.navigationViewRadius.Value=150;
                var panel = (TableLayoutPanel)form.rangedPullEnabled.Parent!;
                var page = (TabPage)panel.Parent!;
                var tabs = (TabControl)page.Parent!;
                form.StartPosition=FormStartPosition.Manual;form.Location=new Point(-32000,-32000);form.ShowInTaskbar=false;
                form.Show();tabs.SelectedTab = page;
                form.PerformLayout();panel.PerformLayout();Application.DoEvents();
                using var bitmap = new Bitmap(form.Width,form.Height);
                form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,form.Size));
                bitmap.Save(Path.Combine(AppContext.BaseDirectory,"ranged-pull-preview.png"));
                foreach(TabPage previewPage in tabs.TabPages)
                {
                    tabs.SelectedTab=previewPage;form.PerformLayout();Application.DoEvents();
                    using var preview=new Bitmap(form.Width,form.Height);
                    form.DrawToBitmap(preview,new Rectangle(Point.Empty,form.Size));
                    preview.Save(Path.Combine(AppContext.BaseDirectory,$"ui-page-{tabs.TabPages.IndexOf(previewPage)}.png"));
                }
                tabs.SelectedIndex=0;
                var advancedToggle=(Button)form.settings.Controls.Find("advancedSettingsToggle",true).Single();
                advancedToggle.PerformClick();form.PerformLayout();Application.DoEvents();
                if(!form.skillSeconds.Visible || !form.manaReserve.Visible)
                    throw new Exception("Advanced settings or mana reserve are inaccessible.");
                form.settings.Enabled=false;
                if(form.skillSeconds.Enabled || form.manaReserve.Enabled)
                    throw new Exception("Settings no longer inherit the running-state lock.");
                form.settings.Enabled=true;
                using(var expanded=new Bitmap(form.Width,form.Height))
                {
                    form.DrawToBitmap(expanded,new Rectangle(Point.Empty,form.Size));
                    expanded.Save(Path.Combine(AppContext.BaseDirectory,"ui-advanced.png"));
                }
                advancedToggle.PerformClick();
                if(form.skillSeconds.Visible)throw new Exception("Advanced settings did not collapse.");
                form.rangedPullEnabled.Checked=false;
                form.healerMode.Checked=true;form.groupEnabled.Checked=true;form.selectedTankName="Preview tank";
                form.CurrentOptions().Save();
                var healbotOptions=Options.Read();
                if(!healbotOptions.HealerMode || !healbotOptions.GroupMode || healbotOptions.GroupTankName!="Preview tank" || !healbotOptions.UseDefensePotions)
                    throw new Exception("Combined healer/group controls did not persist alongside potion settings.");
                form.CheckCompactControls();
                form.Size=form.MinimumSize;form.PerformLayout();Application.DoEvents();
                using(var setupMinimum=new Bitmap(form.Width,form.Height))
                {
                    form.DrawToBitmap(setupMinimum,new Rectangle(Point.Empty,form.Size));
                    setupMinimum.Save(Path.Combine(AppContext.BaseDirectory,"ui-setup-minimum.png"));
                }
                tabs.SelectedTab=page;form.PerformLayout();Application.DoEvents();
                using var small=new Bitmap(form.Width,form.Height);
                form.DrawToBitmap(small,new Rectangle(Point.Empty,form.Size));
                small.Save(Path.Combine(AppContext.BaseDirectory,"ranged-pull-preview-minimum.png"));
                using var overlay=new NavigationOverlay(form.DrawNavigation){Location=new Point(-32000,-32000),Size=new Size(450,450)};
                var foreground=Input.GetForegroundWindow();overlay.Show();Application.DoEvents();
                var foregroundAfter=Input.GetForegroundWindow();
                var radarState=new{Styles=$"0x{overlay.ExtendedWindowStyles:X8}",overlay.HasPassiveWindowStyles,
                    Window=overlay.Handle.ToInt64(),ForegroundBefore=foreground.ToInt64(),ForegroundAfter=foregroundAfter.ToInt64(),
                    Environment.UserInteractive,SystemInformation.TerminalServerSession};
                File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"radar-window-checks.json"),System.Text.Json.JsonSerializer.Serialize(radarState));
                using var radar=new Bitmap(450,450);overlay.DrawToBitmap(radar,new Rectangle(0,0,450,450));
                radar.Save(Path.Combine(AppContext.BaseDirectory,"radar-preview.png"));
                if(!overlay.HasPassiveWindowStyles || foregroundAfter==overlay.Handle && foreground!=overlay.Handle)
                    throw new Exception("Radar preview activated or lost its passive window styles: "+System.Text.Json.JsonSerializer.Serialize(radarState));
                File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"ranged-ui-check.json"),
                    System.Text.Json.JsonSerializer.Serialize(new {Passed=true,Connected=form.connected,Working=form.working,
                        Checks=new[]{"real control values persisted and reloaded","offscreen ranged tab rendered","live startup handler omitted"}}));
                return 0;
            }
            finally {form.timer.Dispose();form.world.Dispose();}
        }
        catch(Exception ex)
        {
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"ranged-ui-check.json"),System.Text.Json.JsonSerializer.Serialize(new {Passed=false,Error=ex.ToString()}));
            return 1;
        }
        finally
        {
            if(existing!=null)File.WriteAllBytes(Options.PathName,existing);
            else if(File.Exists(Options.PathName))File.Delete(Options.PathName);
        }
    }

    readonly RangedPull rangedPull = new();
    readonly CheckBox rangedPullEnabled = new() { Text = "Tag a pack, then let it come to me", AutoSize = true };
    readonly CheckBox experimentalInternalTargeting = new() { Text = "Experimental: select Firing targets internally", AutoSize = true };
    readonly NumericUpDown rangedPullCount = Number(2, 20);
    readonly NumericUpDown rangedMinimumNearby = Number(1, 20);
    readonly NumericUpDown rangedGatherRadius = Number(1, 8, 1);
    readonly NumericUpDown rangedMeleeAttackRange = Number(1, 4, 1);
    readonly NumericUpDown rangedVerticalAimOffset = Number(0, 4, 1);
    readonly NumericUpDown rangedTagDuration = Number(100, 3000);
    readonly NumericUpDown rangedPullTimeout = Number(3, 60);
    readonly Button captureTargetState = new() { Text = "Capture 30 seconds", AutoSize = true };
    bool rangedTagging;
    readonly Dictionary<(uint,uint,long),int> rangedShotFailures = new();
    readonly Dictionary<(uint,uint,long),int> rangedTargetStateFailures = new();
    Entity? rangedFireTarget;
    Entity? rangedPendingTarget;
    Health rangedPendingHealth;
    bool rangedPendingInput,rangedPendingCourtesy,rangedApproaching;

    void StopRangedFire()
    {
        if(rangedFireTarget!=null)Input.HoldMouse(true,false,default);
        rangedFireTarget=null;
    }

    void ClearRangedPending(bool releaseFire=true)
    {
        if(rangedPendingCourtesy && rangedPendingTarget is Entity pending)courtesy.Forget(pending);
        rangedPendingTarget=null;rangedPendingInput=false;rangedPendingCourtesy=false;
        if(releaseFire)StopRangedFire();
    }

    async Task<bool> WaitForClientTargetState(Entity target,CancellationToken token)
    {
        long deadline=Environment.TickCount64+700;TargetStateSnapshot state=world.TargetState();
        while(true)
        {
            var live=world.Find(target.Id);
            if(live==null||TargetIdentity(live)!=TargetIdentity(target))return false;
            state=world.TargetState();
            if(state.Matches(target.Id))
            {
                TraceLog.Record("target state matched",new {target.Id,target.Generation,target.Address,State=state.Status,
                    Values=state.Ids.Select(value=>$"0x{value:X8}").ToArray()});
                return true;
            }
            if(Environment.TickCount64>=deadline)
            {
                TraceLog.Record("target state did not match",new {target.Id,target.Generation,target.Address,State=state.Status,
                    Values=state.Ids.Select(value=>$"0x{value:X8}").ToArray()});
                return false;
            }
            await Input.Delay(25,token);
        }
    }

    static bool RangedPullEnabled(Options options) => options.Ranged && options.RangedPullEnabled && !options.GroupMode && !options.HealerMode;

    void ApplyRangedPackRequirements()
    {
        if(!rangedPullEnabled.Checked)return;
        ranged.Checked=true;
        healerMode.Checked=false;
        groupEnabled.Checked=false;
        if(compactMode.SelectedIndex!=0)compactMode.SelectedIndex=0;
    }

    void AddRangedPullSettings(TabControl tabs)
    {
        var page = new TabPage("Ranged packs") { AutoScroll = true, BackColor = BackColor, ForeColor = ForeColor };
        var panel = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Padding = new Padding(12) };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        void Row(string label, Control control)
        {
            int row = panel.RowCount++;
            panel.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(3, 8, 3, 8) }, 0, row);
            panel.Controls.Add(control, 1, row);
        }
        Row("Solo ranged farming", rangedPullEnabled);
        Row("Internal targeting", experimentalInternalTargeting);
        Row("Maximum monsters to pull at once", rangedPullCount);
        Row("Melee swing radius (units)", rangedGatherRadius);
        Row("Melee attack range (units)", rangedMeleeAttackRange);
        Row("Vertical aim offset (units)", rangedVerticalAimOffset);
        Row("Maximum time adding monsters (seconds)", rangedPullTimeout);
        Row("Stage 1 target discovery", captureTargetState);
        var description = new Label
        {
            AutoSize = true, MaximumSize = new Size(750, 0), Margin = new Padding(3, 12, 3, 8),
            Text = "Use Firing until the configured maximum number of pull IDs is confirmed. The bot then stops pulling, waits for those tagged monsters to enter the melee swing radius, and attacks their densest direction with left-click. Pulling resumes only after every tagged ID is gone. The healing threshold pauses new pulls. Enabling ranged packs automatically selects ranged combat and turns off Group and Healer mode."
        };
        int textRow = panel.RowCount++;
        panel.Controls.Add(description, 0, textRow); panel.SetColumnSpan(description, 2);
        page.Controls.Add(panel); tabs.TabPages.Add(page);
        rangedPullEnabled.CheckedChanged+=(_,_)=>
        {
            ApplyRangedPackRequirements();
            if(!busy&&!working)try{CurrentOptions().Save();}catch(Exception ex){message=ex.Message;}
        };
        try
        {
            var options = Options.Read();
            rangedPullEnabled.Checked = options.RangedPullEnabled;
            experimentalInternalTargeting.Checked=options.ExperimentalInternalTargeting;
            rangedPullCount.Value = Math.Clamp(options.RangedPullCount, 2, 20);
            rangedGatherRadius.Value = Math.Clamp(options.RangedGatherRadius, 1, 8);
            rangedMeleeAttackRange.Value=Math.Clamp(options.RangedMeleeAttackRange,1,4);
            rangedVerticalAimOffset.Value=Math.Clamp(options.RangedVerticalAimOffset,0,4);
            rangedTagDuration.Value = Math.Clamp(options.RangedTagMilliseconds, 100, 3000);
            rangedPullTimeout.Value = Math.Clamp(options.RangedPullTimeoutSeconds, 3, 60);
        }
        catch
        {
            rangedPullCount.Value = 5; rangedGatherRadius.Value = 2; rangedMeleeAttackRange.Value=2; rangedVerticalAimOffset.Value=1; rangedTagDuration.Value = 800;
            rangedPullTimeout.Value = 15;
        }
        captureTargetState.Click+=async (_,_)=>await CaptureTargetStateAsync();
    }

    async Task CaptureTargetStateAsync()
    {
        if(busy||working||!connected)
        {
            message="Stop hunting and connect to the game before starting a read-only target capture.";
            return;
        }
        var snapshot=entities.Where(entity=>entity.Monster&&entity.Position.Finite).ToArray();
        if(snapshot.Select(entity=>entity.Id).Distinct().Count()<2)
        {
            message="Target capture needs at least two distinct loaded monsters. Move near a pair, then try again.";
            return;
        }
        captureTargetState.Enabled=false;
        message="Read-only target capture is running for 30 seconds. Switch to the game and manually aim and fire at two different monsters.";
        TraceLog.Record("target discovery started",new {Candidates=snapshot.Select(entity=>new {entity.Id,entity.DisplayName,entity.Generation,entity.Address}).ToArray()});
        try
        {
            var result=await Task.Run(()=>TargetStateDiscovery.Capture(world,snapshot,TimeSpan.FromSeconds(30),CancellationToken.None));
            message=$"Target capture complete: {result.CandidateFields} candidate fields recorded. Stage 1 report is ready for review.";
            TraceLog.Record("target discovery completed",new {result.CapturePath,result.ReportPath,result.Snapshots,result.CandidateFields});
        }
        catch(Exception ex)
        {
            message="Target capture stopped: "+ex.Message;
            TraceLog.Record("target discovery failed",new {Error=ex.Message});
        }
        finally {captureTargetState.Enabled=true;}
    }

    Options WithRangedPullSettings(Options options)
    {
        options.RangedPullEnabled = rangedPullEnabled.Checked;
        options.ExperimentalInternalTargeting=experimentalInternalTargeting.Checked;
        options.RangedPullCount = (int)rangedPullCount.Value;
        options.RangedMinimumNearby = (int)rangedPullCount.Value;
        options.RangedGatherRadius = rangedGatherRadius.Value;
        options.RangedMeleeAttackRange=rangedMeleeAttackRange.Value;
        options.RangedVerticalAimOffset=rangedVerticalAimOffset.Value;
        options.RangedTagMilliseconds = (int)rangedTagDuration.Value;
        options.RangedPullTimeoutSeconds = (int)rangedPullTimeout.Value;
        if(options.RangedPullEnabled)
        {
            options.Ranged=true;
            options.GroupMode=false;
            options.HealerMode=false;
            options.ContinuousCombatPositioning=false;
            options.TightGathering=false;
            options.RangedMinimumNearby=options.RangedPullCount;
        }
        return options;
    }

    List<Entity> RangedNearbyTargets(Options options,IReadOnlyDictionary<uint,Health> health,Vec position,int level)
    {
        double playerHeight=world.LocalPlayer().Height;
        var ambiguous=entities.GroupBy(e=>e.Id).Where(g=>g.Select(TargetIdentity).Distinct().Skip(1).Any()).Select(g=>g.Key).ToHashSet();
        return entities.Where(e=>!ambiguous.Contains(e.Id) && e.Monster && !e.PriorityLootObject && !Targeting.IsGamekeeper(e) &&
            RangedPull.WithinNearby3D(e,position,playerHeight,(double)options.RangedGatherRadius) &&
            health.GetValueOrDefault(e.Id) is {Known:true,Dead:false} hp &&
            (encounter.IsEngaged(e) || courtesy.StartedHere(e) || hp.Current==hp.Maximum) &&
            (encounter.IsEngaged(e) || courtesy.StartedHere(e) || MatchesRequestedTarget(e,options,level)) &&
            TargetGuardReason(e,hp,position,options)==null)
            .GroupBy(TargetIdentity).Select(g=>g.First()).OrderBy(e=>(e.Position-position).Length).ToList();
    }

    Entity[] RangedShotBlockers()=>entities.Where(e=>e.Monster && !latestHealth.GetValueOrDefault(e.Id).Dead).ToArray();

    // Returns true while the pack controller owns this iteration. The ordinary
    // combat loop takes over only once a gathered member can be cleared in place.
    async Task<bool> RunRangedPullStep(Movement drive, Vec anchor, Options options,
        IReadOnlyDictionary<uint, Health> health, Vec position, int level,
        Dictionary<char, long> skillDue, CancellationToken token)
    {
        if (!RangedPullEnabled(options)) return false;
        activeMovementBoundary=(double)options.HuntRadius;
        if (!rangedPull.Active)
        {
            if (encounter.Active || encounter.HasEngaged || healingRestPending || completionReturnPending || deferredLoot.Count > 0) return false;
            // Preserve the existing priority-object handling between packs.
            if (options.PrioritizeBreakables && entities.Any(e => e.PriorityLootObject && (e.Position-anchor).Length <= (double)options.HuntRadius &&
                health.GetValueOrDefault(e.Id) is { Known: true, Dead: false } && TargetGuardReason(e,health[e.Id],position,options)==null)) return false;
            var pullBar=CheckedHotbar();
            if(SkillRotation.DetectFiringKeys(pullBar).Length==0)
                throw new InvalidOperationException("Ranged packs requires a usable Firing skill on the active hotbar; stopped before attacking.");
            rangedPull.Begin(Environment.TickCount64);rangedShotFailures.Clear();rangedTargetStateFailures.Clear();ClearRangedPending();
        }
        var previous = rangedPull.Phase;
        int nearby=rangedPull.ApplyNearbyPolicy(RangedNearbyTargets(options,health,position,level),health,position,
            (double)options.RangedGatherRadius,options.RangedMinimumNearby,Environment.TickCount64);
        // A normal return hit is expected while building a ranged pack. Stop adding
        // members only at the configured recovery threshold (independent of the
        // auto-heal/item state) or when recovery is already pending.
        bool pressure = RangedPull.ShouldStopTagging(world.TargetHealth(guardSelfId),
            options.HealBelowPercent,healingRestPending);
        rangedPull.Update(entities, health, position, (double)options.RangedGatherRadius,
            Environment.TickCount64, options.RangedPullCount, options.RangedPullTimeoutSeconds,
            options.RangedGatherTimeoutSeconds, pressure, encounter.HasEngaged);
        if (previous != rangedPull.Phase)
            TraceLog.Record("ranged pack phase", new { Phase = rangedPull.Phase.ToString(), rangedPull.AttemptedCount, Nearby=nearby,
                MinimumNearby=options.RangedMinimumNearby,NearbyRadius=options.RangedGatherRadius,Pressure = pressure });
        if(previous!=rangedPull.Phase){ClearRangedPending();drive.StopTravel();Input.Release(preserveNearbyPickup:true);}
        if(previous==RangedPullPhase.Clearing && rangedPull.Phase==RangedPullPhase.Tagging)
        {
            rangedShotFailures.Clear();
            if(!encounter.HasEngaged && deferredLoot.Count>0){rangedPull.Reset();return false;}
        }
        if (rangedPull.Phase == RangedPullPhase.Tagging)
        {
            var ambiguous = entities.GroupBy(e=>e.Id).Where(g=>g.Select(TargetIdentity).Distinct().Skip(1).Any()).Select(g=>g.Key).ToHashSet();
            var permitted = entities.Where(e => e.Monster && e.Position.Finite && double.IsFinite(e.Height) && !ambiguous.Contains(e.Id) && rangedShotFailures.GetValueOrDefault(TargetIdentity(e))<3 && rangedTargetStateFailures.GetValueOrDefault(TargetIdentity(e))<3 &&
                MatchesRequestedTarget(e, options, level) && TargetGuardReason(e, health.GetValueOrDefault(e.Id), position, options) == null).ToArray();
            var camera=world.ReadCamera();var blockers=RangedShotBlockers();
            var shootable=permitted.Where(e=>!RangedTargetVisibility.FindFrontBlocker(camera,e,blockers).Blocked).ToArray();
            var next = rangedPendingTarget is Entity pending ? permitted.FirstOrDefault(e=>TargetIdentity(e)==TargetIdentity(pending)) : null;
            if(next==null && rangedPendingTarget!=null)
            {
                ClearRangedPending();
            }
            next ??= rangedPull.ChooseNext(shootable, health, position, anchor, (double)options.HuntRadius, (double)options.MeleeRange);
            // Continue acquiring fresh members while Tagging, even after the first
            // one becomes engaged. Anchor distance keeps this bounded; the doubled
            // player distance merely covers opposite edges of that same hunt area.
            // Gathering and Clearing never enter this branch and therefore never chase.
            next ??= rangedPull.ChooseNext(shootable,health,position,anchor,
                (double)options.HuntRadius,(double)options.HuntRadius*2);
            // A blocked Firing activation can still attract its target. Favor
            // clear front targets, but do not turn creature overlap into a gate.
            next ??= rangedPull.ChooseNext(permitted,health,position,anchor,(double)options.HuntRadius,(double)options.MeleeRange);
            next ??= rangedPull.ChooseNext(permitted,health,position,anchor,(double)options.HuntRadius,(double)options.HuntRadius*2);
            if (next != null)
            {
                await TagRangedMonster(drive, next, anchor, options, skillDue, token);
                return true;
            }
            rangedPull.BeginGathering(Environment.TickCount64);
        }
        ClearRangedPending();
        if(rangedPull.Phase==RangedPullPhase.Clearing)return false;
        if (!encounter.HasEngaged && rangedPull.AttemptedCount==0)
        {
            if (encounter.Active && nearby==0 && encounter.Candidates.Count==0 && !encounter.HasUnresolvedNearby)
                return false; // Empty encounter: existing pickup/reset can finish.
            if(!encounter.Active)rangedPull.Reset();
            drive.StopApproach(); Input.Release(preserveNearbyPickup:true);
            message = "Ranged packs: waiting for an eligible monster to pull with Firing.";
            await Input.Delay(250, token); return true;
        }
        ReleaseCombatPickup(); drive.StopApproach(); Input.Release(preserveNearbyPickup:true);
        message = $"Melee mode: {rangedPull.AttemptedCount} tagged IDs owned. Waiting for a tagged monster within {options.RangedGatherRadius:0.#} units; pulling resumes only after all tagged IDs are gone.";
        await Input.Delay(100, token); return true;
    }

    async Task TagRangedMonster(Movement drive, Entity target, Vec anchor, Options options,
        Dictionary<char, long> skillDue, CancellationToken token)
    {
        ReleaseCombatPickup(); Input.HoldMouse(false,false,token);
        lockedTarget = target; rangedTagging = true;
        bool retainFire=false;
        bool confirmed=false;
        try
        {
            message = $"Tagging pack {rangedPull.AttemptedCount+1}/{options.RangedPullCount}: {target.DisplayName}";
            if(rangedPendingTarget==null || TargetIdentity(rangedPendingTarget)!=TargetIdentity(target))
            {
                ClearRangedPending(releaseFire:false);
                rangedPendingTarget=target;rangedPendingHealth=world.TargetHealth(target.Id);rangedPendingInput=false;
            }
            if(Input.RightButtonHeld && rangedFireTarget!=null)
            {
                // Held fire can activate while the aim corrections are still in
                // progress. Validate the new sweep target before provisionally
                // owning it, then keep its original HP baseline across frames.
                var aimed=world.Find(target.Id);
                var aimedHp=world.TargetHealth(target.Id);
                var aimedPosition=world.PlayerPosition();
                bool newSweep=!rangedPendingInput;
                if(aimed==null || TargetIdentity(aimed)!=TargetIdentity(target) || !aimedHp.Known || aimedHp.Dead ||
                    newSweep && aimedHp.Current!=aimedHp.Maximum || (aimed.Position-anchor).Length>(double)options.HuntRadius ||
                    TargetGuardReason(aimed,aimedHp,aimedPosition,options)!=null)
                {
                    ClearRangedPending();return;
                }
                if(newSweep)
                {
                    rangedPendingInput=true;
                    if(!courtesy.StartedHere(aimed)){courtesy.MarkAttack(aimed);rangedPendingCourtesy=true;}
                }
            }
            var delta=target.Position-world.PlayerPosition();
            rangedApproaching=delta.Length>(double)options.MeleeRange;
            if(rangedApproaching)
            {
                // Translate independently of camera yaw so aiming remains on the
                // monster while the player closes to Firing range.
                if(rangedFireTarget!=null)rangedFireTarget=target;
                if(!await drive.TravelToward(world,target.Position,target,token))
                {
                    if(!drive.IsTraveling)
                    {
                        ClearRangedPending();rangedPull.BeginGathering(Environment.TickCount64);
                        message="Ranged packs: route to the next monster is blocked; gathering confirmed tags.";
                        return;
                    }
                    await Input.Delay(25,token);
                }
                retainFire=true;return;
            }
            if(rangedFireTarget!=null)rangedFireTarget=target;
            Vec? roam=Input.RightButtonHeld && rangedFireTarget!=null
                ? rangedPull.ChooseRoamWaypoint(world.PlayerPosition(),anchor,target.Position,(double)options.MeleeRange,
                    (double)options.HuntRadius,(from,to)=>drive.CanAdvance?.Invoke(from,to)!=false)
                : null;
            if(roam is Vec travel)await drive.TravelToward(world,travel,target,token);
            else drive.StopTravel();
            if(options.ExperimentalInternalTargeting)
            {
                var selection=world.TrySelectTargetWithCandidates(target,InternalTargetSelectionProbe.CrosshairCandidates);
                if(!selection.Applied)
                {
                    int failures=rangedTargetStateFailures.GetValueOrDefault(TargetIdentity(target))+1;
                    rangedTargetStateFailures[TargetIdentity(target)]=failures;
                    message=$"Internal target selection did not retain {target.DisplayName} ({failures}/3).";
                    TraceLog.Record("internal target selection rejected",new {target.Id,target.Generation,target.Address,selection.Status,Failures=failures});
                    return;
                }
                TraceLog.Record("internal target selection applied",new {target.Id,target.Generation,target.Address,selection.Values});
            }
            else if (!await drive.FaceTarget3D(world,target,token,.01)) {retainFire=true;return;}
            var current = world.Find(target.Id);
            var hp = world.TargetHealth(target.Id);
            var position = world.PlayerPosition();
            if (current == null || TargetIdentity(current) != TargetIdentity(target) || !hp.Known || hp.Dead || (!rangedPendingInput && hp.Current != hp.Maximum) ||
                (current.Position-anchor).Length > (double)options.HuntRadius || (current.Position-position).Length > (double)options.MeleeRange ||
                TargetGuardReason(current,hp,position,options) != null) return;
            if(!options.ExperimentalInternalTargeting && !await WaitForClientTargetState(current,token))
            {
                int failures=rangedTargetStateFailures.GetValueOrDefault(TargetIdentity(current))+1;
                rangedTargetStateFailures[TargetIdentity(current)]=failures;
                message=$"Client did not select {current.DisplayName}; selecting again ({failures}/3).";
                return;
            }
            lockedTarget = current;
            void Started()
            {
                // Temporarily permit health changes during this shot's observation.
                // Submission alone must not enroll the monster or count a tag.
                rangedPendingInput=true;
                if(!courtesy.StartedHere(current)){courtesy.MarkAttack(current);rangedPendingCourtesy=true;}
                TraceLog.Record("ranged shot submitted",new {current.Id,current.Generation,current.Address,Distance=(current.Position-position).Length});
            }
            var bar = CheckedHotbar();
            if (options.AutoDetectSkills)
            {
                options.SkillKeys = AttackKeys(SkillRotation.DetectKeys(bar),bar,options.MaintainAreaBuffs);
                foreach (char key in options.SkillKeys) skillDue.TryAdd(key,0);
            }
            var firingKeys = SkillRotation.DetectFiringKeys(bar);
            bool activated=false;
            char? firedKey=null;
            if(rangedPendingInput && rangedPull.ConfirmShot(current,rangedPendingHealth,hp,false,Environment.TickCount64))
            {
                confirmed=true;
                if(firingKeys.Length>0)firedKey=firingKeys[0];
            }
            else if (firingKeys.Length > 0)
            {
                int index = SkillRotation.ChooseRanged(firingKeys,bar,skillDue,Environment.TickCount64);
                if (index < 0) { await Input.Delay(50,token);retainFire=true; return; }
                char key = firingKeys[index]; var slot = bar.Slot(key);
                if(!HealthSkillAllowed(slot,options))
                {
                    message=$"Mana reserve is holding Firing on {key}; waiting for more MP.";
                    return;
                }
                if(world.SelectedSkill()!=slot.Id)
                {
                    StopRangedFire();
                    await Input.Key((Keys)key,50,token); await Input.Delay(80,token);
                }
                ushort selected=world.SelectedSkill();
                if(selected!=slot.Id)
                    throw new InvalidOperationException($"Firing on key {key} was not selected by the game (expected skill {slot.Id}, read {selected}).");
                firedKey=key;
                Input.HoldRangedFire(token);rangedFireTarget=current;Started();
                var observed=await ObserveRangedShot(drive,current,hp,key,slot,anchor,options,token);
                activated=observed.Activated;
                skillDue[key] = Environment.TickCount64 + SkillRotation.RetryDelayMilliseconds(slot,activated,options.SkillSeconds);
                confirmed=rangedPull.ConfirmShot(current,rangedPendingHealth,observed.Health,activated,Environment.TickCount64);
                TraceLog.Record("ranged tag skill observed",new {current.Id,Key=key,SelectedSkill=selected,Activated=activated,HealthBefore=hp,HealthAfter=observed.Health,Confirmed=confirmed});
            }
            else throw new InvalidOperationException("Firing is no longer available on the active hotbar; stopped before using a basic attack to pull.");
            if(confirmed)
            {
                if(!encounter.Active)
                {
                    encounter.Begin();encounterAnchor=anchor;
                    encounterExistingDrops=world.Loot().Select(i=>(i.KeyA,i.KeyB)).ToHashSet();
                }
                encounter.MarkAttack(current,world.TargetHealth(current.Id));encounterHasAttack=true;
                rangedShotFailures.Remove(TargetIdentity(current));
                rangedTargetStateFailures.Remove(TargetIdentity(current));
                rangedPendingCourtesy=false;rangedPendingTarget=null;rangedPendingInput=false;
                TraceLog.Record("ranged tag confirmed",new{current.Id,SkillActivated=activated,rangedPull.AttemptedCount,
                    Evidence="Pull activation or target damage; nearby distance determines arrival"});
            }
            else
            {
                int failures=rangedShotFailures.GetValueOrDefault(TargetIdentity(current))+1;
                rangedShotFailures[TargetIdentity(current)]=failures;
                message=$"No pull activation observed on {current.DisplayName}; attempt {failures}/3.";
                TraceLog.Record("ranged shot not confirmed",new{current.Id,Key=firedKey,Activated=activated,Failures=failures});
                if(failures>=3)
                {
                    // Keep building the pack from other candidates. This target
                    // is excluded for the current pull rather than stopping a
                    // whole five-monster cycle over one blocked shot.
                    message=$"Skipping {current.DisplayName}: Firing did not activate after three attempts; selecting another pack target.";
                    TraceLog.Record("ranged target skipped after failed firing",new {current.Id,current.Generation,current.Address,Failures=failures});
                    return;
                }
            }
            retainFire=Input.RightButtonHeld && rangedFireTarget!=null;
        }
        catch (TurnUnresponsiveException)
        {
            throw new InvalidOperationException("Could not face the next ranged target; stopped before tagging blindly.");
        }
        catch (RangedTargetOutOfRangeException)
        {
            // Camera-relative movement can briefly lose a moving target at the
            // edge of the range disk. Abandon only this pending shot; the next
            // Tagging iteration may approach it again or choose another member.
            TraceLog.Record("ranged tag range recovery",new {target.Id,rangedPull.AttemptedCount});
        }
        catch (TargetProtectionException ex)
        {
            if(rangedPull.Phase == RangedPullPhase.Tagging)rangedPull.BeginGathering(Environment.TickCount64);
            TraceLog.Record("ranged tag protected",new {target.Id,Reason=ex.Message});
        }
        finally
        {
            if(!retainFire)
            {
                ClearRangedPending();Input.Release(preserveNearbyPickup:true);drive.StopApproach();drive.StopTravel();
            }
            lockedTarget = null; rangedTagging = false;
            rangedApproaching=false;
        }
    }

    async Task<(bool Activated,Health Health)> ObserveRangedShot(Movement drive,Entity target,Health before,char? key,HotbarSlot? beforeSlot,
        Vec anchor,Options options,CancellationToken token)
    {
        long deadline=Environment.TickCount64+800;
        while(true)
        {
            var live=world.Find(target.Id);
            if(live==null || TargetIdentity(live)!=TargetIdentity(target))return (false,default);
            var health=world.TargetHealth(target.Id);
            bool activated=false;
            if(key is char slotKey)
            {
                var slot=CheckedHotbar().Slot(slotKey);
                // A cooldown already running before this target was aimed is not
                // evidence that held fire activated against this target.
                bool wasCooling=beforeSlot is { } prior && (prior.RemainingCooldown>0 || prior.Locked);
                activated=!wasCooling && (slot.RemainingCooldown>0 || slot.Locked) ||
                    beforeSlot is { } baseline && (slot.RemainingCooldown>baseline.RemainingCooldown || slot.LockRemaining>baseline.LockRemaining);
            }
            if(RangedPull.ShotConfirmed(before,health,activated) || Environment.TickCount64>=deadline)return (activated,health);
            Vec? roam=rangedPull.ChooseRoamWaypoint(world.PlayerPosition(),anchor,live.Position,(double)options.MeleeRange,
                (double)options.HuntRadius,(from,to)=>drive.CanAdvance?.Invoke(from,to)!=false);
            if(roam is Vec waypoint)await drive.TravelToward(world,waypoint,live,token);
            else drive.StopTravel();
            await Input.Delay(25,token);
        }
    }
}

