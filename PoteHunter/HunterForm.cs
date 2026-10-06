using System.Text.Json;

namespace PoteHunter;

public sealed partial class HunterForm : Form
{
    const int LootTrackerPollMilliseconds=40;
    readonly bool offlinePreviewMode;
    protected override bool ShowWithoutActivation=>offlinePreviewMode || base.ShowWithoutActivation;
    readonly World world = new();
    readonly CombatCourtesy courtesy = new();
    readonly PlayerGreeting playerGreeting = new();
    readonly CheckBox greetPlayers = new() { Text = "Say hello to recognized players within 25 map units", AutoSize = true, Checked = true };
    readonly Encounter encounter = new();
    readonly CombatPressure combatPressure = new();
    bool defensePending, defenseRepositioning;
    Vec? defenseStep;
    Entity? inferredDefense;
    bool buffInProgress;
    Entity? pendingPriorityGamekeeper;
    bool gamekeeperTransition, priorityInterruptibleActivity;
    readonly ToolTip priorityHint = new();
    readonly Navigation navigation = new();
    readonly ChestCatalog chestCatalog = new();
    readonly LootTracker lootTracker = new();
    readonly LootTrackerLog lootTrackerLog = new(System.IO.Path.Combine(AppContext.BaseDirectory, "loot-wallet-session-log.csv"));
    readonly Dictionary<(int Zone,uint Id),long> chestGuideCooldown = new();
    readonly ZoneMapBackground zoneMapBackground = new();
    readonly TabPage groupPage=new("Group");
    readonly ComboBox tankPicker=new(){DropDownStyle=ComboBoxStyle.DropDownList,Width=230,DisplayMember="Name"};
    readonly CheckBox groupEnabled=new(){Text="Group mode",AutoSize=true};
    readonly NumericUpDown groupFollow=Number(2,20,1);
    readonly NumericUpDown groupAttack=Number(1,30,1);
    readonly NumericUpDown groupLimit=Number(20,300);
    GroupDecision groupDecision=new(GroupAction.Wait,"Group mode is off");
    readonly ListView partyList=new(){View=View.Details,Dock=DockStyle.Fill,FullRowSelect=true};
    readonly Label partyStatus=new(){Dock=DockStyle.Fill,AutoSize=true};
    PartySnapshot currentParty=new(false,[],"Connect to read the party roster");
    string selectedTankName="";
    readonly CheckBox automaticRouting = new() {Text="Route around observed obstacles",AutoSize=true,Checked=true};
    readonly CheckBox autoRevive = new() {Text="Auto revive after death",AutoSize=true,Checked=true};
    readonly NumericUpDown revivalDelaySeconds = Number(0,600);
    readonly CheckBox farmOnArrival = new() {Text="Farm on arrival",AutoSize=true,Checked=true};
    readonly TextBox reviveKey = new() {Text="R",Width=52};
    readonly TabPage navigationPage = new("Navigation");
    readonly Panel navigationCanvas = new() {Dock=DockStyle.Fill};
    readonly Label navigationLabel = new() {Dock=DockStyle.Fill,AutoSize=true};
    readonly Button clearNavigation = new() {Text="Clear observations",AutoSize=true};
    readonly Dictionary<(uint,uint,long),long> unreachableTargets=new();
    Vec navigationPosition;
    int navigationZone;
    int? runZone;
    readonly Queue<LootJob> deferredLoot = new();
    sealed record LootJob(Entity Target,Vec Position,int HoldMs,HashSet<(uint,uint)> ExistingDrops);
    sealed class EncounterInterruptedException : Exception;
    sealed class EngagedTargetPriorityException : Exception;
    sealed class ReturnToHuntingAreaException : Exception;
    sealed class HealingRestInterruptedException : Exception;
    sealed class RecoverBeforeFreshTargetException : Exception;
    sealed class RecoverUnderDamageException : Exception;
    HuntExcursion? activeExcursion;
    long nextInsideTargetCheck;
    HashSet<(uint,uint)>? encounterExistingDrops;
    Vec? encounterAnchor;
    long encounterQuietSince, encounterUnknownSince;
    bool encounterHasAttack;
    bool returningFromPriority;
    bool navigationInputOwned;
    Func<StationaryReturnDefense.Observation>? stationaryReturnDefenseGuard;
    sealed class StationaryReturnYieldException : Exception;
    bool HasNavigationInputOwner => navigationInputOwned || returningFromPriority || deathReturnInProgress || lootGuardPosition.HasValue;
    long nextEngagementObservation;
    double activeMovementBoundary;
    double activeCompletionBoundary;
    bool completionReturnPending;
    string? healingWarning;
    bool healingRestPending;
    HealingRest? healingRest;
    string lastEngagementState="";
    TargetSearchReport? targetSearch;
    string lastTargetWait="";
    readonly CheckBox clearNearby = new() {Text="Before looting",AutoSize=true,Checked=true};
    readonly CheckBox leaveAreaWhenEmpty = new() {Text="Leave when empty, then return",AutoSize=true};
    readonly NumericUpDown nearbyRadius=Number(2,15);
    readonly CheckBox antiKillSteal = new() { Text = "Anti-kill-stealing", AutoSize = true, Checked = true };
    readonly NumericUpDown playerBuffer = Number(1,100);
    readonly DataGridView avoidGrid = new() { Dock = DockStyle.Fill, AllowUserToAddRows = true, AllowUserToDeleteRows = true, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, RowHeadersWidth = 30 };
    readonly TableLayoutPanel protectionPanel = new() { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
    readonly TabPage protectionPage = new("Protection");
    List<AvoidRule> avoidRules = new();
    List<AvoidZone> avoidZones = new();
    uint guardSelfId;
    Options? activeGuardOptions;
    RetreatRecovery? retreatRecovery;
    Movement? retreatDrive;
    long guardRefreshedAt;
    Vec? lootGuardPosition;
    HashSet<(uint,uint)>? lootBeforeFight;
    readonly DataRecorder recorder = new(Path.Combine(AppContext.BaseDirectory, "data"));
    string? recordingError;
    readonly System.Windows.Forms.Timer timer = new() { Interval = 200 };
    readonly CheckBox autoSkills=new(){Text="Auto",AutoSize=true,Checked=true};
    readonly CheckBox smartSkillTargeting=new(){Text="Smart skill targeting",AutoSize=true,Checked=true};
    readonly CheckBox centerAreaSkills=new(){Text="Center area / line skills",AutoSize=true,Checked=true};
    readonly CheckBox retargetSkillTargets=new(){Text="Retarget single targets",AutoSize=true,Checked=true};
    readonly TextBox player = new() {ReadOnly=true,PlaceholderText="Waiting for character...",TabStop=false}, filter = new(), skillKeys = new();
    Entity? detectedCharacter,runCharacter;
    long nextCharacterReconnect;
    readonly NumericUpDown radius = Number(5, 150, 1), melee = Number(1.5m, 10, 1), skillSeconds = Number(1, 120);
    readonly NumericUpDown routeCorridorRadius = new() { Minimum=.5m, Maximum=30, DecimalPlaces=1, Increment=.5m, Value=10, Width=130 };
    readonly NumericUpDown lootPickupRadius = new() { Minimum=.5m, Maximum=30, DecimalPlaces=1, Increment=.5m, Value=10, Width=130 };
    readonly CheckBox ranged = new() { Text = "Ranged (bow/crossbow)", AutoSize = true };
    readonly CheckBox archerClass = new() { Text = "Archer class", AutoSize = true };
    readonly NumericUpDown lootHold = Number(0, 3000);
    readonly NumericUpDown gamekeeperRadius = Number(10,150);
    readonly CheckBox combatPickup = new() {Text="Hold E during skill cooldowns",AutoSize=true,Checked=true};
    readonly CheckBox nearbyLootPickup = new() {Text="Automatic ground loot",AutoSize=true,Checked=true};
    List<GroundItem> nearbyPickupSnapshot = new();
    int nearbyPickupCount;
    long nextNearbyPickupRead;
    long nextLootTrackerRead;
    readonly CheckBox healerMode = new() {Text="Healer mode",AutoSize=true};
    readonly TableLayoutPanel supportSettings=new(){Dock=DockStyle.Top,AutoSize=true,ColumnCount=4,Padding=new Padding(8)};
    readonly TabPage supportPage=new("Healer / Buffs"){AutoScroll=true};
    long nextSupportPreflight;
    readonly CheckBox maintainBuffs=new(){Text="Maintain AOE buffs / chants",AutoSize=true};
    readonly NumericUpDown encourageDuration=Number(0,7200),hardenSkinDuration=Number(0,7200);
    readonly Label buffStatus=new(){AutoSize=true,MaximumSize=new Size(750,0),Margin=new Padding(3,8,3,8)};
    readonly LiveBuffUpkeep buffPolicy=new();
    IReadOnlyList<LiveBuffDecision> buffDecisions=[];
    ActiveEffectSnapshot activeEffects=new(false,"Connect to read active effects",[]);
    readonly CheckBox autoHealingSkills = new() {Text="Auto",AutoSize=true,Checked=true};
    readonly TextBox healingSkillKeys = new() {ReadOnly=true,Width=145};
    readonly NumericUpDown healCharge = Number(50,10000);
    readonly NumericUpDown partyHealBelow = Number(1,100);
    readonly NumericUpDown partyHealRange = Number(1,150);
    int healingSkillCursor;
    bool combatPickupHeld;
    HashSet<(uint,uint)>? combatPickupBaseline;
    Vec? activeHuntAnchor;
    readonly CheckBox prioritizeGamekeeper = new() { Text = "Prioritize Gamekeeper", AutoSize = true, Checked = true };
    readonly CheckBox stationaryGamekeeperPriority = new() { Text = "Hold position during Gamekeeper priority", AutoSize = true };
    readonly CheckBox returnToHuntLocation = new() { Text = "Return to saved hunt location after Gamekeeper", AutoSize = true, Checked = true };
    readonly CheckBox prioritizeBreakables = new() { Text = "Prioritize boxes, barrels & treasure boxes", AutoSize = true, Checked = true };
    readonly CheckBox autoHeal = new() { Text = "Use detected", AutoSize = true, Checked = true };
    readonly NumericUpDown healBelow = Number(1, 95), healDelay = Number(1, 120);
    readonly Label status = new(), position = new(), hint = new();
    readonly ListView list = new() { View = View.Details, FullRowSelect = true, GridLines = false, HideSelection = false, Dock = DockStyle.Fill };
    readonly ListView lootList = new() { View = View.Details, FullRowSelect = true, Dock = DockStyle.Fill };
    readonly TabPage lootPage = new("Ground loot");
    readonly ListView hotbarList = new() { View = View.Details, FullRowSelect = true, Dock = DockStyle.Fill };
    readonly TabPage hotbarPage = new("Hotbar");
    readonly TextBox hotbarDescription = new() { Multiline=true, ReadOnly=true, Dock=DockStyle.Fill, ScrollBars=ScrollBars.Vertical, Text="Select a slotted item to read its description." };
    readonly TextBox lootDescription = new() { Multiline=true, ReadOnly=true, Dock=DockStyle.Fill, ScrollBars=ScrollBars.Vertical, Text="Select a ground item to read its description." };
    string? selectedHotbarKey;
    string? selectedGroundKey;
    readonly Button connect = new() { Text = "Connect / refresh", AutoSize = true }, start = new() { Text = "Start Â· F8", AutoSize = true }, stop = new() { Text = "Stop Â· F9", AutoSize = true };
    readonly TableLayoutPanel settings = new() { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 4, Padding = new Padding(0, 8, 0, 12) };
    readonly Dictionary<Threat, CheckBox> difficultyBoxes = new();
    bool connected, busy, working, hotkeys;
    bool deathRecoveryActive, deathReturnInProgress;
    readonly DeathRecoveryState deathRecovery = new();
    readonly FaultDeathWatch faultDeathWatch = new();
    long recoveryReadFailureAt;
    sealed record HotkeyRegistration(string Key, int Id, bool Registered, int WindowsError);
    const int StartRouteHotkeyId=10, FinishRouteHotkeyId=11;
    static readonly (int Id, Keys Key)[] HotkeyBindings =
        [(6,Keys.F6),(8,Keys.F8),(9,Keys.F9),(StartRouteHotkeyId,Keys.Home),(FinishRouteHotkeyId,Keys.End)];
    readonly List<HotkeyRegistration> hotkeyRegistrations = new();
    string? hotkeyFailure;
    CancellationTokenSource? cancel;
    Movement? movement;
    List<Entity> entities = new();
    List<GroundItem> groundLoot = new();
    Dictionary<uint, Health> latestHealth = new();
    long lastEvidence;
    long startVersion;
    string message = "Connect to read the gameâ€™s live creature list.";
    string DisplayMessage => (hotkeyFailure == null || message == hotkeyFailure ? message : hotkeyFailure + " Â· " + message) +
        (working && healingWarning!=null ? " Â· " + healingWarning : "");
    string? lastCalibrationError;
    Entity? lockedTarget;
    long nextHealAt;
    int? runHotbarPage;
    HotbarSnapshot? currentHotbar;
    int recoveryCursor;
    HealTarget? activeHealTarget;
    string? lastHealingSkill;
    static NumericUpDown Number(decimal min, decimal max, int decimals = 0) => new() { Minimum = min, Maximum = max, DecimalPlaces = decimals, Increment = decimals > 0 ? .5m : 1, Width = 130 };
    public HunterForm() : this(false) { }
    internal HunterForm(bool offlinePreview)
    {
        offlinePreviewMode=offlinePreview;
        try{var o=Options.Read();maintainBuffs.Checked=o.MaintainAreaBuffs;encourageDuration.Value=Math.Clamp(o.EncourageDurationSeconds,0,7200);hardenSkinDuration.Value=Math.Clamp(o.HardenSkinDurationSeconds,0,7200);}catch{maintainBuffs.Checked=false;}
        Text = PoteMemoryProbe.WindowsClientRead.Enabled ? "POTE Hunter Â· Fixed detection" : "POTE Hunter Â· Memory controller"; Size = new Size(1080, 1200); MinimumSize = new Size(920, 1000);
        StartPosition = FormStartPosition.CenterScreen; Font = new Font("Segoe UI", 10); BackColor = Color.FromArgb(20, 25, 34); ForeColor = Color.WhiteSmoke;
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 7, ColumnCount = 1, Padding = new Padding(22) };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 55)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        Controls.Add(root);
        root.Controls.Add(new Label { Text = "POTE HUNTER", Font = new Font("Segoe UI Semibold", 21), AutoSize = true }, 0, 0);
        settings.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 22)); settings.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28)); settings.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 22)); settings.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28));
        void Field(string label, Control control, int row, int col) { settings.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 9, 5, 9) }, col, row); control.Anchor = AnchorStyles.Left | AnchorStyles.Right; settings.Controls.Add(control, col + 1, row); }
        Field("Character (automatic)", player, 0, 0); Field("Name filter (optional)", filter, 0, 2); filter.PlaceholderText = "Blank = any allowed monster";
        Field("Hunt radius", radius, 1, 0);
        var attackRangeLabel = new Label { Text = "Melee distance", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 9, 5, 9) };
        var attackRangeRow = new FlowLayoutPanel { AutoSize = true, WrapContents = true, MaximumSize = new Size(270, 0), Margin = Padding.Empty };
        attackRangeRow.Controls.AddRange([ranged, archerClass, melee]); melee.Anchor = AnchorStyles.Left;
        settings.Controls.Add(attackRangeLabel, 2, 1); settings.Controls.Add(attackRangeRow, 3, 1);
        void ApplyRangedMode()
        {
            if (archerClass.Checked) ranged.Checked = true;
            melee.Maximum = ranged.Checked ? 30m : 10m;
            melee.Value = Math.Clamp(melee.Value, melee.Minimum, melee.Maximum);
            double minimumRange = archerClass.Checked ? Targeting.ArcherAttackRange : Targeting.BowAttackRange;
            if (ranged.Checked && (double)melee.Value < minimumRange) melee.Value = (decimal)minimumRange;
            if (!ranged.Checked && (double)melee.Value > 10) melee.Value = 2m;
            attackRangeLabel.Text = ranged.Checked ? "Attack range" : "Melee distance";
            priorityHint.SetToolTip(attackRangeLabel, ranged.Checked
                ? (archerClass.Checked ? "Archer class: 24-unit engine range. The bot stops at this distance and uses Firing skills while closing." : "Bow/crossbow: 18-unit engine range. The bot stops at this distance and uses Firing skills while closing.")
                : "Melee approach distance. The client's own gate is 1.5 units (ATTACKABLE_RANGE 150).");
        }
        ranged.Click += (_, _) => { if (!ranged.Checked) archerClass.Checked = false; ApplyRangedMode(); };
        archerClass.Click += (_, _) => ApplyRangedMode();
        var skillSelection=new FlowLayoutPanel {AutoSize=true,WrapContents=false,Margin=Padding.Empty};
        skillKeys.Width=145;skillKeys.ReadOnly=true;skillSelection.Controls.AddRange([autoSkills,skillKeys]);
        Field("Skill keys", skillSelection, 2, 0); Field("Skill retry fallback (s)", skillSeconds, 2, 2);
        Field("Loot: hold E (ms)", lootHold, 3, 0);
        settings.Controls.Add(new Label { Text = "Hold W to approach Â· Hold left-click to combo", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 9, 0, 9) }, 2, 3); settings.SetColumnSpan(settings.GetControlFromPosition(2, 3)!, 2);
        var difficultyRow = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        foreach (var kind in new[] { Threat.Grey, Threat.Green, Threat.Yellow, Threat.Orange, Threat.Red, Threat.Gold, Threat.Magenta, Threat.Cyan })
        {
            var box = new CheckBox { Text = kind.ToString(), AutoSize = true, ForeColor = MonsterDefinition.DisplayColor(kind), Margin = new Padding(0, 8, 18, 8) };
            difficultyBoxes.Add(kind, box); difficultyRow.Controls.Add(box);
        }
        settings.Controls.Add(new Label { Text = "Attack these colors", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 4);
        settings.Controls.Add(difficultyRow, 1, 4); settings.SetColumnSpan(difficultyRow, 3);
        healBelow.Width = 52;
        var healingRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        healingRow.Controls.AddRange([autoHeal, new Label { Text = "below", AutoSize = true, Padding = new Padding(0, 4, 0, 0) }, healBelow, new Label { Text = "%", AutoSize = true, Padding = new Padding(0, 4, 0, 0) }]);
        Field("Automatic healing", healingRow, 5, 0); Field("Heal delay (seconds)", healDelay, 5, 2);
        Field("Clear nearby enemies",clearNearby,6,0); Field("Nearby enemy radius",nearbyRadius,6,2);
        var priorityOptions=new FlowLayoutPanel {AutoSize=true,WrapContents=true,Margin=Padding.Empty};
        priorityOptions.Controls.AddRange([prioritizeGamekeeper,stationaryGamekeeperPriority,prioritizeBreakables,returnToHuntLocation]);
        Field("Target priority",priorityOptions,7,0);
        priorityHint.SetToolTip(prioritizeGamekeeper,"Gamekeeper interrupts other fights, healing, sitting, buffs, pickup and returning home. It is pursued within the response radius; other work resumes afterward.");
        priorityHint.SetToolTip(stationaryGamekeeperPriority,"Keeps the character planted during Gamekeeper priority. It attacks the Gamekeeper or already engaged targets only when they are inside the attack range, then resumes normal movement after priority handling.");
        priorityHint.SetToolTip(returnToHuntLocation,"Saves the original hunt location when a Gamekeeper is handled, then routes back there before selecting the next target.");
        priorityHint.SetToolTip(prioritizeBreakables,"When enabled, verified boxes, barrels, and treasure boxes take priority over ordinary monsters. When disabled, they are used only if no valid monster is available.");
        Field("Gamekeeper response radius",gamekeeperRadius,7,2);
        var pickupOptions=new FlowLayoutPanel {AutoSize=true,WrapContents=true,Margin=Padding.Empty};
        pickupOptions.Controls.AddRange([nearbyLootPickup,combatPickup]);
        Field("Automatic pickup",pickupOptions,8,0); settings.SetColumnSpan(pickupOptions,3);
        priorityHint.SetToolTip(nearbyLootPickup,"Solo hunting collects reachable loot within the pickup radius of the saved anchor after enemies clear, then returns to the anchor and saved facing. Hold E only within pickup reach. Group/healer pickup stays near the character.");
        priorityHint.SetToolTip(radius,"Farming and target-selection radius around the anchor. Stationary monsters keep the existing short melee approach and return behavior.");
        priorityHint.SetToolTip(routeCorridorRadius,"Distance from a compatible saved path at which Start/F8 or client recovery may join that route. Independent of the farming and loot radii.");
        priorityHint.SetToolTip(lootPickupRadius,"Solo ground-loot radius around the anchor. Collection stays inside this circle and then returns to the exact anchor and facing; pickup reach remains three map units.");
        priorityHint.SetToolTip(combatPickup,"Optional cooldown pickup when nearby-loot pickup is disabled.");
        Field("Hunting area",leaveAreaWhenEmpty,9,0); settings.SetColumnSpan(leaveAreaWhenEmpty,3);
        var deathRecoveryRow=new FlowLayoutPanel{AutoSize=true,WrapContents=false,Margin=Padding.Empty};
        revivalDelaySeconds.Width=60;
        deathRecoveryRow.Controls.AddRange([autoRevive,new Label{Text="delay",AutoSize=true,Padding=new Padding(8,4,0,0)},revivalDelaySeconds,new Label{Text="s",AutoSize=true,Padding=new Padding(0,4,4,0)},new Label{Text="key",AutoSize=true,Padding=new Padding(4,4,0,0)},reviveKey,farmOnArrival]);
        Field("Death recovery",deathRecoveryRow,14,0); settings.SetColumnSpan(deathRecoveryRow,3);
        priorityHint.SetToolTip(autoRevive,"On: revive and follow the recorded route to your anchor, restoring its facing. Off: stop on death. Record a route in Navigation before enabling.");
        priorityHint.SetToolTip(revivalDelaySeconds,"Wait this many seconds after confirmed death before sending the revive key. The value is stored in the saved route profile and clamped to 0-600 seconds.");
        priorityHint.SetToolTip(reviveKey,"Client-specific death-screen key, for example R or Enter. The setting is saved with the hunt profile.");
        priorityHint.SetToolTip(farmOnArrival,"Keep hunting after returning to this saved route. Clear it to use the route only as a recovery destination.");
        AddManaSettings();
        var healingSkillSelection=new FlowLayoutPanel {AutoSize=true,WrapContents=false,Margin=Padding.Empty};
        healingSkillSelection.Controls.AddRange([autoHealingSkills,healingSkillKeys]);
        root.Controls.Add(settings, 0, 1);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill };
        buttons.Controls.AddRange([connect, start, stop, new Label { Text = "F8 calibrates + hunts Â· F9 stops", AutoSize = true, Padding = new Padding(12, 8, 0, 0) }]); root.Controls.Add(buttons, 0, 2);
        position.Dock = DockStyle.Fill; position.ForeColor = Color.FromArgb(93, 222, 179); position.TextAlign = ContentAlignment.MiddleLeft; root.Controls.Add(position, 0, 3);
        list.Columns.Add("Target", 225); list.Columns.Add("Color / type", 95); list.Columns.Add("HP", 105); list.Columns.Add("Allowed", 85); list.Columns.Add("Map X", 75); list.Columns.Add("Map Y", 75); list.Columns.Add("Distance", 75); list.Columns.Add("Entity ID", 95);
        list.BackColor = Color.FromArgb(28, 35, 47); list.ForeColor = ForeColor;
        var tabs = new HeaderlessTabControl { Dock = DockStyle.Fill }; var monstersPage = new TabPage("Targets"); monstersPage.Controls.Add(list); tabs.TabPages.Add(monstersPage);
        supportPage.BackColor=list.BackColor;supportPage.ForeColor=ForeColor;
        foreach(float width in new[]{22f,28f,22f,28f})supportSettings.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,width));
        void SupportField(string label,Control control,int row,int col)
        {
            supportSettings.Controls.Add(new Label{Text=label,AutoSize=true,Anchor=AnchorStyles.Left,Margin=new Padding(0,7,5,7)},col,row);
            control.Anchor=AnchorStyles.Left|AnchorStyles.Right;supportSettings.Controls.Add(control,col+1,row);
        }
        SupportField("Role",healerMode,0,0);SupportField("Buff upkeep",maintainBuffs,0,2);
        SupportField("Healing skill keys",healingSkillSelection,1,0);SupportField("Charged skill hold (ms)",healCharge,1,2);
        SupportField("Heal party below %",partyHealBelow,2,0);SupportField("Party heal range",partyHealRange,2,2);
        var buffHelp=new Label{Text="Buffs follow their hotbar slots automatically. Live effects control upkeep: INSTANCE = click, CAST = charge, CHANT = turn on once. Party buffs do not use target F-keys. Unknown effects/types are not auto-maintained.",AutoSize=true,MaximumSize=new Size(750,0),Margin=new Padding(3,8,3,8)};
        supportSettings.Controls.Add(buffHelp,0,3);supportSettings.SetColumnSpan(buffHelp,4);
        supportSettings.Controls.Add(buffStatus,0,4);supportSettings.SetColumnSpan(buffStatus,4);
        var saveSupport=new Button{Text="Save support settings",AutoSize=true,FlatStyle=FlatStyle.Flat};
        saveSupport.Click+=(_,_)=>{try{CurrentOptions().Save();message="Healer and buff settings saved.";}catch(Exception ex){message=ex.Message;}};
        AddPotionSupport();
        supportSettings.Controls.Add(saveSupport,0,8);supportSettings.SetColumnSpan(saveSupport,4);
        supportPage.Controls.Add(supportSettings);tabs.TabPages.Add(supportPage);
        lootList.Columns.Add("Ground item", 340); lootList.Columns.Add("Distance", 100); lootList.Columns.Add("Map X", 100); lootList.Columns.Add("Map Y", 100);
        lootList.BackColor = list.BackColor; lootList.ForeColor = ForeColor;
        void ItemPane(TabPage page, ListView rows, TextBox description)
        {
            var pane=new TableLayoutPanel { Dock=DockStyle.Fill, RowCount=2, ColumnCount=1 }; pane.RowStyles.Add(new RowStyle(SizeType.Percent,100)); pane.RowStyles.Add(new RowStyle(SizeType.Absolute,80));
            description.BackColor=list.BackColor; description.ForeColor=ForeColor; pane.Controls.Add(rows,0,0); pane.Controls.Add(description,0,1); page.Controls.Add(pane);
        }
        ItemPane(lootPage,lootList,lootDescription); tabs.TabPages.Add(lootPage);
        hotbarList.Columns.Add("Key", 45); hotbarList.Columns.Add("Detected skill / item", 215); hotbarList.Columns.Add("Type", 65); hotbarList.Columns.Add("Role", 120); hotbarList.Columns.Add("Ready / remaining", 135); hotbarList.Columns.Add("Total cooldown", 110); hotbarList.Columns.Add("ID", 65);
        hotbarList.BackColor = list.BackColor; hotbarList.ForeColor = ForeColor; ItemPane(hotbarPage,hotbarList,hotbarDescription); tabs.TabPages.Add(hotbarPage); root.Controls.Add(tabs, 0, 4);
        protectionPage.BackColor=list.BackColor; protectionPage.ForeColor=ForeColor;
        protectionPanel.RowStyles.Add(new RowStyle(SizeType.Absolute,40)); protectionPanel.RowStyles.Add(new RowStyle(SizeType.Absolute,28));
        protectionPanel.RowStyles.Add(new RowStyle(SizeType.Percent,100)); protectionPanel.RowStyles.Add(new RowStyle(SizeType.Absolute,36));
        var protectionHeader = new FlowLayoutPanel { Dock=DockStyle.Fill, WrapContents=false, Padding=new Padding(6) };
        playerBuffer.Width=70;
        protectionHeader.Controls.AddRange([antiKillSteal,new Label { Text="Keep targets away from other players:",AutoSize=true,Padding=new Padding(12,4,0,0) },playerBuffer,new Label {Text="map units",AutoSize=true,Padding=new Padding(0,4,0,0)}]);
        protectionPanel.Controls.Add(protectionHeader,0,0);
        protectionPanel.Controls.Add(new Label { Text="Do not go near: names match without case; partial monster/player names are supported.",Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft,Padding=new Padding(6,0,0,0)},0,1);
        avoidGrid.Columns.Add(new DataGridViewTextBoxColumn { Name="Name",HeaderText="Monster / player name",FillWeight=75 });
        avoidGrid.Columns.Add(new DataGridViewTextBoxColumn { Name="Radius",HeaderText="Keep away (map units)",FillWeight=25 });
        avoidGrid.BackgroundColor=list.BackColor; avoidGrid.EnableHeadersVisualStyles=false;
        avoidGrid.DefaultCellStyle.BackColor=list.BackColor; avoidGrid.DefaultCellStyle.ForeColor=ForeColor;
        avoidGrid.ColumnHeadersDefaultCellStyle.BackColor=Color.FromArgb(42,54,72); avoidGrid.ColumnHeadersDefaultCellStyle.ForeColor=ForeColor;
        avoidGrid.RowHeadersDefaultCellStyle.BackColor=list.BackColor; avoidGrid.RowHeadersDefaultCellStyle.ForeColor=ForeColor;
        avoidGrid.DefaultValuesNeeded += (_,e) => e.Row.Cells["Radius"].Value=15;
        protectionPanel.Controls.Add(avoidGrid,0,2);
        var saveProtection = new Button { Text="Save protection settings",AutoSize=true,FlatStyle=FlatStyle.Flat,BackColor=Color.FromArgb(42,54,72),ForeColor=ForeColor };
        saveProtection.Click += (_,_) => { try { CurrentOptions().Save(); message="Protection settings saved."; } catch(Exception ex) { message=ex.Message; } };
        protectionPanel.Controls.Add(saveProtection,0,3); protectionPage.Controls.Add(protectionPanel); tabs.TabPages.Add(protectionPage);
        navigationPage.BackColor=list.BackColor; navigationPage.ForeColor=ForeColor;
        var navLayout=new TableLayoutPanel {Dock=DockStyle.Fill,RowCount=3,ColumnCount=1};
        navLayout.RowStyles.Add(new RowStyle(SizeType.Absolute,34)); navLayout.RowStyles.Add(new RowStyle(SizeType.Percent,100)); navLayout.RowStyles.Add(new RowStyle(SizeType.Absolute,44));
        var navControls=new FlowLayoutPanel {Dock=DockStyle.Fill,WrapContents=false};
        clearNavigation.FlatStyle=FlatStyle.Flat; clearNavigation.BackColor=Color.FromArgb(42,54,72); clearNavigation.ForeColor=ForeColor;
        navControls.Controls.AddRange([automaticRouting,clearNavigation]);
        InitializeNavigationOverlay(navControls);
        clearNavigation.Click+=(_,_)=> {navigation.Clear();unreachableTargets.Clear();navigationCanvas.Invalidate();};
        automaticRouting.CheckedChanged+=(_,_)=> {if(!working && !busy) {try{CurrentOptions().Save();}catch(Exception ex){message=ex.Message;}}};
        navigationCanvas.BackColor=Color.FromArgb(18,24,32); navigationCanvas.Paint+=PaintNavigation;
        navLayout.Controls.Add(navControls,0,0); navLayout.Controls.Add(navigationCanvas,0,1); navLayout.Controls.Add(navigationLabel,0,2);
        navigationPage.Controls.Add(navLayout);tabs.TabPages.Add(navigationPage);
        InitializeNavigation3DPage(navLayout,navControls);
        list.ShowItemToolTips=true;
        hotbarList.SelectedIndexChanged += (_,_) => { if (hotbarList.SelectedItems.Count>0) { selectedHotbarKey=hotbarList.SelectedItems[0].Text; UpdateItemDescriptions(); } };
        lootList.SelectedIndexChanged += (_,_) => { if (lootList.SelectedItems.Count>0) { selectedGroundKey=lootList.SelectedItems[0].Tag as string; UpdateItemDescriptions(); } };
        var groupLayout=new TableLayoutPanel{Dock=DockStyle.Fill,RowCount=3,ColumnCount=1};
        groupLayout.RowStyles.Add(new RowStyle(SizeType.Absolute,42));groupLayout.RowStyles.Add(new RowStyle(SizeType.Percent,100));groupLayout.RowStyles.Add(new RowStyle(SizeType.Absolute,58));
        var groupHeader=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false,Padding=new Padding(4)};
        groupPage.BackColor=list.BackColor;groupPage.ForeColor=ForeColor;
        groupFollow.Width=60;groupAttack.Width=60;groupLimit.Width=60;tankPicker.Width=160;
        groupHeader.Controls.AddRange([groupEnabled,new Label{Text="Tank:",AutoSize=true,Padding=new Padding(0,5,0,0)},tankPicker,new Label{Text="Follow:",AutoSize=true,Padding=new Padding(0,5,0,0)},groupFollow,new Label{Text="Attack radius:",AutoSize=true,Padding=new Padding(0,5,0,0)},groupAttack,new Label{Text="Follow limit:",AutoSize=true,Padding=new Padding(0,5,0,0)},groupLimit]);
        partyList.Columns.Add("Party member",230);partyList.Columns.Add("Role",90);partyList.Columns.Add("Nearby",85);partyList.Columns.Add("Distance",90);partyList.Columns.Add("Character ID",120);
        partyList.BackColor=list.BackColor;partyList.ForeColor=ForeColor;
        groupLayout.Controls.Add(groupHeader,0,0);groupLayout.Controls.Add(partyList,0,1);groupLayout.Controls.Add(partyStatus,0,2);
        groupPage.Controls.Add(groupLayout);tabs.TabPages.Add(groupPage);
        AddRangedPullSettings(tabs);
        tankPicker.SelectionChangeCommitted+=(_,_)=> {if(tankPicker.SelectedItem is PartyMember member){selectedTankName=member.Name;if(!working)CurrentOptions().Save();}};
        try{var groupOptions=Options.Read();selectedTankName=groupOptions.GroupTankName;groupEnabled.Checked=groupOptions.GroupMode;groupFollow.Value=Math.Clamp(groupOptions.GroupFollowDistance,2,20);groupAttack.Value=Math.Clamp(groupOptions.GroupAttackRadius,1,30);groupLimit.Value=Math.Clamp(groupOptions.GroupFollowLimit,20,300);}catch{selectedTankName="";groupFollow.Value=4;groupAttack.Value=6;groupLimit.Value=150;}
        hint.Text = "Named keep-away threats trigger retreat, recovery, then automatic resume when safe.\nF8 calibrates + hunts Â· F9 stops. Edit names and distances on the Protection tab.";
        hint.Dock = DockStyle.Fill; hint.ForeColor = Color.Silver; hint.TextAlign = ContentAlignment.MiddleLeft; root.Controls.Add(hint, 0, 5);
        status.Dock = DockStyle.Fill; status.TextAlign = ContentAlignment.MiddleLeft; root.Controls.Add(status, 0, 6);
        foreach (var b in new[] { connect, start, stop }) { b.FlatStyle = FlatStyle.Flat; b.BackColor = Color.FromArgb(42, 54, 72); b.ForeColor = ForeColor; b.Padding = new Padding(8, 2, 8, 2); }
        try { var o = Options.Read(); player.Text = ""; filter.Text = o.Target; radius.Value = Math.Clamp(o.HuntRadius, radius.Minimum, radius.Maximum); routeCorridorRadius.Value=o.RouteCorridorRadius; lootPickupRadius.Value=o.LootPickupRadius; leaveAreaWhenEmpty.Checked=o.LeaveAreaWhenEmpty; autoRevive.Checked=o.AutoReviveAfterDeath; autoRepair.Checked=o.AutoRepairAfterDeath;durabilityRepair.Checked=o.AutoRepairLowDurability;durabilityThreshold.Value=o.RepairDurabilityPercent; visualRevival.Checked=o.VisualRevivalDetection; revivalDelaySeconds.Value=Math.Clamp(o.RevivalDelaySeconds,revivalDelaySeconds.Minimum,revivalDelaySeconds.Maximum); farmOnArrival.Checked=o.FarmOnArrival; reviveKey.Text=string.IsNullOrWhiteSpace(o.ReviveKey)?"R":o.ReviveKey; archerClass.Checked=o.ArcherClass; ranged.Checked = o.Ranged || archerClass.Checked; melee.Maximum = ranged.Checked ? 30m : 10m; double minimumRange = archerClass.Checked ? Targeting.ArcherAttackRange : Targeting.BowAttackRange; melee.Value = Math.Clamp(ranged.Checked ? Math.Max(o.MeleeRange, (decimal)minimumRange) : Math.Min(o.MeleeRange, 10m), melee.Minimum, melee.Maximum); attackRangeLabel.Text = ranged.Checked ? "Attack range" : "Melee distance"; autoSkills.Checked=o.AutoDetectSkills;skillKeys.ReadOnly=o.AutoDetectSkills;skillKeys.Text = o.SkillKeys; autoHealingSkills.Checked=o.AutoDetectHealingSkills;healingSkillKeys.ReadOnly=o.AutoDetectHealingSkills;healingSkillKeys.Text=o.HealingSkillKeys; healCharge.Value=Math.Clamp(o.HealChargeMilliseconds,healCharge.Minimum,healCharge.Maximum); partyHealBelow.Value=Math.Clamp(o.PartyHealBelowPercent,partyHealBelow.Minimum,partyHealBelow.Maximum); partyHealRange.Value=Math.Clamp(o.PartyHealRange,partyHealRange.Minimum,partyHealRange.Maximum); healerMode.Checked=o.HealerMode; skillSeconds.Value = Math.Clamp(o.SkillSeconds, skillSeconds.Minimum, skillSeconds.Maximum); lootHold.Value = Math.Clamp(o.LootHoldMs, lootHold.Minimum, lootHold.Maximum); foreach (var (kind, box) in difficultyBoxes) box.Checked = (o.AllowedDifficulties ?? []).Contains(kind.ToString()); gamekeeperRadius.Value=Math.Clamp(o.GamekeeperResponseRadius,gamekeeperRadius.Minimum,gamekeeperRadius.Maximum); combatPickup.Checked = o.LootDuringSkillCooldowns; nearbyLootPickup.Checked=o.AutoPickupNearbyLoot; combatPickup.Enabled=!nearbyLootPickup.Checked; prioritizeGamekeeper.Checked = o.PrioritizeGamekeeper; stationaryGamekeeperPriority.Checked=o.StationaryGamekeeperPriority; autoHeal.Checked = o.AutoHeal; healBelow.Value = Math.Clamp(o.HealBelowPercent, healBelow.Minimum, healBelow.Maximum); healDelay.Value = Math.Clamp(o.HealDelaySeconds, healDelay.Minimum, healDelay.Maximum); antiKillSteal.Checked=o.AntiKillSteal; playerBuffer.Value=Math.Clamp(o.OtherPlayerRadius,playerBuffer.Minimum,playerBuffer.Maximum); greetPlayers.Checked=o.GreetPlayers; avoidRules=o.AvoidNames ?? new(); Avoidance.Validate(avoidRules); foreach(var rule in avoidRules) avoidGrid.Rows.Add(rule.Name,rule.Radius); clearNearby.Checked=o.ClearNearbyEnemies; nearbyRadius.Value=Math.Clamp(o.NearbyEnemyRadius,nearbyRadius.Minimum,nearbyRadius.Maximum); automaticRouting.Checked=o.AutomaticRouting; }
        catch { player.Text = ""; filter.Text = "Ichman Villager"; message = "Settings could not be loaded. Check the fields before use."; }
        try { var savedOptions=Options.Read(); prioritizeBreakables.Checked=savedOptions.PrioritizeBreakables; returnToHuntLocation.Checked=savedOptions.ReturnToHuntLocationAfterGamekeeper; } catch { }
        prioritizeGamekeeper.CheckedChanged += (_,_) => { if(!working && !busy) { try { CurrentOptions().Save(); } catch(Exception ex) { message=ex.Message; } } };
        stationaryGamekeeperPriority.CheckedChanged += (_,_) => { if(!working && !busy) { try { CurrentOptions().Save(); } catch(Exception ex) { message=ex.Message; } } };
        prioritizeBreakables.CheckedChanged += (_,_) => { if(!working && !busy) { try { CurrentOptions().Save(); } catch(Exception ex) { message=ex.Message; } } };
        returnToHuntLocation.CheckedChanged += (_,_) => { if(!working && !busy) { try { CurrentOptions().Save(); } catch(Exception ex) { message=ex.Message; } } };
        combatPickup.CheckedChanged += (_,_) => {if(!working && !busy) {try{CurrentOptions().Save();}catch(Exception ex){message=ex.Message;}}};
        combatPickup.Enabled=!nearbyLootPickup.Checked;
        nearbyLootPickup.CheckedChanged += (_,_) =>
        {
            combatPickup.Enabled=!nearbyLootPickup.Checked;
            if(!working && !busy){try{CurrentOptions().Save();}catch(Exception ex){message=ex.Message;}}
        };
        leaveAreaWhenEmpty.CheckedChanged += (_,_) => {if(!working && !busy) {try{CurrentOptions().Save();}catch(Exception ex){message=ex.Message;}}};
        autoRevive.CheckedChanged += (_,_) => {if(!working && !busy) {try{CurrentOptions().Save();}catch(Exception ex){message=ex.Message;}}};
        revivalDelaySeconds.ValueChanged += (_,_) => {if(!working && !busy) {try{CurrentOptions().Save();}catch(Exception ex){message=ex.Message;}}};
        farmOnArrival.CheckedChanged += (_,_) => {if(!working && !busy) {try{CurrentOptions().Save();}catch(Exception ex){message=ex.Message;}}};
        reviveKey.Validated += (_,_) => {if(!working && !busy) {try{CurrentOptions().Save();}catch(Exception ex){message=ex.Message;}}};
        autoSkills.CheckedChanged+=(_,_)=>{skillKeys.ReadOnly=autoSkills.Checked;if(autoSkills.Checked && currentHotbar!=null)UpdateSkillKeys(currentHotbar);if(!busy && !working)CurrentOptions().Save();};
        autoHealingSkills.CheckedChanged+=(_,_)=>{healingSkillKeys.ReadOnly=autoHealingSkills.Checked;if(autoHealingSkills.Checked && currentHotbar!=null)UpdateHealingSkillKeys(currentHotbar);if(!busy && !working)CurrentOptions().Save();};
        healerMode.CheckedChanged+=(_,_)=>{if(!busy && !working)CurrentOptions().Save();};
        connect.Click += async (_, _) => await Connect(); stop.Click += (_, _) => Stop("Stopped by user.");
        start.Click += async (_,_) =>
        {
            if(busy || working) return;
            long version=++startVersion;
            if(!connected) await Connect();
            if(!connected || version!=startVersion) return;
            Input.SetForegroundWindow(world.Window);
            await Task.Delay(200);
            if(version==startVersion) await StartHunting(version);
        };
        timer.Tick += async (_, _) => {
            Tick();
            RefreshNavigation3DState();
            UpdateNavigationOverlay();
            if(pendingClientResume!=null && !busy && !working && !clientRecoveryRunning) await RecoverClientIfPending();
            if(!clientRecoveryRunning && pendingClientResume==null && !connected && !busy && !working && Environment.TickCount64>=nextCharacterReconnect)
            { nextCharacterReconnect=Environment.TickCount64+5000; await Connect(); }
        };
        if(!offlinePreview)Shown += async (_, _) => { RegisterKeys(); timer.Start(); await Connect(); };
        FormClosing += (_, e) => { Stop("Closed."); if (busy || working || clientRecoveryRunning) { e.Cancel = true; message = "Stopping. Close again when the current operation has finished."; return; } timer.Stop(); DisposeNavigationOverlay(); zoneMapBackground.Dispose(); world.Dispose(); foreach(var registration in hotkeyRegistrations.Where(result=>result.Registered)) Input.UnregisterHotKey(Handle, registration.Id); };
        Input.Allowed = () => connected && world.CheckInputWindow().Allowed;
        FormClosed += (_,_)=>{DisposeNavigationOverlay();zoneMapBackground.Dispose();};
        WindowsClientInput.Bind(world);
        ApplyModernLayout(root,tabs);
    }
    void RegisterKeys()
    {
        hotkeyRegistrations.Clear();
        foreach (var (id, key) in HotkeyBindings)
        {
            bool registered = Input.RegisterHotKey(Handle, id, 0x4000, (uint)key);
            int error = registered ? 0 : System.Runtime.InteropServices.Marshal.GetLastWin32Error();
            var registration = new HotkeyRegistration(key.ToString(), id, registered, error);
            hotkeyRegistrations.Add(registration);
            TraceLog.Record("hotkey registration", registration);
        }
        // A route-shortcut conflict must not disable the existing hunt controls.
        hotkeys = hotkeyRegistrations.Where(result => result.Id is 6 or 8 or 9).All(result => result.Registered);
        hotkeyFailure = hotkeyRegistrations.All(result=>result.Registered) ? null : "Hotkeys unavailable: " +
            string.Join(", ", hotkeyRegistrations.Where(result => !result.Registered)
                .Select(result => $"{result.Key} (Windows error {result.WindowsError})")) +
            ". Close other bot or hotkey apps, then reopen this bot.";
        if (hotkeyFailure != null) message = hotkeyFailure;
    }
    bool RequireHotkeys()
    {
        if (hotkeys) return true;
        message = hotkeyFailure ?? "Hotkeys are not ready. Reopen this bot before starting.";
        TraceLog.Record("start blocked by hotkeys", new { Reason = message, Registrations = hotkeyRegistrations });
        return false;
    }
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x312)
        {
            int id = m.WParam.ToInt32();
            if(id==ItemGradeHotkeys.Id){if(!offlinePreviewMode&&connected&&Input.GetForegroundWindow()==world.Window)ShowItemGradeUnderCursor();base.WndProc(ref m);return;}
            bool routeKey=id is StartRouteHotkeyId or FinishRouteHotkeyId;
            IntPtr foreground=routeKey ? Input.GetForegroundWindow() : IntPtr.Zero;
            if(!routeKey || foreground==Handle || connected && foreground==world.Window)
                HandleHotkey(id);
        }
        base.WndProc(ref m);
    }
    void HandleHotkey(int id)
    {
        Keys key=HotkeyBindings.FirstOrDefault(binding=>binding.Id==id).Key;
        if(key==Keys.None)return;
        TraceLog.Record("hotkey received",new {Key=key.ToString(),Connected=connected,Working=working,Busy=busy});
        if (id == 9 || id == 8 && working) Stop("Stopped by hotkey.");
        else if (id == 6) _ = Calibrate();
        else if (id == 8) _ = StartHunting();
        else if (id == StartRouteHotkeyId) StartNavigationRouteRecording();
        else if (id == FinishRouteHotkeyId) SaveNavigationRouteFromNavigationTab(false,true);
    }
    Options CurrentOptions()
    {
        var options=CurrentOptionsCore();
        options.MaintainAreaBuffs=maintainBuffs.Checked;
        options.EncourageDurationSeconds=encourageDuration.Value;
        options.HardenSkinDurationSeconds=hardenSkinDuration.Value;
        options.GreetPlayers=greetPlayers.Checked;
        options.GreetingRadius=25;
        return WithItemGradeSettings(options);
    }
    Options CurrentOptionsCore()
    {
        string keys = new(skillKeys.Text.Where(c => !char.IsWhiteSpace(c) && c != ',').ToArray());
        string healingKeys = new(healingSkillKeys.Text.Where(c => !char.IsWhiteSpace(c) && c != ',').ToArray());
        string configuredReviveKey=reviveKey.Text.Trim();
        if(!TryParseReviveKey(configuredReviveKey,out var parsedReviveKey))
            throw new InvalidOperationException("Revive key must be a valid keyboard key other than Escape or the bot hotkeys.");
        configuredReviveKey=parsedReviveKey.ToString();
        // The connected client supplies the character name; no typed name is required.
        if (keys.Any(c => !"1234567890".Contains(c)) || keys.Distinct().Count() != keys.Length) throw new InvalidOperationException("Skill keys must be unique digits from 1 through 0.");
        if (healingKeys.Any(c => !"1234567890".Contains(c)) || healingKeys.Distinct().Count() != healingKeys.Length) throw new InvalidOperationException("Healing skill keys must be unique digits from 1 through 0.");
        if(groupEnabled.Checked && (groupFollow.Value>groupLimit.Value || groupAttack.Value>groupLimit.Value))throw new InvalidOperationException("Group follow distance and attack radius must fit inside the follow limit.");
        avoidGrid.EndEdit();
        var rules=new List<AvoidRule>();
        foreach (DataGridViewRow row in avoidGrid.Rows)
        {
            if (row.IsNewRow) continue;
            string name=Convert.ToString(row.Cells["Name"].Value)?.Trim() ?? "";
            if(name.Length==0) continue;
            if (!double.TryParse(Convert.ToString(row.Cells["Radius"].Value),out double distance)) throw new InvalidOperationException("Each avoid name needs a distance from 1 to 150.");
            rules.Add(new AvoidRule(name,distance));
        }
        Avoidance.Validate(rules); avoidRules=rules;
        return WithOverlaySettings(WithManaSettings(WithRangedPullSettings(new Options { Player = player.Text.Trim(), Target = filter.Text.Trim(), HuntRadius = radius.Value, TurnSpeedDegreesPerSecond=turnSpeedLimit.Value, RouteCorridorRadius=routeCorridorRadius.Value, LootPickupRadius=lootPickupRadius.Value, LeaveAreaWhenEmpty=leaveAreaWhenEmpty.Checked, AutoReviveAfterDeath=autoRevive.Checked, AutoRepairAfterDeath=autoRepair.Checked, AutoRepairLowDurability=durabilityRepair.Checked, RepairDurabilityPercent=durabilityThreshold.Value, VisualRevivalDetection=visualRevival.Checked, RevivalDelaySeconds=(int)revivalDelaySeconds.Value, FarmOnArrival=farmOnArrival.Checked, ReviveKey=configuredReviveKey, MeleeRange = (decimal)Targeting.AttackRange(ranged.Checked || archerClass.Checked, archerClass.Checked, (double)melee.Value), Ranged = ranged.Checked || archerClass.Checked, ArcherClass = archerClass.Checked, SkillKeys = keys, AutoDetectSkills=autoSkills.Checked, SkillSeconds = skillSeconds.Value, LootHoldMs = lootHold.Value, AllowedDifficulties = difficultyBoxes.Where(kv => kv.Value.Checked).Select(kv => kv.Key.ToString()).ToArray(), GamekeeperResponseRadius = gamekeeperRadius.Value, LootDuringSkillCooldowns = combatPickup.Checked, AutoPickupNearbyLoot=nearbyLootPickup.Checked, HealerMode=healerMode.Checked, AutoDetectHealingSkills=autoHealingSkills.Checked, HealingSkillKeys=healingKeys, HealChargeMilliseconds=healCharge.Value, PartyHealBelowPercent=partyHealBelow.Value, PartyHealRange=partyHealRange.Value, PrioritizeGamekeeper = prioritizeGamekeeper.Checked, StationaryGamekeeperPriority=stationaryGamekeeperPriority.Checked, ReturnToHuntLocationAfterGamekeeper=returnToHuntLocation.Checked, PrioritizeBreakables=prioritizeBreakables.Checked, AutoHeal = autoHeal.Checked, HealBelowPercent = healBelow.Value, HealDelaySeconds = healDelay.Value, AntiKillSteal=antiKillSteal.Checked, OtherPlayerRadius=playerBuffer.Value, AvoidNames=rules, ClearNearbyEnemies=clearNearby.Checked, NearbyEnemyRadius=nearbyRadius.Value, AutomaticRouting=automaticRouting.Checked,GroupTankName=selectedTankName,GroupMode=groupEnabled.Checked,GroupFollowDistance=groupFollow.Value,GroupAttackRadius=groupAttack.Value,GroupFollowLimit=groupLimit.Value })));
    }
    async Task Connect()
    {
        if (busy || working) return;
        ClearPlayerRecognition();
        nextCharacterReconnect=Environment.TickCount64+5000;
        busy = true; connected = false; movement = null; connect.Enabled = false; message = "Reading active creaturesâ€¦";
        player.Text="";
        WriteState(new { TimeUtc=DateTime.UtcNow, Connected=false, Working=false, Calibrated=false, Status=message });
        try { var options = CurrentOptions(); options.Save(); await Task.Run(world.Connect); WindowsClientInput.ValidateReady(); connected = true; UpdateDetectedCharacter(world.LocalPlayer()); message = (world.AutomaticProfile ? "Updated client detected automatically. " : "Connected. ") + "Press F8 or Start to calibrate and hunt."; }
        catch (Exception ex) { message = ex.Message; }
        finally { busy = false; connect.Enabled = true; }
        if (!connected) {player.Text=""; WriteState(new { TimeUtc = DateTime.UtcNow, Connected = false, Working = false, Calibrated = false, Status = message });}
        if (connected)
        {
            Tick(); await Task.Delay(300);
            using var preview = new Bitmap(Width, Height); DrawToBitmap(preview, new Rectangle(0, 0, Width, Height));
            preview.Save(Path.Combine(AppContext.BaseDirectory, "preview.png"));
            if(supportPage.Parent is TabControl supportTabs)
            {
                var selected=supportTabs.SelectedTab;
                try{supportTabs.SelectedTab=supportPage;PerformLayout();using var supportPreview=new Bitmap(Width,Height);DrawToBitmap(supportPreview,new Rectangle(0,0,Width,Height));supportPreview.Save(Path.Combine(AppContext.BaseDirectory,"preview-support.png"));}
                finally{supportTabs.SelectedTab=selected;}
            }
            if (protectionPage.Parent is TabControl pages)
            {
                var selected=pages.SelectedTab;
                try { pages.SelectedTab=protectionPage; PerformLayout(); using var protectionPreview=new Bitmap(Width,Height); DrawToBitmap(protectionPreview,new Rectangle(0,0,Width,Height)); protectionPreview.Save(Path.Combine(AppContext.BaseDirectory,"preview-protection.png")); }
                finally { pages.SelectedTab=selected; }
            }
            if(navigationPage.Parent is TabControl navigationTabs)
            {
                var selected=navigationTabs.SelectedTab;
                try {navigationTabs.SelectedTab=navigationPage;PerformLayout();using var navPreview=new Bitmap(Width,Height);DrawToBitmap(navPreview,new Rectangle(0,0,Width,Height));navPreview.Save(Path.Combine(AppContext.BaseDirectory,"preview-navigation.png"));}
                finally {navigationTabs.SelectedTab=selected;}
            }
            if(groupPage.Parent is TabControl groupTabs)
            {
                var selected=groupTabs.SelectedTab;
                try{groupTabs.SelectedTab=groupPage;PerformLayout();using var groupPreview=new Bitmap(Width,Height);DrawToBitmap(groupPreview,new Rectangle(0,0,Width,Height));groupPreview.Save(Path.Combine(AppContext.BaseDirectory,"preview-group.png"));}
                finally{groupTabs.SelectedTab=selected;}
            }
        }
    }
    void UpdateSkillKeys(HotbarSnapshot bar)
    {
        if(!autoSkills.Checked)return;
        string detected=AttackKeys(SkillRotation.DetectKeys(bar),bar,maintainBuffs.Checked);
        if(skillKeys.Text==detected)return;
        skillKeys.Text=detected;
        if(!busy && !working)CurrentOptions().Save();
    }
    void UpdateHealingSkillKeys(HotbarSnapshot bar)
    {
        if(!autoHealingSkills.Checked)return;
        string detected=HealerPolicy.DetectHealingKeys(bar);
        if(healingSkillKeys.Text==detected)return;
        healingSkillKeys.Text=detected;
        if(!busy && !working)CurrentOptions().Save();
    }
    void UpdateDetectedCharacter(Entity self)
    {
        bool changed=detectedCharacter!=null && !LocalCharacter.Same(detectedCharacter,self);
        if(changed)ClearPlayerRecognition();
        bool revivedBody=changed && deathRecovery.Pending && RecoveryRouting.SameCharacter(detectedCharacter!,self);
        if(changed && !revivedBody)
        {
            movement=null;
            if(working)Stop("Character changed. Press F8 to start with "+self.Name+".");
        }
        bool nameChanged=player.Text!=self.Name;
        detectedCharacter=self;player.Text=self.Name;
        if(nameChanged)CurrentOptions().Save();
    }
    void Tick()
    {
        status.Text = DisplayMessage + (recordingError == null ? "" : " Â· Data recording: " + recordingError) + (HuntingSessionLog.Current?.LastError is string logError ? " · Session log: "+logError : "");
        lootTracker.ObserveActivity(working && connected && activeGuardOptions!=null);
        if(!connected){latestDurability=new(false,"",[],DateTime.UtcNow,"Client disconnected");durabilityStatus.Text="Durability unavailable: Client disconnected";ClearPlayerRecognition();HuntingSessionLog.Current?.ObservationGap("Client disconnected");lootTracker.ObserveWallet(new(false,0,"",DateTime.UtcNow,"Client disconnected"));return;}
        if(busy)return;
        try
        {
            if(working && !world.ClientProcessAlive && TryQueueClientRecovery())return;
            if (working && !Input.Allowed()) Stop("Stopped: switched away from the game. Recalibrate before starting again.");
            int beforeZone=world.ActiveZone();
            Entity self;
            try { entities=world.Poll();self=world.LocalPlayer();recoveryReadFailureAt=0; }
            catch(InvalidOperationException) when(working && deathRecovery.Pending && !deathReturnInProgress && cancel?.IsCancellationRequested==false)
            {
                ClearPlayerRecognition();Input.Release();
                long now=Environment.TickCount64;
                if(recoveryReadFailureAt==0)recoveryReadFailureAt=now;
                if(now-recoveryReadFailureAt>15000)throw;
                message="Waiting for the character to reappear after death";
                return;
            }
            UpdateDetectedCharacter(self);
            lootTracker.ObserveWallet(world.ReadWallet());
            RefreshDurabilityStatus();
            Vec pos = self.Position;
            var health = world.HealthSnapshot();
            latestHealth = health;
            navigationZone=world.ActiveZone(); navigationPosition=pos;
            navigation3DPlayerHeight=self.Height;navigation3DPlayerHeading=self.Heading;navigation3DPlayerSeen=Environment.TickCount64;
            ObserveLoggedHealth(self,health.GetValueOrDefault(self.Id),navigationZone);
            chestCatalog.Observe(navigationZone,entities.Where(entity=>Targeting.IsChest(entity) && !health.GetValueOrDefault(entity.Id).Dead));
            lootTracker.ObserveZone(navigationZone);
            if(beforeZone!=navigationZone) {ClearPlayerRecognition();navigation.Clear();if(working)Stop("Map zone changed; stopped.");return;}
            navigation.Observe(world.NavigationContext(self),pos,self.Height);
            if(working && runZone.HasValue && navigationZone!=runZone) Stop("Map zone changed; stopped.");
            navigationLabel.Text=$"Zone {navigationZone} · {ZonePlayerStatus()} · {navigation.Status}\nPlayer diamonds: pink enemy / blue same faction / teal party / gold non-PvP opponent / gray unknown · {(showNavigationRoutes.Checked ? "Routes shown" : "Routes hidden")} · Gold boxes: treasure";
            RefreshNavigationRecordingControls();
            navigationCanvas.Invalidate();
            guardSelfId=self.Id;
            UpdateParty(self);
            UpdatePlayerRecognition(self);
            avoidZones=Avoidance.BuildZones(avoidRules,entities,self.Id);
            int level = world.PlayerLevel();
            if(working)ObserveDeath(health.GetValueOrDefault(self.Id));
            if(working && !deathRecovery.Pending && !deathReturnInProgress && !repairInProgress && activeGuardOptions?.GroupMode==true)RefreshGroupDecision();
            if(working && !deathRecovery.Pending && !deathReturnInProgress && !repairInProgress && activeGuardOptions is {GroupMode:false} combatOptions) ObserveEncounter(combatOptions,health,pos,level);
            var selfHealth = health.GetValueOrDefault(self.Id);
            var selfMana = world.ReadMana();
            int chestCount = entities.Count(e => Targeting.IsChest(e) && !health.GetValueOrDefault(e.Id).Dead);
            position.Text = $"HP {(selfHealth.Known ? $"{Math.Max(0,selfHealth.Current)}/{selfHealth.Maximum}" : "unknown")} · MP {(selfMana.Known ? $"{selfMana.Current}/{selfMana.Maximum}" : "unknown")}";
            priorityHint.SetToolTip(position,$"{self.Name} · Lv. {level} · {pos.X:F2}, {pos.Y:F2} · {entities.Count(e=>e.Monster)} monsters");
            string[] allowedColors = difficultyBoxes.Where(kv => kv.Value.Checked).Select(kv => kv.Key.ToString()).ToArray();
            list.BeginUpdate(); list.Items.Clear();
            foreach (var e in entities.Where(e => e.Targetable).OrderByDescending(e => Targeting.PriorityRank(e,prioritizeGamekeeper.Checked,prioritizeBreakables.Checked)).ThenBy(e => (e.Position - pos).Length).Take(30))
            {
                Threat threat = world.Difficulty(e, level);
                var hp = health.GetValueOrDefault(e.Id);
                string? protectedReason=TargetGuardReason(e,hp,pos);
                bool engaged = encounter.IsEngaged(e);
                bool allowed = (engaged && hp.Known && !hp.Dead || Targeting.Eligible(e, hp, threat, filter.Text, allowedColors,prioritizeGamekeeper.Checked)) && protectedReason==null;
                double targetLimit=prioritizeGamekeeper.Checked && Targeting.IsGamekeeper(e) ? Targeting.ResponseRadius((double)radius.Value,(double)gamekeeperRadius.Value) :
                    leaveAreaWhenEmpty.Checked ? (double)radius.Value : Targeting.TargetRadius(e,prioritizeGamekeeper.Checked,(double)radius.Value,(double)gamekeeperRadius.Value);
                bool outsideArea=working && activeGuardOptions?.GroupMode!=true && activeHuntAnchor is Vec areaCenter && !engaged && (e.Position-areaCenter).Length>targetLimit;
                string? areaReason=outsideArea && allowed ? $"{(e.Position-activeHuntAnchor!.Value).Length:F1} units from original hunt center; home radius {targetLimit:F0}."+
                    (leaveAreaWhenEmpty.Checked ? $" Outside targets up to {targetLimit*2:F0} are considered only when no approved targets remain inside; return follows each outside fight." : " Distance column is from your character.") : null;
                string protectedLabel=protectedReason?.StartsWith("Player nearby") == true ? "Player near" : protectedReason?.StartsWith("Already damaged") == true ? "Damaged" : "Avoid";
                var row = new ListViewItem([e.DisplayName, e.PriorityLootObject ? "Breakable" : threat.ToString(), hp.Known ? $"{Math.Max(0,hp.Current)}/{hp.Maximum}" : "Unknown", hp.Dead ? "Dead" : protectedReason!=null ? protectedLabel : allowed ? engaged ? "Engaged" : outsideArea ? "Outside area" : Targeting.PriorityRank(e,prioritizeGamekeeper.Checked,prioritizeBreakables.Checked)>0 ? "Priority" : "Yes" : "No", e.Position.X.ToString("F2"), e.Position.Y.ToString("F2"), (e.Position - pos).Length.ToString("F1"), $"{e.Id:X8}"]) {ToolTipText=protectedReason??areaReason??""};
                row.ForeColor = e.PriorityLootObject ? Color.LightSkyBlue : MonsterDefinition.DisplayColor(threat);
                if (lockedTarget?.Id == e.Id) row.BackColor = Color.FromArgb(53, 61, 78);
                list.Items.Add(row);
            }
            list.EndUpdate();
            groundLoot = world.Loot();
            Vec? lootCenter=working?activeHuntAnchor:null;
            double? lootRadius=working?(double?)(activeGuardOptions?.HuntRadius ?? radius.Value):null;
            lootTracker.ObserveDrops(groundLoot,navigationZone,lootCenter,lootRadius); lootPage.Text = $"Ground loot ({groundLoot.Count})";
            string? lootTop = lootList.TopItem?.Tag as string;
            lootList.BeginUpdate(); lootList.Items.Clear();
            foreach (var item in groundLoot.OrderBy(item => (item.Position-pos).Length).Take(100))
            {
                string key=$"{item.KeyA}:{item.KeyB}"; var row=new ListViewItem([item.Name,(item.Position-pos).Length.ToString("F1"),item.Position.X.ToString("F2"),item.Position.Y.ToString("F2")]) {Tag=key};
                lootList.Items.Add(row); if (key==selectedGroundKey) row.Selected=true;
            }
            if (lootTop!=null) { var top=lootList.Items.Cast<ListViewItem>().FirstOrDefault(r=>(r.Tag as string)==lootTop); if (top!=null) lootList.TopItem=top; }
            lootList.EndUpdate();
            currentHotbar = world.Hotbar(); UpdateSkillKeys(currentHotbar); UpdateHealingSkillKeys(currentHotbar); hotbarPage.Text = $"Hotbar Â· {currentHotbar.Page}";
            ObserveBuffs(activeGuardOptions??CurrentOptions(),currentHotbar);
            supportSettings.Enabled=!working;
            if (working && !deathRecovery.Pending && !deathReturnInProgress && !repairInProgress && runHotbarPage.HasValue && currentHotbar.PageBase != runHotbarPage.Value) Stop("Hotbar page changed; stopped. Check the selected keys before restarting.");
            string? hotbarTop = hotbarList.TopItem?.Text;
            hotbarList.BeginUpdate(); hotbarList.Items.Clear();
            foreach (var slot in currentHotbar.Slots)
            {
                string state = slot.Kind == SlotKind.Empty ? "Empty" : slot.Locked ? $"Locked ({Math.Max(0,slot.LockRemaining)/1000.0:F1}s)" : slot.Ready ? "Ready" : $"{slot.RemainingCooldown/1000.0:F1}s";
                string role = PotionItems.Role(slot);
                if(role.Length==0 && slot.Kind==SlotKind.Skill && skillKeys.Text.Contains(slot.Key))role="Attack skill";
                var buff=buffDecisions.FirstOrDefault(b=>b.Key==slot.Key && b.SkillId==slot.Id);
                if(buff!=null)role=buff.Eligible?$"Party {(slot.SkillUse==SkillUseKind.Chant?"chant":"buff")}":"Effect: monitor only";
                else if(HealerPolicy.IsHealingSkill(slot))role="Healing skill";
                var row = new ListViewItem([slot.Key, slot.Name, slot.Kind==SlotKind.Skill?slot.SkillUse.ToString():slot.Kind.ToString(), role, state, slot.TotalCooldown > 0 ? $"{slot.TotalCooldown/1000.0:F1}s" : "None reported", slot.Id == 0 ? "" : slot.Id.ToString()]);
                if (slot.Ready) row.ForeColor = Color.FromArgb(93,222,179);
                hotbarList.Items.Add(row);
                if (slot.Key==selectedHotbarKey) row.Selected=true;
            }
            if (hotbarTop!=null) { var top=hotbarList.Items.Cast<ListViewItem>().FirstOrDefault(r=>r.Text==hotbarTop); if (top!=null) hotbarList.TopItem=top; }
            hotbarList.EndUpdate();
            UpdateItemDescriptions();
            if (Environment.TickCount64 - lastEvidence > 1000)
            {
                lastEvidence = Environment.TickCount64;
                RecordObservations(self, level, health);
                var targetState=world.TargetState();
                WriteState(new { TimeUtc = DateTime.UtcNow, Connected = true, Working = working, Calibrated = movement != null, Player = self.Name, PlayerHP = selfHealth, PlayerMP = selfMana, ManaRecoveryStatus=manaRecoveryStatus, CameraSupported=world.CameraSupported, CameraStatus=world.CameraStatus, TargetState=new {targetState.Available,targetState.Status,TargetIds=targetState.Ids.Select(id=>$"0x{id:X8}").ToArray()}, Level = level, Position = pos, Hotbar = currentHotbar, DetectedHealingItems = currentHotbar.Slots.Where(RecoveryItems.Recognized), DetectedManaItems=currentHotbar.Slots.Where(ManaRecovery.Recognized), Radar=new{Enabled=showNavigationOverlay.Checked,Visible=navigationOverlay is {Visible:true},RoutesVisible=showNavigationRoutes.Checked,Size=(int)navigationOverlaySize.Value}, LootTrackerOverlay=new{Enabled=showLootTrackerOverlay.Checked,Visible=lootTrackerOverlay is {Visible:true},Position=lootTrackerOverlay?.Location}, LootTracker=lootTracker.Snapshot(), Healer = HealerState(), Recording = new { Enabled = true, ObjectCount = entities.Count, Error = recordingError }, Protection = ProtectionState(), Combat = CombatState(), Group = GroupState(), Navigation = navigation.Snapshot(), Gamekeepers=entities.Where(Targeting.IsGamekeeper).Select(e=>new {
                    e.Id,e.Position,HP=health.GetValueOrDefault(e.Id),Distance=(e.Position-pos).Length,
                    AnchorDistance=(e.Position-(activeHuntAnchor ?? pos)).Length,
                    ResponseRadius=Targeting.ResponseRadius((double)(activeGuardOptions?.HuntRadius ?? radius.Value),(double)(activeGuardOptions?.GamekeeperResponseRadius ?? gamekeeperRadius.Value)),
                    Protection=TargetGuardReason(e,health.GetValueOrDefault(e.Id),pos)
                }), LockedTarget = lockedTarget == null ? null : new { lockedTarget.Name, lockedTarget.DisplayName, lockedTarget.Id, lockedTarget.Generation, lockedTarget.PriorityLootObject }, Status = message, GroundLoot = groundLoot.OrderBy(i => (i.Position-pos).Length).Take(20), PriorityObjects = entities.Where(e => e.PriorityLootObject).OrderBy(e => (e.Position-pos).Length).Select(e => new { e.DisplayName, e.Name, e.Id, e.Model, e.Position, HP = health.GetValueOrDefault(e.Id), Allowed = Targeting.Eligible(e, health.GetValueOrDefault(e.Id), Threat.Unknown, filter.Text, allowedColors) && TargetGuardReason(e,health.GetValueOrDefault(e.Id),pos)==null, Distance = (e.Position-pos).Length }), Monsters = entities.Where(e => e.Monster).OrderBy(e => (e.Position - pos).Length).Select(e => new { e.Name, e.Id, e.Position, HP = health.GetValueOrDefault(e.Id), Difficulty = world.Difficulty(e, level).ToString(), Distance = (e.Position - pos).Length }) });
            }
        }
        catch (Exception ex) { ClearPlayerRecognition();connected = false; player.Text=""; nextCharacterReconnect=Environment.TickCount64+3000; if(!TryQueueClientRecovery())Stop(ex.Message); WriteState(new { TimeUtc = DateTime.UtcNow, Connected = false, Working = false, Calibrated = false, Status = message }); }
    }
    void RecordObservations(Entity self, int level, Dictionary<uint, Health> health)
    {
        try
        {
            var observed = entities.Select(e =>
            {
                uint? prototype = (e.Id & 0xf0000000) == 0x80000000 ? e.Id & 0xffff : null;
                MonsterDefinition definition = prototype.HasValue ? world.Definition(prototype.Value) : default;
                return new { e.Id, e.Generation, e.Name, e.DisplayName, e.Model, e.Position, e.Height, e.Heading,
                    HP = health.GetValueOrDefault(e.Id), PrototypeId = prototype, Definition = definition,
                    e.Monster, e.PriorityLootObject, e.Targetable };
            }).ToArray();
            recorder.Capture(new { TimeUtc = DateTime.UtcNow, ClientSha256 = world.ClientHash, ProcessId = world.Pid,
                Working = working, PlayerId = self.Id, PlayerLevel = level, LoadedObjectCount = world.CandidateCount,
                ReadableObjectCount = observed.Length, Objects = observed, GroundItems = groundLoot, LootTracker = lootTracker.Snapshot(), Hotbar = currentHotbar,
                LockedTargetId = lockedTarget?.Id, Status = message, Protection = ProtectionState(), Combat = CombatState(), Group = GroupState(), Navigation = navigation.Snapshot() },
                observed.Select(e => new ObjectTypeObservation($"{e.Id >> 28:X}:{e.PrototypeId?.ToString() ?? e.Name}:{e.Model}",
                    e.PrototypeId, e.Name, e.Model, e.Definition.Name ?? "", e.PrototypeId.HasValue ? e.Definition.Level : null,
                    e.PrototypeId.HasValue ? e.Definition.Category : null, e.PriorityLootObject)));
            recordingError = recorder.LastError;
        }
        catch (Exception ex) { recordingError = ex.Message; }
    }
    object ProtectionState() => new { AntiKillSteal=activeGuardOptions?.AntiKillSteal ?? antiKillSteal.Checked,
        OtherPlayerRadius=activeGuardOptions?.OtherPlayerRadius ?? playerBuffer.Value, AvoidNames=avoidRules, ActiveAvoidZones=avoidZones,
        DeathRecovery=new {Enabled=activeGuardOptions?.AutoReviveAfterDeath ?? autoRevive.Checked,deathRecovery.Pending,
            deathRecovery.Episode,deathRecovery.PostRevivalPrepared,deathRecovery.RepairCompleted,
            AutoRepair=activeGuardOptions?.AutoRepairAfterDeath ?? autoRepair.Checked,
            VisualRevival=activeGuardOptions?.VisualRevivalDetection ?? visualRevival.Checked,
            Phase=repairInProgress?"Repairing":deathReturnInProgress?savedReturnPhase:deathRecoveryActive?"Reviving":deathRecovery.Pending?"Waiting":faultDeathWatch.Active?"Fault death watch":"Inactive",
            FaultReason=faultDeathWatch.Reason},
        Recovery=new {Enabled=true,Phase=retreatRecovery?.Phase.ToString() ?? "Inactive",retreatRecovery?.HealthTarget,ClearanceBeyondRule=RetreatPlanner.Clearance,
            world.RestSupported,RestPosture=world.RestSupported?world.RestState().Posture.ToString():"Unavailable"},
        PlayerRecognition=PlayerRecognitionState(),OtherPlayers=RecognizedPlayerState() };
    void UpdateParty(Entity self)
    {
        currentParty=world.Party();
        ApplyPartyNames();
        var candidates=currentParty.Members.Where(m=>m.Id!=self.Id).ToArray();
        if(string.IsNullOrWhiteSpace(selectedTankName))selectedTankName=candidates.FirstOrDefault(m=>m.Leader)?.Name ?? "";
        if(!tankPicker.Items.Cast<PartyMember>().SequenceEqual(candidates))
        {
            tankPicker.Items.Clear();tankPicker.Items.AddRange(candidates);
            tankPicker.SelectedItem=candidates.FirstOrDefault(m=>m.Name.Equals(selectedTankName,StringComparison.OrdinalIgnoreCase));
        }
        tankPicker.Enabled=!working && currentParty.Available;
        groupEnabled.Enabled=!working&&!rangedPullEnabled.Checked;groupFollow.Enabled=!working&&!rangedPullEnabled.Checked;groupAttack.Enabled=!working&&!rangedPullEnabled.Checked;groupLimit.Enabled=!working&&!rangedPullEnabled.Checked;
        partyList.BeginUpdate();partyList.Items.Clear();
        foreach(var member in currentParty.Members)
        {
            var entity=entities.FirstOrDefault(e=>e.Id==member.Id);
            partyList.Items.Add(new ListViewItem([member.Name,member.Leader?"Leader":"Member",entity!=null?"Yes":"Not loaded",entity==null?"â€”":(entity.Position-self.Position).Length.ToString("F1"),$"{member.Id:X8}"]));
        }
        partyList.EndUpdate();
        partyStatus.Text=currentParty.Status+" Â· "+(string.IsNullOrWhiteSpace(selectedTankName)?"Select a tank from the party list.":"Selected tank: "+selectedTankName)+Environment.NewLine+
            (working && activeGuardOptions?.GroupMode==true?groupDecision.Status:healerMode.Checked?"Healbot: follows the selected tank and heals nearby party members. Attack radius is unused; pickup is off.":"Follows the tank and attacks allowed targets inside the attack radius. Other-party players and avoidance rules still apply. Group pickup is off.");
    }
    object GroupState()=>new {Enabled=activeGuardOptions?.GroupMode ?? groupEnabled.Checked,Tank=activeGuardOptions?.GroupTankName ?? selectedTankName,
        Party=currentParty,Action=groupDecision.Action.ToString(),Status=working?groupDecision.Status:groupEnabled.Checked?"Ready to follow selected tank":"Group mode is off",TargetId=groupDecision.Target?.Id,TargetSelection="Nearby tank",FollowDistance=activeGuardOptions?.GroupFollowDistance ?? groupFollow.Value,AttackRadius=activeGuardOptions?.GroupAttackRadius ?? groupAttack.Value,FollowLimit=activeGuardOptions?.GroupFollowLimit ?? groupLimit.Value};
    void ApplyPartyNames()
    {
        if(!currentParty.Available)return;
        entities=entities.Select(e=>string.IsNullOrWhiteSpace(e.Name) && currentParty.Members.FirstOrDefault(m=>m.Id==e.Id) is PartyMember member?e with{Name=member.Name}:e).ToList();
    }
    void RefreshGroupDecision()
    {
        var options=activeGuardOptions;
        if(options?.GroupMode!=true)return;
        currentParty=world.Party();var self=world.LocalPlayer();
        var member=currentParty.Members.FirstOrDefault(m=>m.Name.Equals(options.GroupTankName,StringComparison.OrdinalIgnoreCase));
        var tank=member==null?null:entities.FirstOrDefault(e=>e.Id==member.Id && CombatCourtesy.IsOtherPlayer(e,self.Id));
        var health=world.HealthSnapshot();
        if(options.HealerMode)
        {
            groupDecision=GroupHealerPolicy.Follow(currentParty,options.GroupTankName,self,entities,health,
                (double)options.GroupFollowDistance,(double)options.GroupFollowLimit);
            return;
        }
        groupDecision=GroupPolicy.Decide(currentParty,options.GroupTankName,self,entities,health,(double)options.GroupFollowDistance,(double)options.GroupAttackRadius,(double)options.HuntRadius,
            (entity,hp)=>GroupCandidateReason(entity,hp,self.Position,options)==null,lockedTarget,options.PrioritizeGamekeeper);
    }
    string? GroupCandidateReason(Entity target,Health hp,Vec position,Options options)
    {
        if(!Targeting.Eligible(target,hp,world.Difficulty(target,world.PlayerLevel()),options.Target,options.AllowedDifficulties,options.PrioritizeGamekeeper))return "Excluded by target filters";
        return (unreachableTargets.TryGetValue(TargetIdentity(target),out long until) && Environment.TickCount64<until?"Temporarily unreachable":null) ??
            (options.AutomaticRouting?Avoidance.BlockedPoint(target.Position,avoidZones):Avoidance.BlockedSegment(position,target.Position,avoidZones)) ??
            courtesy.Blocked(target,hp,entities.Where(e=>!currentParty.Members.Any(m=>m.Id==e.Id)),guardSelfId,options.AntiKillSteal,(double)options.OtherPlayerRadius,true);
    }
    void WriteState(object value)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "live-status.json");
        var state=JsonSerializer.SerializeToNode(value)!;
        state["ReaderPid"]=Environment.ProcessId;
        state["EquipmentDurability"]=JsonSerializer.SerializeToNode(latestDurability);
        state["MemoryBackend"]=PoteMemoryProbe.WindowsClientRead.Backend;
        state["InputBackend"]=WindowsClientInput.Backend;
        state["InputCompatibilityEnabled"]=WindowsClientInput.Enabled;
        state["SessionLog"]=JsonSerializer.SerializeToNode(new {Path=HuntingSessionLog.Current?.FilePath,Error=HuntingSessionLog.Current?.LastError});
        state["Hotkeys"]=JsonSerializer.SerializeToNode(new { Ready=hotkeys, Failure=hotkeyFailure, Registrations=hotkeyRegistrations });
        state["Status"]=DisplayMessage;
        state["TargetSearch"]=working ? JsonSerializer.SerializeToNode(targetSearch) : null;
        state["HuntingArea"]=JsonSerializer.SerializeToNode(new {Mode=(activeGuardOptions?.GroupMode ?? groupEnabled.Checked)?"Group":(activeGuardOptions?.LeaveAreaWhenEmpty ?? leaveAreaWhenEmpty.Checked)?"Fixed with outside trips":"Fixed",Center=activeHuntAnchor,Radius=activeGuardOptions?.HuntRadius ?? radius.Value,OutsideSearchRadius=(activeGuardOptions?.HuntRadius ?? radius.Value)*2,OutsideTrip=working && activeExcursion?.OutsideTrip==true,ReturnPending=working && completionReturnPending,CompletionRadius=working?activeCompletionBoundary:(double?)null});
        File.WriteAllText(path + ".tmp", state.ToJsonString(new JsonSerializerOptions { WriteIndented = true })); File.Move(path + ".tmp", path, true);
    }
    void UpdateItemDescriptions()
    {
        var slot=currentHotbar?.Slots.FirstOrDefault(s=>s.Key==selectedHotbarKey);
        if (slot!=null) hotbarDescription.Text=$"{slot.Name}" + (slot.Category.Length>0?$" Â· {slot.Category}":"") + Environment.NewLine + (slot.Description.Length>0?slot.Description:slot.Kind==SlotKind.Skill?"Skill assignment. Its ready state and cooldown are shown above.":"No base description is available.");
        var item=groundLoot.FirstOrDefault(i=>$"{i.KeyA}:{i.KeyB}"==selectedGroundKey);
        if (item!=null) lootDescription.Text=item.Name+Environment.NewLine+(item.Description.Length>0?item.Description:"No base description is available.");
        else if (selectedGroundKey!=null) lootDescription.Text="The selected item is no longer on the ground.";
    }
    void Stop(string reason)
    {
        if(!internalClientStop)CancelClientRecovery();
        try {TraceLog.Record("stop requested",new{Reason=reason,RecoveryPending=deathRecovery.Pending,Reviving=deathRecoveryActive,Returning=deathReturnInProgress,Repairing=repairInProgress});}
        catch(IOException) { }
        catch(UnauthorizedAccessException) { }
        lootTracker.ObserveActivity(false);
        FinishLoggedHunt(reason);
        startVersion++;
        faultDeathWatch.Reset();
        rangedPull.Reset(); rangedTagging = false;
        Input.PickupHoldProvider=null;nearbyPickupSnapshot.Clear();nearbyPickupCount=0;
        cancel?.Cancel(); ReleaseCombatPickup(); Input.Release(); movement = null; lockedTarget = null; message = reason;
    }
    bool SaveLootLog(string reason)
    {
        var snapshot=lootTracker.Snapshot();
        HuntingSessionLog.Current?.RecordLoot(snapshot,reason);
        bool saved = lootTrackerLog.TrySave(snapshot, reason);
        if (!saved) message = $"Loot event could not be saved ({reason}).";
        return saved;
    }
    static bool TryParseReviveKey(string value,out Keys key)
    {
        if(!Enum.TryParse(value,true,out key) || !Enum.IsDefined(key) || (int)key<8 || (int)key>254 || key==Keys.Escape)return false;
        Keys parsed=key;
        return !HotkeyBindings.Any(binding=>binding.Key==parsed);
    }

    void ObserveDeath(Health health)
    {
        ObserveLoggedPlayerHealth(health);
        if(deathRecovery.Observe(health,Environment.TickCount64))
        {
            SaveLootLog("Death");
            Input.PickupHoldProvider=null;ReleaseCombatPickup();movement?.StopApproach();Input.Release();
            message="Death detected. Preparing recovery to the saved anchor.";
            TraceLog.Record("character death detected",new{Anchor=activeHuntAnchor,Zone=runZone,HP=health,RecordedRoutesPreserved=true});
        }
        if(health.Dead && (activeGuardOptions?.AutoReviveAfterDeath!=true || activeGuardOptions.HealerMode))Stop("Character died; stopped.");
    }
    void PaintNavigation(object? sender,PaintEventArgs e)
    {
        DrawNavigation(e.Graphics,navigationCanvas.ClientSize);
    }
    void DrawNavigation(Graphics g,Size canvasSize)
    {
        g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        float span=(float)NavigationViewRadius(),scale=Math.Min(canvasSize.Width,canvasSize.Height)/(span*2);
        float cx=canvasSize.Width/2f, cy=canvasSize.Height/2f;
        Vec viewCenter=NavigationViewCenter();
        PointF Project(Vec v)=>new(cx+(float)(v.X-viewCenter.X)*scale,cy-(float)(v.Y-viewCenter.Y)*scale);
        if(zoneMapBackground.TryGet(navigationZone,out var mapImage,out var mapBounds))
        {
            float left=cx+(float)(mapBounds.MinX-viewCenter.X)*scale;
            float right=cx+(float)(mapBounds.MaxX-viewCenter.X)*scale;
            float top=cy-(float)(mapBounds.MaxY-viewCenter.Y)*scale;
            float bottom=cy-(float)(mapBounds.MinY-viewCenter.Y)*scale;
            g.DrawImage(mapImage,RectangleF.FromLTRB(left,top,right,bottom));
            using var shade=new SolidBrush(Color.FromArgb(60,ImperialTheme.Window));g.FillRectangle(shade,new Rectangle(Point.Empty,canvasSize));
        }
        using var trailPen=new Pen(Color.SeaGreen,1.5f);using var routePen=new Pen(Color.DeepSkyBlue,2);using var obstaclePen=new Pen(Color.Orange,2);using var avoidPen=new Pen(Color.IndianRed,1.5f);
        var trail=navigation.Trail.Where(p=>(p-navigationPosition).Length<span*2).Select(Project).ToArray();if(trail.Length>1)g.DrawLines(trailPen,trail);
        foreach(var obstacle in navigation.Blocked) {var p=Project(obstacle.Center);float r=(float)obstacle.Radius*scale;g.DrawEllipse(obstaclePen,p.X-r,p.Y-r,r*2,r*2);}
        foreach(var zone in avoidZones) {var p=Project(zone.Center);float r=(float)(zone.Radius+1)*scale;g.DrawEllipse(avoidPen,p.X-r,p.Y-r,r*2,r*2);}
        if(showNavigationRoutes.Checked)
        {
            var savedPens=new[]{new Pen(ImperialTheme.Gold,2),new Pen(ImperialTheme.RouteBlue,2),new Pen(ImperialTheme.RouteRose,2)};
            var savedBrushes=new[]{new SolidBrush(ImperialTheme.Gold),new SolidBrush(ImperialTheme.RouteBlue),new SolidBrush(ImperialTheme.RouteRose)};
            try
            {
                foreach(var (slot,saved) in navigation.SavedRoutesForZone(navigationZone))
                {
                    var savedPoints=saved.Points.Where(point=>(point-navigationPosition).Length<span*2).Select(Project).ToArray();
                    if(savedPoints.Length>1)g.DrawLines(savedPens[slot],savedPoints);
                    var marker=Project(saved.Anchor);
                    g.FillEllipse(savedBrushes[slot],marker.X-4,marker.Y-4,8,8);
                }
            }
            finally { foreach(var pen in savedPens)pen.Dispose(); foreach(var brush in savedBrushes)brush.Dispose(); }
            var route=new[]{navigationPosition}.Concat(navigation.Route).Select(Project).ToArray();if(route.Length>1)g.DrawLines(routePen,route);
        }

        DrawDirectionCone(g,canvasSize);
        DrawRadarMonsters(g,canvasSize);
        DrawRadarPlayers(g,canvasSize);
        if(!showTreasureChestMarkers.Checked)
        {
            var selfMarker=Project(navigationPosition);g.FillEllipse(Brushes.White,selfMarker.X-4,selfMarker.Y-4,8,8);
            return;
        }
        var liveChests=entities.Where(ent=>ent.Position.Finite && Targeting.IsChest(ent) && !latestHealth.GetValueOrDefault(ent.Id).Dead).ToList();
        var liveChestIds=liveChests.Select(chest=>chest.Id).ToHashSet();
        var chests=liveChests.Select(chest=>new {chest.Position,Label=Targeting.ChestLabel(chest),Live=true}).Concat(
            chestCatalog.ForZone(navigationZone).Where(chest=>!liveChestIds.Contains(chest.Id)).Select(chest=>new {chest.Position,chest.Label,Live=false})).ToList();
        if(chests.Count>0)
        {
            using var chestFill=new SolidBrush(Color.FromArgb(255,215,0));
            using var rememberedFill=new SolidBrush(Color.FromArgb(155,215,165,32));
            using var chestOutline=new Pen(Color.FromArgb(140,90,0),1.4f);
            using var claspBrush=new SolidBrush(Color.WhiteSmoke);
            using var font=new Font("Segoe UI",7.5f,FontStyle.Bold);
            using var tagBorderPen=new Pen(Color.FromArgb(180,218,165,32),1f);
            using var offscreenBrush=new SolidBrush(Color.FromArgb(255,215,0));

            int w=canvasSize.Width, h=canvasSize.Height;
            foreach(var chest in chests)
            {
                double dist=(chest.Position-navigationPosition).Length;
                var p=Project(chest.Position);
                bool onScreen=p.X>=12 && p.X<=w-12 && p.Y>=12 && p.Y<=h-12;
                if(onScreen)
                {
                    float bx=p.X-6, by=p.Y-4.5f;
                    g.FillRectangle(chest.Live?chestFill:rememberedFill,bx,by,12,9);
                    g.DrawRectangle(chestOutline,bx,by,12,9);
                    g.DrawLine(chestOutline,bx,by+3.5f,bx+12,by+3.5f);
                    g.FillRectangle(claspBrush,p.X-1f,by+2.5f,2.5f,2.5f);
                    g.DrawRectangle(Pens.Black,p.X-1f,by+2.5f,2.5f,2.5f);

                    string label=$"{chest.Label}{(chest.Live?"":" seen")} ({dist:F0}m)";
                    while(label.Length>1 && g.MeasureString(label,font).Width>w-12)label=label[..^2]+"…";
                    var sz=g.MeasureString(label,font);
                    float lx=Math.Clamp(p.X-sz.Width/2f,3,w-sz.Width-3);
                    float ly=by-sz.Height-2;
                    if(ly<2) ly=by+11;
                    MapOverlayText.Draw(g,label,font,new(lx,ly));
                    g.DrawRectangle(tagBorderPen,lx-2,ly-1,sz.Width+4,sz.Height+2);
                }
                else
                {
                    float dx=p.X-cx, dy=p.Y-cy;
                    float len=MathF.Sqrt(dx*dx+dy*dy);
                    if(len>1e-3f)
                    {
                        float margin=16f;
                        float maxX=cx-margin, maxY=cy-margin;
                        float sx=Math.Abs(dx)>1e-4f?maxX/Math.Abs(dx):float.MaxValue;
                        float sy=Math.Abs(dy)>1e-4f?maxY/Math.Abs(dy):float.MaxValue;
                        float s=Math.Min(sx,sy);
                        float edgeX=cx+dx*s, edgeY=cy+dy*s;

                        float nx=dx/len, ny=dy/len;
                        float px=-ny, py=nx;
                        PointF tip=new(edgeX,edgeY);
                        PointF b1=new(edgeX-nx*9+px*4.5f,edgeY-ny*9+py*4.5f);
                        PointF b2=new(edgeX-nx*9-px*4.5f,edgeY-ny*9-py*4.5f);
                        g.FillPolygon(offscreenBrush,new[]{tip,b1,b2});
                        g.DrawPolygon(chestOutline,new[]{tip,b1,b2});

                        string tag=$"{dist:F0}m";
                        var tsz=g.MeasureString(tag,font);
                        float tx=Math.Clamp(edgeX-nx*16-tsz.Width/2f,2,w-tsz.Width-2);
                        float ty=Math.Clamp(edgeY-ny*16-tsz.Height/2f,2,h-tsz.Height-2);
                        MapOverlayText.Draw(g,tag,font,new(tx,ty));
                        g.DrawRectangle(tagBorderPen,tx-2,ty-1,tsz.Width+4,tsz.Height+2);
                    }
                }
            }
        }

        var self=Project(navigationPosition);g.FillEllipse(Brushes.White,self.X-4,self.Y-4,8,8);
    }
    bool RoutingEnabled => activeGuardOptions?.AutomaticRouting ?? automaticRouting.Checked;
    static (uint,uint,long) TargetIdentity(Entity e)=>(e.Id,e.Generation,e.Address);
    async Task<bool> NavigateTo(Movement drive,Vec goal,Vec anchor,Options options,CancellationToken token,double bodyReach=0,double? boundaryRadius=null,bool watchTurns=false,double arrivalTolerance=0)
    {
        drive.TurnSpeedDegreesPerSecond=(double)options.TurnSpeedDegreesPerSecond;
        bool previousOwner=navigationInputOwned;navigationInputOwned=true;
        try
        {
        activeMovementBoundary=boundaryRadius ?? (double)options.HuntRadius;
        if(!goal.Finite || (goal-anchor).Length>activeMovementBoundary)throw new RouteUnavailableException("Destination is beyond the current movement boundary.");
        var self=world.LocalPlayer();navigationPosition=self.Position;
        navigation.Observe(world.NavigationContext(self),self.Position,self.Height);
        RefreshImportedCollisionObstacles();
        if(!options.AutomaticRouting) {await drive.Approach(world,self.Position,goal-self.Position,token,watchTurns,arrivalTolerance);return false;}
        int version=navigation.RouteVersion;
        Vec waypoint;
        try {waypoint=navigation.Waypoint(self.Position,goal,anchor,boundaryRadius ?? (double)options.HuntRadius,avoidZones);}
        catch(RouteUnavailableException) when(bodyReach>0 && (goal-self.Position).Length<=bodyReach) {drive.StopApproach();return true;}
        if(version!=navigation.RouteVersion) drive.StopApproach();
        try {await drive.Approach(world,self.Position,waypoint-self.Position,token,watchTurns,arrivalTolerance);}
        catch(MovementBlockedException blocked)
        {
            drive.StopApproach();Input.HoldMouse(false,false,token);
            if(bodyReach>0 && (goal-blocked.Position).Length<=bodyReach)
            {
                TraceLog.Record("close body collision; checking attack reach",new {blocked.Position,Goal=goal,Distance=(goal-blocked.Position).Length});
                return true;
            }
            navigation.RecordBlock(blocked.Position,blocked.Direction,self.Height);
            TraceLog.Record("navigation blocked direction",new {Zone=navigationZone,blocked.Position,blocked.Direction,navigation.RecoveryAttempts,Goal=goal});
            await Input.Delay(100,token);
        }
        return false;
        }
        finally { navigationInputOwned=previousOwner; }
    }
    string? StartRecoveryRouteProblem(Options options)
    {
        if(!options.AutoReviveAfterDeath || options.HealerMode)return null;
        var self=world.LocalPlayer();int zone=world.ActiveZone();Vec anchor=world.PlayerPosition();
        navigation.SelectTargetSelection(options.Target);
        int slot=options.GroupMode?-1:RecoveryTravel.StartupSlot(navigation.SavedRoutes,anchor,zone,self.Name,self.Height,SelectedSavedNavigationSlot(),(double)options.RouteCorridorRadius);
        // StartupSlot verifies the recorded path, identity and entry corridor.
        // Its destination height is not the player's current path elevation.
        if(slot>=0)return null;
        SavedNavigationRoute? route=navigation.SavedRoutesForZone(zone)
            .Where(item=>RecoveryRouting.Compatible(item.Route,zone,self.Name,self.Height) && (item.Route.Anchor-anchor).Length<=2.5)
            .OrderBy(item=>(item.Route.Anchor-anchor).Length).Select(item=>item.Route).FirstOrDefault();
        bool assigned=navigation.SavedRoutes.Any(item=>item!=null&&RecoveryTravel.Recorded(item));
        if(route==null && assigned)
            return $"Start within {options.RouteCorridorRadius:0.#} map units of a recorded {navigation.RouteTargetLabel} path for this character and map, or stand at its saved anchor on the matching floor.";
        if(route==null && !assigned && navigation.UnassignedRouteCount>0)
            return $"Choose {navigation.RouteTargetLabel} in Overview, then Assign existing routes in Navigation, or record its return route with Home / End.";
        return RecoveryRouting.SavedReturnProblem(route,zone,self.Name,self.Height,anchor);
    }
    async Task StartHunting(long? requestedVersion=null)
    {
        if(!RequireHotkeys() || busy || working || !connected || clientRecoveryRunning) return;
        long version=requestedVersion ?? ++startVersion;
        if(version!=startVersion) return;
        var readiness=world.CheckInputWindow();
        TraceLog.Record("start requested",readiness);
        if(!readiness.Allowed) { message=readiness.BlockReason!; return; }
        var requested=CurrentOptions();
        string? routeProblem=ClientRecoveryStartProblem(requested) ?? StartRecoveryRouteProblem(requested);
        if(routeProblem!=null)
        {
            message=routeProblem;TraceLog.Record("start blocked by recovery route",new{Reason=routeProblem,Position=world.PlayerPosition(),Height=world.LocalPlayer().Height,Zone=world.ActiveZone(),Target=requested.Target,JoinRadius=requested.RouteCorridorRadius});return;
        }
        if(requested.HealerMode && requested.GroupMode)
        {
            var party=world.Party();var self=world.LocalPlayer();
            if(!party.Available || party.Members.Count(m=>m.Id!=self.Id && m.Name.Equals(requested.GroupTankName,StringComparison.OrdinalIgnoreCase))!=1)
            {message="Select another current party member as tank on the Group page before starting the healbot.";return;}
        }
        if(requested.HealerMode && !requested.GroupMode)
        {
            message="Healer mode ready. Press F8 again to stop.";
            await Hunt();
            return;
        }
        if(movement==null && requested.Ranged && !requested.HealerMode && RangedAimPersistence.TryRestore(Path.Combine(AppContext.BaseDirectory,"calibration.json"),world,out var restored,out var restoreStatus))
        {
            movement=restored;
            message="Live facing ready. "+restoreStatus;
            TraceLog.Record("3D aim sensitivity restored",new{world.ClientHash,Character=world.LocalPlayer().Name,restoreStatus});
        }
        if(movement==null) await Calibrate();
        if(version!=startVersion || movement==null || !Input.Allowed()) return;
        message="Calibration ready. Starting huntâ€¦";
        await Hunt();
    }
    string? TargetGuardReason(Entity target, Health hp, Vec position, Options? options=null)
    {
        var active=options ?? activeGuardOptions;
        if(active?.GroupMode==true)
        {
            if(hp.Dead)return null;
            if(active.PrioritizeGamekeeper && Targeting.IsGamekeeper(target))return GroupCandidateReason(target,hp,position,active);
            if(groupDecision.Action!=GroupAction.Attack || groupDecision.Target is not Entity selected || TargetIdentity(selected)!=TargetIdentity(target))
                return "Group: "+groupDecision.Status;
            var tank=groupDecision.Tank;
            var liveTank=tank==null?null:world.Find(tank.Id);
            var liveTarget=world.Find(target.Id);
            if(liveTank==null || tank==null || TargetIdentity(liveTank)!=TargetIdentity(tank) || liveTarget==null || TargetIdentity(liveTarget)!=TargetIdentity(target))
                return "Group tank or target changed";
            if((liveTarget.Position-liveTank.Position).Length>(double)active.GroupAttackRadius)return "Target moved outside the tank attack radius";
            return GroupCandidateReason(target,hp,position,active);
        }
        return (unreachableTargets.TryGetValue(TargetIdentity(target),out long until) && Environment.TickCount64<until ? "Temporarily unreachable" : null) ??
        ((options?.AutomaticRouting ?? RoutingEnabled) ? Avoidance.BlockedPoint(target.Position,avoidZones) : Avoidance.BlockedSegment(position,target.Position,avoidZones)) ??
        courtesy.Blocked(target,hp,entities,guardSelfId,options?.AntiKillSteal ?? activeGuardOptions?.AntiKillSteal ?? antiKillSteal.Checked,
            (double)(options?.OtherPlayerRadius ?? activeGuardOptions?.OtherPlayerRadius ?? playerBuffer.Value),
            encounter.MayHaveReceivedOurDamage(target) && CombatCourtesy.PlayerNear(target.Position,entities,guardSelfId,
                Math.Max((double)(options?.OtherPlayerRadius ?? activeGuardOptions?.OtherPlayerRadius ?? playerBuffer.Value),6))==null);
    }

    double BaseTargetRadius(Entity entity,Options options) => options.PrioritizeGamekeeper && Targeting.IsGamekeeper(entity) ?
        Targeting.ResponseRadius((double)options.HuntRadius,(double)options.GamekeeperResponseRadius) : options.LeaveAreaWhenEmpty && !options.GroupMode ?
        activeExcursion is {OutsideTrip:true} trip ? trip.OutsideSearchRadius : (double)options.HuntRadius :
        Targeting.TargetRadius(entity,options.PrioritizeGamekeeper,(double)options.HuntRadius,(double)options.GamekeeperResponseRadius);

    Entity? PriorityGamekeeper(Options options)
    {
        if(!options.PrioritizeGamekeeper || activeHuntAnchor is not Vec anchor)return null;
        if(Environment.TickCount64-guardRefreshedAt>=100)RefreshGuardScene();
        var health=world.HealthSnapshot();var position=world.PlayerPosition();
        var retained=lockedTarget is Entity locked && Targeting.IsGamekeeper(locked) ? locked : pendingPriorityGamekeeper;
        double? retainedRadius=retained!=null && (encounter.IsEngaged(retained) || courtesy.StartedHere(retained)) ? activeCompletionBoundary : null;
        var candidates=entities.Where(e=>Targeting.IsGamekeeper(e)).ToArray();
        var selected=GamekeeperPriority.Choose(candidates.Where(e=>TargetGuardReason(e,health.GetValueOrDefault(e.Id),position,options)==null),
            health,position,anchor,(double)options.HuntRadius,(double)options.GamekeeperResponseRadius,true,retained,retainedRadius);
        if(selected!=null)return selected;

        // A failed route used to place the Gamekeeper in the generic
        // unreachable cache for 30 seconds. That cache is useful for ordinary
        // pulls, but it also made a live priority target silently disappear.
        // Retry only that transient guard when no protected candidate was
        // found, preserving avoidance, courtesy, HP, and response boundaries.
        var retry=candidates.Where(e=>TargetGuardReason(e,health.GetValueOrDefault(e.Id),position,options)=="Temporarily unreachable").ToArray();
        selected=GamekeeperPriority.Choose(retry,health,position,anchor,(double)options.HuntRadius,(double)options.GamekeeperResponseRadius,true,retained,retainedRadius);
        if(selected!=null)
        {
            unreachableTargets.Remove(TargetIdentity(selected));
            TraceLog.Record("Gamekeeper priority recovered from transient route guard",new {selected.Id,selected.DisplayName,Position=selected.Position});
        }
        return selected;
    }

    async Task StandForGamekeeper(CancellationToken token)
    {
        ReleaseCombatPickup();movement?.StopApproach();Input.Release(preserveNearbyPickup:true);
        gamekeeperTransition=true;
        try {if(world.RestSupported)await EnsurePosture(false,token);}
        finally {gamekeeperTransition=false;}
    }

    bool MatchesRequestedTarget(Entity entity,Options options,int level) =>
        Targeting.Eligible(entity,new Health(1,1),world.Difficulty(entity,level),options.Target,options.AllowedDifficulties,options.PrioritizeGamekeeper);

    void ObserveEncounter(Options options,IReadOnlyDictionary<uint,Health> health,Vec position,int level)
    {
        if(options.GroupMode || !encounter.Active) return;
        bool WithinBoundary(Entity entity,bool finishing=false)
        {
            double limit=BaseTargetRadius(entity,options);
            if(finishing)limit=Math.Max(activeCompletionBoundary,Targeting.CompletionRadius(limit,(double)options.NearbyEnemyRadius,(double)options.MeleeRange));
            return !encounterAnchor.HasValue || (entity.Position-encounterAnchor.Value).Length<=limit;
        }
        encounter.Observe(entities,health,position,(double)options.NearbyEnemyRadius,(entity,hp)=>
            Targeting.Eligible(entity,hp,world.Difficulty(entity,level),options.Target,options.AllowedDifficulties,options.PrioritizeGamekeeper) &&
            WithinBoundary(entity) && TargetGuardReason(entity,hp,position,options)==null,
            mayRemainEngaged:(entity,hp)=>WithinBoundary(entity,true) && TargetGuardReason(entity,hp,position,options)==null,
            clearNearby:options.ClearNearbyEnemies && !healingRestPending && !(options.LeaveAreaWhenEmpty && completionReturnPending),
            mayClaimCollateral:entity=>(!RangedPullEnabled(options) || !rangedTagging) && CombatCourtesy.PlayerNear(entity.Position,entities,guardSelfId,Math.Max(6,(double)options.OtherPlayerRadius))==null,
            attackHeld:Input.BasicAttackHeld,attackReach:Encounter.CollateralReach(RangedPullEnabled(options) ? (double)options.RangedMeleeAttackRange : (double)options.MeleeRange),
            confirmedKill:entity=>lootTracker.RecordKill(entity,entity.Position,runZone??navigationZone));
        string state=$"{encounter.EngagedCount}:{encounter.HasUnresolvedEngaged}:"+string.Join(",",encounter.EngagedCandidates.OrderBy(e=>e.Id).Select(e=>$"{e.Id}:{e.Generation}:{e.Address}"));
        if(state!=lastEngagementState)
        {
            lastEngagementState=state;
            TraceLog.Record("engaged enemies updated",new {Count=encounter.EngagedCount,encounter.HasUnresolvedEngaged,Enemies=encounter.EngagedCandidates.Select(e=>new {e.Id,e.DisplayName,e.Generation}).ToArray()});
        }
    }

    object CombatState() => new {BasicAttackHeld=Input.BasicAttackHeld,SkillHealthRule=new{Enabled=activeGuardOptions?.HealthSkillCondition ?? healthSkillCondition.Checked,Percent=activeGuardOptions?.HealthSkillPercent ?? healthSkillPercent.Value,ExtraKeys=activeGuardOptions?.HealthConditionKeys ?? healthConditionKeys.Text,HealthSource=(activeGuardOptions?.HealerMode ?? healerMode.Checked)?"Healing target":"Character"},
        AutoDetectSkills=activeGuardOptions?.AutoDetectSkills ?? autoSkills.Checked,ActiveSkillKeys=activeGuardOptions?.SkillKeys,GamekeeperResponseRadius=activeGuardOptions?.GamekeeperResponseRadius ?? gamekeeperRadius.Value,LootDuringSkillCooldowns=activeGuardOptions?.LootDuringSkillCooldowns ?? combatPickup.Checked,PickupHeld=Input.PickupHeld,NearbyLootPickupEnabled=activeGuardOptions?.AutoPickupNearbyLoot ?? nearbyLootPickup.Checked,NearbyLootCount=nearbyPickupCount,PickupRadius=!(activeGuardOptions?.GroupMode ?? groupEnabled.Checked) && !(activeGuardOptions?.HealerMode ?? healerMode.Checked) ? activeGuardOptions?.LootPickupRadius ?? lootPickupRadius.Value : activeGuardOptions?.NearbyEnemyRadius ?? nearbyRadius.Value,PrioritizeGamekeeper=activeGuardOptions?.PrioritizeGamekeeper ?? prioritizeGamekeeper.Checked,Enabled=activeGuardOptions?.ClearNearbyEnemies ?? clearNearby.Checked,
        Radius=activeGuardOptions?.NearbyEnemyRadius ?? nearbyRadius.Value,encounter.Active,
        NearbyEnemies=encounter.Candidates.Select(e=>new {e.Id,e.DisplayName,e.Position}).ToArray(),
        EngagedEnemies=encounter.EngagedCandidates.Select(e=>new {e.Id,e.DisplayName,e.Position}).ToArray(),
        encounter.HasEngaged,encounter.EngagedCount,encounter.HasUnresolvedEngaged,HealingWarning=healingWarning,RestToFullPending=healingRestPending,RestToFullActive=healingRest!=null,
        encounter.HasUnresolvedNearby,PendingLoot=deferredLoot.Count,DefensePending=defensePending,LastIncomingDamageAt=combatPressure.LastDamageAt ,
        RangedPack=new {Enabled=activeGuardOptions?.RangedPullEnabled ?? rangedPullEnabled.Checked,Phase=rangedPull.Phase.ToString(),rangedPull.AttemptedCount}};
    object HealerState()=>new {Enabled=activeGuardOptions?.HealerMode ?? healerMode.Checked,AutoDetectSkills=activeGuardOptions?.AutoDetectHealingSkills ?? autoHealingSkills.Checked,
        SkillKeys=activeGuardOptions?.HealingSkillKeys ?? healingSkillKeys.Text,ChargeMilliseconds=activeGuardOptions?.HealChargeMilliseconds ?? healCharge.Value,
        HealBelowPercent=activeGuardOptions?.PartyHealBelowPercent ?? partyHealBelow.Value,Range=activeGuardOptions?.PartyHealRange ?? partyHealRange.Value,
        Target=activeHealTarget==null?null:new {activeHealTarget.Member.Id,activeHealTarget.Member.Name,activeHealTarget.PartyIndex,activeHealTarget.Health,activeHealTarget.Percent},LastSkill=lastHealingSkill,
        Buffs=new{Mode="Live active-effect memory",Enabled=activeGuardOptions?.MaintainAreaBuffs??maintainBuffs.Checked,activeEffects.Available,activeEffects.Status,Decisions=buffDecisions,Active=activeEffects.Effects.Where(e=>e.Active)}};

    string AttackKeys(string keys,HotbarSnapshot bar,bool upkeep)
    {
        var effects=world.ActiveEffects();
        return new(keys.Where(key=>!upkeep || !bar.Slots.Any(s=>s.Key==key.ToString() &&
            (LiveBuffUpkeep.CanMaintain(s,effects) || s.Kind==SlotKind.Skill && s.SkillTarget==SkillTargetKind.Party))).ToArray());
    }

    void ObserveBuffs(Options o,HotbarSnapshot bar)
    {
        var self=world.LocalPlayer();
        activeEffects=world.ActiveEffects();
        ObservePotionBuffs(o,bar);
        buffDecisions=buffPolicy.Evaluate(world.NavigationContext(self)+$":{bar.PageBase}",bar,activeEffects,o.MaintainAreaBuffs,Environment.TickCount64);
        buffStatus.Text=!activeEffects.Available?activeEffects.Status:buffDecisions.Count==0?"No slotted skills match the current effect catalog.":
            string.Join("\n",buffDecisions.Select(b=>$"{b.Key}: {b.Skill} ({b.Use}) â€” {b.Status}"));
    }

    async Task<bool> TryMaintainBuff(Options o,CancellationToken token)
    {
        if(HasActiveFight() || combatPressure.RecentDamage(Environment.TickCount64) || PriorityGamekeeper(o)!=null)return false;
        if(await TryUseBuffPotion(o,token))return true;
        if(!o.MaintainAreaBuffs)return false;
        var bar=CheckedHotbar();ObserveBuffs(o,bar);
        var decision=buffDecisions.FirstOrDefault(b=>b.ShouldCast && HealthSkillAllowed(bar.Slot(b.Key[0]),o));
        if(decision==null)return false;
        var hp=world.HealthSnapshot().GetValueOrDefault(world.LocalPlayer().Id);
        if(!hp.Known||hp.Dead)return false;
        movement?.StopApproach();ReleaseCombatPickup();ClearRangedPending();Input.Release(preserveNearbyPickup:true);
        var slot=bar.Slot(decision.Key[0]);
        var attempt=Environment.TickCount64;
        message=$"{(slot.SkillUse==SkillUseKind.Chant?"Turning on chant":"Casting AOE buff")}: {slot.Name}";
        TraceLog.Record("area buff attempt",new{slot.Key,slot.Id,slot.Name,Mode="live effect",slot.SkillUse,TargetSelection="none",ChargeMilliseconds=slot.SkillUse==SkillUseKind.Cast?o.HealChargeMilliseconds:0});
        priorityInterruptibleActivity=true;buffInProgress=true;
        try
        {
            await Input.Key((Keys)slot.Key[0],70,token);
            await Input.Delay(100,token);
            var selected=CheckedHotbar().Slot(slot.Key[0]);
            var afterKeyEffect=world.ActiveEffects().Match(slot.Name);
            if(afterKeyEffect is{Active:true}){ObserveBuffs(o,CheckedHotbar());return true;}
            if(selected.Id!=slot.Id || selected.Kind!=SlotKind.Skill || !selected.Ready || world.SelectedSkill()!=slot.Id)
            {
                buffPolicy.RecordAttempt(decision,attempt,false);
                TraceLog.Record("area buff selection failed",new{slot.Key,slot.Id,SelectedSkill=world.SelectedSkill(),selected.Ready,selected.RemainingCooldown,selected.Locked});return true;
            }
            var effect=world.ActiveEffects().Match(slot.Name);
            if(effect==null)return false;
            if(effect.Active)return true;
            attempt=Environment.TickCount64;
            buffPolicy.RecordAttempt(decision,attempt);
            if(!await CastHealthCheckedSkill(slot,o,token))return false;
            await Input.Delay(180,token);
            ObserveBuffs(o,CheckedHotbar());
            TraceLog.Record("area buff activation observation",new{slot.Key,slot.Id,Decision=buffDecisions.FirstOrDefault(b=>b.Key==decision.Key && b.SkillId==decision.SkillId)});
            return true;
        }
        catch {buffPolicy.RecordAttempt(decision,attempt,false);throw;}
        finally{priorityInterruptibleActivity=false;buffInProgress=false;Input.Release(preserveNearbyPickup:true);}
    }

    void SupportPreflight()
    {
        if(Environment.TickCount64<nextSupportPreflight)return;
        nextSupportPreflight=Environment.TickCount64+100;
        if(runCharacter==null || !LocalCharacter.Same(runCharacter,world.LocalPlayer()) || runZone!=world.ActiveZone())
            throw new OperationCanceledException("Character or zone changed during support casting.");
        _=CheckedHotbar();
        var hp=world.HealthSnapshot().GetValueOrDefault(runCharacter.Id);
        if(!hp.Known||hp.Dead)throw new OperationCanceledException("Support stopped: character health unavailable or character dead.");
        if(activeGuardOptions is {GroupMode:true,HealerMode:true} groupHealer)GroupHealerPreflight(groupHealer);
    }

    async Task<bool> RunLootJob(LootJob job,Movement drive,Vec anchor,Options options,CancellationToken token)
    {
        // Timed pickup uses the same anchor boundary as the automatic sweep.
        navigation.BeginGoal($"loot:{job.Target.Id}:{job.Target.Generation}");
        lootGuardPosition=anchor; lootBeforeFight=job.ExistingDrops;
        try
        {
            TraceLog.Record("target loot started",new {job.Target.Id,job.Target.DisplayName,job.Target.PriorityLootObject,job.HoldMs});
            await CollectLoot(drive,job.Position,anchor,options,token,job.HoldMs,job.Target.PriorityLootObject,job.ExistingDrops);
            TraceLog.Record("target loot finished",new {job.Target.Id,job.Target.DisplayName,job.Target.PriorityLootObject});
            return true;
        }
        catch(EncounterInterruptedException)
        {
            encounterQuietSince=0;
            TraceLog.Record("loot paused for nearby enemies",new {job.Target.Id,Enemies=encounter.Candidates.Select(e=>e.Id).ToArray()});
            return false;
        }
        catch(TargetProtectionException ex) { message="Pickup skipped: "+ex.Message; TraceLog.Record("loot protection skip",new {job.Target.Id,Reason=ex.Message}); return true; }
        catch(RouteUnavailableException ex) {message="Pickup route unavailable: "+ex.Message;TraceLog.Record("loot navigation skip",new {job.Target.Id,Reason=ex.Message});return true;}
        finally {lootGuardPosition=null;lootBeforeFight=null;drive.StopApproach();Input.Release(preserveNearbyPickup:true);}
    }

    async Task RunAnchorLoot(Movement drive,Vec anchor,Options options,CancellationToken token)
    {
        navigation.BeginGoal($"{options.LootPickupRadius:0.#}-unit anchor loot sweep");
        var previousPickup=Input.PickupHoldProvider;
        Input.PickupHoldProvider=null;Input.Release();
        lootGuardPosition=anchor;lootBeforeFight=new();
        try
        {
            TraceLog.Record("anchor loot started",new{Anchor=anchor,Radius=options.LootPickupRadius});
            await CollectLoot(drive,anchor,anchor,options,token,Math.Max(600,(int)options.LootHoldMs),false,lootBeforeFight);
        }
        catch(EncounterInterruptedException){encounterQuietSince=0;TraceLog.Record("anchor loot paused for enemies",new{Anchor=anchor});}
        catch(TargetProtectionException ex){TraceLog.Record("anchor loot protection skip",new{Reason=ex.Message});}
        catch(RouteUnavailableException ex){TraceLog.Record("anchor loot route unavailable",new{Reason=ex.Message});}
        finally
        {
            drive.StopApproach();Input.Release();lootGuardPosition=null;lootBeforeFight=null;
            Input.PickupHoldProvider=previousPickup;
        }
    }

    void RefreshGuardScene()
    {
        entities=world.Poll(); guardSelfId=world.LocalPlayer().Id;
        if(activeGuardOptions?.GroupMode==true){currentParty=world.Party();ApplyPartyNames();}
        avoidZones=Avoidance.BuildZones(avoidRules,entities,guardSelfId);
        guardRefreshedAt=Environment.TickCount64;
        RefreshGroupDecision();
    }

    void ReleaseCombatPickup()
    {
        if(combatPickupHeld)Input.Hold(Keys.E,false,default);
        combatPickupHeld=false;combatPickupBaseline=null;
    }

    bool PreserveEngagedSwing(Entity target,string reason,CancellationToken token)
    {
        // World snapshots can briefly omit a creature while the client rebuilds
        // its object tree.  An encounter that already recorded damage is still
        // authoritative during that short read gap, so do not drop the basic
        // attack and make the user wait for a fresh target selection.  Re-arm
        // only when the input tracker says the button is actually up; the
        // idempotent hold avoids extra mouse-down packets during a live combo.
        if(!encounter.IsEngaged(target))return false;
        if(!Input.BasicAttackHeld)
        {
            Input.HoldMouse(false,true,token);
            TraceLog.Record("engaged swing rearmed",new{target.Id,target.DisplayName,Reason=reason});
        }
        return true;
    }

    Entity? StationarySwingCandidate(Options options,Vec position,double range,IReadOnlyDictionary<uint,Health> health) =>
        options.GroupMode || options.Ranged || deathRecovery.Pending ? null :
        Targeting.ChooseStationaryEngaged(encounter.EngagedCandidates.Where(e=>
            TargetGuardReason(e,health.GetValueOrDefault(e.Id),position,options)==null),health,position,range);

    void StartNearbyPickup(Options options)
    {
        nearbyPickupSnapshot.Clear();nearbyPickupCount=0;nextNearbyPickupRead=0;nextLootTrackerRead=0;
        Input.PickupHoldProvider=options.AutoPickupNearbyLoot ? WantsNearbyPickup : null;
    }

    void ObserveLootTrackerDrops(Options options,IEnumerable<GroundItem> drops,Vec? center=null,double? farmingRadius=null)
    {
        Vec? lootCenter=center ?? (working?activeHuntAnchor:null);
        double? lootRadius=farmingRadius ?? (working?(double?)(activeGuardOptions?.HuntRadius ?? options.HuntRadius):null);
        lootTracker.ObserveDrops(drops,runZone ?? navigationZone,lootCenter,lootRadius);
    }

    bool WantsNearbyPickup()
    {
        var options=activeGuardOptions;
        if(!working || !connected || options==null || deathRecovery.Pending || deathReturnInProgress || repairInProgress || !options.AutoPickupNearbyLoot || cancel?.IsCancellationRequested!=false || returningFromPriority)return false;
        if(RangedPullEnabled(options) && rangedPull.Active && encounter.HasEngaged)return false;
        var self=world.LocalPlayer();
        if(runCharacter==null || !LocalCharacter.Same(runCharacter,self) || runZone!=world.ActiveZone())
            throw new OperationCanceledException("Character or zone changed during ground pickup.");
        var hp=world.TargetHealth(self.Id);
        if(!hp.Known || hp.Dead)
        {
            if(options.AutoReviveAfterDeath && hp.Dead){ObserveDeath(hp);return false;}
            throw new InvalidOperationException("Ground pickup stopped: player health is unavailable or dead.");
        }
        long now=Environment.TickCount64;
        if(now>=nextNearbyPickupRead)
        {
            nearbyPickupSnapshot=world.Loot();
            ObserveLootTrackerDrops(options,nearbyPickupSnapshot);
            nextNearbyPickupRead=now+LootTrackerPollMilliseconds;
        }
        bool anchored=!options.GroupMode && !options.HealerMode && activeHuntAnchor.HasValue;
        if(anchored && lootGuardPosition.HasValue)return false; // Explicit protected pickup owns E during travel.
        var decision=anchored ? NearbyLootPickup.EvaluateAnchor(self.Position,activeHuntAnchor!.Value,nearbyPickupSnapshot,(double)options.LootPickupRadius) :
            NearbyLootPickup.Evaluate(self.Position,(double)options.NearbyEnemyRadius,nearbyPickupSnapshot);
        if(decision.NearbyCount!=nearbyPickupCount)
        {
            nearbyPickupCount=decision.NearbyCount;
            TraceLog.Record("nearby ground loot hold changed",new {Count=nearbyPickupCount,Radius=anchored ? (double)options.LootPickupRadius : (double)options.NearbyEnemyRadius,HoldingE=decision.HoldLoot,Position=self.Position});
        }
        return decision.HoldLoot;
    }
    void SetCombatPickup(bool requested,HashSet<(uint,uint)> baseline,Options options,CancellationToken token)
    {
        if(options.AutoPickupNearbyLoot || !requested || PriorityGamekeeper(options)!=null || CombatPickup.Blocked(world.PlayerPosition(),entities,guardSelfId,world.Loot(),baseline,
            options.AntiKillSteal,(double)options.OtherPlayerRadius)!=null) {ReleaseCombatPickup();return;}
        if(!options.GroupMode && activeHuntAnchor is Vec pickupAnchor &&
            !NearbyLootPickup.MayPickupAt(world.PlayerPosition(),pickupAnchor,world.Loot(),(double)options.LootPickupRadius)){ReleaseCombatPickup();return;}
        combatPickupBaseline=baseline;
        Input.Hold(Keys.E,true,token);combatPickupHeld=true;
    }

    void ProtectionPreflight()
    {
        // The death screen still requires one deliberate client input. Do not
        // let the normal combat guard reject that revive key while HP is zero.
        if(deathRecoveryActive)return;
        var o=activeGuardOptions; if (o==null) return;
        if(rangedFireTarget!=null && Input.RightButtonHeld)
        {
            var aimed=world.Find(rangedFireTarget.Id);
            if(rangedPull.Phase!=RangedPullPhase.Tagging || healingRestPending || retreatRecovery!=null ||
                aimed==null || TargetIdentity(aimed)!=TargetIdentity(rangedFireTarget) || !aimed.Position.Finite ||
                activeHuntAnchor is not Vec home || (aimed.Position-home).Length>(double)o.HuntRadius ||
                TargetGuardReason(aimed,world.TargetHealth(aimed.Id),world.PlayerPosition(),o)!=null)
                ClearRangedPending();
        }
        if(runCharacter!=null && !LocalCharacter.Same(runCharacter,world.LocalPlayer()) &&
            !(deathRecovery.Pending && RecoveryRouting.SameCharacter(runCharacter,world.LocalPlayer())))
            throw new OperationCanceledException("Character changed during the hunt.");
        if(runZone.HasValue && world.ActiveZone()!=runZone) throw new InvalidOperationException("Map zone changed; stopped.");
        if(Environment.TickCount64-guardRefreshedAt>=100) RefreshGuardScene();
        Vec position=world.PlayerPosition();
        var playerHealth=world.TargetHealth(guardSelfId);
        if(!playerHealth.Known || playerHealth.Dead)
        {
            DeathRecoveryState.InterruptIfDead(playerHealth,o.AutoReviveAfterDeath,ObserveDeath);
            throw new InvalidOperationException("Player health is unavailable or the character died; stopped.");
        }
        if(stationaryReturnDefenseGuard is { } defenseGuard)
        {
            // A short, in-place defense may borrow return ownership after
            // repair. It renews the same live protections before every input.
            if(!StationaryReturnDefense.CanDefend(defenseGuard()))throw new StationaryReturnYieldException();
            _=CheckedHotbar();return;
        }
        if(deathReturnInProgress || repairInProgress)
        {
            // Recovery owns movement until the destination and facing are
            // restored. Combat priority must not interrupt this route.
            string? blocked=Avoidance.BlockedPoint(position,avoidZones);
            if(blocked!=null)throw new RouteUnavailableException("Recovery route blocked: "+blocked);
            return;
        }
        if(gamekeeperTransition)
        {
            var standingHealth=world.TargetHealth(guardSelfId);
            if(!standingHealth.Known || standingHealth.Dead)
            {
                DeathRecoveryState.InterruptIfDead(standingHealth,o.AutoReviveAfterDeath,ObserveDeath);
                throw new InvalidOperationException("Cannot stand for Gamekeeper: player health is unavailable or dead.");
            }
            _=CheckedHotbar();return;
        }
        var priority=PriorityGamekeeper(o);
        if(retreatRecovery==null && priority!=null &&
            (lockedTarget!=null && GamekeeperPriority.ShouldYield(lockedTarget,priority) || returningFromPriority || lootGuardPosition.HasValue || priorityInterruptibleActivity))
        {
            pendingPriorityGamekeeper=priority;healingRestPending=false;healingWarning=null;
            ClearRangedPending();ReleaseCombatPickup();movement?.StopApproach();Input.Release(preserveNearbyPickup:true);throw new PriorityTargetException(priority);
        }
        if(combatPickupHeld && (retreatRecovery!=null || combatPickupBaseline==null ||
            !o.GroupMode && activeHuntAnchor is Vec heldPickupAnchor &&
            !NearbyLootPickup.MayPickupAt(position,heldPickupAnchor,world.Loot(),(double)o.LootPickupRadius) ||
            CombatPickup.Blocked(position,entities,guardSelfId,world.Loot(),combatPickupBaseline,o.AntiKillSteal,(double)o.OtherPlayerRadius)!=null))
            ReleaseCombatPickup();
        if(retreatRecovery!=null)
        {
            var hp=world.TargetHealth(guardSelfId);
            if(!hp.Known || hp.Dead)
            {
                DeathRecoveryState.InterruptIfDead(hp,o.AutoReviveAfterDeath,ObserveDeath);
                throw new InvalidOperationException("Retreat stopped: player HP is unavailable or the character died.");
            }
            if(!o.GroupMode && combatPressure.Observe(hp,Environment.TickCount64) && !HasActiveFight())defensePending=true;
            _=CheckedHotbar();
            // A moving threat can invalidate held W between controller updates.
            var drive=retreatDrive;
            Vec forward=Movement.FromClientHeading(world.PlayerHeading());
            if(drive?.CanAdvance?.Invoke(position,position+forward*.5)!=true) drive?.StopApproach();
            return;
        }
        if(o.GroupMode && lockedTarget==null && groupDecision.Action!=GroupAction.Follow)movement?.StopApproach();
        string? inside=Avoidance.BlockedPoint(position,avoidZones);
        if (inside!=null) throw new RetreatRequiredException(inside);
        if(!o.GroupMode)
        {
            if(Environment.TickCount64>=nextEngagementObservation)
            {
                nextEngagementObservation=Environment.TickCount64+100;
                ObserveEncounter(o,world.HealthSnapshot(),position,world.PlayerLevel());
                ObserveCombatPressure(o,playerHealth,position);
            }
            if(priority==null && encounter.HasEngaged && (defenseRepositioning || lootGuardPosition.HasValue || buffInProgress || !returningFromPriority && !rangedTagging && !(RangedPullEnabled(o) && rangedPull.Phase==RangedPullPhase.Clearing) && lockedTarget!=null && !encounter.IsEngaged(lockedTarget) &&
                !(courtesy.StartedHere(lockedTarget) && world.TargetHealth(lockedTarget.Id).Dead)))
            {
                ReleaseCombatPickup();movement?.StopApproach();Input.Release(preserveNearbyPickup:true);
                throw new EngagedTargetPriorityException();
            }
            if(priority==null && defensePending && !HasActiveFight() &&
                (lockedTarget!=null || lootGuardPosition.HasValue || buffInProgress))throw new RecoverUnderDamageException();
        }
        if (lockedTarget!=null)
        {
            if(rangedTagging)
            {
                var live=world.Find(lockedTarget.Id);
                if(live==null || TargetIdentity(live)!=TargetIdentity(lockedTarget) || !live.Position.Finite ||
                    !activeHuntAnchor.HasValue || (live.Position-activeHuntAnchor.Value).Length>(double)o.HuntRadius)
                    throw new TargetProtectionException("Ranged tag target changed or left the hunt area.");
                if(!rangedApproaching && (live.Position-position).Length>(double)o.MeleeRange)
                    throw new RangedTargetOutOfRangeException();
                lockedTarget=live;
            }
            if(priority==null && healingRestPending && !HasActiveFight() && !courtesy.StartedHere(lockedTarget))
            {
                ReleaseCombatPickup();movement?.StopApproach();Input.Release(preserveNearbyPickup:true);
                throw new RecoverBeforeFreshTargetException();
            }
            if(priority==null && o.LeaveAreaWhenEmpty && activeExcursion is {OutsideTrip:true} excursion && !encounter.HasEngaged && !courtesy.StartedHere(lockedTarget) &&
                Environment.TickCount64>=nextInsideTargetCheck)
            {
                nextInsideTargetCheck=Environment.TickCount64+100;
                int level=world.PlayerLevel();
                if(HuntingArea.HasApprovedInside(entities,world.HealthSnapshot(),excursion,e=>MatchesRequestedTarget(e,o,level)))
                {
                    ReleaseCombatPickup();movement?.StopApproach();Input.Release(preserveNearbyPickup:true);
                    throw new ReturnToHuntingAreaException();
                }
            }
            string? blocked=TargetGuardReason(lockedTarget,world.TargetHealth(lockedTarget.Id),position,o);
            if (blocked!=null) throw new TargetProtectionException(blocked);
        }
        movement?.ValidateTravelStep(world);
        if (lootGuardPosition is Vec lootPoint)
        {
            if(encounter.Active)
            {
                ObserveEncounter(o,world.HealthSnapshot(),position,world.PlayerLevel());
                if(encounter.Candidates.Count>0 || encounter.HasUnresolvedNearby) throw new EncounterInterruptedException();
            }
            string? blocked=o.AutomaticRouting ? Avoidance.BlockedPoint(lootPoint,avoidZones) : Avoidance.BlockedSegment(position,lootPoint,avoidZones);
            if (o.AntiKillSteal) blocked ??= CombatCourtesy.PlayerNear(lootPoint,entities,guardSelfId,(double)o.OtherPlayerRadius)
                ?? CombatCourtesy.PlayerNear(position,entities,guardSelfId,(double)o.OtherPlayerRadius);
            if (blocked!=null) throw new TargetProtectionException(blocked);
            if (o.AntiKillSteal && lootBeforeFight!=null && groundLoot.Any(i=>lootBeforeFight.Contains((i.KeyA,i.KeyB)) && (i.Position-position).Length<=3))
                throw new TargetProtectionException("Pre-existing drops are inside pickup range");
        }
    }

    Entity? NearestTreasureChest(Vec position,Vec anchor,Options options)
    {
        if(!options.GuideTreasureChests || options.GroupMode || encounter.HasEngaged)return null;
        long now=Environment.TickCount64;
        return entities.Where(entity=>Targeting.IsChest(entity) && entity.Position.Finite &&
                !latestHealth.GetValueOrDefault(entity.Id).Dead &&
                (entity.Position-anchor).Length<=(double)options.HuntRadius &&
                (!chestGuideCooldown.TryGetValue((navigationZone,entity.Id),out var retryAt)||now>=retryAt))
            .OrderBy(entity=>(entity.Position-position).Length).FirstOrDefault();
    }

    async Task<bool> GuideToTreasureChest(Movement drive,Vec anchor,Options options,CancellationToken token)
    {
        var chest=NearestTreasureChest(world.PlayerPosition(),anchor,options);
        if(chest==null)return false;
        navigation.BeginGoal($"treasure-chest:{navigationZone}:{chest.Id}");
        message=$"Guiding to {Targeting.ChestLabel(chest)}";
        try
        {
            await NavigateTo(drive,chest.Position,anchor,options,token,bodyReach:2.5);
            drive.StopApproach();
            await Input.Delay(100,token);
            if((world.PlayerPosition()-chest.Position).Length<=3)
            {
                message=$"Opening {Targeting.ChestLabel(chest)}";
                await Input.Key(Keys.E,(int)Math.Clamp(options.LootHoldMs,100,3000),token);
                chestGuideCooldown[(navigationZone,chest.Id)]=Environment.TickCount64+120000;
                TraceLog.Record("treasure chest interaction",new {navigationZone,chest.Id,chest.Position,chest.DisplayName});
            }
        }
        catch(RouteUnavailableException ex)
        {
            chestGuideCooldown[(navigationZone,chest.Id)]=Environment.TickCount64+10000;
            drive.StopApproach();
            message=$"Chest route unavailable: {ex.Message}";
        }
        return true;
    }

    bool TryGreetNearbyPlayer(Options options, Vec position, IReadOnlyList<Entity> observed, uint selfId)
    {
        if (!options.GreetPlayers || Input.BasicAttackHeld || Input.RightButtonHeld || !Input.Allowed()) return false;
        var player = playerGreeting.Next(position, observed, selfId, (double)options.GreetingRadius);
        if (player == null) return false;
        try
        {
            Input.Release(preserveNearbyPickup: true);
            Input.Chat("Hello!");
            playerGreeting.Mark(player);
            message = $"Said hello to { (string.IsNullOrWhiteSpace(player.Name) ? $"player {player.Id:X8}" : player.Name) }.";
            TraceLog.Record("nearby player greeted", new { player.Id, player.Name, player.Model, Distance = (player.Position - position).Length, Radius = options.GreetingRadius });
            return true;
        }
        catch (Exception ex)
        {
            TraceLog.Record("nearby player greeting failed", new { player.Id, Error = ex.Message });
            return false;
        }
    }
    async Task Calibrate()
    {
        if (!RequireHotkeys() || busy || working || !connected || clientRecoveryRunning) return;
        var readiness=world.CheckInputWindow();
        if (!readiness.Allowed) { message = readiness.BlockReason!; TraceLog.Record("calibration blocked",readiness); return; }
        try
        {
            CurrentOptions().Save(); working = true; settings.Enabled = false; protectionPanel.Enabled=false; automaticRouting.Enabled=false;clearNavigation.Enabled=false; connect.Enabled = false; start.Enabled=false; cancel = new(); movement = null;
            if(world.RestSupported) await EnsurePosture(false,cancel.Token);
            message = "Calibrating aim from live facing angleâ€¦";
            lastCalibrationError = null;
            bool rangedAim=CurrentOptions().Ranged && !CurrentOptions().HealerMode;
            if(rangedAim && !world.CameraSupported)throw new InvalidOperationException("3D ranged aiming is unavailable: "+world.CameraStatus);
            movement = rangedAim
                ? await Movement.CalibrateRanged(world,cancel.Token,TraceLog.Record,value=>message=value)
                : await Movement.Calibrate(world,cancel.Token,TraceLog.Record);
            message = rangedAim ? "Horizontal and vertical aiming ready. Press F8 in game to hunt." : "Turning ready. Press F8 in game to hunt.";
            System.Media.SystemSounds.Asterisk.Play();
            string calibrationPath=Path.Combine(AppContext.BaseDirectory,"calibration.json");
            if(rangedAim) RangedAimPersistence.Save(calibrationPath,world,movement);
            else File.WriteAllText(calibrationPath,JsonSerializer.Serialize(new { TimeUtc = DateTime.UtcNow, movement.Forward, movement.RadiansPerPixel, movement.UnitsPerMs }, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) { lastCalibrationError = ex.Message; TraceLog.Record("calibration failed", new { Error = ex.Message }); Stop(ex.Message); System.Media.SystemSounds.Exclamation.Play(); }
        finally { Input.PickupHoldProvider=null;nearbyPickupCount=0;working = false; settings.Enabled = true; protectionPanel.Enabled=true; automaticRouting.Enabled=true;clearNavigation.Enabled=true; connect.Enabled = true; start.Enabled=true; Input.Release(); cancel?.Dispose(); cancel = null; }
    }
    async Task RunHealer()
    {
        Options o=CurrentOptions();o.Save();working=true;settings.Enabled=false;protectionPanel.Enabled=false;automaticRouting.Enabled=false;clearNavigation.Enabled=false;connect.Enabled=false;start.Enabled=false;cancel=new();var token=cancel.Token;
        buffPolicy.Restart(); playerGreeting.Reset();
        var previousAdvance=movement?.CanAdvance;
        try
        {
            if(o.GroupMode)
            {
                if(movement==null || !world.PartySupported || string.IsNullOrWhiteSpace(o.GroupTankName))
                    throw new InvalidOperationException("Select a tank from the live party roster and calibrate movement before starting the healbot.");
                o.HuntRadius=o.GroupFollowLimit;o.AutoPickupNearbyLoot=false;o.PrioritizeGamekeeper=false;
                o.ClearNearbyEnemies=false;lockedTarget=null;encounter.Reset();combatPressure.Reset();
                movement.CanAdvance=(from,to)=>activeHuntAnchor is Vec anchor &&
                    Targeting.BoundaryStepAllowed(from,to,anchor,(double)o.GroupFollowLimit) &&
                    (o.AutomaticRouting?navigation.CanAdvance(from,to,avoidZones):Avoidance.BlockedSegment(from,to,avoidZones)==null);
            }
            nextSupportPreflight=0;nextHealAt=0;manaRecovery.Reset();
            runCharacter=world.LocalPlayer();runZone=world.ActiveZone();activeGuardOptions=o;activeHuntAnchor=runCharacter.Position;activeHealTarget=null;lastHealingSkill=null;
            BeginLoggedHunt(o);
            RefreshGuardScene();
            currentHotbar=CheckedHotbar();runHotbarPage=currentHotbar.PageBase;
            Input.Preflight=SupportPreflight;StartNearbyPickup(o);
            if(world.RestSupported)await EnsurePosture(false,token);
            while(true)
            {
                await Input.Delay(100,token);
                var self=world.LocalPlayer();UpdateDetectedCharacter(self);entities=world.Poll();guardSelfId=self.Id;currentParty=world.Party();ApplyPartyNames();
                if (TryGreetNearbyPlayer(o, self.Position, entities, self.Id)) { await Input.Delay(250, token); continue; }
                var health=world.HealthSnapshot();currentHotbar=CheckedHotbar();
                if(o.GroupMode && await TryHealbotRecovery(o,token))continue;
                if(await TryRestoreMana(o,token))continue;
                if(o.AutoDetectHealingSkills)o.HealingSkillKeys=HealerPolicy.DetectHealingKeys(currentHotbar);
                else o.HealingSkillKeys=HealerPolicy.AvailableHealingKeys(o.HealingSkillKeys,currentHotbar);
                o.HealingSkillKeys=AttackKeys(o.HealingSkillKeys,currentHotbar,o.MaintainAreaBuffs);
                activeHealTarget=HealerPolicy.Select(currentParty,self,entities,health,(double)o.PartyHealRange,o.PartyHealBelowPercent);
                var healEffects=world.ActiveEffects();
                var usableHealingKeys=new string(o.HealingSkillKeys.Where(key=>{
                    var s=currentHotbar.Slot(key);var effect=healEffects.Match(s.Name);
                    return (!o.GroupMode || activeHealTarget!=null && GroupHealerPolicy.HealingSkill(s,activeHealTarget.IsSelf(self))) && HealthSkillAllowed(s,o) && (s.SkillUse is SkillUseKind.Instance or SkillUseKind.Cast) && (effect==null || !effect.Active) &&
                        (s.SkillTarget!=SkillTargetKind.Party || healEffects.Available && effect!=null);
                }).ToArray());
                activeHealTarget=HealerPolicy.Select(currentParty,self,entities,health,(double)o.PartyHealRange,o.PartyHealBelowPercent);
                var skill=activeHealTarget==null?null:HealerPolicy.ChooseReady(usableHealingKeys,currentHotbar,healingSkillCursor);
                if(skill==null && await TryDurabilityRepair(o,token))continue;
                if(skill==null && o.GroupMode && await FollowHealbotTank(o,token))continue;
                if(skill==null && await TryMaintainBuff(o,token))continue;
                if(activeHealTarget==null)
                {
                    movement?.StopApproach();Input.Release(preserveNearbyPickup:true);message=o.GroupMode?groupDecision.Status:currentParty.Available?"Healer waiting: party members are above the heal threshold or out of range.":"Healer waiting: character is above the heal threshold.";continue;
                }
                if(skill==null)
                {
                    Input.Release(preserveNearbyPickup:true);message=$"Healer waiting: {activeHealTarget.Member.Name} needs help, but healing skills are cooling down.";continue;
                }
                var targetKey=skill.SkillTarget==SkillTargetKind.Party?null:HealerPolicy.PartyTargetKey(currentParty,activeHealTarget.Member.Id);
                if(targetKey==null && skill.SkillTarget!=SkillTargetKind.Party && !activeHealTarget.IsSelf(self))
                {
                    Input.Release(preserveNearbyPickup:true);message=$"Healer waiting: no safe F-key is mapped for {activeHealTarget.Member.Name}.";continue;
                }
                movement?.StopApproach();Input.Release(preserveNearbyPickup:true);
                int chargeMs=skill.SkillUse==SkillUseKind.Cast?(int)o.HealChargeMilliseconds:0;
                message=$"Healing {activeHealTarget.Member.Name} with {skill.Name} Â· {(chargeMs==0?"instant click":$"charge {chargeMs} ms")}";
                TraceLog.Record("healer target selected",new {activeHealTarget.Member.Id,activeHealTarget.Member.Name,activeHealTarget.PartyIndex,activeHealTarget.Health,activeHealTarget.Percent,TargetKey=targetKey?.ToString(),SkillKey=skill.Key,Skill=skill.Name,ChargeMilliseconds=chargeMs});
                healerRecipientKey=HealerPolicy.PartyTargetKey(currentParty,activeHealTarget.Member.Id);
                healerCasting=true;
                try
                {
                    if(o.GroupMode)GroupHealerPreflight(o);
                    if(targetKey.HasValue)await Input.Key(targetKey.Value,70,token);
                    await Input.Key((Keys)skill.Key[0],70,token);
                    lastHealingSkill=skill.Key;
                    if(o.GroupMode)GroupHealerPreflight(o);
                    if(!await CastHealthCheckedSkill(skill,o,token))continue;
                }
                catch(HealerRecipientChangedException)
                {
                    movement?.StopApproach();Input.Release();activeHealTarget=null;
                    message="Healbot: recipient changed or left range; selecting again.";
                    continue;
                }
                finally{healerCasting=false;healerRecipientKey=null;}
                healingSkillCursor=(healingSkillCursor+1)%Math.Max(1,o.HealingSkillKeys.Length);
                TraceLog.Record("healer cast finished",new {Target=activeHealTarget.Member.Name,TargetKey=targetKey?.ToString(),SkillKey=skill.Key,Skill=skill.Name,ChargeMilliseconds=chargeMs,After=world.HealthSnapshot().GetValueOrDefault(activeHealTarget.Member.Id)});
            }
        }
        catch(OperationCanceledException){TraceLog.Record("healer stopped",new {Reason="Stop/focus/cancellation"});Stop("Healer stopped. Press F8 to start again.");}
        catch(Exception ex){TraceLog.Record("healer failed",new {Error=ex.Message});Stop(ex.Message);}
        finally{FinishLoggedHunt(message);deathRecovery.Reset();healerFollowing=false;healerCasting=false;healerRecipientKey=null;movement?.StopApproach();if(movement!=null)movement.CanAdvance=previousAdvance;Input.PickupHoldProvider=null;nearbyPickupCount=0;working=false;settings.Enabled=true;protectionPanel.Enabled=true;automaticRouting.Enabled=true;clearNavigation.Enabled=true;connect.Enabled=true;start.Enabled=true;Input.Release();Input.Preflight=null;activeHealTarget=null;lastHealingSkill=null;runCharacter=null;activeHuntAnchor=null;activeGuardOptions=null;runHotbarPage=null;runZone=null;cancel?.Dispose();cancel=null;}
    }
    async Task Hunt()
    {
        if (!RequireHotkeys() || busy || working || !connected || clientRecoveryRunning) return;
        if (!Input.Allowed()) { message = "Press F8 while the game is in front."; return; }
        if (CurrentOptions().HealerMode)
        {
            if(CurrentOptions().GroupMode && movement==null){message="Calibrate movement before starting group healer mode.";return;}
            await RunHealer(); return;
        }
        if (movement == null) { message = lastCalibrationError == null ? "Press F6 in game to calibrate movement first." : "F8 paused: " + lastCalibrationError; return; }
        try
        {
            Options o = CurrentOptions(); o.Save(); working = true; settings.Enabled = false; protectionPanel.Enabled=false;automaticRouting.Enabled=false;clearNavigation.Enabled=false; connect.Enabled = false; start.Enabled=false; cancel = new(); var token = cancel.Token;
            if(o.GroupMode)
            {
                if(!world.PartySupported || string.IsNullOrWhiteSpace(o.GroupTankName))throw new InvalidOperationException("Select a tank from the live party roster before starting group mode.");
                o.ClearNearbyEnemies=false; // Group combat is bounded to the tank area; it does not pull unrelated adds or leave for loot.
                o.HuntRadius=o.GroupFollowLimit;
            }
            var drive = movement;
            drive.TurnSpeedDegreesPerSecond=(double)o.TurnSpeedDegreesPerSecond;
            runCharacter=world.LocalPlayer();
            // Capture the activation point once.  Solo fixed-target combat uses
            // this immutable point as its standing location; only group mode is
            // allowed to replace the working anchor with the tank position.
            Vec activationLocation = world.PlayerPosition();
            Vec anchor = activationLocation;
            double savedHuntHeading=runCharacter.Heading;
            double savedHuntHeight=runCharacter.Height;
            SavedNavigationRoute? selectedSavedRoute=null;
            int selectedSavedRouteSlot=-1;
            int activeSavedRouteSlot=0;
            drive.TargetHeightOffset=(double)o.RangedVerticalAimOffset;
            runZone=world.ActiveZone();unreachableTargets.Clear();
            navigation.SelectTargetSelection(o.Target);
            // If the primary saved farming point is occupied when the hunt is
            // activated, choose the first free alternative instead of entering
            // another player's spot. The activation point is otherwise kept as
            // the normal anchor so existing profiles behave unchanged.
            entities=world.Poll();
            guardSelfId=runCharacter.Id;
            bool RouteCompatible(SavedNavigationRoute route) =>
                RecoveryRouting.Compatible(route,runZone.Value,runCharacter.Name,savedHuntHeight);
            bool RouteHasProfile(SavedNavigationRoute route) =>
                !string.IsNullOrWhiteSpace(route.Character) || route.Height>0 || route.HuntRadius>0 ||
                route.RevivalDelaySeconds>0 || !route.FarmOnArrival || route.RepairAfterDeath;
            SavedNavigationRoute? activeRouteProfile=null;
            int activeRevivalDelaySeconds=Math.Clamp(o.RevivalDelaySeconds,0,600);
            bool activeFarmOnArrival=o.FarmOnArrival;
            void ApplyRouteProfile(SavedNavigationRoute? route)
            {
                activeRouteProfile=route;
                activeRevivalDelaySeconds=Math.Clamp(route is not null && RouteHasProfile(route) ? route.RevivalDelaySeconds : o.RevivalDelaySeconds,0,600);
                activeFarmOnArrival=route is not null && RouteHasProfile(route) ? route.FarmOnArrival : o.FarmOnArrival;
            }
            var activationRoute=navigation.SavedRoutesForZone(runZone.Value)
                .Where(item=>RouteCompatible(item.Route) && (item.Route.Anchor-anchor).Length<=2.5)
                .OrderBy(item=>(item.Route.Anchor-anchor).Length).FirstOrDefault();
            int startupSlot=o.GroupMode?-1:RecoveryTravel.StartupSlot(navigation.SavedRoutes,anchor,runZone.Value,runCharacter.Name,savedHuntHeight,SelectedSavedNavigationSlot(),(double)o.RouteCorridorRadius);
            bool startupRouteTravel=startupSlot>=0;
            if(startupRouteTravel)
            {
                var route=navigation.GetSavedRoute(startupSlot)!;
                activationRoute=(startupSlot,route);
                // Starting along a recorded path farms at its saved endpoint;
                // only activation already at that endpoint preserves the exact spot.
                if((route.Anchor-anchor).Length>2.5)
                {
                    anchor=activationLocation=route.Anchor;savedHuntHeading=route.Heading;
                    if(route.Height>0)savedHuntHeight=route.Height;
                }
                TraceLog.Record("startup route selected",new{Slot=startupSlot,JoinRadius=o.RouteCorridorRadius,Position=world.PlayerPosition(),Anchor=anchor,Height=runCharacter.Height,DestinationHeight=route.Height,DistanceToPath=RecoveryTravel.Nearest(route,runCharacter.Position).Distance,PathHeightsRecorded=false});
            }
            if(resumingClient is {} resumed)
            {
                if(!ClientRecoveryPolicy.SameCharacter(resumed,runCharacter.Name,runZone.Value,o.Target))
                    throw new InvalidOperationException("Client resume character/zone/targets changed.");
                var resumeRoute=navigation.GetSavedRoute(resumed.Slot);
                if(resumeRoute==null || RecoveryTravel.Nearest(resumeRoute,world.PlayerPosition()).Distance>(double)o.RouteCorridorRadius)
                    throw new InvalidOperationException("Client resume requires the saved route corridor.");
                activationRoute=(resumed.Slot,resumeRoute);startupSlot=resumed.Slot;startupRouteTravel=true;
                anchor=activationLocation=resumed.Anchor;savedHuntHeading=resumed.Heading;savedHuntHeight=resumed.Height;
                resumingClient=null;
            }
            if(activationRoute.Route!=null)activeSavedRouteSlot=activationRoute.Slot;
            ApplyRouteProfile(activationRoute.Route);
            // A saved anchor is a standing location, so use a small floor
            // radius even when anti-kill-steal is configured narrowly. Prefer
            // the route's farming radius so occupancy follows the saved tab.
            bool startSavedReturn=startupRouteTravel || !o.GroupMode && o.UseAlternativeHuntRoutes && activeRouteProfile!=null &&
                RecoveryRouting.Occupied(anchor,savedHuntHeight,activeRouteProfile.HuntRadius>0?activeRouteProfile.HuntRadius:(double)o.HuntRadius,entities,runCharacter.Id);
            activeHuntAnchor=activationLocation;
            ArmClientRecovery(runCharacter,runZone.Value,anchor,savedHuntHeading,savedHuntHeight,o,activeRouteProfile,activeSavedRouteSlot);
            if(o.AutoReviveAfterDeath)
            {
                string? routeProblem=RecoveryRouting.SavedReturnProblem(activeRouteProfile,runZone.Value,runCharacter.Name,savedHuntHeight,anchor);
                if(routeProblem!=null)throw new InvalidOperationException(routeProblem);
            }
            activeExcursion=o.GroupMode ? null : new HuntExcursion(anchor,(double)o.HuntRadius);
            navigation.Observe(world.NavigationContext(runCharacter),runCharacter.Position,runCharacter.Height);
            // Manual Home/End recordings own the persistent route slots.
            navigation.EndRecording();deathRecovery.Reset();faultDeathWatch.Reset();deathReturnInProgress=false;
            pendingPriorityGamekeeper=null;gamekeeperTransition=false;priorityInterruptibleActivity=false;returningFromPriority=false;deathRecoveryActive=false;
            rangedPull.Reset(); rangedTagging=false;
            combatPressure.Reset();defensePending=false;defenseRepositioning=false;defenseStep=null;inferredDefense=null;buffInProgress=false;nextEngagementObservation=0;
            courtesy.Reset(); playerGreeting.Reset(); encounter.Reset(); deferredLoot.Clear(); encounterExistingDrops=null; encounterAnchor=null; encounterHasAttack=false; encounterQuietSince=0; encounterUnknownSince=0;
            targetSearch=null;lastTargetWait="";
            activeGuardOptions=o; RefreshGuardScene();
            combatPressure.Observe(world.TargetHealth(guardSelfId),Environment.TickCount64);
            if(world.RestSupported) await EnsurePosture(false,token);
            Input.Preflight=ProtectionPreflight;StartNearbyPickup(o);
            activeMovementBoundary=(double)o.HuntRadius;healingWarning=null;healingRestPending=false;healingRest=null;
            drive.CanAdvance=(from,to)=>Targeting.BoundaryStepAllowed(from,to,anchor,activeMovementBoundary) &&
                (o.AutomaticRouting ? navigation.CanAdvance(from,to,avoidZones) : Avoidance.BlockedSegment(from,to,avoidZones)==null);
            nextHealAt = 0;
            recoveryCursor = 0;
            var startingBar = world.Hotbar(); runHotbarPage = startingBar.PageBase;
            string configuredKeys=o.SkillKeys; o.SkillKeys=AttackKeys(o.AutoDetectSkills ? SkillRotation.DetectKeys(startingBar) : SkillRotation.AvailableKeys(configuredKeys,startingBar),startingBar,o.MaintainAreaBuffs);
            if(o.SkillKeys!=configuredKeys)TraceLog.Record("unavailable attack slots skipped",new {Configured=configuredKeys,Using=o.SkillKeys,BasicAttackOnly=o.SkillKeys.Length==0});
            BeginLoggedHunt(o);
             TraceLog.Record("hunt started", new { Anchor = activationLocation, ActivationLocation = activationLocation, o.HuntRadius,o.RouteCorridorRadius,o.LootPickupRadius,o.LeaveAreaWhenEmpty,o.Player, o.Target, o.SkillKeys, o.LootHoldMs, PriorityLootObjects = true, o.AntiKillSteal, o.OtherPlayerRadius, o.AvoidNames });
            var skillDue = o.SkillKeys.ToDictionary(c => c, _ => 0L);
            int skillCursor = 0; bool gamekeeperExcursion=false;
            // Offensive combat skills are deliberately infrequent.  A cast is
            // permitted only for a wounded five-target pack; this timer is
            // shared across target switches so a retarget cannot bypass the
            // 1.5-second spacing requirement.
            long nextCombatSkillAt=0;
            double responseRadius=Targeting.ResponseRadius((double)o.HuntRadius,(double)o.GamekeeperResponseRadius);
             bool gamekeeperReturnPending=false;
             bool lootReturnPending=false;
             long nextAnchorLootSweep=0;
             bool gamekeeperDefeated=false;
             bool gamekeeperReturnRouting=false;
             long nextGamekeeperReturnTrace=0;
              Vec gamekeeperReturnLocation=anchor;
              double gamekeeperReturnHeading=savedHuntHeading;
              bool stationaryAssistReturnPending=false;
              bool stopAfterDeathReturn=false;
            bool startupReturnPending=false;
            long anchorAdjustmentRetryAt=0,nextAnchorAdjustmentTrace=0;
            bool StationaryReturnContext()
            {
                if(!activeFarmOnArrival || o.GroupMode || o.HealerMode || o.Ranged || !Targeting.IsStationaryHuntFilter(o.Target) ||
                    repairInProgress || deathRecovery.Pending && !deathRecovery.RepairCompleted || runCharacter==null ||
                    world.ActiveZone()!=runZone || !StationaryReturnDefense.NearAnchor(world.PlayerPosition(),anchor))return false;
                var self=world.LocalPlayer();
                return LocalCharacter.Same(runCharacter,self) && world.TargetHealth(self.Id) is {Known:true,Dead:false} &&
                    double.IsFinite(self.Height) && double.IsFinite(savedHuntHeight) && self.Height>0 && savedHuntHeight>0 &&
                    Math.Abs(self.Height-savedHuntHeight)<2 && Avoidance.BlockedPoint(self.Position,avoidZones)==null;
            }
            bool RetryAnchorAdjustment(Exception failure)=>failure is TurnUnresponsiveException or MovementBlockedException or AnchorReturnException && StationaryReturnContext();
            void DeferAnchorAdjustment(Exception failure)
            {
                ReleaseCombatPickup();drive.StopApproach();Input.Release();drive.ResetTurnResponse();lockedTarget=null;
                faultDeathWatch.Reset();anchorAdjustmentRetryAt=Environment.TickCount64+500;
                message="Holding near the saved anchor; defending nearby targets before retrying arrival.";
                if(Environment.TickCount64>=nextAnchorAdjustmentTrace)
                {
                    nextAnchorAdjustmentTrace=Environment.TickCount64+2000;
                    TraceLog.Record("anchor adjustment deferred for stationary defense",new{Error=failure.Message,
                        Position=world.PlayerPosition(),Anchor=anchor,ReturnPending=deathRecovery.Pending,
                        lootReturnPending,stationaryAssistReturnPending,startupReturnPending});
                }
            }
            async Task<bool> DefendDuringAnchorReturn(CancellationToken defenseToken)
            {
                Input.CheckSafety(defenseToken);
                if(!StationaryReturnContext())return false;
                RefreshGuardScene();
                var health=world.HealthSnapshot();var position=world.PlayerPosition();int level=world.PlayerLevel();
                ObserveEncounter(o,health,position,level);
                double swingRange=Math.Max(Math.Min((double)o.MeleeRange,Targeting.MeleeAttackRange)+.35,Targeting.MeleeAttackRange+1.0);
                var candidate=entities.Where(e=>e.Monster && !e.PriorityLootObject &&
                    (Targeting.IsStationaryHuntTargetId(e.Id) || o.PrioritizeGamekeeper && Targeting.IsGamekeeper(e)) &&
                    health.GetValueOrDefault(e.Id) is {Known:true,Dead:false} && (e.Position-position).Length<=swingRange &&
                    (encounter.IsEngaged(e) || courtesy.StartedHere(e) || MatchesRequestedTarget(e,o,level)) &&
                    TargetGuardReason(e,health.GetValueOrDefault(e.Id),position,o)==null)
                    .OrderBy(e=>o.PrioritizeGamekeeper && Targeting.IsGamekeeper(e)?0:1)
                    .ThenByDescending(e=>health.GetValueOrDefault(e.Id).Current).FirstOrDefault();
                if(candidate==null)return false;
                long episode=deathRecovery.Episode;Vec defenseAnchor=anchor;
                StationaryReturnDefense.Observation ObserveDefense()
                {
                    RefreshGuardScene();var liveHealth=world.HealthSnapshot();var self=world.LocalPlayer();
                    var current=entities.FirstOrDefault(e=>TargetIdentity(e)==TargetIdentity(candidate));
                    bool ambiguous=entities.Any(e=>e.Id==candidate.Id && TargetIdentity(e)!=TargetIdentity(candidate));
                    ObserveEncounter(o,liveHealth,self.Position,level);
                    return new(self.Position,anchor,liveHealth.GetValueOrDefault(self.Id),current,
                        liveHealth.GetValueOrDefault(candidate.Id),swingRange,
                        !ambiguous && deathRecovery.Episode==episode && anchor==defenseAnchor && StationaryReturnContext(),
                        current!=null && (encounter.IsEngaged(current) || courtesy.StartedHere(current) || MatchesRequestedTarget(current,o,level)) &&
                        TargetGuardReason(current,liveHealth.GetValueOrDefault(current.Id),self.Position,o)==null);
                }
                if(!StationaryReturnDefense.CanDefend(ObserveDefense()))return false;
                bool previousNavigationOwner=navigationInputOwned;var previousGuard=stationaryReturnDefenseGuard;
                var previousPickup=Input.PickupHoldProvider;
                navigationInputOwned=true;stationaryReturnDefenseGuard=ObserveDefense;lockedTarget=candidate;defensePending=false;defenseStep=null;
                Input.PickupHoldProvider=null;ReleaseCombatPickup();
                if(!encounter.Active)
                {
                    encounter.Begin();encounterExistingDrops=world.Loot().Select(item=>(item.KeyA,item.KeyB)).ToHashSet();
                    encounterAnchor=anchor;encounterHasAttack=false;
                }
                TraceLog.Record("stationary defense during anchor return",new{candidate.Id,candidate.DisplayName,
                    Position=position,Anchor=anchor,Distance=(candidate.Position-position).Length,SwingRange=swingRange,
                    ReturnPending=deathRecovery.Pending,Episode=episode});
                try
                {
                    var outcome=await StationaryReturnDefense.RunAsync(ObserveDefense,()=>drive.StopApproach(),async (sample,ct)=>
                    {
                        var current=sample.Target!;lockedTarget=current;
                        Input.HoldMouse(false,true,ct);
                        courtesy.MarkAttack(current);encounter.MarkAttack(current,sample.TargetHealth);encounterHasAttack=true;
                        encounter.NoteAttack(sample.Position,Encounter.CollateralReach((double)o.MeleeRange));encounterQuietSince=0;
                        try {await drive.Face(world,current.Position-world.PlayerPosition(),ct,.035);}
                        catch(TurnUnresponsiveException) {drive.ResetTurnResponse();}
                        // Reuse survival rules without casting offensive skills,
                        // chasing, resting, or repeating completed repair stages.
                        await TryHeal(drive,o,ct);
                        var bar=CheckedHotbar();long now=Environment.TickCount64;
                        int healIndex=CombatSkillPolicy.Choose(o.SkillKeys,skillCursor,bar,skillDue,now,SkillConditionHealth(o),o,
                            default,targetReady:true,offensiveReady:false,delayReady:false,
                            eligible:slot=>CombatSkillPolicy.IsPriorityHeal(slot,o) && ManaSkillAllowed(slot,o));
                        if(healIndex>=0)
                        {
                            char key=o.SkillKeys[healIndex];var slot=bar.Slot(key);
                            await Input.Key((Keys)key,50,ct);await Input.Delay(80,ct);
                            if(await CastHealthCheckedSkill(slot,o,ct))
                            {
                                Input.HoldMouse(false,true,ct);await Input.Delay(150,ct);
                                var after=CheckedHotbar().Slot(key);
                                skillDue[key]=Environment.TickCount64+SkillRotation.RetryDelayMilliseconds(slot,after.RemainingCooldown>0 || after.Locked,o.SkillSeconds);
                                skillCursor=(healIndex+1)%Math.Max(1,o.SkillKeys.Length);
                            }
                        }
                    },()=>Input.HoldMouse(false,false,default),Input.Delay,()=>Environment.TickCount64,defenseToken);
                    return outcome!=StationaryReturnDefenseOutcome.Unavailable;
                }
                catch(StationaryReturnYieldException){return true;}
                finally
                {
                    drive.StopApproach();Input.HoldMouse(false,false,default);lockedTarget=null;
                    stationaryReturnDefenseGuard=previousGuard;navigationInputOwned=previousNavigationOwner;
                    Input.PickupHoldProvider=previousPickup;
                }
            }

            Task RestoreSavedHuntFacing(CancellationToken restoreToken)=>RestoreHuntFacing(restoreToken,1);
            Task RestoreLootFacing(CancellationToken restoreToken)=>RestoreHuntFacing(restoreToken,3);
            async Task RestoreHuntFacing(CancellationToken restoreToken,int attempts)
            {
                if(!double.IsFinite(gamekeeperReturnHeading))return;
                Vec desiredForward=Movement.FromClientHeading(gamekeeperReturnHeading);
                int observations=await FacingRestore.RunAsync(ct=>drive.Face(world,desiredForward,ct),drive.ResetTurnResponse,
                    ()=>drive.StopApproach(),Input.Delay,()=>Environment.TickCount64,
                    ()=>new TurnUnresponsiveException(world.PlayerPosition(),desiredForward),restoreToken,attempts,
                    attempt=>TraceLog.Record("saved facing retry",new{Attempt=attempt,Position=world.PlayerPosition(),Heading=gamekeeperReturnHeading}),
                    timeoutMilliseconds:FacingRestore.TimeoutMilliseconds(drive.TurnSpeedDegreesPerSecond),
                    failureObserved:failure=>TraceLog.Record("saved facing failed",new{failure.Reason,failure.Attempt,
                        failure.ElapsedMilliseconds,failure.TimeoutMilliseconds,failure.BestErrorRadians,failure.ProgressExtensions,
                        failure.TotalElapsedMilliseconds,Position=world.PlayerPosition(),
                        ActualHeading=world.PlayerHeading(),SavedHeading=gamekeeperReturnHeading,drive.TurnSpeedDegreesPerSecond}),
                    deadlineFailure:()=>new TurnUnresponsiveException(world.PlayerPosition(),desiredForward,
                        "Saved facing did not settle within its turn-speed allowance."),
                    errorRadians:()=>Movement.Angle(Movement.FromClientHeading(world.PlayerHeading()),desiredForward));
                TraceLog.Record("restored saved hunt facing",new {Heading=gamekeeperReturnHeading,Position=world.PlayerPosition(),Attempts=observations,Zone=runZone});
            }
            var fallbackCycle=new RecoveryFallbackCycle(activeSavedRouteSlot);
            var recoveryReference=activeRouteProfile;
            var originalSavedPoint=(Slot:activeSavedRouteSlot,Location:anchor,Heading:savedHuntHeading,Height:savedHuntHeight);
            if(startSavedReturn)
            {
                try { await MoveToSavedHuntAnchor(token,false); }
                catch(DeathRecoveryRequiredException) { /* Continue into the recovery loop below. */ }
                catch(Exception ex) when(RetryAnchorAdjustment(ex)) {startupReturnPending=true;DeferAnchorAdjustment(ex);}
                if(!startupReturnPending && !startupRouteTravel && !deathRecovery.Pending && !activeFarmOnArrival)
                {
                    message="Saved route reached; farming on arrival is disabled.";
                    TraceLog.Record("saved route reached without farming",new{Slot=selectedSavedRouteSlot,Location=anchor,Zone=runZone});
                    return;
                }
            }
            async Task<bool> ReturnAfterGamekeeper(CancellationToken returnToken)
            {
                // This transition must run before healing, pickup, or target
                // selection.  Those activities call Input.Check, whose
                // preflight otherwise sees the surviving encounter and keeps
                // throwing EngagedTargetPriorityException before the return
                // route can start.
                if(o.GroupMode || !gamekeeperReturnPending || !gamekeeperDefeated)return false;
                // Keep the route alive between controller passes. Release1.20
                // stopped and restarted W on every pass, then spent the same
                // pass restoring the saved heading. That produced short,
                // uneven movement pulses and could leave the player circling
                // just outside the saved point.
                bool startingReturn=!gamekeeperReturnRouting;
                if(startingReturn)
                {
                    ReleaseCombatPickup();
                    Input.HoldMouse(false,false,returnToken);
                    drive.StopApproach();
                    lockedTarget=null;
                    navigation.BeginGoal("return to saved hunt location after Gamekeeper");
                    TraceLog.Record("Gamekeeper return started",new {Position=world.PlayerPosition(),Location=gamekeeperReturnLocation,Zone=runZone,Engaged=encounter.EngagedCount});
                }
                gamekeeperReturnRouting=true;
                const double returnArrivalTolerance=3.25;
                message="Returning to saved hunt location after Gamekeeper";
                returningFromPriority=true;
                try
                {
                    Vec current=world.PlayerPosition();
                    // The client can stop a character against a collision
                    // envelope a few units wide. Treat that settled envelope
                    // as the saved point, then restore facing once; do not
                    // keep turning while the route is still moving.
                     if((current-gamekeeperReturnLocation).Length>returnArrivalTolerance)
                    {
                        // Use the active completion boundary as a route
                        // allowance.  An engaged Gamekeeper can legitimately
                        // carry the player beyond the fresh-response radius;
                        // the destination is still the fixed activation point.
                        await NavigateTo(drive,gamekeeperReturnLocation,anchor,o,returnToken,
                            boundaryRadius:Math.Max(responseRadius,activeCompletionBoundary));
                    }
                }
                catch(RouteUnavailableException ex)
                {
                    drive.StopApproach();
                    TraceLog.Record("Gamekeeper return route retry",new {Reason=ex.Message,Position=world.PlayerPosition(),Location=gamekeeperReturnLocation,Zone=runZone});
                    await Input.Delay(100,returnToken);
                    return true;
                }
                catch(MovementBlockedException ex)
                {
                    drive.StopApproach();
                    TraceLog.Record("Gamekeeper return movement retry",new {Reason=ex.Message,Position=ex.Position,Location=gamekeeperReturnLocation,Zone=runZone});
                    await Input.Delay(100,returnToken);
                    return true;
                }
                finally { returningFromPriority=false; }

                Vec settled=world.PlayerPosition();
                bool settledAtPoint=(settled-gamekeeperReturnLocation).Length<=returnArrivalTolerance;
                if(settledAtPoint)
                {
                    drive.StopApproach();
                    try { await RestoreSavedHuntFacing(returnToken); }
                    catch(TurnUnresponsiveException ex)
                    {
                        drive.ResetTurnResponse();
                        TraceLog.Record("Gamekeeper return facing retry",new {Reason=ex.Message,Position=ex.Position,Location=gamekeeperReturnLocation,Zone=runZone});
                        await Input.Delay(100,returnToken);
                        return true;
                    }
                    gamekeeperReturnPending=false;
                    gamekeeperDefeated=false;
                    gamekeeperReturnRouting=false;
                    gamekeeperExcursion=false;
                    TraceLog.Record("returned to saved hunt location after Gamekeeper",new {Position=settled,Location=gamekeeperReturnLocation,Heading=gamekeeperReturnHeading,Zone=runZone,Engaged=encounter.EngagedCount});
                }
                else
                {
                    long now=Environment.TickCount64;
                    if(now>=nextGamekeeperReturnTrace)
                    {
                        nextGamekeeperReturnTrace=now+500;
                        TraceLog.Record("Gamekeeper return still in progress",new {Position=settled,Location=gamekeeperReturnLocation,Distance=(settled-gamekeeperReturnLocation).Length,Zone=runZone,Engaged=encounter.EngagedCount});
                    }
                }
                return true;
            }
            async Task ReturnToSavedHuntPointAfterLoot(CancellationToken returnToken)
            {
                ReleaseCombatPickup();Input.HoldMouse(false,false,returnToken);drive.StopApproach();
                drive.ResetTurnResponse(); // Loot/idle time does not belong to the new return goal.
                message="Returning to saved hunt point after loot";
                navigation.BeginGoal("return to saved hunt point after loot");
                returningFromPriority=true;
                try
                {
                    bool arrived=await AnchorArrival.ReturnAsync(world.PlayerPosition,anchor,NearbyLootPickup.AnchorArrivalTolerance,
                        async ct=>{await NavigateTo(drive,anchor,anchor,o,ct,
                            boundaryRadius:(double)o.LootPickupRadius,arrivalTolerance:NearbyLootPickup.AnchorArrivalTolerance);},
                        ()=>drive.StopApproach(),RestoreLootFacing,Input.Delay,()=>Environment.TickCount64,returnToken,
                        defend:DefendDuringAnchorReturn);
                    if(!arrived)throw new AnchorReturnException("Loot return could not settle at the saved anchor; stopped rather than resume away from it.");
                    lootReturnPending=false;
                    TraceLog.Record("returned to saved hunt point after loot",new {Position=world.PlayerPosition(),Location=anchor,Heading=gamekeeperReturnHeading,Zone=runZone});
                }
                finally{returningFromPriority=false;drive.StopApproach();}
            }
            async Task<bool> ReturnToSavedHuntPointAfterStationaryAssist(CancellationToken returnToken)
            {
                const double arrivalTolerance=NearbyLootPickup.AnchorArrivalTolerance;
                ReleaseCombatPickup();Input.HoldMouse(false,false,returnToken);drive.StopApproach();
                message="Returning to saved hunt point after stationary melee assist";
                navigation.BeginGoal("return after stationary melee assist");
                returningFromPriority=true;
                bool arrived=false;
                try
                {
                    arrived=await AnchorArrival.ReturnAsync(world.PlayerPosition,anchor,arrivalTolerance,
                        async ct=>
                        {
                            try {await NavigateTo(drive,anchor,anchor,o,ct,
                                boundaryRadius:Math.Max((double)o.HuntRadius,activeCompletionBoundary),arrivalTolerance:arrivalTolerance);}
                            catch(RouteUnavailableException ex) {drive.StopApproach();TraceLog.Record("stationary assist return route retry",new {Reason=ex.Message,Anchor=anchor});}
                        },()=>drive.StopApproach(),RestoreSavedHuntFacing,Input.Delay,()=>Environment.TickCount64,returnToken,
                        defend:DefendDuringAnchorReturn);
                    if(!arrived)
                    {
                        TraceLog.Record("stationary assist return still pending",new {Position=world.PlayerPosition(),Anchor=anchor});
                        return false;
                    }
                    TraceLog.Record("returned to saved hunt point after stationary melee assist",new {Position=world.PlayerPosition(),Location=anchor,Heading=gamekeeperReturnHeading,Zone=runZone});
                    return true;
                }
                finally { returningFromPriority=false; if(!arrived)drive.StopApproach(); }
            }
            async Task MoveToSavedHuntAnchor(CancellationToken returnToken,bool afterDeath)
            {
                var reference=recoveryReference ?? activeRouteProfile ?? throw new RouteUnavailableException("Record a return route before using saved-route fallback.");
                double previousBoundary=activeMovementBoundary;
                deathReturnInProgress=true;returningFromPriority=true;
                Input.PickupHoldProvider=null;ReleaseCombatPickup();drive.StopApproach();Input.Release();drive.ResetTurnResponse();
                RecoveryPath? path=null;
                long nextOccupancyCheck=0,progressAt=Environment.TickCount64;
                int progressIndex=-1;double bestDistance=double.PositiveInfinity;
                bool alternatives=!o.GroupMode && o.UseAlternativeHuntRoutes;
                bool Occupied(int slot)
                {
                    var route=navigation.GetSavedRoute(slot)!;
                    return RecoveryRouting.Occupied(route.Anchor,route.Height,route.HuntRadius>0?route.HuntRadius:(double)o.HuntRadius,entities,guardSelfId);
                }
                bool CompatibleSlot(int slot)=>navigation.GetSavedRoute(slot) is {} route && RecoveryRouting.CompatibleIdentity(route,runZone!.Value,runCharacter.Name) && RecoveryTravel.SharedOrigin(reference,route);
                void ResetPath(){path=null;progressIndex=-1;bestDistance=double.PositiveInfinity;progressAt=Environment.TickCount64;drive.StopApproach();drive.ResetTurnResponse();}
                void Activate(int slot)
                {
                    var route=navigation.GetSavedRoute(slot)!;
                    bool original=slot==originalSavedPoint.Slot;
                    anchor=activationLocation=gamekeeperReturnLocation=original?originalSavedPoint.Location:route.Anchor;
                    savedHuntHeading=gamekeeperReturnHeading=original?originalSavedPoint.Heading:route.Heading;
                    if(original)savedHuntHeight=originalSavedPoint.Height;
                    else if(route.Height>0)savedHuntHeight=route.Height;
                    activeSavedRouteSlot=selectedSavedRouteSlot=slot;selectedSavedRoute=route;ApplyRouteProfile(route);
                    activeHuntAnchor=anchor;ArmClientRecovery(runCharacter,runZone!.Value,anchor,savedHuntHeading,savedHuntHeight,o,route,slot);activeExcursion=new HuntExcursion(anchor,(double)o.HuntRadius);ResetPath();
                    TraceLog.Record("recovery destination selected",new{Slot=slot,Anchor=anchor,AfterDeath=afterDeath});
                }
                bool ChooseDestination()
                {
                    if(!alternatives || fallbackCycle.Waiting || !fallbackCycle.Rejected(activeSavedRouteSlot) && !Occupied(activeSavedRouteSlot))return false;
                    fallbackCycle.Reject(activeSavedRouteSlot);
                    int slot=fallbackCycle.Select(Navigation.SavedRouteSlotCount,CompatibleSlot,Occupied);
                    if(slot>=0)Activate(slot);
                    else
                    {
                        fallbackCycle.BeginWait();ResetPath();
                        TraceLog.Record("recovery spots occupied",new{ReturnTo=reference.RevivalOrigin,CompatibleRoutes=Enumerable.Range(0,Navigation.SavedRouteSlotCount).Count(CompatibleSlot),RetrySeconds=RecoveryFallbackCycle.RetryMilliseconds/1000});
                    }
                    return true;
                }
                try
                {
                    while(true)
                    {
                        await Input.Delay(0,returnToken);
                        long now=Environment.TickCount64;
                        if(now>=nextOccupancyCheck){RefreshGuardScene();ChooseDestination();nextOccupancyCheck=now+250;}
                        Vec current=world.PlayerPosition();
                        if(path==null)
                        {
                            var destination=fallbackCycle.Waiting?reference:activeRouteProfile!;
                            if(!RecoveryRouting.CompatibleIdentity(destination,runZone!.Value,runCharacter.Name))throw new RouteUnavailableException("Saved route no longer matches the character or map.");
                            var routes=alternatives?navigation.SavedRoutes:new SavedNavigationRoute?[]{destination};
                            var plan=RecoveryTravel.Plan(routes,destination,current,fallbackCycle.Waiting,!afterDeath && startupRouteTravel?(double)o.RouteCorridorRadius:20);
                            // Activation may be up to 2.5 units from the route's
                            // saved endpoint. Finish at the actual hunt anchor.
                            path=new RecoveryPath(plan.Points,fallbackCycle.Waiting?plan.Destination:anchor);
                            activeMovementBoundary=Math.Max((double)o.HuntRadius,Math.Max((current-anchor).Length+2,
                                plan.Points.Select(p=>(p-anchor).Length+2).DefaultIfEmpty(0).Max()));
                            navigation.BeginGoal(fallbackCycle.Waiting?"retreat on saved route to revival point":"follow saved return route");
                        }
                        long defenseAt=Environment.TickCount64;
                        if(!fallbackCycle.Waiting && await DefendDuringAnchorReturn(returnToken))
                        {
                            // Fighting in place is not failed route progress.
                            progressAt+=Math.Max(0,Environment.TickCount64-defenseAt);
                            await Input.Delay(20,returnToken);continue;
                        }
                        Vec? goal=path.Next(current,(from,to)=>Avoidance.BlockedSegment(from,to,avoidZones)==null && navigation.CanAdvance(from,to,avoidZones));
                        if(goal==null)
                        {
                            drive.StopApproach();
                            if(fallbackCycle.Waiting)
                            {
                                if(fallbackCycle.Wait(now))
                                {
                                    int retrySlot=fallbackCycle.StartingSlot;fallbackCycle.Restart();Activate(retrySlot);nextOccupancyCheck=0;continue;
                                }
                                savedReturnPhase="waiting for a free farming spot";
                                string occupiedStatus=Enumerable.Range(0,Navigation.SavedRouteSlotCount).Count(CompatibleSlot)<=1
                                    ?"Saved spot occupied; no compatible alternatives saved."
                                    :"All saved spots occupied.";
                                message=$"{occupiedStatus} At route start; retry in {Math.Ceiling(fallbackCycle.Remaining/1000d):0}s";
                                await Input.Delay(100,returnToken);continue;
                            }
                            RefreshGuardScene();if(ChooseDestination())continue;
                            var arrivedBody=world.LocalPlayer();
                            if(!RecoveryRouting.Compatible(activeRouteProfile!,runZone!.Value,runCharacter.Name,arrivedBody.Height))
                                throw new RouteUnavailableException("Saved anchor position reached on a different or unreadable floor; route arrival was not confirmed.");
                            await RestoreSavedHuntFacing(returnToken);
                            RefreshGuardScene();if(ChooseDestination())continue;
                            if((world.PlayerPosition()-anchor).Length>.5){ResetPath();continue;}
                            fallbackCycle.Arrived(activeSavedRouteSlot);
                            message="Returned to saved anchor and facing";
                            TraceLog.Record("saved hunt anchor reached",new{Position=world.PlayerPosition(),Anchor=anchor,Heading=gamekeeperReturnHeading,Slot=activeSavedRouteSlot,AfterDeath=afterDeath,UsedSavedRoute=true,Zone=runZone});
                            return;
                        }
                        double distance=(goal.Value-current).Length;
                        if(path.Index!=progressIndex || distance<bestDistance-.15){progressIndex=path.Index;bestDistance=distance;progressAt=now;}
                        else if(now-progressAt>20000)throw new RouteUnavailableException("Recovery made no progress toward the recorded waypoint; the route may be blocked.");
                        savedReturnPhase=fallbackCycle.Waiting?"returning to route start":"following saved route";
                        message=$"{savedReturnPhase} · {distance:F1} to waypoint";
                        if(Avoidance.BlockedSegment(current,goal.Value,avoidZones)!=null || !navigation.CanAdvance(current,goal.Value,avoidZones))
                            throw new RouteUnavailableException("The recorded return segment is blocked; stopped before leaving the saved path.");
                        // Approach keeps forward held through successive recorded
                        // waypoints. Resetting the local planner at every point
                        // used to release/repress movement and cause jerky turns.
                        await drive.Approach(world,current,goal.Value-current,returnToken,watchTurns:true,arrivalTolerance:path.ArrivalTolerance);
                        await Input.Delay(25,returnToken);
                    }
                }
                finally
                {
                    fallbackCycle.Pause();savedReturnPhase="";
                    deathReturnInProgress=false;returningFromPriority=false;activeMovementBoundary=previousBoundary;drive.StopApproach();
                    if(!deathRecovery.Pending)StartNearbyPickup(o);
                }
            }
            async Task<bool> TryRecoverAfterDeath(CancellationToken recoveryToken)
            {
                if(!o.AutoReviveAfterDeath)return false;
                Entity self;Health hp;
                try {self=world.LocalPlayer();hp=world.TargetHealth(self.Id);}
                catch(InvalidOperationException) when(deathRecovery.Pending)
                {
                    Input.Release();message="Waiting for the character after death";
                    await Task.Delay(100,recoveryToken);return true;
                }
                ObserveDeath(hp);
                if(!deathRecovery.Pending)return false;
                Input.PickupHoldProvider=null;ReleaseCombatPickup();drive.StopApproach();Input.Release();lockedTarget=null;
                // Keep this request through manual revival and unreadable HP.
                deathRecoveryActive=true;
                try
                {
                    if(!hp.Known)
                    {
                        message="Waiting for health confirmation after death";
                        await Input.Delay(100,recoveryToken);return true;
                    }
                    long remaining=deathRecovery.ReadyAt(activeRevivalDelaySeconds,o.VisualRevivalDetection)-Environment.TickCount64;
                    if(hp.Dead && remaining>0)
                    {
                        message=$"Death detected. Waiting {Math.Ceiling(remaining/1000d):0}s before revive";
                        await Input.Delay((int)Math.Min(250,remaining),recoveryToken);return true;
                    }
                    Keys revive=TryParseReviveKey(o.ReviveKey,out var configured)?configured:Keys.R;
                    bool revived=hp.Known && !hp.Dead;
                    bool ReadRevivalHealth()
                    {
                        try {self=world.LocalPlayer();hp=world.TargetHealth(self.Id);ObserveLoggedHealth(self,hp,world.ActiveZone());return hp.Known;}
                        catch(InvalidOperationException) {hp=default;return false;}
                    }
                    if(o.VisualRevivalDetection && !revived)
                    {
                        await VisualRevival.Run(new LiveRevivalSurface(world,runCharacter!,world.Pid,runZone!.Value,
                            text=>message=text),deathRecovery.ObservedAt,recoveryToken);
                        if(!ReadRevivalHealth() || hp.Dead)throw new InvalidOperationException("Living HP was not confirmed after visual revival.");
                        revived=true;
                    }
                    for(int attempt=0;!o.VisualRevivalDetection && attempt<3 && !revived;attempt++)
                    {
                        message=$"Reviving character (attempt {attempt+1}/3)";
                        TraceLog.Record("revive key input",new{Key=revive.ToString(),Attempt=attempt+1});
                        await Input.Key(revive,100,recoveryToken);
                        await Input.Delay(450,recoveryToken);
                        if(!ReadRevivalHealth())return true;
                        revived=!hp.Dead;
                        // Do not send confirmation into the living character's chat.
                        if(hp.Dead && revive!=Keys.Enter)
                        {
                            TraceLog.Record("revive fallback input",new{Key=Keys.Enter.ToString(),Attempt=attempt+1});
                            await Input.Key(Keys.Enter,100,recoveryToken);
                            await Input.Delay(450,recoveryToken);
                            if(!ReadRevivalHealth())return true;
                            revived=!hp.Dead;
                        }
                    }
                    if(!revived)throw new InvalidOperationException("Revival was not confirmed. Check the revive key under Setup → Death recovery.");
                    if(world.ActiveZone()!=runZone || !RecoveryRouting.SameCharacter(runCharacter!,self))
                        throw new InvalidOperationException("Revival changed the map or character; the saved anchor cannot be used here.");
                    runCharacter=detectedCharacter=self;guardSelfId=self.Id;navigationZone=runZone!.Value;navigationPosition=self.Position;
                    navigation.Observe(world.NavigationContext(self),self.Position,self.Height);
        RefreshImportedCollisionObstacles();
                    deathRecovery.Observe(hp,Environment.TickCount64);deathRecoveryActive=false;
                    long recoveryEpisode=deathRecovery.Episode;
                    if(!deathRecovery.PostRevivalPrepared)
                    {
                    encounter.Reset();courtesy.Reset();deferredLoot.Clear();encounterExistingDrops=null;encounterAnchor=null;encounterHasAttack=false;encounterQuietSince=0;encounterUnknownSince=0;
                    pendingPriorityGamekeeper=null;gamekeeperReturnPending=false;gamekeeperDefeated=false;gamekeeperReturnRouting=false;gamekeeperExcursion=false;completionReturnPending=false;stationaryAssistReturnPending=false;
                    gamekeeperReturnLocation=anchor;gamekeeperReturnHeading=savedHuntHeading;
                    rangedPull.Reset();rangedTagging=false;combatPressure.Reset();defensePending=false;defenseRepositioning=false;defenseStep=null;inferredDefense=null;healingRestPending=false;healingWarning=null;
                    retreatRecovery=null;retreatDrive=null;lootGuardPosition=null;buffInProgress=false;gamekeeperTransition=false;priorityInterruptibleActivity=false;
                    runHotbarPage=world.Hotbar().PageBase;
                    TraceLog.Record("character revived",new{Position=self.Position,Anchor=anchor,Zone=runZone,HP=hp,Episode=recoveryEpisode});
                    if(!deathRecovery.MarkPostRevivalPrepared(recoveryEpisode))throw new DeathRecoveryRequiredException();
                    }
                    if(!deathRecovery.RepairCompleted)
                    {
                        if(o.AutoRepairAfterDeath)await RunRepairAsync(recoveryToken);
                        if(!deathRecovery.MarkRepairCompleted(recoveryEpisode))throw new DeathRecoveryRequiredException();
                    }
                    else TraceLog.Record("saved-route recovery resumed",new{Episode=recoveryEpisode,RepairAlreadyCompleted=true,Position=self.Position,Anchor=anchor});
                    await MoveToSavedHuntAnchor(recoveryToken,true);
                    if(deathRecovery.Episode!=recoveryEpisode)throw new DeathRecoveryRequiredException();
                    deathRecovery.Reset();
                    stopAfterDeathReturn=!activeFarmOnArrival;
                    if(stopAfterDeathReturn)message="Saved anchor reached; Resume farming is disabled.";
                    else StartNearbyPickup(o);
                    return true;
                }
                catch(DeathRecoveryRequiredException)
                {
                    message="Died while returning. Preparing revival again.";
                    return true;
                }
                finally {deathRecoveryActive=false;}
            }
            void RememberGamekeeperReturn(Vec current)
            {
                if(o.GroupMode || !o.ReturnToHuntLocationAfterGamekeeper || gamekeeperReturnPending)return;
                gamekeeperReturnPending=true;gamekeeperDefeated=false;gamekeeperReturnLocation=activationLocation;
                TraceLog.Record("saved hunt location for Gamekeeper return",new {Location=gamekeeperReturnLocation,Heading=gamekeeperReturnHeading,Current=current,Zone=runZone});
            }
            completionReturnPending=false;
            activeCompletionBoundary=Targeting.CompletionRadius((double)o.HuntRadius,(double)o.NearbyEnemyRadius,(double)o.MeleeRange);
            while (true)
            {
                try
                {
                if(faultDeathWatch.Active && StationaryReturnContext())
                {
                    faultDeathWatch.Reset();anchorAdjustmentRetryAt=Environment.TickCount64+500;
                    TraceLog.Record("near anchor fault watch resumed stationary defense",new{Position=world.PlayerPosition(),Anchor=anchor});
                }
                if(Environment.TickCount64<anchorAdjustmentRetryAt)
                {
                    await DefendDuringAnchorReturn(token);await Input.Delay(50,token);continue;
                }
                if(faultDeathWatch.Active)
                {
                    Input.CheckSafety(token);
                    if(faultDeathWatch.Expired(Environment.TickCount64,deathRecovery.Pending))
                        throw new OperationCanceledException("Movement fault recovery timed out. Press F8 to start again.");
                }
                using var faultRecoveryBudget=faultDeathWatch.Active?CancellationTokenSource.CreateLinkedTokenSource(token):null;
                faultRecoveryBudget?.CancelAfter(faultDeathWatch.RecoveryRemaining(Environment.TickCount64));
                if(await TryRecoverAfterDeath(faultRecoveryBudget?.Token ?? token))
                {
                    if(!deathRecovery.Pending && faultDeathWatch.Active)
                    {
                        faultDeathWatch.Reset();TraceLog.Record("fault death recovery completed",new{Position=world.PlayerPosition(),Anchor=anchor,Resume=activeFarmOnArrival});
                    }
                    if(stopAfterDeathReturn)return;
                    continue;
                }
                if(faultDeathWatch.Active)
                {
                    var waitingSelf=world.LocalPlayer();
                    if(runCharacter==null || !LocalCharacter.Same(runCharacter,waitingSelf) || world.ActiveZone()!=runZone)
                        throw new OperationCanceledException("Character or map changed during movement fault recovery.");
                    _=CheckedHotbar();
                    message="Movement fault: input released; monitoring for death for up to two minutes. "+faultDeathWatch.Reason;
                    await Task.Delay(100,token);continue;
                }
                if(startupReturnPending && !deathRecovery.Pending)
                {
                    await MoveToSavedHuntAnchor(token,false);startupReturnPending=false;
                    if(!startupRouteTravel && !activeFarmOnArrival)return;
                    continue;
                }
                await Input.Delay(rangedPull.Active?15:encounter.Active?25:100, token);
                var gamekeeper=PriorityGamekeeper(o);
                if(gamekeeper!=null)RememberGamekeeperReturn(world.PlayerPosition());
                // A dead priority target can leave healing or nearby-pickup
                // input ahead of the return transition. Complete the return
                // first so the surviving encounter is reselected from the
                // saved point instead of interrupting this loop indefinitely.
                if(gamekeeper==null && gamekeeperReturnPending && gamekeeperDefeated && await ReturnAfterGamekeeper(token))continue;
                if(gamekeeper==null && stationaryAssistReturnPending)
                {
                    if(await ReturnToSavedHuntPointAfterStationaryAssist(token))stationaryAssistReturnPending=false;
                    continue;
                }
                if(gamekeeper==null && lootReturnPending)
                {
                    await ReturnToSavedHuntPointAfterLoot(token);continue;
                }
                if (await TryHeal(drive, o, token)) continue;
                if (deathRecovery.Pending)continue;
                if(await TryDurabilityRepair(o,token,lootReturnPending||stationaryAssistReturnPending||gamekeeperReturnPending&&gamekeeperDefeated))continue;
                if(gamekeeper==null && healingRestPending && !HasActiveFight() && combatPressure.RecentDamage(Environment.TickCount64))
                {
                    message="Holding position while checking nearby threats; waiting for damage to stop before resting.";
                    await Input.Delay(100,token);continue;
                }
                if (await TryRestoreMana(o,token)) continue;
                if (deathRecovery.Pending)continue;
                if(gamekeeper==null)
                {
                    if(defensePending && !HasActiveFight())
                    {
                        if(StationaryReturnDefense.ShouldHandOffPressure(o.GroupMode,Targeting.IsStationaryHuntFilter(o.Target),
                            world.PlayerPosition(),anchor,world.TargetHealth(guardSelfId)))
                        {
                            // We are already home. Let protected target selection
                            // run in this pass instead of re-entering the same
                            // damage-return branch on every preflight.
                            defensePending=false;defenseStep=null;drive.StopApproach();
                        }
                        else {await RepositionUnderPressure(drive,anchor,o,token);continue;}
                    }
                    if(!(RangedPullEnabled(o) && rangedPull.Phase==RangedPullPhase.Tagging) && await TryMaintainBuff(o,token))continue;
                    if(deathRecovery.Pending)continue;
                }
                var pos = world.PlayerPosition();
                entities = world.Poll();
                if (TryGreetNearbyPlayer(o, pos, entities, guardSelfId)) { await Input.Delay(250, token); continue; }
                long now = Environment.TickCount64;
                if(now>=nextLootTrackerRead)
                {
                    nextLootTrackerRead=now+LootTrackerPollMilliseconds;
                    ObserveLootTrackerDrops(o,world.Loot());
                }
                int level = world.PlayerLevel();
                RefreshGuardScene();
                var health = world.HealthSnapshot();
                ObserveEncounter(o,health,pos,level);
                gamekeeper=PriorityGamekeeper(o);
                if(gamekeeper!=null)
                {
                    RememberGamekeeperReturn(pos);
                    pendingPriorityGamekeeper=gamekeeper;healingRestPending=false;healingWarning=null;
                    activeCompletionBoundary=Math.Max(activeCompletionBoundary,Targeting.CompletionRadius(responseRadius,(double)o.NearbyEnemyRadius,(double)o.MeleeRange));
                }
                if(!o.GroupMode)
                {
                    if(o.LeaveAreaWhenEmpty && (pos-anchor).Length>(double)o.HuntRadius)completionReturnPending=true;
                    if(encounter.HasEngaged)
                    {
                        double engagedBase=encounter.EngagedCandidates.Select(e=>BaseTargetRadius(e,o))
                            .DefaultIfEmpty(gamekeeperExcursion?responseRadius:(double)o.HuntRadius).Max();
                        activeCompletionBoundary=Math.Max(activeCompletionBoundary,Targeting.CompletionRadius(engagedBase,(double)o.NearbyEnemyRadius,(double)o.MeleeRange));
                        if((pos-anchor).Length>(double)o.HuntRadius || encounter.EngagedCandidates.Any(e=>(e.Position-anchor).Length>(double)o.HuntRadius))completionReturnPending=true;
                    }
                    double playerBoundary=gamekeeper!=null || encounter.HasEngaged || completionReturnPending || o.LeaveAreaWhenEmpty && encounter.Active ? activeCompletionBoundary : gamekeeperExcursion ? responseRadius : (double)o.HuntRadius;
                    if((pos-anchor).Length>playerBoundary+2)throw new InvalidOperationException("Stopped beyond the combat completion boundary.");
                    if(gamekeeper==null && completionReturnPending && !encounter.HasEngaged &&
                        (!o.LeaveAreaWhenEmpty || !encounter.Active && deferredLoot.Count==0))
                    {
                        if((pos-anchor).Length>(double)o.HuntRadius-1)
                        {
                            ReleaseCombatPickup();Input.HoldMouse(false,false,token);
                            message="Returning inside the original hunting area before choosing another targetâ€¦";
                            navigation.BeginGoal("return after engaged fight");returningFromPriority=true;
                            try {await NavigateTo(drive,anchor,anchor,o,token,boundaryRadius:activeCompletionBoundary);}
                            finally {returningFromPriority=false;}
                            continue;
                        }
                        if(activeExcursion is {OutsideTrip:true} trip && !trip.TryCompleteReturn(pos,encounter.HasEngaged))
                            throw new InvalidOperationException("Outside trip cannot finish before returning inside the original hunting area.");
                        drive.StopApproach();completionReturnPending=false;gamekeeperExcursion=false;
                        TraceLog.Record("returned to original hunting area",new {Position=pos,Anchor=anchor,o.HuntRadius});
                        activeCompletionBoundary=Targeting.CompletionRadius((double)o.HuntRadius,(double)o.NearbyEnemyRadius,(double)o.MeleeRange);
                    }
                    if(!encounter.HasEngaged && healingWarning!=null && await TryHeal(drive,o,token))continue;
                }
                // Complete any return that became observable only after the
                // refreshed world snapshot. This remains before ranged pulls
                // and engaged-target selection.
                if(gamekeeper==null && gamekeeperReturnPending && gamekeeperDefeated && await ReturnAfterGamekeeper(token))continue;
                if(gamekeeper==null && await RunRangedPullStep(drive,anchor,o,health,pos,level,skillDue,token))continue;
                Entity? target=o.GroupMode?gamekeeper:GamekeeperPriority.ChooseFirst(encounter,gamekeeper,()=>null);
                if(gamekeeper==null && target!=null && Targeting.IsStationaryHuntTargetId(target.Id))
                    target=StationarySwingCandidate(o,pos,Math.Max(Math.Min((double)o.MeleeRange,Targeting.MeleeAttackRange)+.35,
                        Targeting.MeleeAttackRange+1),health) ?? target;
                if(gamekeeper==null && RangedPullEnabled(o) && rangedPull.Phase==RangedPullPhase.Clearing)
                {
                    // Confirmed Firing tags define the pack lifetime. Within
                    // that lifetime, use the ordinary melee candidate set so
                    // nearby engaged or eligible monsters are cleared like
                    // melee-only combat instead of waiting on one exact tag.
                    var meleeFocus=RangedPull.ChooseMeleeCluster(RangedNearbyTargets(o,health,pos,level),pos);
                    target=meleeFocus?.Target;
                    if(meleeFocus is { } focus)TraceLog.Record("pack melee focus",new {focus.Target.Id,focus.Target.DisplayName,focus.Density,Direction=focus.Target.Position-pos});
                }
                if(!o.GroupMode && RangedPullEnabled(o) && rangedPull.Phase==RangedPullPhase.Clearing && target==null)
                {
                    ReleaseCombatPickup();drive.StopApproach();Input.HoldMouse(false,false,token);
                    message=$"Melee mode: waiting for a nearby combat target to enter {o.RangedMeleeAttackRange:0.#}-unit attack range.";
                    await Input.Delay(100,token);continue;
                }
                if(encounter.HasEngaged && target==null)
                {
                    // Prefer the last locked member (or any still-engaged
                    // member) when target selection has one stale snapshot.
                    // Releasing all input here made a healthy combo look like
                    // a random disengage whenever the object tree refreshed.
                    var retainedEngaged=lockedTarget is Entity locked && encounter.IsEngaged(locked) ? locked : encounter.EngagedCandidates.FirstOrDefault();
                    if(retainedEngaged!=null)
                    {
                        target=retainedEngaged;lockedTarget=retainedEngaged;
                        encounterQuietSince=0;encounterUnknownSince=0;
                        ReleaseCombatPickup();drive.StopApproach();
                        PreserveEngagedSwing(retainedEngaged,"target selection refresh",token);
                    }
                    else
                    {
                        ReleaseCombatPickup();drive.StopApproach();
                        if(encounterUnknownSince==0)encounterUnknownSince=Environment.TickCount64;
                        if(Environment.TickCount64-encounterUnknownSince>1500)
                            throw new InvalidOperationException("An engaged enemy is unavailable or blocked; stopped before selecting a new target.");
                        encounterQuietSince=0;message="Waiting for an engaged enemy to become availableâ€¦";
                        await Input.Delay(100,token);continue;
                    }
                }
                if(target!=null && encounter.IsEngaged(target)){encounterQuietSince=0;encounterUnknownSince=0;}
                if(!o.GroupMode && !o.LeaveAreaWhenEmpty && !encounter.HasEngaged && gamekeeperExcursion && target==null)
                {
                    if((pos-anchor).Length>2)
                    {
                        ReleaseCombatPickup();Input.HoldMouse(false,false,token);
                        message="Returning to hunting area after Gamekeeper";
                        navigation.BeginGoal("return after Gamekeeper");
                        returningFromPriority=true;
                        try {await NavigateTo(drive,anchor,anchor,o,token,boundaryRadius:responseRadius);}
                        finally {returningFromPriority=false;}
                        continue;
                    }
                    drive.StopApproach();gamekeeperExcursion=false;
                }
                if(o.GroupMode && gamekeeper==null)
                {
                    var decision=groupDecision;
                    if(decision.Tank!=null)anchor=decision.Tank.Position;
                    message=decision.Status;
                    if(decision.Action==GroupAction.Follow && decision.Destination is Vec followGoal && decision.Tank!=null)
                    {
                        Input.HoldMouse(false,false,token);
                        navigation.BeginGoal($"follow:{decision.Tank.Id}:{decision.Tank.Generation}");
                        try{await NavigateTo(drive,followGoal,anchor,o,token);}
                        catch(RouteUnavailableException ex){drive.StopApproach();message="Tank follow route blocked: "+ex.Message;await Input.Delay(500,token);}
                        continue;
                    }
                    drive.StopApproach();
                    if(decision.Action!=GroupAction.Attack || decision.Target==null){Input.Release(preserveNearbyPickup:true);await Input.Delay(150,token);continue;}
                    target=decision.Target;
                }
                if(encounter.Active && target==null)
                {
                    target=healingRestPending || o.LeaveAreaWhenEmpty && completionReturnPending ? null : Targeting.ChooseEncounter(entities.Where(e=>TargetGuardReason(e,health.GetValueOrDefault(e.Id),pos,o)==null),
                        encounter.Candidates,health,pos,anchor,o.LeaveAreaWhenEmpty ? (double)o.HuntRadius : responseRadius,o.PrioritizeGamekeeper,o.PrioritizeBreakables);
                    if(target!=null) { encounterQuietSince=0; encounterUnknownSince=0; }
                    else if(encounter.HasUnresolvedNearby)
                    {
                        if(encounterUnknownSince==0) encounterUnknownSince=Environment.TickCount64;
                        if(Environment.TickCount64-encounterUnknownSince>1500) throw new InvalidOperationException("Nearby enemy HP is unavailable; pickup paused and hunt stopped.");
                        encounterQuietSince=0; message="Waiting for nearby enemy HP before pickupâ€¦";
                        await Input.Delay(100,token); continue;
                    }
                    else
                    {
                        encounterUnknownSince=0;
                        if(encounterQuietSince==0) encounterQuietSince=Environment.TickCount64;
                        if(Environment.TickCount64-encounterQuietSince<500) { message="Checking that nearby enemies are clearâ€¦"; await Input.Delay(100,token); continue; }
                        if(deferredLoot.Count>0)
                        {
                            lootReturnPending=true;
                            if(await RunLootJob(deferredLoot.Peek(),drive,anchor,o,token))
                            {
                                deferredLoot.Dequeue();
                                await ReturnToSavedHuntPointAfterLoot(token);
                            }
                            continue;
                        }
                        TraceLog.Record("encounter cleared",new {Position=pos});
                        encounter.Reset(); rangedPull.Reset(); encounterExistingDrops=null; encounterAnchor=null; encounterHasAttack=false; encounterQuietSince=0;
                        if(!completionReturnPending)activeCompletionBoundary=Targeting.CompletionRadius((double)o.HuntRadius,(double)o.NearbyEnemyRadius,(double)o.MeleeRange);
                        if(RangedPullEnabled(o))continue;
                        if(o.LeaveAreaWhenEmpty || healingRestPending)continue;
                    }
                }
                if(target==null && !o.GroupMode && !encounter.Active && !healingRestPending &&
                    o.AutoPickupNearbyLoot && Environment.TickCount64>=nextAnchorLootSweep &&
                    world.Loot().Any(drop=>NearbyLootPickup.InsideAnchor(drop.Position,anchor,(double)o.LootPickupRadius)))
                {
                    // Remember the return before input, including priority/death interruptions.
                    lootReturnPending=true;
                    try{await RunAnchorLoot(drive,anchor,o,token);}
                    finally{nextAnchorLootSweep=Environment.TickCount64+15000;}
                    await ReturnToSavedHuntPointAfterLoot(token);continue;
                }
                if(target==null && !o.GroupMode && !encounter.Active && !healingRestPending &&
                    await GuideToTreasureChest(drive,anchor,o,token))continue;
                if(target==null && healingRestPending && !encounter.HasEngaged){await Input.Delay(100,token);continue;}
                if(!o.GroupMode)target ??= o.LeaveAreaWhenEmpty ? HuntingArea.Choose(entities,health,pos,activeExcursion!,
                    e=>MatchesRequestedTarget(e,o,level),(e,hp)=>TargetGuardReason(e,hp,pos,o)==null,e=>Targeting.PriorityRank(e,o.PrioritizeGamekeeper,o.PrioritizeBreakables),o.PrioritizeBreakables) :
                    Targeting.Choose(entities.Where(e=>TargetGuardReason(e,health.GetValueOrDefault(e.Id),pos,o)==null), health, pos, anchor, (double)o.HuntRadius, e => world.Difficulty(e, level), o.Target, o.AllowedDifficulties,prioritizeGamekeeper:o.PrioritizeGamekeeper,prioritizeBreakables:o.PrioritizeBreakables);
                if (target == null)
                {
                    drive.StopApproach();Input.HoldMouse(false,false,token);
                    bool insideApproval=o.LeaveAreaWhenEmpty && !o.GroupMode && HuntingArea.HasApprovedInside(entities,health,activeExcursion!,e=>MatchesRequestedTarget(e,o,level));
                    double searchRadius=o.LeaveAreaWhenEmpty && !o.GroupMode && !insideApproval ? activeExcursion!.OutsideSearchRadius : (double)o.HuntRadius;
                    var diagnosticEntities=insideApproval ? entities.Where(e=>(e.Position-anchor).Length<=(double)o.HuntRadius) : entities;
                    targetSearch=o.GroupMode ? null : TargetSearch.Explain(diagnosticEntities,health,pos,anchor,searchRadius,o.LeaveAreaWhenEmpty ? searchRadius : responseRadius,
                        e=>world.Difficulty(e,level),o.Target,o.AllowedDifficulties,o.PrioritizeGamekeeper,(e,hp)=>TargetGuardReason(e,hp,pos,o));
                    message=targetSearch?.Message ?? "Waiting: "+groupDecision.Status;
                    if(lastTargetWait!=message)
                    {
                        lastTargetWait=message;
                        TraceLog.Record("target search waiting",new {Position=pos,Anchor=anchor,o.HuntRadius,Report=targetSearch,Status=message});
                    }
                    await Input.Delay(500,token);continue;
                }
                targetSearch=null;lastTargetWait="";
                if(o.LeaveAreaWhenEmpty && !o.GroupMode && !(o.PrioritizeGamekeeper && Targeting.IsGamekeeper(target)) && !encounter.IsEngaged(target) && (target.Position-anchor).Length>(double)o.HuntRadius)
                {
                    activeExcursion!.BeginOutsideTrip(target,(double)o.NearbyEnemyRadius,(double)o.MeleeRange);
                    nextInsideTargetCheck=0;
                    completionReturnPending=true;activeCompletionBoundary=Math.Max(activeCompletionBoundary,activeExcursion.MovementBoundary);
                    TraceLog.Record("outside trip started; home area empty",new {Anchor=anchor,o.HuntRadius,activeExcursion.OutsideSearchRadius,Target=target.Id,target.Position});
                }
                TraceLog.Record("target selected", new { target.Id, target.Name, target.DisplayName, target.PriorityLootObject, target.Position, Distance = (target.Position - pos).Length });
                if(!o.GroupMode && o.PrioritizeGamekeeper && Targeting.IsGamekeeper(target) && (target.Position-anchor).Length>(double)o.HuntRadius)
                {gamekeeperExcursion=true;completionReturnPending=true;}
                lockedTarget = target;
                if(o.PrioritizeGamekeeper && Targeting.IsGamekeeper(target))
                {
                    if(o.GroupMode && activeHuntAnchor.HasValue)anchor=activeHuntAnchor.Value;
                    await StandForGamekeeper(token);
                }
                navigation.BeginGoal($"target:{target.Id}:{target.Generation}:{target.Address}");
                drive.ResetTurnResponse();
                int turnRecoveryAttempts=0;
                int? lastCombatHp=null;
                long approachStarted = now, combatStart = 0; Vec lastTargetPosition = target.Position; int missingHealth = 0;
                long stationaryAttackHeldAt=0,nextStationaryRangeTrace=0; int stationaryAttackBaselineHp=-1;
                bool stationaryAssistUsed=false,stationaryAssistActive=false; Vec stationaryAssistGoal=default;
                long targetMissingSince = 0;
                double bodyAllowance=0; int? bodyProbeHp=null;long bodyProbeAt=0;
                long nextPriorityCheck = 0;
                bool collectAfterTarget = false;
                var existingDrops=encounterExistingDrops ?? world.Loot().Select(i=>(i.KeyA,i.KeyB)).ToHashSet();
                try
                {
                while (true)
                {
                    token.ThrowIfCancellationRequested();
                    if(deathRecovery.Pending)
                    {
                        ReleaseCombatPickup();drive.StopApproach();Input.Release(preserveNearbyPickup:true);lockedTarget=null;
                        break;
                    }
                    if(o.GroupMode && !(o.PrioritizeGamekeeper && Targeting.IsGamekeeper(target)))
                    {
                        if(Environment.TickCount64-guardRefreshedAt>=100)RefreshGuardScene();
                        if(groupDecision.Tank!=null)anchor=groupDecision.Tank.Position;
                    }
                    if (await TryHeal(drive, o, token)) continue;
                    if(deathRecovery.Pending)
                    {
                        ReleaseCombatPickup();drive.StopApproach();Input.Release(preserveNearbyPickup:true);
                        break;
                    }
                    if (await TryRestoreMana(o,token)) continue;
                    if(deathRecovery.Pending)
                    {
                        ReleaseCombatPickup();drive.StopApproach();Input.Release(preserveNearbyPickup:true);
                        break;
                    }
                    if(await TryDurabilityRepair(o,token,lootReturnPending||gamekeeperReturnPending&&gamekeeperDefeated))
                    {
                        // Repair released input. Re-read the target before normal
                        // combat can resume, and exclude UI time from watchdogs.
                        approachStarted=Environment.TickCount64;combatStart=0;
                        stationaryAttackHeldAt=0;stationaryAttackBaselineHp=-1;
                        bodyProbeHp=null;bodyProbeAt=0;lastCombatHp=null;
                        drive.ResetTurnResponse();
                        continue;
                    }
                    var current = world.Find(target.Id);
                    if (current == null)
                    {
                        // A single failed tree read is not proof that the target
                        // disappeared. The client briefly removes/rebuilds creature
                        // nodes while turning, strafing, or correcting a position.
                        // Keep an engaged swing alive, refresh the scene, and keep
                        // the same identity locked for a short bounded
                        // reacquisition window.  Release only when the encounter
                        // no longer confirms this target.
                        if (targetMissingSince == 0) targetMissingSince = Environment.TickCount64;
                        ReleaseCombatPickup(); drive.StopApproach();
                        bool preserveMissingSwing=PreserveEngagedSwing(target,"target node refresh",token);
                        if(!preserveMissingSwing)Input.HoldMouse(false,false,token);
                        do
                        {
                            await Input.Delay(70, token);
                            RefreshGuardScene();
                            current = entities.FirstOrDefault(e => e.Id == target.Id);
                        }
                        while (current == null && Environment.TickCount64 - targetMissingSince < 1200);
                        if (current == null)
                        {
                            var missingHealthSnapshot = world.TargetHealth(target.Id);
                            bool stillEngaged = encounter.IsEngaged(target);
                            TraceLog.Record("target reacquisition timed out", new { target.Id, target.DisplayName, stillEngaged, missingHealthSnapshot.Known, missingHealthSnapshot.Dead, Elapsed = Environment.TickCount64 - targetMissingSince });
                            if (stillEngaged && !missingHealthSnapshot.Dead && Environment.TickCount64 - targetMissingSince < 1800)
                            {
                                message = $"Reacquiring engaged target: {target.DisplayName}…";
                                PreserveEngagedSwing(target,"target reacquisition",token);
                                await Input.Delay(100, token);
                                continue;
                            }
                            if(Targeting.IsGamekeeper(target) && missingHealthSnapshot.Known && missingHealthSnapshot.Dead)
                                gamekeeperDefeated=true;
                            if(missingHealthSnapshot.Known && missingHealthSnapshot.Dead)
                            {
                                Input.HoldMouse(false,false,token);
                                lootTracker.RecordKill(target,target.Position,runZone??navigationZone);
                                if(stationaryAssistUsed && Targeting.IsStationaryHuntTargetId(target.Id))stationaryAssistReturnPending=true;
                            }
                            collectAfterTarget = courtesy.StartedHere(target);
                            break;
                        }
                    }
                    if (current.Address != target.Address || current.Generation != target.Generation || current.Name != target.Name || current.Model != target.Model || !current.Targetable)
                    {
                        collectAfterTarget = courtesy.StartedHere(target);
                        TraceLog.Record("target inactive or replaced", new { target.Id, target.DisplayName, Current = current.Targetable ? current.DisplayName : "non-targetable" });
                        break;
                    }
                    targetMissingSince = 0;
                    lockedTarget=current;
                    lastTargetPosition = current.Position;
                    if(!o.GroupMode && !o.LeaveAreaWhenEmpty && o.PrioritizeGamekeeper && Targeting.IsGamekeeper(current) &&
                        (current.Position-anchor).Length>(double)o.HuntRadius)gamekeeperExcursion=true;
                    var hp = world.TargetHealth(target.Id);
                    if (hp.Dead)
                    {
                        if(Targeting.IsGamekeeper(target))gamekeeperDefeated=true;
                        lootTracker.RecordKill(target,current.Position,runZone??navigationZone);
                        if(stationaryAssistUsed && Targeting.IsStationaryHuntTargetId(target.Id))stationaryAssistReturnPending=true;
                        collectAfterTarget = courtesy.StartedHere(target);
                        TraceLog.Record("target dead", new { target.Id, target.Name, target.DisplayName, target.PriorityLootObject, hp.Current, hp.Maximum, Position = current.Position });
                        break;
                    }
                    if (!hp.Known)
                    {
                        ReleaseCombatPickup(); drive.StopApproach();
                        bool preserveUnknownSwing=PreserveEngagedSwing(current,"target HP refresh",token);
                        if(!preserveUnknownSwing)Input.HoldMouse(false,false,token);
                        if (++missingHealth >= 10)
                        {
                            // These 1-HP breakables can lose their HP record before their world object disappears.
                            // Stop attacking and sweep actual drops; unknown HP is never reported as a confirmed kill.
                            if (current.PriorityLootObject && courtesy.StartedHere(target))
                            {
                                Input.HoldMouse(false,false,token);
                                collectAfterTarget = true;
                                TraceLog.Record("priority HP removed after attack", new { target.Id, target.DisplayName, Position = current.Position });
                                break;
                            }
                            throw new InvalidOperationException("Target HP is unavailable; stopped.");
                        }
                        await Input.Delay(100, token); continue;
                    }
                    missingHealth = 0;
                    if(lastCombatHp.HasValue && hp.Current<lastCombatHp.Value)turnRecoveryAttempts=0;
                    lastCombatHp=hp.Current;
                    if(bodyProbeHp.HasValue && hp.Current<bodyProbeHp.Value) {bodyProbeHp=null;bodyProbeAt=0;TraceLog.Record("close collision attack confirmed",new {target.Id,hp.Current});}
                    if(bodyProbeHp.HasValue && bodyProbeAt!=0 && Environment.TickCount64-bodyProbeAt>3500) throw new RouteUnavailableException("Close target did not take damage; repositioning is needed");
                    pos = world.PlayerPosition();
                    bool priorityFight=o.PrioritizeGamekeeper && Targeting.IsGamekeeper(current);
                    double targetRadius=priorityFight ? BaseTargetRadius(current,o) : o.GroupMode ? (double)o.HuntRadius : BaseTargetRadius(current,o);
                    if(!o.GroupMode && encounter.IsEngaged(current) || priorityFight && courtesy.StartedHere(current))
                    {
                        targetRadius=Math.Max(activeCompletionBoundary,Targeting.CompletionRadius(targetRadius,(double)o.NearbyEnemyRadius,(double)o.MeleeRange));
                        activeCompletionBoundary=Math.Max(activeCompletionBoundary,targetRadius);
                        if((pos-anchor).Length>(double)o.HuntRadius || (current.Position-anchor).Length>(double)o.HuntRadius)completionReturnPending=true;
                    }
                    // Completion tracking may allow an engaged target to remain
                    // observable beyond the hunt radius, but it must never widen
                    // the character's movement boundary. Outside trips and the
                    // explicit Gamekeeper response are the only exceptions.
                    bool outsideTrip=activeExcursion is {OutsideTrip:true} && !o.GroupMode;
                    double movementBoundary=priorityFight || outsideTrip ? targetRadius : Math.Min(targetRadius,(double)o.HuntRadius);
                    activeMovementBoundary=movementBoundary;
                    if(LeashReturn.ShouldReturn(o.GroupMode,encounter.IsEngaged(current),priorityFight,outsideTrip,
                        (current.Position-anchor).Length,(double)o.HuntRadius,targetRadius))
                    {
                        TraceLog.Record("engaged target left range; returning home",new{current.Id,current.Position,Anchor=anchor});
                        await ReturnAndWaitForLeashedTarget(drive,current,anchor,o,RestoreSavedHuntFacing,token);
                        break;
                    }
                    if ((pos - anchor).Length > movementBoundary + 2) throw new InvalidOperationException("Stopped at the target response boundary.");
                    if (!priorityFight && !outsideTrip && !o.GroupMode && (current.Position-anchor).Length>movementBoundary)
                    {
                        ReleaseCombatPickup();drive.StopApproach();Input.HoldMouse(false,false,token);
                        message=$"Holding hunt boundary while {current.DisplayName} remains outside the saved area.";
                        TraceLog.Record("target outside strict hunt boundary",new {current.Id,current.DisplayName,TargetDistance=(current.Position-anchor).Length,Boundary=movementBoundary,Engaged=encounter.IsEngaged(current)});
                        await Input.Delay(150,token);
                        break;
                    }
                    if ((current.Position - anchor).Length > targetRadius)
                    {
                        if(encounter.IsEngaged(current))throw new InvalidOperationException("An engaged enemy moved beyond the combat completion area; stopped before pulling another target.");
                        break;
                    }
                    if(o.GroupMode && o.PrioritizeGamekeeper && !Targeting.IsGamekeeper(current) && groupDecision.Target is Entity groupPriority && Targeting.IsGamekeeper(groupPriority))
                    { TraceLog.Record("target preempted for Gamekeeper",new {PreviousId=current.Id,PriorityId=groupPriority.Id}); break; }
                    string? protection=TargetGuardReason(current,hp,pos,o);
                    if (protection!=null) throw new TargetProtectionException(protection);
                    if (!o.GroupMode && !encounter.HasEngaged && !healingRestPending && (!o.LeaveAreaWhenEmpty || !completionReturnPending) && Targeting.PriorityRank(current,o.PrioritizeGamekeeper,o.PrioritizeBreakables)<2 && Environment.TickCount64 >= nextPriorityCheck)
                    {
                        nextPriorityCheck = Environment.TickCount64 + 250;
                        RefreshGuardScene(); var priorityHealth=world.HealthSnapshot();
                        var protectedCandidates=entities.Where(e=>TargetGuardReason(e,priorityHealth.GetValueOrDefault(e.Id),pos,o)==null).ToArray();
                        var priority = Targeting.ChooseUrgent(protectedCandidates,priorityHealth,pos,anchor,o.LeaveAreaWhenEmpty ? (double)o.HuntRadius : responseRadius,o.PrioritizeGamekeeper) ??
                            (encounter.Active ? null : Targeting.Choose(protectedCandidates, priorityHealth, pos, anchor, (double)o.HuntRadius,
                            e => world.Difficulty(e,level), o.Target, o.AllowedDifficulties, priorityOnly: true,prioritizeGamekeeper:o.PrioritizeGamekeeper,prioritizeBreakables:o.PrioritizeBreakables));
                        if (priority != null && Targeting.PriorityRank(priority,o.PrioritizeGamekeeper,o.PrioritizeBreakables)>Targeting.PriorityRank(current,o.PrioritizeGamekeeper,o.PrioritizeBreakables))
                        {
                            TraceLog.Record("target preempted for priority", new { PreviousId = target.Id, PreviousName = target.DisplayName, PriorityId = priority.Id, PriorityName = priority.DisplayName });
                            break;
                        }
                    }
                    Vec delta = current.Position - pos;
                    message = $"{(turnRecoveryAttempts>0 ? $"Re-aiming after an unresponsive turn (retry {turnRecoveryAttempts})" : encounter.HasEngaged ? $"Finishing engaged ({encounter.EngagedCount})" : encounter.Active ? "Clearing nearby" : Targeting.PriorityRank(current,o.PrioritizeGamekeeper,o.PrioritizeBreakables)>0 ? "Priority" : "Locked")}: {current.DisplayName} Â· HP {hp.Current}/{hp.Maximum} Â· {delta.Length:F1} away";
                    // Once engaged, small target/animation movements should not break a combo.
                    // Pack clearing uses its configured melee attack gate; the ranged field is the bow stop distance.
                    bool packClearing=RangedPullEnabled(o) && rangedPull.Active && rangedPull.Phase==RangedPullPhase.Clearing && !priorityFight;
                    double attackStop = packClearing ? (double)o.RangedMeleeAttackRange : o.Ranged ? (double)o.MeleeRange : Math.Min((double)o.MeleeRange, Targeting.MeleeAttackRange);
                    double chaseThreshold = packClearing ? attackStop : attackStop + Math.Max(bodyAllowance,combatStart != 0 ? 1.25 : 0);
                    // Keep a small hysteresis window around melee reach. A live
                    // creature can move a few tenths of a unit between reads;
                    // releasing the left button at that boundary starves the
                    // client's swing animation and makes nearby targets look
                    // untargeted. The window is only used after the target is
                    // already close enough to start a melee attack.
                    // The client's melee check uses the target's collision
                    // envelope around its center. Keep the configured stop as
                    // the navigation preference, but allow a center distance
                    // up to the verified 1.5-unit attack gate plus a generous
                    // target-envelope allowance before declaring a target out
                    // of reach. The live client reports Mimic centers about
                    // two units away while their collision envelope is already
                    // in basic-attack reach.
                    double swingWindow = o.Ranged ? attackStop + .35 :
                        Math.Max(attackStop + .35, Targeting.MeleeAttackRange + 1.0);
                    // Once one of the four fixed hunt assignments is locked,
                    // its low-word identity is authoritative for movement. The
                    // client can briefly report a qualified or blank model while
                    // refreshing a creature node; using the model gate here made
                    // the controller fall through to NavigateTo(target) during
                    // that refresh and caused the character to chase the target.
                    // These assignments never chase a target. The saved point is
                    // the standing reference; once combat starts, the controller
                    // stops approach input and only faces and swings in place,
                    // except for one bounded assist step when an engaged target
                    // is just outside the swing window.
                    // Gamekeeper priority and group mode remain the explicit
                    // movement exceptions.
                    bool stationaryFarmTarget=!o.GroupMode && !priorityFight && Targeting.IsStationaryHuntTargetId(current.Id);
                    bool stationaryAttackReady=false;
                    if(stationaryFarmTarget)
                    {
                        // Fixed targets stay at the saved point by default. If an
                        // already engaged target is only slightly beyond the
                        // swing window, allow one bounded assist step (at most
                        // 1.5 map units) so the attack can connect. The outer
                        // hunt loop returns to the activation point after the
                        // assisted target dies.
                        if(!stationaryAssistActive)drive.StopApproach();
                        delta=current.Position-pos;
                        bool keepStationarySwing=Input.BasicAttackHeld && encounter.IsEngaged(current) &&
                            Targeting.StationarySwingInRange(delta.Length,swingWindow,true);
                        if(delta.Length>swingWindow && !keepStationarySwing)
                        {
                            RefreshGuardScene();var swingHealth=world.HealthSnapshot();
                            var inRange=StationarySwingCandidate(o,pos,swingWindow,swingHealth);
                            if(inRange!=null && inRange.Id!=current.Id)
                            {
                                TraceLog.Record("stationary swing target replaced",new{Previous=current.Id,Next=inRange.Id,Reason="Engaged target in melee range"});
                                target=current=inRange;lockedTarget=inRange;hp=swingHealth[inRange.Id];
                                delta=current.Position-pos;lastTargetPosition=current.Position;
                                missingHealth=0;targetMissingSince=0;lastCombatHp=hp.Current;
                                stationaryAttackBaselineHp=-1;stationaryAttackHeldAt=Environment.TickCount64;
                                stationaryAssistActive=false;drive.StopApproach();
                            }
                        }
                        if(delta.Length>swingWindow && !keepStationarySwing)
                        {
                            if(!stationaryAssistActive && !stationaryAssistUsed && encounter.IsEngaged(current) &&
                                Targeting.TryStationaryAssistStep(delta.Length,swingWindow,out double assistStep))
                            {
                                Vec direction=delta/delta.Length;
                                stationaryAssistGoal=pos+direction*assistStep;
                                stationaryAssistActive=true;stationaryAssistUsed=true;
                                navigation.BeginGoal("stationary melee assist");
                                TraceLog.Record("stationary melee assist started",new {current.Id,current.DisplayName,Position=pos,Goal=stationaryAssistGoal,Step=assistStep,Distance=delta.Length,SwingWindow=swingWindow});
                            }
                            if(stationaryAssistActive)
                            {
                                Vec assistPosition=world.PlayerPosition();
                                double goalDistance=(stationaryAssistGoal-assistPosition).Length;
                                if(delta.Length<=swingWindow)
                                {
                                    drive.StopApproach();stationaryAssistActive=false;
                                }
                                else if(goalDistance>.25)
                                {
                                    ReleaseCombatPickup();Input.HoldMouse(false,false,token);
                                    await NavigateTo(drive,stationaryAssistGoal,anchor,o,token,
                                        boundaryRadius:Math.Max((double)o.HuntRadius,activeCompletionBoundary));
                                    await Input.Delay(25,token);
                                    continue;
                                }
                                else
                                {
                                    drive.StopApproach();stationaryAssistActive=false;
                                    message=$"Holding assisted hunt point; waiting for {current.DisplayName} to enter melee range ({delta.Length:F1}/{swingWindow:F1})";
                                    await Input.Delay(100,token);
                                    continue;
                                }
                            }
                            if(!stationaryAssistActive && delta.Length>swingWindow)
                            {
                                ReleaseCombatPickup();Input.HoldMouse(false,false,token);
                                if(Environment.TickCount64>=nextStationaryRangeTrace)
                                {
                                    nextStationaryRangeTrace=Environment.TickCount64+1000;
                                    TraceLog.Record("stationary swing waiting for range",new{current.Id,current.DisplayName,Distance=delta.Length,AttackRange=swingWindow,ReleaseRange=swingWindow+.35});
                                }
                                try { await drive.Face(world,delta,token,.035); }
                                catch(TurnUnresponsiveException) { TraceLog.Record("stationary target face unavailable",new {current.Id,current.DisplayName}); }
                                message=$"Holding saved hunt point; waiting for {current.DisplayName} to enter melee range ({delta.Length:F1}/{swingWindow:F1})";
                                await CombatFacingWait(drive,current,o,100,token);
                                continue;
                            }
                        }
                        if(stationaryAssistActive){drive.StopApproach();stationaryAssistActive=false;}
                        // Enter the swing state as soon as the target is in the
                        // hysteresis window. Facing is feedback, not a gate for
                        // the attack input: a transient turn read must not
                        // prevent a nearby target from receiving a swing.
                        stationaryAttackReady=true;
                        try
                        {
                            await drive.Face(world,delta,token,.18);
                        }
                        catch(TurnUnresponsiveException)
                        {
                            TraceLog.Record("stationary target attack aim unavailable",new {current.Id,current.DisplayName});
                        }
                    }
                    bool holdPriorityPosition=!stationaryFarmTarget && o.StationaryGamekeeperPriority && !o.GroupMode && (priorityFight || encounter.IsEngaged(current));
                    if(holdPriorityPosition && delta.Length>chaseThreshold)
                    {
                        ReleaseCombatPickup();drive.StopApproach();Input.HoldMouse(false,false,token);
                        message=$"Holding position for priority combat; waiting for {current.DisplayName} ({delta.Length:F1}m) to enter attack range.";
                        await Input.Delay(100,token);break;
                    }
                    if(packClearing && !RangedPull.WithinNearby3D(current,pos,world.LocalPlayer().Height,(double)o.RangedGatherRadius))
                    {
                        // This member left the melee-phase admission circle. Yield
                        // so the pack policy can select another nearby member or
                        // resume pulling when the circle is empty.
                        ReleaseCombatPickup();drive.StopApproach();Input.Release(preserveNearbyPickup:true);break;
                    }
                    if(packClearing && delta.Length>(double)o.RangedMeleeAttackRange)
                    {
                        // Pack clearing is stationary: monsters come to the
                        // player. Do not move closer merely to start a swing.
                        ReleaseCombatPickup();drive.StopApproach();Input.HoldMouse(false,false,token);
                        message=$"Melee mode: waiting for {current.DisplayName} to enter {o.RangedMeleeAttackRange:0.#}-unit attack range.";
                        await Input.Delay(100,token);break;
                    }
                    // Fixed hunt assignments must never fall through to the
                    // normal chase route.  Their target can be several tenths
                    // beyond the configured stop while still inside the
                    // verified swing window; navigating here would make the
                    // character leave the activation point before it attacks.
                    if (!stationaryFarmTarget && delta.Length > chaseThreshold)
                    {
                        ReleaseCombatPickup();
                        Input.HoldMouse(false, false, token);
                        if (approachStarted == 0) approachStarted = Environment.TickCount64;
                        if (o.Ranged && !packClearing)
                        {
                            // The ranged skill (Firing) is for distance: try it while
                            // closing, then fall through to the walk and pick up the
                            // weapon swing once the target is in reach.
                            var rangedBar = CheckedHotbar();
                            int rangedIndex = o.SkillKeys.Length > 0 ? SkillRotation.ChooseRanged(o.SkillKeys, rangedBar, skillDue, Environment.TickCount64) : -1;
                            if (rangedIndex >= 0)
                            {
                                char key = o.SkillKeys[rangedIndex];
                                var slot = rangedBar.Slot(key);
                                TraceLog.Record("ranged skill while approaching", new { Key = key.ToString(), slot.Name, target.Id, Distance = delta.Length });
                                await Input.Key((Keys)key, 50, token); await Input.Delay(80, token); if(!await CastHealthCheckedSkill(slot,o,token))continue; await Input.Delay(150, token);
                                var after = CheckedHotbar().Slot(key);
                                bool cooldownStarted = after.RemainingCooldown > 0 || after.Locked;
                                skillDue[key] = Environment.TickCount64 + SkillRotation.RetryDelayMilliseconds(slot, cooldownStarted, o.SkillSeconds);
                                skillCursor = (rangedIndex + 1) % o.SkillKeys.Length;
                                TraceLog.Record("ranged skill cooldown observed", new { Key = key.ToString(), Remaining = after.RemainingCooldown, After = after.Locked });
                                await Input.Delay(150, token);
                                continue;
                            }
                        }
                        if (Environment.TickCount64 - approachStarted > 20000) throw new RouteUnavailableException("Target could not be reached within the approach limit");
                        try
                        {
                            double completionDistance=packClearing ? attackStop : attackStop+1;
                            if(await NavigateTo(drive,current.Position,anchor,o,token,completionDistance,boundaryRadius:activeMovementBoundary,watchTurns:true))
                            {
                                if(!packClearing){bodyAllowance=1;bodyProbeHp=hp.Current;}
                                bodyProbeAt=0;approachStarted=0;
                            }
                        }
                        catch(TurnUnresponsiveException ex)
                        {
                            await RecoverUnresponsiveTurn(drive,current,anchor,o,activeMovementBoundary,++turnRecoveryAttempts,ex,token);
                            approachStarted=Environment.TickCount64;
                        }
                        continue;
                    }
                    approachStarted = 0;
                    if (drive.StopApproach()) { await Input.Delay(120, token); continue; }
                    now = Environment.TickCount64;
                    if (combatStart == 0) combatStart = now;
                    if(!o.GroupMode && !encounter.Active)
                    {
                        encounter.Begin(); encounterHasAttack=false; encounterExistingDrops=existingDrops; encounterAnchor=anchor;
                        RefreshGuardScene(); ObserveEncounter(o,world.HealthSnapshot(),pos,level);
                    }
                    if(stationaryAttackReady)
                    {
                        // Keep the basic attack held throughout a stationary
                        // engagement. A few client builds stop advancing a
                        // held swing after a long no-damage interval, so the
                        // watchdog below re-arms it only after the target has
                        // remained at the same HP for a bounded period.
                        long stationaryNow=Environment.TickCount64;
                        bool rearm=Targeting.StationaryAttackNeedsRearm(hp.Current,Input.BasicAttackHeld,stationaryNow,
                            ref stationaryAttackBaselineHp,ref stationaryAttackHeldAt);
                        if(rearm)
                        {
                            bool wasHeld=Input.BasicAttackHeld;
                            if(wasHeld)
                            {
                                // Give the client a real key-up interval.  An
                                // immediate up/down pair can be coalesced by
                                // the game and leave its swing state disabled.
                                Input.HoldMouse(false,false,token);
                                await Input.Delay(35,token);
                            }
                            Input.HoldMouse(false,true,token);
                            stationaryAttackHeldAt=stationaryNow;
                            TraceLog.Record("stationary melee engaged",new{current.Id,current.DisplayName,Distance=delta.Length,AttackRange=swingWindow,ConfiguredRange=attackStop,Anchor=anchor,Rearmed=true,WasHeld=wasHeld});
                        }
                        courtesy.MarkAttack(current);
                        encounter.MarkAttack(current,hp);
                        encounterHasAttack=true;
                        message=$"Swinging at {current.DisplayName} from saved hunt point";
                        await CombatFacingWait(drive,current,o,45,token);
                        // Stationary farm targets used to continue here before
                        // reaching the skill rotation. That made the bot swing
                        // forever while every configured skill was skipped.
                        // Reuse the live hotbar, target-selection, health rule,
                        // mana reserve, and cooldown bookkeeping used by the
                        // moving combat path, while preserving the fixed
                        // standing position.
                        var stationaryBar=CheckedHotbar();
                        if(o.AutoDetectSkills)
                        {
                            string detected=AttackKeys(SkillRotation.DetectKeys(stationaryBar),stationaryBar,o.MaintainAreaBuffs);
                            if(detected!=o.SkillKeys)
                            {
                                o.SkillKeys=detected;skillCursor=0;
                                foreach(char key in detected)skillDue.TryAdd(key,0);
                                TraceLog.Record("skill slots detected",new{Keys=detected,Mode="stationary"});
                            }
                        }
                        var stationarySkillGroup=CombatSkillGroup(current,hp,pos,o);
                        int stationaryReadyIndex=CombatSkillPolicy.Choose(o.SkillKeys,skillCursor,stationaryBar,skillDue,stationaryNow,
                            SkillConditionHealth(o),o,stationarySkillGroup,
                            targetReady:!current.PriorityLootObject && hp.Known && !hp.Dead,
                            offensiveReady:stationaryNow-combatStart>=1200,delayReady:stationaryNow>=nextCombatSkillAt,
                            eligible:slot=>(!RangedPullEnabled(o) || !SkillRotation.IsRangedSkill(slot.Name)) && ManaSkillAllowed(slot,o));
                        if(stationaryReadyIndex>=0)
                        {
                            ReleaseCombatPickup();
                            char key=o.SkillKeys[stationaryReadyIndex];
                            var slot=stationaryBar.Slot(key);
                            bool delayExempt=SkillHealthRule.Applies(slot,o);
                            bool prioritySelfHeal=CombatSkillPolicy.IsPriorityHeal(slot,o);
                            // Heal on the valid combat target already in reach; do not delay
                            // survival by retargeting to a farther member of the pack.
                            if(o.SmartSkillTargeting && !prioritySelfHeal)
                            {
                                var skillHealth=new Dictionary<uint,Health>(world.HealthSnapshot());
                                if(hp.Known)skillHealth[current.Id]=hp;
                                var skillTarget=SkillTargeting.Choose(slot,current,encounter.EngagedCandidates,skillHealth,pos,
                                    (double)o.NearbyEnemyRadius,o.CenterAreaSkills,o.RetargetSingleTargetSkills);
                                if(skillTarget!=null && skillTarget.Id!=current.Id)
                                {
                                    TraceLog.Record("skill target retarget",new{Skill=slot.Name,From=current.Id,To=skillTarget.Id,Area=SkillTargeting.IsAreaOrLine(slot),Mode="stationary"});
                                    // Keep the left button held while changing
                                    // the skill target.  The next loop will face
                                    // the new target without dropping the swing.
                                    Input.HoldMouse(false,true,token);
                                    stationaryAttackHeldAt=stationaryNow;
                                    target=skillTarget;lockedTarget=skillTarget;
                                    await Input.Delay(20,token);
                                    continue;
                                }
                            }
                            try
                            {
                                TraceLog.Record("skill input",new{Key=key.ToString(),slot.Name,target.Id,Distance=delta.Length,RemainingBefore=slot.RemainingCooldown,Mode="stationary",PackTargets=stationarySkillGroup.InRangeTargets,HighestHealthPercent=stationarySkillGroup.HighestHealthPercent,DelayExempt=delayExempt,PrioritySelfHeal=prioritySelfHeal,NextSkillAt=nextCombatSkillAt});
                                await Input.Key((Keys)key,50,token);
                                await Input.Delay(80,token);
                                if(!await CastHealthCheckedSkill(slot,o,token))continue;
                                // Skills use the right button, but some client
                                // builds clear the left-button state while the
                                // animation starts. Reassert the swing before
                                // waiting for cooldown telemetry.
                                Input.HoldMouse(false,true,token);
                                if(!delayExempt)nextCombatSkillAt=Environment.TickCount64+SkillGroupStatus.DelayMilliseconds;
                                await Input.Delay(150,token);
                                var after=CheckedHotbar().Slot(key);
                                bool fallbackRelease=false;
                                if(slot.HasCooldown && after.Ready)
                                {
                                    // If the client ignored the skill while the
                                    // left button was held, retry once with a short
                                    // release, then restore the continuous swing.
                                    fallbackRelease=true;
                                    Input.HoldMouse(false,false,token);
                                    await Input.Delay(35,token);
                                    if(!await CastHealthCheckedSkill(slot,o,token))continue;
                                    if(!delayExempt)nextCombatSkillAt=Environment.TickCount64+SkillGroupStatus.DelayMilliseconds;
                                    Input.HoldMouse(false,true,token);
                                    stationaryAttackHeldAt=Environment.TickCount64;
                                    await Input.Delay(100,token);
                                    after=CheckedHotbar().Slot(key);
                                }
                                bool cooldownStarted=after.RemainingCooldown>0 || after.Locked;
                                TraceLog.Record("skill cooldown observed",new{Key=key.ToString(),after.Name,Remaining=after.RemainingCooldown,after.Locked,CooldownStarted=cooldownStarted,FallbackRelease=fallbackRelease,DelayExempt=delayExempt,Mode="stationary"});
                                skillDue[key]=Environment.TickCount64+SkillRotation.RetryDelayMilliseconds(slot,cooldownStarted,o.SkillSeconds);
                                skillCursor=(stationaryReadyIndex+1)%Math.Max(1,o.SkillKeys.Length);
                            }
                            finally
                            {
                                ResumeBasicAttackAfterSkill(current,o,token,"stationary");
                            }
                        }
                        else
                        {
                            await CombatFacingWait(drive,current,o,75,token);
                        }
                        continue;
                    }
                    // Start the melee hold before the final aim correction once
                    // the target is close. Keeping the held state through a
                    // small heading correction prevents repeated down/up
                    // pulses from starving the client's swing animation.
                    bool meleeSwingWindow=!o.Ranged && delta.Length<=swingWindow;
                    if(meleeSwingWindow && !Input.BasicAttackHeld)
                    {
                        Input.HoldMouse(false,true,token);
                        TraceLog.Record("melee swing window entered",new {current.Id,current.DisplayName,Distance=delta.Length,AttackRange=attackStop});
                    }
                    // Face a new/side target before starting an attack
                    // animation. A running combo stays held while the client
                    // settles onto the target.
                    try
                    {
                        if(!stationaryAttackReady)
                        {
                            if(packClearing && !await drive.Face(world,delta,token,Input.BasicAttackHeld ? .12 : .035))continue;
                            if(!await (o.Ranged ? drive.FaceTarget3D(world,current,token,Input.BasicAttackHeld ? .025 : .01) :
                                drive.Face(world,delta,token,Input.BasicAttackHeld ? .12 : .035)))continue;
                        }
                        if(packClearing)
                        {
                            // Optical aim is necessary for this 3D client, but the
                            // melee swing also needs the character body to follow it.
                            Vec liveDelta=current.Position-world.PlayerPosition();
                            double bodyError=Movement.Angle(Movement.FromClientHeading(world.PlayerHeading()),liveDelta);
                            if(Math.Abs(bodyError)>.12)
                            {
                                ReleaseCombatPickup();Input.HoldMouse(false,false,token);
                                TraceLog.Record("pack melee body not aligned",new {current.Id,ErrorDegrees=bodyError*180/Math.PI,Distance=liveDelta.Length});
                                continue;
                            }
                        }
                    }
                    catch(TurnUnresponsiveException ex)
                    {
                        await RecoverUnresponsiveTurn(drive,current,anchor,o,activeMovementBoundary,++turnRecoveryAttempts,ex,token);
                        continue;
                    }
                    Input.HoldMouse(false, true, token);
                    turnRecoveryAttempts=0;
                    if(bodyProbeHp.HasValue && bodyProbeAt==0) bodyProbeAt=Environment.TickCount64;
                    courtesy.MarkAttack(current);
                    if(!o.GroupMode)
                    {
                        activeCompletionBoundary=Math.Max(activeCompletionBoundary,Targeting.CompletionRadius(
                            BaseTargetRadius(current,o),(double)o.NearbyEnemyRadius,(double)o.MeleeRange));
                        if((pos-anchor).Length>(double)o.HuntRadius || (current.Position-anchor).Length>(double)o.HuntRadius)completionReturnPending=true;
                        encounter.MarkAttack(current,hp);
                    }
                    if(encounter.Active)
                    {
                        if(!encounterHasAttack) TraceLog.Record("encounter started",new {Position=pos,Radius=o.NearbyEnemyRadius,Enemies=encounter.Candidates.Select(e=>e.Id).ToArray()});
                        encounterHasAttack=true; encounter.NoteAttack(pos,Encounter.CollateralReach(RangedPullEnabled(o) ? (double)o.RangedMeleeAttackRange : (double)o.MeleeRange));
                    }
                    var bar = CheckedHotbar();
                    if(o.AutoDetectSkills)
                    {
                        string detected=AttackKeys(SkillRotation.DetectKeys(bar),bar,o.MaintainAreaBuffs);
                        if(detected!=o.SkillKeys)
                        {
                            o.SkillKeys=detected;skillCursor=0;
                            foreach(char key in detected)skillDue.TryAdd(key,0);
                            TraceLog.Record("skill slots detected",new {Keys=detected});
                        }
                    }
                    var combatSkillGroup=CombatSkillGroup(current,hp,pos,o);
                    int readyIndex=CombatSkillPolicy.Choose(o.SkillKeys,skillCursor,bar,skillDue,now,
                        SkillConditionHealth(o),o,combatSkillGroup,
                        targetReady:!current.PriorityLootObject && hp.Known && !hp.Dead,
                        offensiveReady:now-combatStart>=1200,delayReady:now>=nextCombatSkillAt,
                        eligible:slot=>(!RangedPullEnabled(o) || !SkillRotation.IsRangedSkill(slot.Name)) && ManaSkillAllowed(slot,o));
                    if (readyIndex >= 0)
                    {
                        ReleaseCombatPickup();
                        char key = o.SkillKeys[readyIndex];
                        var slot = bar.Slot(key);
                        bool delayExempt=SkillHealthRule.Applies(slot,o);
                        bool prioritySelfHeal=CombatSkillPolicy.IsPriorityHeal(slot,o);
                        // Heal on the valid combat target already in reach; do not delay
                        // survival by retargeting to a farther member of the pack.
                        if(o.SmartSkillTargeting && !prioritySelfHeal)
                        {
                            var skillHealth=new Dictionary<uint,Health>(world.HealthSnapshot());
                            if(hp.Known)skillHealth[current.Id]=hp;
                            var skillTarget = SkillTargeting.Choose(slot,current,encounter.EngagedCandidates,skillHealth,pos,(double)o.NearbyEnemyRadius,o.CenterAreaSkills,o.RetargetSingleTargetSkills);
                            if (skillTarget != null && skillTarget.Id != current.Id)
                            {
                                TraceLog.Record("skill target retarget",new {Skill=slot.Name,From=current.Id,To=skillTarget.Id,Area=SkillTargeting.IsAreaOrLine(slot)});
                                // Keep the swing active while the selected
                                // engaged target changes.  Releasing here caused
                                // intermittent attack gaps between skills.
                                Input.HoldMouse(false,true,token);
                                target=skillTarget; lockedTarget=skillTarget; await Input.Delay(20,token); continue;
                            }
                        }
                        try
                        {
                            TraceLog.Record("skill input", new { Key = key.ToString(), slot.Name, target.Id, Distance = delta.Length, RemainingBefore = slot.RemainingCooldown, PackTargets=combatSkillGroup.InRangeTargets, HighestHealthPercent=combatSkillGroup.HighestHealthPercent, DelayExempt=delayExempt, PrioritySelfHeal=prioritySelfHeal, NextSkillAt=nextCombatSkillAt });
                            // Keep the basic combo held while the skill is selected and right-clicked.
                            await Input.Key((Keys)key, 50, token); await Input.Delay(80, token); if(!await CastHealthCheckedSkill(slot,o,token))continue;
                            Input.HoldMouse(false,true,token);
                            if(!delayExempt)nextCombatSkillAt=Environment.TickCount64+SkillGroupStatus.DelayMilliseconds;
                            await Input.Delay(150, token);
                            var after = CheckedHotbar().Slot(key);
                            bool fallbackRelease = false;
                            if (slot.HasCooldown && after.Ready)
                            {
                                // Some skills cannot start during a held basic attack. Retry once with a short release.
                                fallbackRelease = true;
                                Input.HoldMouse(false, false, token); await Input.Delay(35, token); if(!await CastHealthCheckedSkill(slot,o,token))continue;
                                if(!delayExempt)nextCombatSkillAt=Environment.TickCount64+SkillGroupStatus.DelayMilliseconds;
                                Input.HoldMouse(false, true, token); await Input.Delay(100, token);
                                after = CheckedHotbar().Slot(key);
                            }
                            bool cooldownStarted = after.RemainingCooldown > 0 || after.Locked;
                            TraceLog.Record("skill cooldown observed", new { Key = key.ToString(), after.Name, Remaining = after.RemainingCooldown, after.Locked, FallbackRelease = fallbackRelease, CooldownStarted = cooldownStarted, DelayExempt = delayExempt });
                            skillDue[key] = Environment.TickCount64 + SkillRotation.RetryDelayMilliseconds(slot,cooldownStarted,o.SkillSeconds);
                            skillCursor = (readyIndex + 1) % o.SkillKeys.Length;
                        }
                        finally
                        {
                            ResumeBasicAttackAfterSkill(current,o,token,"combat");
                        }
                    }
                    else
                    {
                        Input.HoldMouse(false, true, token);
                        SetCombatPickup(o.LootDuringSkillCooldowns && CombatPickup.SkillsCooling(o.SkillKeys,bar),existingDrops,o,token);
                        await CombatFacingWait(drive,current,o,50,token);
                    }
                }
                }
                catch(PriorityTargetException ex)
                {
                    RememberGamekeeperReturn(world.PlayerPosition());
                    collectAfterTarget=courtesy.StartedHere(target) && world.TargetHealth(target.Id).Dead;
                    ReleaseCombatPickup();drive.StopApproach();Input.Release(preserveNearbyPickup:true);
                    pendingPriorityGamekeeper=ex.Target;healingRestPending=false;healingWarning=null;
                    message="Prioritizing: "+ex.Target.DisplayName;
                    TraceLog.Record("immediate Gamekeeper preemption",new {PreviousId=target.Id,PriorityId=ex.Target.Id});
                }
                catch(TargetProtectionException ex)
                {
                    if(ReleaseUnstartedDefense(target))throw new RecoverUnderDamageException();
                    if(encounter.IsEngaged(target))throw new InvalidOperationException("Cannot safely finish an engaged enemy: "+ex.Message);
                    collectAfterTarget=false; courtesy.Forget(target); encounter.Forget(target); Input.Release(preserveNearbyPickup:true);
                    if(encounter.Active && !encounterHasAttack) {encounter.Reset();encounterExistingDrops=null;encounterAnchor=null;}
                    message="Skipped: " + ex.Message;
                    TraceLog.Record("target protection skip",new {target.Id,target.DisplayName,Reason=ex.Message});
                }
                catch(RouteUnavailableException ex)
                {
                    if(inferredDefense is Entity inferred && TargetIdentity(inferred)==TargetIdentity(target) && !courtesy.StartedHere(target))
                    {
                        unreachableTargets[TargetIdentity(target)]=Environment.TickCount64+30000;
                        ReleaseUnstartedDefense(target);throw new RecoverUnderDamageException();
                    }
                    if(encounter.IsEngaged(target))throw new InvalidOperationException("Cannot reach an engaged enemy; stopped before selecting a new target: "+ex.Message);
                    collectAfterTarget=false;courtesy.Forget(target);encounter.Forget(target);Input.Release(preserveNearbyPickup:true);
                    unreachableTargets[TargetIdentity(target)]=Environment.TickCount64+30000;
                    message="Target route skipped: "+ex.Message;
                    TraceLog.Record("target navigation skip",new {target.Id,target.DisplayName,Reason=ex.Message});
                    if(encounter.Active && (lastTargetPosition-world.PlayerPosition()).Length<=(double)o.NearbyEnemyRadius)
                        throw new InvalidOperationException("A nearby enemy could not be reached; stopped before pickup.");
                }
                ReleaseCombatPickup();drive.StopApproach();
                bool preserveTransitionSwing=false;
                if(Input.BasicAttackHeld && !token.IsCancellationRequested && Input.Allowed() &&
                    !gamekeeperReturnPending && !stationaryAssistReturnPending && Targeting.IsStationaryHuntTargetId(target.Id))
                {
                    RefreshGuardScene();var transitionHealth=world.HealthSnapshot();var transitionPosition=world.PlayerPosition();
                    ObserveEncounter(o,transitionHealth,transitionPosition,level);
                    preserveTransitionSwing=transitionHealth.GetValueOrDefault(world.LocalPlayer().Id) is {Known:true,Dead:false} &&
                        StationarySwingCandidate(o,transitionPosition,
                        Math.Max(Math.Min((double)o.MeleeRange,Targeting.MeleeAttackRange)+.35,Targeting.MeleeAttackRange+1),transitionHealth)!=null;
                }
                Input.Release(preserveNearbyPickup:true,preserveBasicAttack:preserveTransitionSwing);
                if(preserveTransitionSwing)TraceLog.Record("basic attack preserved between engaged targets",new{Previous=target.Id,Count=encounter.EngagedCount});
                lockedTarget = null;
                int pickupHoldMs = o.GroupMode?0:Targeting.LootHoldMilliseconds(target, o.LootHoldMs);
                if (collectAfterTarget && pickupHoldMs > 0 && !o.AutoPickupNearbyLoot)
                {
                    var job=new LootJob(target,lastTargetPosition,pickupHoldMs,existingDrops);
                    deferredLoot.Enqueue(job); encounterQuietSince=0;
                    if(!encounter.Active){encounter.Begin();encounterExistingDrops=existingDrops;encounterAnchor=anchor;encounterHasAttack=false;}
                    TraceLog.Record("loot deferred until enemies clear",new {target.Id,target.DisplayName,Pending=deferredLoot.Count});
                }
                if(collectAfterTarget)courtesy.Forget(target);
                }
                catch(PriorityTargetException ex)
                {
                    RememberGamekeeperReturn(world.PlayerPosition());
                    ReleaseCombatPickup();drive.StopApproach();Input.Release(preserveNearbyPickup:true);lockedTarget=null;
                    pendingPriorityGamekeeper=ex.Target;healingRestPending=false;healingWarning=null;
                    try {await StandForGamekeeper(token);}
                    catch(DeathRecoveryRequiredException) {Input.Release();continue;}
                    message="Gamekeeper first: interrupting the current activity.";
                    TraceLog.Record("activity interrupted for Gamekeeper",new {PriorityId=ex.Target.Id,Engaged=encounter.EngagedCount,PendingLoot=deferredLoot.Count,HP=world.TargetHealth(world.LocalPlayer().Id)});
                }
                catch(RecoverBeforeFreshTargetException)
                {
                    ReleaseCombatPickup();drive.StopApproach();Input.Release(preserveNearbyPickup:true);lockedTarget=null;
                    if(encounter.Active && !encounterHasAttack){encounter.Reset();encounterExistingDrops=null;encounterAnchor=null;}
                    message="Recovering to full health before starting a new fight.";
                    TraceLog.Record("fresh approach paused for health recovery",new {Position=world.PlayerPosition(),ReturnPending=completionReturnPending});
                }
                catch(ReturnToHuntingAreaException)
                {
                    ReleaseCombatPickup();drive.StopApproach();Input.Release(preserveNearbyPickup:true);lockedTarget=null;
                    if(encounter.Active && !encounterHasAttack){encounter.Reset();encounterExistingDrops=null;encounterAnchor=null;}
                    message="An approved target appeared inside the original area. Returning before starting a new fight.";
                    TraceLog.Record("outside approach cancelled; home target available",new {Anchor=anchor,Position=world.PlayerPosition()});
                }
                catch(EngagedTargetPriorityException)
                {
                    ReleaseCombatPickup();drive.StopApproach();Input.Release(preserveNearbyPickup:true);lockedTarget=null;
                    message="Finishing engaged enemies before moving to a new target.";
                    TraceLog.Record("fresh target interrupted for engaged enemies",new {Count=encounter.EngagedCount});
                }
                catch(RecoverUnderDamageException)
                {
                    ReleaseCombatPickup();drive.StopApproach();Input.Release(preserveNearbyPickup:true);lockedTarget=null;
                    defensePending=true;defenseStep=null;encounterQuietSince=0;
                    message="Incoming damage: staying upright and looking for a nearby enemy to defend against.";
                    TraceLog.Record("recovery resumed under incoming damage",new {HP=world.TargetHealth(guardSelfId),Engaged=encounter.EngagedCount});
                }
                catch(Exception ex) when(RetryAnchorAdjustment(ex))
                {
                    DeferAnchorAdjustment(ex);
                }
                catch(Exception ex) when(faultDeathWatch.TryBegin(ex,o.AutoReviveAfterDeath,o.GroupMode,token.IsCancellationRequested,Environment.TickCount64))
                {
                    Input.PickupHoldProvider=null;ReleaseCombatPickup();drive.StopApproach();Input.Release();drive.ResetTurnResponse();lockedTarget=null;
                    TraceLog.Record("movement fault death watch started",new{Error=ex.Message,WaitSeconds=FaultDeathWatch.WaitMilliseconds/1000,
                        RecoverySeconds=FaultDeathWatch.RecoveryMilliseconds/1000,Anchor=anchor,Zone=runZone});
                }
                catch(DeathRecoveryRequiredException)
                {
                    Input.PickupHoldProvider=null;ReleaseCombatPickup();drive.StopApproach();Input.Release();lockedTarget=null;
                    message="Death detected. Preparing revival.";
                }
                catch(RetreatRequiredException ex)
                {
                    try {await RetreatAndRecover(drive,anchor,o,token,ex.Message);}
                    catch(DeathRecoveryRequiredException) {Input.Release();message="Death detected during retreat. Preparing revival.";}
                }
            }
        }
        catch (OperationCanceledException ex)
        {
            string reason=cancel?.IsCancellationRequested==true?message:ex.Message;
            TraceLog.Record("hunt stopped",new{Reason=reason,RecoveryPending=deathRecovery.Pending});
            if(pendingClientResume==null && !TryQueueClientRecovery())Stop(reason);
        }
        catch (Exception ex) { TraceLog.Record("hunt failed", new { Error = ex.Message }); if(!TryQueueClientRecovery())Stop(ex.Message); }
        finally { stationaryReturnDefenseGuard=null;FinishLoggedHunt(message);navigation.EndRecording();faultDeathWatch.Reset();deathRecoveryActive=false;deathRecovery.Reset();deathReturnInProgress=false;combatPressure.Reset();defensePending=false;defenseRepositioning=false;defenseStep=null;inferredDefense=null;buffInProgress=false;returningFromPriority=false;navigationInputOwned=false;Input.PickupHoldProvider=null;nearbyPickupCount=0;working = false; settings.Enabled = true; protectionPanel.Enabled=true;automaticRouting.Enabled=true;clearNavigation.Enabled=true; connect.Enabled = true; start.Enabled=true; ReleaseCombatPickup(); Input.Release(); Input.Preflight=null; healingRestPending=false; healingRest=null; runCharacter=null; activeHuntAnchor=null; activeExcursion=null; activeGuardOptions=null; retreatRecovery=null;retreatDrive=null;lootGuardPosition=null; lootBeforeFight=null; encounter.Reset(); deferredLoot.Clear(); encounterExistingDrops=null; encounterAnchor=null; encounterHasAttack=false; courtesy.Reset(); playerGreeting.Reset(); movement = null; runHotbarPage = null;runZone=null; cancel?.Dispose(); cancel = null; }
    }

    async Task RecoverUnresponsiveTurn(Movement drive,Entity target,Vec anchor,Options options,double boundary,int attempt,
        TurnUnresponsiveException failure,CancellationToken token)
    {
        // Keep the encounter and target. Accepted mouse packets alone are not
        // evidence that the client actually turned; retry using measured state.
        ReleaseCombatPickup();drive.StopApproach();Input.Release(preserveNearbyPickup:true);
        drive.ResetTurnResponse();
        message=$"Game did not respond to turning; re-aiming {target.DisplayName} (retry {attempt}).";
        TraceLog.Record("unresponsive combat turn recovery",new {target.Id,target.Generation,Attempt=attempt,
            failure.Position,failure.Forward,TargetPosition=target.Position,HP=world.TargetHealth(target.Id),Engaged=encounter.EngagedCount});
        await Input.Delay(120,token);
        var current=world.Find(target.Id);
        if(current==null || TargetIdentity(current)!=TargetIdentity(target) || world.TargetHealth(target.Id).Dead)return;
        var position=world.PlayerPosition();
        double heading=world.PlayerHeading();
        var forward=Movement.FromClientHeading(heading);
        var end=position+forward*2.5;
        bool canStep=Targeting.BoundaryStepAllowed(position,end,anchor,boundary) && drive.CanAdvance?.Invoke(position,end)==true;
        try
        {
            // This is a short straight step, requiring no successful turn. It
            // remains bounded by the same area, route and input checks as chase.
            if(canStep)await Input.Key(Keys.W,180,token);
            await Input.Delay(100,token);
            var after=world.PlayerPosition();
            double angle=world.PlayerHeading()-heading;
            TraceLog.Record("combat turn recovery observed",new {target.Id,Attempt=attempt,ForwardStep=canStep,
                PositionBefore=position,PositionAfter=after,Moved=(after-position).Length,
                HeadingChange=Math.Atan2(Math.Sin(angle),Math.Cos(angle))*180/Math.PI,HP=world.TargetHealth(target.Id)});
        }
        finally {drive.StopApproach();drive.ResetTurnResponse();}
    }

    async Task RetreatAndRecover(Movement drive,Vec anchor,Options options,CancellationToken token,string reason)
    {
        bool interruptedEngagement=encounter.HasEngaged || encounter.Active && encounterHasAttack;
        double retreatRadius=options.LeaveAreaWhenEmpty && !options.GroupMode && completionReturnPending ?
            Math.Max((double)options.HuntRadius,activeCompletionBoundary) : (double)options.HuntRadius;
        ReleaseCombatPickup(); drive.StopApproach(); Input.Release(preserveNearbyPickup:true);
        TraceLog.Record("retreat triggered",new {Reason=reason,Position=world.PlayerPosition(),Target=lockedTarget?.Id,PendingLoot=deferredLoot.Count});
        // Escape first; an interrupted engaged fight must not resume with fresh pulls.
        lockedTarget=null;lootGuardPosition=null;lootBeforeFight=null;
        encounterQuietSince=0;encounterUnknownSince=0;
        navigation.BeginGoal("retreat:"+Environment.TickCount64);
        var previousAdvance=drive.CanAdvance;
        var recovery=new RetreatRecovery(Environment.TickCount64,options.HealBelowPercent);
        retreatRecovery=recovery;retreatDrive=drive;
        drive.CanAdvance=(from,to)=>RetreatPlanner.CanEscape(from,to,anchor,retreatRadius,avoidZones,navigation.Obstacles([]));
        long nextRouteTrace=0; RecoveryPhase? lastPhase=null;
        long lastDamageAt=Environment.TickCount64;
        int lastHp=world.TargetHealth(world.LocalPlayer().Id).Current;
        bool CanRestNow()
        {
            if(PriorityGamekeeper(options)!=null)return false;
            var position=world.PlayerPosition();var health=world.HealthSnapshot();
            var hp=health.GetValueOrDefault(world.LocalPlayer().Id);
            if(!hp.Known || hp.Dead)return false;
            if(hp.Current<lastHp)lastDamageAt=Environment.TickCount64;
            lastHp=hp.Current;
            return RestToggle.SafeToRest(position,entities,health,avoidZones,(double)options.NearbyEnemyRadius,Environment.TickCount64,lastDamageAt);
        }
        try
        {
            while(true)
            {
                await Input.Delay(25,token);
                RefreshGuardScene();
                var self=world.LocalPlayer(); var hp=world.TargetHealth(self.Id);
                if(PriorityGamekeeper(options) is Entity recoveryPriority && Avoidance.BlockedPoint(self.Position,avoidZones)==null)
                {
                    pendingPriorityGamekeeper=recoveryPriority;healingRestPending=false;healingWarning=null;
                    await StandForGamekeeper(token);
                    TraceLog.Record("retreat recovery interrupted for Gamekeeper",new {PriorityId=recoveryPriority.Id,HP=hp});
                    return;
                }
                if(hp.Current<lastHp)lastDamageAt=Environment.TickCount64;
                lastHp=hp.Current;
                navigation.Observe(world.NavigationContext(self),self.Position,self.Height);
        RefreshImportedCollisionObstacles();
                var action=recovery.Update(Environment.TickCount64,self.Position,Movement.FromClientHeading(world.PlayerHeading()),anchor,
                    retreatRadius,avoidZones,navigation.Obstacles([]),hp);
                if(lastPhase!=action.Phase)
                {
                    TraceLog.Record("retreat phase",new {Phase=action.Phase.ToString(),self.Position,HP=hp,recovery.HealthTarget});
                    drive.StopApproach();lastPhase=action.Phase;
                }
                if(action.Phase==RecoveryPhase.Ready)
                {
                    if(world.RestSupported) await EnsurePosture(false,token);
                    RefreshGuardScene();
                    var afterStand=world.TargetHealth(world.LocalPlayer().Id);
                    if(!afterStand.Known || afterStand.Dead)throw new InvalidOperationException("Player HP became unavailable during recovery.");
                    if(Avoidance.BlockedPoint(world.PlayerPosition(),avoidZones,RetreatPlanner.Clearance)!=null ||
                        afterStand.Current*100.0/afterStand.Maximum<(double)recovery.HealthTarget) continue;
                    if(interruptedEngagement)throw new InvalidOperationException("Recovered after retreating from an unfinished fight; stopped before selecting a new target.");
                    TraceLog.Record("retreat recovered; hunt resuming",new {self.Position,HP=hp});
                    message="Area clear and health recovered. Resuming huntâ€¦";
                    return;
                }
                if(action.Waypoint is Vec waypoint)
                {
                    if(world.RestSupported && world.RestState().Posture!=RestPosture.Standing)
                    {
                        drive.StopApproach();await EnsurePosture(false,token);continue;
                    }
                    navigation.ShowRetreatRoute(waypoint,"Retreating from keep-away threat");
                    message="Retreating: "+reason;
                    if(Environment.TickCount64>=nextRouteTrace)
                    {
                        nextRouteTrace=Environment.TickCount64+500;
                        TraceLog.Record("retreat route",new {self.Position,Waypoint=waypoint,Zones=avoidZones});
                    }
                    try {await drive.Approach(world,self.Position,waypoint-self.Position,token);}
                    catch(MovementBlockedException blocked)
                    {
                        drive.StopApproach();navigation.RecordBlock(blocked.Position,blocked.Direction,self.Height);
                        TraceLog.Record("retreat blocked direction",new {blocked.Position,blocked.Direction,navigation.RecoveryAttempts});
                    }
                }
                else
                {
                    drive.StopApproach();navigation.ShowRetreatRoute(null,"Recovering outside keep-away zones");
                    bool needsHealth=hp.Current*100.0/hp.Maximum<(double)recovery.HealthTarget;
                    if(world.RestSupported)
                    {
                        bool missingSupplies=HealingRest.MissingHealingItem(CheckedHotbar());
                        bool alreadyResting=world.RestState().Posture is RestPosture.Resting or RestPosture.SittingDown;
                        bool wantRest=needsHealth && CanRestNow() && (!missingSupplies || !interruptedEngagement &&
                            (alreadyResting || recovery.HealthTarget==100 || HealingRest.ShouldTrigger(hp,options.HealBelowPercent)));
                        if(wantRest && missingSupplies)recovery.RequireFullHealth();
                        if(!await EnsurePosture(wantRest,token,CanRestNow))
                        {
                            await EnsurePosture(false,token);continue;
                        }
                    }
                    if(!options.AutoHeal && !world.RestSupported && needsHealth)
                        throw new InvalidOperationException("Retreated to safety. Automatic healing is disabled; recover health before restarting.");
                    message=$"{(world.RestSupported && world.RestState().Posture==RestPosture.Resting?"Resting with C":"Recovering safely")} Â· HP {hp.Current}/{hp.Maximum} Â· resume at {recovery.HealthTarget}% after 2 clear seconds";
                }
                // Respect AutoHeal and the same recognized-item/cooldown rules used during hunting.
                await TryHeal(drive,options,token,recovery.HealthTarget);
            }
        }
        catch(Exception ex)
        {
            TraceLog.Record("retreat stopped",new {Reason=ex.Message,Phase=recovery.Phase.ToString()});
            throw;
        }
        finally
        {
            drive.StopApproach();Input.Release(preserveNearbyPickup:true);drive.CanAdvance=previousAdvance;
            retreatRecovery=null;retreatDrive=null;navigation.BeginGoal("retreat finished:"+Environment.TickCount64);
        }
    }

    async Task<bool> EnsurePosture(bool wantRest,CancellationToken token,Func<bool>? canRest=null)
    {
        var toggle=new RestToggle(wantRest,Environment.TickCount64);
        while(true)
        {
            await Input.Delay(25,token);
            if(wantRest && canRest?.Invoke()!=true) return false;
            var reading=world.RestState();
            switch(toggle.Next(reading,Environment.TickCount64))
            {
                case RestCommand.Complete:return true;
                case RestCommand.Toggle:
                    message=wantRest?"Resting safely Â· pressing C":"Standing up before continuing Â· pressing C";
                    TraceLog.Record("rest toggle input",new {WantRest=wantRest,Before=reading});
                    await Input.Key(Keys.C,70,token);break;
                default:await Input.Delay(50,token);break;
            }
        }
    }
    async Task CollectLoot(Movement drive, Vec deathPosition, Vec anchor, Options options, CancellationToken token, int pickupHoldMs, bool priorityObject,HashSet<(uint,uint)> existingDrops)
    {
        if(deathRecovery.Pending)return;
        double lootRadius=(double)options.LootPickupRadius;
        bool EligibleDrop(GroundItem i) => (!options.AntiKillSteal || !existingDrops.Contains((i.KeyA,i.KeyB))) &&
            NearbyLootPickup.InsideAnchor(i.Position,anchor,lootRadius);
        List<GroundItem> ReadDrops()
        {
            var snapshot=world.Loot();
            ObserveLootTrackerDrops(options,snapshot);
            return snapshot;
        }
        await Input.Delay(250, token);
        var drops = ReadDrops().Where(EligibleDrop).ToList();
        long waitUntil = Environment.TickCount64 + (priorityObject ? 1500 : 350);
        while (drops.Count == 0 && Environment.TickCount64 < waitUntil) { await Input.Delay((int)Math.Min(100, Math.Max(1, waitUntil-Environment.TickCount64)), token); drops = ReadDrops().Where(EligibleDrop).ToList(); }
        if (drops.Count == 0) { TraceLog.Record("no nearby drops", new { Position = deathPosition, Anchor=anchor, Radius=lootRadius }); return; }
        for (int attempt = 0; attempt < 3 && drops.Count > 0; attempt++)
        {
            if(deathRecovery.Pending)return;
            await TryHeal(drive, options, token);
            if(deathRecovery.Pending)return;
            var item = drops.OrderBy(i => (i.Position-world.PlayerPosition()).Length).First();
            lootGuardPosition=item.Position;
            long started = Environment.TickCount64;
            while ((item.Position-world.PlayerPosition()).Length > 2.5 && Environment.TickCount64-started < 5000)
            {
                if(deathRecovery.Pending)return;
                if (await TryHeal(drive, options, token)) continue;
                if(deathRecovery.Pending)return;
                var pos = world.PlayerPosition();
                if ((pos-anchor).Length > lootRadius) break;
                message = $"Approaching loot: {item.Name}";
                await NavigateTo(drive,item.Position,anchor,options,token,boundaryRadius:lootRadius);
            }
            drive.StopApproach(); await Input.Delay(100, token);
            var pickupSnapshot=ReadDrops();
            if(!NearbyLootPickup.MayPickupAt(world.PlayerPosition(),anchor,pickupSnapshot,lootRadius))
            {
                TraceLog.Record("anchor loot pickup boundary blocked",new{Anchor=anchor,Position=world.PlayerPosition(),Radius=lootRadius});break;
            }
            var before = pickupSnapshot.Where(i => (i.Position-world.PlayerPosition()).Length <= NearbyLootPickup.PickupReach).ToList();
            if (options.AntiKillSteal && before.Any(i=>existingDrops.Contains((i.KeyA,i.KeyB)))) throw new TargetProtectionException("Pre-existing drops are inside pickup range");
            if (before.Count == 0) break;
            message = $"Picking up {before.Count} nearby drop(s) Â· holding E";
            TraceLog.Record("loot input", new { Items = before.Select(i => new { i.Name, i.KeyA, i.KeyB }), HoldMs = pickupHoldMs, Radius=lootRadius });
            await Input.Key(Keys.E, pickupHoldMs, token); await Input.Delay(200, token);
            var after = ReadDrops();
            int removed = before.Count(i => !after.Any(a => a.KeyA == i.KeyA && a.KeyB == i.KeyB));
            TraceLog.Record("loot result", new { Removed = removed, Before = before.Count, Player = world.PlayerPosition() });
            drops = after.Where(EligibleDrop).ToList();
        }
        drive.StopApproach();
    }
    public static bool ShouldHeal(Health hp, decimal threshold, long now, long nextAllowed) => hp.Known && !hp.Dead && now >= nextAllowed && hp.Current * 100.0 / hp.Maximum <= (double)threshold;
    HotbarSnapshot CheckedHotbar()
    {
        var bar = world.Hotbar();
        if (runHotbarPage.HasValue && bar.PageBase != runHotbarPage.Value) throw new InvalidOperationException("Hotbar page changed; stopped.");
        return bar;
    }
    bool HasActiveFight() => encounter.HasEngaged || lockedTarget is Entity fighting &&
        courtesy.StartedHere(fighting) && !world.TargetHealth(fighting.Id).Dead;

    bool ReleaseUnstartedDefense(Entity target)
    {
        if(inferredDefense is not Entity inferred || TargetIdentity(inferred)!=TargetIdentity(target) || courtesy.StartedHere(target))return false;
        encounter.Forget(target);inferredDefense=null;
        defensePending=combatPressure.RecentDamage(Environment.TickCount64);
        TraceLog.Record("inferred defense unavailable before first attack",new {target.Id,target.Generation});
        return true;
    }

    bool DefenseWithinBoundary(Entity entity,Options options) => activeHuntAnchor is Vec anchor &&
        (entity.Position-anchor).Length<=Math.Max(activeCompletionBoundary,
            Targeting.CompletionRadius(BaseTargetRadius(entity,options),(double)options.NearbyEnemyRadius,(double)options.MeleeRange));

    bool HoldStationaryAnchorDuringDamage(Options options,Vec position)
    {
        if(options.GroupMode || activeHuntAnchor is not Vec anchor || !position.Finite ||
            (position-anchor).Length>1.5)return false;
        if(lockedTarget is Entity target)
            return Targeting.ShouldHoldStationaryAnchor(target,options,position,anchor,world.TargetHealth(target.Id));
        // On a restart the first nearby target can still be protected by the
        // previous hunt, so there is no locked target yet. Fixed Mimic/Tribal/
        // Pulkhan/Tower profiles must still remain on their saved anchor while
        // that stale damage is being observed.
        return Targeting.IsStationaryHuntFilter(options.Target);
    }

    void ObserveCombatPressure(Options options,Health playerHealth,Vec position)
    {
        if(options.GroupMode)return;
        long now=Environment.TickCount64;
        bool damaged=combatPressure.Observe(playerHealth,now);
        if(inferredDefense is Entity inferred)
        {
            if(courtesy.StartedHere(inferred))inferredDefense=null;
            else
            {
                var current=entities.FirstOrDefault(e=>TargetIdentity(e)==TargetIdentity(inferred));
                var hp=world.TargetHealth(inferred.Id);
                bool ambiguous=entities.Any(e=>e.Id==inferred.Id && TargetIdentity(e)!=TargetIdentity(inferred));
                if(current==null || ambiguous || !encounter.IsEngaged(inferred) || !hp.Known || hp.Dead ||
                    !DefenseWithinBoundary(current,options) || TargetGuardReason(current,hp,position,options)!=null)ReleaseUnstartedDefense(inferred);
            }
        }
        // An explicit route/loot return owns movement. Health is still observed
        // above; death, focus, avoidance and priority checks remain in preflight.
        if(HasNavigationInputOwner)return;
        bool stationary=HoldStationaryAnchorDuringDamage(options,position);
        if(stationary)
        {
            defenseStep=null;movement?.StopApproach();
            if(damaged)TraceLog.Record("stationary anchor held after incoming damage",new {Position=position,Anchor=activeHuntAnchor,Target=lockedTarget?.Id,HP=playerHealth});
        }
        if((damaged || stationary && combatPressure.RecentDamage(now)) && !HasActiveFight())defensePending=true;
        if(HasActiveFight() || !combatPressure.RecentDamage(now))
        {
            defensePending=false;
            if(defenseStep.HasValue)movement?.StopApproach();
            defenseStep=null;
            return;
        }
        if(!defensePending || activeHuntAnchor is not Vec anchor)return;
        var health=world.HealthSnapshot();
        double radius=stationary ? Math.Max((double)options.MeleeRange+.35,Targeting.MeleeAttackRange+1.0) : Math.Clamp((double)options.NearbyEnemyRadius,5,10);
        int level=stationary?world.PlayerLevel():0;
        var defender=CombatPressure.ChooseDefense(entities,health,position,radius,(entity,hp)=>
            DefenseWithinBoundary(entity,options) &&
            (!stationary || encounter.IsEngaged(entity) || courtesy.StartedHere(entity) || MatchesRequestedTarget(entity,options,level)) &&
            TargetGuardReason(entity,hp,position,options)==null,
            entity=>encounter.IsEngaged(entity) || courtesy.StartedHere(entity));
        if(defender==null)return;
        if(!encounter.Active)
        {
            encounter.Begin();encounterAnchor=anchor;
            encounterExistingDrops=world.Loot().Select(item=>(item.KeyA,item.KeyB)).ToHashSet();
            encounterHasAttack=false;
        }
        encounter.MarkDefensive(defender,health.GetValueOrDefault(defender.Id));
        inferredDefense=defender;
        defensePending=false;defenseStep=null;movement?.StopApproach();encounterQuietSince=0;encounterUnknownSince=0;
        TraceLog.Record("nearby defense after incoming damage",new {defender.Id,defender.DisplayName,defender.Generation,
            Distance=(defender.Position-position).Length,Radius=radius,HP=playerHealth,
            Evidence="Player HP fell; nearby candidate inferred, attacker ID unavailable"});
    }

    async Task RepositionUnderPressure(Movement drive,Vec anchor,Options options,CancellationToken token)
    {
        // Keep input and scene checks running if the damage source cannot yet
        // be selected. Each step stays inside the current completion area.
        ReleaseCombatPickup();Input.HoldMouse(false,false,token);
        var position=world.PlayerPosition();
        bool stationaryAssignment=!options.GroupMode &&
            (lockedTarget is Entity locked ? Targeting.IsStationaryHuntTargetId(locked.Id) :
                Targeting.IsStationaryHuntFilter(options.Target));
        if(stationaryAssignment)
        {
            if((position-anchor).Length>1.5)
            {
                // A fixed hunt target may trigger the generic damage response
                // before an encounter or target lock exists. Return directly
                // to the anchor; never create another outward defense step.
                message="Incoming damage: returning to the saved hunt anchor.";
                navigation.BeginGoal("return to saved hunt anchor after damage");
                defenseRepositioning=true;
                try
                {
                    await NavigateTo(drive,anchor,anchor,options,token,
                        boundaryRadius:Math.Max((double)options.HuntRadius,activeCompletionBoundary));
                }
                catch(RouteUnavailableException ex)
                {
                    drive.StopApproach();
                    TraceLog.Record("saved hunt anchor return retry",new{Reason=ex.Message,Position=world.PlayerPosition(),Anchor=anchor});
                    await Input.Delay(100,token);
                }
                catch(MovementBlockedException ex)
                {
                    drive.StopApproach();
                    TraceLog.Record("saved hunt anchor return blocked",new{Reason=ex.Message,Position=ex.Position,Anchor=anchor});
                    await Input.Delay(100,token);
                }
                finally { defenseRepositioning=false; }
            }
            if((world.PlayerPosition()-anchor).Length<=1.5)
            {
                defensePending=false;defenseStep=null;drive.StopApproach();
                TraceLog.Record("returned to saved hunt anchor after damage",new {Position=world.PlayerPosition(),Anchor=anchor,Target=lockedTarget?.Id,StationaryAssignment=true});
            }
            return;
        }
        double boundary=(position-anchor).Length>(double)options.HuntRadius ?
            Math.Max((double)options.HuntRadius,activeCompletionBoundary) : (double)options.HuntRadius;
        if(defenseStep is Vec reached && (reached-position).Length<=1){drive.StopApproach();defenseStep=null;}
        if(!defenseStep.HasValue)
        {
            var health=world.HealthSnapshot();
            var nearest=entities.Where(e=>e.Monster && e.Position.Finite && health.GetValueOrDefault(e.Id) is {Known:true,Dead:false})
                .OrderBy(e=>(e.Position-position).Length).FirstOrDefault();
            Vec away=nearest!=null && (position-nearest.Position).Length>.1 ?
                (position-nearest.Position)/(position-nearest.Position).Length : Movement.FromClientHeading(world.PlayerHeading())*-1;
            if((position-anchor).Length>(double)options.HuntRadius)away=(anchor-position)/(anchor-position).Length;
            foreach(double turn in new[]{0d,Math.PI/4,-Math.PI/4,Math.PI/2,-Math.PI/2,Math.PI})
            {
                var goal=position+Movement.Rotate(away,turn)*2.5;
                if((goal-anchor).Length>boundary || Avoidance.BlockedSegment(position,goal,avoidZones)!=null)continue;
                if(options.AutomaticRouting && !navigation.CanAdvance(position,goal,avoidZones))continue;
                defenseStep=goal;navigation.BeginGoal("reacquire after incoming damage");
                TraceLog.Record("damage response reposition",new {Position=position,Goal=goal,Boundary=boundary});
                break;
            }
        }
        message="Incoming damage: moving and checking for a nearby enemy before recovering.";
        if(defenseStep is not Vec destination)
        {
            drive.StopApproach();message="Incoming damage: checking for an enemy or an open escape route.";
            await Input.Delay(100,token);return;
        }
        defenseRepositioning=true;
        try {await NavigateTo(drive,destination,anchor,options,token,boundaryRadius:boundary);}
        catch(RouteUnavailableException ex)
        {
            drive.StopApproach();defenseStep=null;
            TraceLog.Record("damage response route unavailable",new {Reason=ex.Message});
            await Input.Delay(100,token);
        }
        finally {defenseRepositioning=false;}
    }

    async Task RecoverToFullByResting(Movement drive,Options options,CancellationToken token)
    {
        var initial=world.TargetHealth(world.LocalPlayer().Id);
        DeathRecoveryState.InterruptIfDead(initial,options.AutoReviveAfterDeath,ObserveDeath);
        if(!HealingRest.ShouldTrigger(initial,options.HealBelowPercent)){healingRestPending=false;healingWarning=null;return;}
        if(!world.RestSupported)throw new InvalidOperationException("No healing item is slotted and the client's sitting state cannot be verified.");
        ReleaseCombatPickup();drive.StopApproach();Input.Release(preserveNearbyPickup:true);
        var recovery=new HealingRest(initial,Environment.TickCount64);
        healingRest=recovery;
        bool requestingSit=false;
        Entity? recoveryPriority=null;
        string? threat=null;
        Action? normalPreflight=Input.Preflight;
        HealingRestDecision ObserveRecovery()
        {
            if(Environment.TickCount64-guardRefreshedAt>=100)RefreshGuardScene();
            var self=world.LocalPlayer();var hp=world.TargetHealth(self.Id);
            DeathRecoveryState.InterruptIfDead(hp,options.AutoReviveAfterDeath,ObserveDeath);
            if(encounter.Active)ObserveEncounter(options,world.HealthSnapshot(),self.Position,world.PlayerLevel());
            ObserveCombatPressure(options,hp,self.Position);
            recoveryPriority ??= PriorityGamekeeper(options);
            threat=Avoidance.BlockedPoint(self.Position,avoidZones,RetreatPlanner.Clearance);
            return recovery.Evaluate(hp,world.RestState(),HasActiveFight() || threat!=null || recoveryPriority!=null,false,Environment.TickCount64);
        }
        Input.Preflight=()=>
        {
            var current=world.LocalPlayer();
            DeathRecoveryState.InterruptIfDead(world.TargetHealth(current.Id),options.AutoReviveAfterDeath,ObserveDeath);
            if(runCharacter!=null && !LocalCharacter.Same(runCharacter,current))throw new OperationCanceledException("Character changed during recovery.");
            if(runZone.HasValue && world.ActiveZone()!=runZone)throw new InvalidOperationException("Map zone changed during recovery; stopped.");
            _=CheckedHotbar();
            var decision=ObserveRecovery();
            // Stand-up input remains available after an interruption. Only a
            // pending sit request is cancelled if combat starts before its C edge.
            if(requestingSit && decision.Action is HealingRestAction.Stand or HealingRestAction.Interrupted or HealingRestAction.Complete)
                throw new HealingRestInterruptedException();
        };
        TraceLog.Record("no-item rest recovery started",new {initial.Current,initial.Maximum,TriggerPercent=options.HealBelowPercent,TargetPercent=100,Engaged=encounter.EngagedCount});
        try
        {
            while(true)
            {
                await Input.Delay(100,token);
                var decision=ObserveRecovery();
                var hp=world.TargetHealth(world.LocalPlayer().Id);
                message=$"{decision.Reason} HP {hp.Current}/{hp.Maximum}";
                switch(decision.Action)
                {
                    case HealingRestAction.Sit:
                        requestingSit=true;
                        try
                        {
                            TraceLog.Record("rest recovery posture requested",new {Resting=true,hp.Current,hp.Maximum});
                            await EnsurePosture(true,token,()=>ObserveRecovery().Action is not (HealingRestAction.Stand or HealingRestAction.Interrupted or HealingRestAction.Complete));
                        }
                        catch(HealingRestInterruptedException) { }
                        finally {requestingSit=false;}
                        break;
                    case HealingRestAction.Stand:
                        TraceLog.Record("rest recovery posture requested",new {Resting=false,hp.Current,hp.Maximum});
                        await EnsurePosture(false,token);
                        break;
                    case HealingRestAction.Complete:
                        // Recheck both after standing, before clearing the
                        // requirement that prevents any fresh attack.
                        if(ObserveRecovery().Action!=HealingRestAction.Complete)continue;
                        healingRestPending=false;healingWarning=null;
                        TraceLog.Record("rest recovery full and standing",new {HP=world.TargetHealth(world.LocalPlayer().Id),Posture=world.RestState().Posture.ToString()});
                        message="Health full. Resuming hunt.";
                        return;
                    case HealingRestAction.Interrupted:
                        TraceLog.Record("rest recovery interrupted after standing",new {hp.Current,hp.Maximum,Engaged=encounter.EngagedCount,Threat=threat});
                        if(recoveryPriority!=null){healingRestPending=false;healingWarning=null;throw new PriorityTargetException(recoveryPriority);}
                        if(threat!=null)throw new RetreatRequiredException(threat);
                        if(HasActiveFight())throw new EngagedTargetPriorityException();
                        if(options.GroupMode)throw new InvalidOperationException("Rest interrupted by incoming damage outside the selected tank fight; standing confirmed.");
                        throw new RecoverUnderDamageException();
                }
            }
        }
        finally {requestingSit=false;Input.Release(preserveNearbyPickup:true);Input.Preflight=normalPreflight;healingRest=null;}
    }

    async Task<bool> TryHeal(Movement drive, Options options, CancellationToken token,decimal? recoveryTarget=null)
    {
        if (!options.AutoHeal) return false;
        var self = world.LocalPlayer(); var hp = world.TargetHealth(self.Id);
        if (!hp.Known) throw new InvalidOperationException("Player HP is unavailable; auto-heal cannot be checked.");
        if(hp.Dead)
        {
            if(options.AutoReviveAfterDeath){ObserveDeath(hp);return false;}
            throw new InvalidOperationException("Character died; stopped.");
        }
        if(!recoveryTarget.HasValue && !healingRestPending && hp.Current>=hp.Maximum){healingWarning=null;return false;}
        if(!options.GroupMode && !recoveryTarget.HasValue)
        {
            if(Environment.TickCount64-guardRefreshedAt>=100)RefreshGuardScene();
            ObserveCombatPressure(options,hp,self.Position);
        }
        var bar = CheckedHotbar();
        bool missingItem=HealingRest.MissingHealingItem(bar);
        bool restRequested=missingItem && HealingRest.ShouldTrigger(hp,options.HealBelowPercent);
        if(!recoveryTarget.HasValue && healingRest==null && !restRequested){healingRestPending=false;healingWarning=null;}
        if(!recoveryTarget.HasValue && restRequested)
        {
            if(!healingRestPending)TraceLog.Record("no healing item; full recovery required",new {hp.Current,hp.Maximum,TriggerPercent=options.HealBelowPercent,Engaged=encounter.EngagedCount});
            healingRestPending=true;
            if(!options.GroupMode && !HealingRest.ReadyAfterDamage(combatPressure.LastDamageAt,Environment.TickCount64))
            {
                healingWarning="Incoming damage: stay upright and finish nearby threats before resting.";
                return false;
            }
            if(defensePending && !HasActiveFight())return false;
            if(!HealingRest.MayStart(hp,bar,encounter.HasEngaged,completionReturnPending,encounter.Active,deferredLoot.Count,HasActiveFight(),lootGuardPosition.HasValue,options.HealBelowPercent))
            {
                healingWarning=HasActiveFight() ? "No healing item: finish the fight, then sit to full HP" :
                    completionReturnPending ? "No healing item: return home, then sit to full HP" : "No healing item: finish pickup, then sit to full HP";
                if(lockedTarget!=null && !HasActiveFight() && !courtesy.StartedHere(lockedTarget))throw new RecoverBeforeFreshTargetException();
                return false;
            }
            healingWarning=null;
            ClearRangedPending();
            await RecoverToFullByResting(drive,options,token);
            return true;
        }
        decimal threshold=recoveryTarget ?? options.HealBelowPercent;
        if(recoveryTarget.HasValue && hp.Current*100.0/hp.Maximum >= (double)threshold) return false;
        if (!ShouldHeal(hp, threshold, Environment.TickCount64, nextHealAt))
        {
            if(hp.Known && hp.Current*100.0/hp.Maximum>(double)threshold)healingWarning=null;
            return false;
        }
        if (!bar.Slots.Any(RecoveryItems.Recognized))
        {
            // Finish escaping before stopping for missing supplies.
            if(recoveryTarget.HasValue && (world.RestSupported || retreatRecovery?.Phase==RecoveryPhase.Retreating)) return false;
            if(encounter.HasEngaged || completionReturnPending || lockedTarget is Entity fighting && courtesy.StartedHere(fighting) && !world.TargetHealth(fighting.Id).Dead)
            {
                if(healingWarning==null)TraceLog.Record("healing supplies missing; finishing engaged fight",new {hp.Current,hp.Maximum,Engaged=encounter.EngagedCount});
                healingWarning=completionReturnPending && !encounter.HasEngaged ? "No healing item: finishing the return to the hunting area" : "No healing item: finishing the current fight";
                return false;
            }
            throw new InvalidOperationException("No food or potion with a supported health-restoration description is slotted.");
        }
        healingWarning=null;
        var slot = RecoveryItems.Choose(bar, recoveryCursor);
        if (slot == null) return false;
        ClearRangedPending();ReleaseCombatPickup();recoveryCursor++;
        message = $"Using {slot.Name} on {slot.Key} Â· HP {hp.Current}/{hp.Maximum}";
        TraceLog.Record("healing item input", new { Key = slot.Key, Item = slot.Name, hp.Current, hp.Maximum, Threshold = threshold, DuringRetreat=recoveryTarget.HasValue, RemainingBefore = slot.RemainingCooldown });
        priorityInterruptibleActivity=true;
        try
        {
            await Input.Key((Keys)slot.Key[0], 70, token);
            nextHealAt = Environment.TickCount64 + (long)options.HealDelaySeconds * 1000;
            if(ManaRecovery.Recognized(slot))manaRecovery.RecordUse(DateTimeOffset.UtcNow);
            await Input.Delay(100, token);
        }
        finally {priorityInterruptibleActivity=false;}
        TraceLog.Record("healing health check", new { Before = hp, After = world.TargetHealth(self.Id) });
        return true;
    }
}




