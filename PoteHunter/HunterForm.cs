using System.Text.Json;\n\nnamespace PoteHunter;\n\npublic sealed partial class HunterForm : Form
{
    const int LootTrackerPollMilliseconds=40;
    readonly bool offlinePreviewMode;\n    protected override bool ShowWithoutActivation=>offlinePreviewMode || base.ShowWithoutActivation;\n    readonly World world = new();\n    readonly CombatCourtesy courtesy = new();
    readonly PlayerGreeting playerGreeting = new();
    readonly CheckBox greetPlayers = new() { Text = "Say hello to recognized players within 25 map units", AutoSize = true, Checked = true };
    readonly Encounter encounter = new();\n    readonly CombatPressure combatPressure = new();\n    bool defensePending, defenseRepositioning;\n    Vec? defenseStep;\n    Entity? inferredDefense;\n    bool buffInProgress;\n    Entity? pendingPriorityGamekeeper;\n    bool gamekeeperTransition, priorityInterruptibleActivity;\n    readonly ToolTip priorityHint = new();\n    readonly Navigation navigation = new();
    readonly ChestCatalog chestCatalog = new();
    readonly LootTracker lootTracker = new();
    readonly LootTrackerLog lootTrackerLog = new(System.IO.Path.Combine(AppContext.BaseDirectory, "loot-session-log.csv"));
    readonly Dictionary<(int Zone,uint Id),long> chestGuideCooldown = new();
    readonly ZoneMapBackground zoneMapBackground = new();\n    readonly TabPage groupPage=new("Group");\n    readonly ComboBox tankPicker=new(){DropDownStyle=ComboBoxStyle.DropDownList,Width=230,DisplayMember="Name"};\n    readonly CheckBox groupEnabled=new(){Text="Group mode",AutoSize=true};\n    readonly NumericUpDown groupFollow=Number(2,20,1);\n    readonly NumericUpDown groupAttack=Number(1,30,1);\n    readonly NumericUpDown groupLimit=Number(20,300);\n    GroupDecision groupDecision=new(GroupAction.Wait,"Group mode is off");\n    readonly ListView partyList=new(){View=View.Details,Dock=DockStyle.Fill,FullRowSelect=true};\n    readonly Label partyStatus=new(){Dock=DockStyle.Fill,AutoSize=true};\n    PartySnapshot currentParty=new(false,[],"Connect to read the party roster");\n    string selectedTankName="";\n    readonly CheckBox automaticRouting = new() {Text="Route around observed obstacles",AutoSize=true,Checked=true};
    readonly CheckBox autoRevive = new() {Text="Auto revive after death",AutoSize=true,Checked=true};
    readonly NumericUpDown revivalDelaySeconds = Number(0,600);
    readonly CheckBox farmOnArrival = new() {Text="Farm on arrival",AutoSize=true,Checked=true};
    readonly TextBox reviveKey = new() {Text="R",Width=52};
    readonly TabPage navigationPage = new("Navigation");\n    readonly Panel navigationCanvas = new() {Dock=DockStyle.Fill};\n    readonly Label navigationLabel = new() {Dock=DockStyle.Fill,AutoSize=true};\n    readonly Button clearNavigation = new() {Text="Clear observations",AutoSize=true};\n    readonly Dictionary<(uint,uint,long),long> unreachableTargets=new();\n    Vec navigationPosition;\n    int navigationZone;\n    int? runZone;\n    readonly Queue<LootJob> deferredLoot = new();\n    sealed record LootJob(Entity Target,Vec Position,int HoldMs,HashSet<(uint,uint)> ExistingDrops);\n    sealed class EncounterInterruptedException : Exception;\n    sealed class EngagedTargetPriorityException : Exception;\n    sealed class ReturnToHuntingAreaException : Exception;\n    sealed class HealingRestInterruptedException : Exception;\n    sealed class RecoverBeforeFreshTargetException : Exception;\n    sealed class RecoverUnderDamageException : Exception;\n    HuntExcursion? activeExcursion;\n    long nextInsideTargetCheck;\n    HashSet<(uint,uint)>? encounterExistingDrops;\n    Vec? encounterAnchor;\n    long encounterQuietSince, encounterUnknownSince;\n    bool encounterHasAttack;\n    bool returningFromPriority;
    long nextEngagementObservation;\n    double activeMovementBoundary;\n    double activeCompletionBoundary;\n    bool completionReturnPending;\n    string? healingWarning;\n    bool healingRestPending;\n    HealingRest? healingRest;\n    string lastEngagementState="";\n    TargetSearchReport? targetSearch;\n    string lastTargetWait="";\n    readonly CheckBox clearNearby = new() {Text="Before looting",AutoSize=true,Checked=true};\n    readonly CheckBox leaveAreaWhenEmpty = new() {Text="Leave when empty, then return",AutoSize=true};\n    readonly NumericUpDown nearbyRadius=Number(2,15);\n    readonly CheckBox antiKillSteal = new() { Text = "Anti-kill-stealing", AutoSize = true, Checked = true };\n    readonly NumericUpDown playerBuffer = Number(1,100);\n    readonly DataGridView avoidGrid = new() { Dock = DockStyle.Fill, AllowUserToAddRows = true, AllowUserToDeleteRows = true, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, RowHeadersWidth = 30 };\n    readonly TableLayoutPanel protectionPanel = new() { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };\n    readonly TabPage protectionPage = new("Protection");\n    List<AvoidRule> avoidRules = new();\n    List<AvoidZone> avoidZones = new();\n    uint guardSelfId;\n    Options? activeGuardOptions;\n    RetreatRecovery? retreatRecovery;\n    Movement? retreatDrive;\n    long guardRefreshedAt;\n    Vec? lootGuardPosition;\n    HashSet<(uint,uint)>? lootBeforeFight;\n    readonly DataRecorder recorder = new(Path.Combine(AppContext.BaseDirectory, "data"));\n    string? recordingError;\n    readonly System.Windows.Forms.Timer timer = new() { Interval = 200 };\n    readonly CheckBox autoSkills=new(){Text="Auto",AutoSize=true,Checked=true};
    readonly CheckBox smartSkillTargeting=new(){Text="Smart skill targeting",AutoSize=true,Checked=true};
    readonly CheckBox centerAreaSkills=new(){Text="Center area / line skills",AutoSize=true,Checked=true};
    readonly CheckBox retargetSkillTargets=new(){Text="Retarget single targets",AutoSize=true,Checked=true};
    readonly TextBox player = new() {ReadOnly=true,PlaceholderText="Waiting for character...",TabStop=false}, filter = new(), skillKeys = new();\n    Entity? detectedCharacter,runCharacter;\n    long nextCharacterReconnect;\n    readonly NumericUpDown radius = Number(5, 150), melee = Number(1.5m, 10, 1), skillSeconds = Number(1, 120);
    readonly CheckBox ranged = new() { Text = "Ranged (bow/crossbow)", AutoSize = true };\n    readonly CheckBox archerClass = new() { Text = "Archer class", AutoSize = true };\n    readonly NumericUpDown lootHold = Number(0, 3000);\n    readonly NumericUpDown gamekeeperRadius = Number(10,150);\n    readonly CheckBox combatPickup = new() {Text="Hold E during skill cooldowns",AutoSize=true,Checked=true};\n    readonly CheckBox nearbyLootPickup = new() {Text="Hold E for nearby loot",AutoSize=true,Checked=true};\n    List<GroundItem> nearbyPickupSnapshot = new();\n    int nearbyPickupCount;
    long nextNearbyPickupRead;
    long nextLootTrackerRead;
    readonly CheckBox healerMode = new() {Text="Healer mode",AutoSize=true};\n    readonly TableLayoutPanel supportSettings=new(){Dock=DockStyle.Top,AutoSize=true,ColumnCount=4,Padding=new Padding(8)};\n    readonly TabPage supportPage=new("Healer / Buffs"){AutoScroll=true};\n    long nextSupportPreflight;\n    readonly CheckBox maintainBuffs=new(){Text="Maintain AOE buffs / chants",AutoSize=true};\n    readonly NumericUpDown encourageDuration=Number(0,7200),hardenSkinDuration=Number(0,7200);\n    readonly Label buffStatus=new(){AutoSize=true,MaximumSize=new Size(750,0),Margin=new Padding(3,8,3,8)};\n    readonly LiveBuffUpkeep buffPolicy=new();\n    IReadOnlyList<LiveBuffDecision> buffDecisions=[];\n    ActiveEffectSnapshot activeEffects=new(false,"Connect to read active effects",[]);\n    readonly CheckBox autoHealingSkills = new() {Text="Auto",AutoSize=true,Checked=true};\n    readonly TextBox healingSkillKeys = new() {ReadOnly=true,Width=145};\n    readonly NumericUpDown healCharge = Number(50,10000);\n    readonly NumericUpDown partyHealBelow = Number(1,100);\n    readonly NumericUpDown partyHealRange = Number(1,150);\n    int healingSkillCursor;\n    bool combatPickupHeld;\n    HashSet<(uint,uint)>? combatPickupBaseline;\n    Vec? activeHuntAnchor;\n    readonly CheckBox prioritizeGamekeeper = new() { Text = "Prioritize Gamekeeper", AutoSize = true, Checked = true };
    readonly CheckBox stationaryGamekeeperPriority = new() { Text = "Hold position during Gamekeeper priority", AutoSize = true };
    readonly CheckBox returnToHuntLocation = new() { Text = "Return to saved hunt location after Gamekeeper", AutoSize = true, Checked = true };
    readonly CheckBox prioritizeBreakables = new() { Text = "Prioritize boxes, barrels & treasure boxes", AutoSize = true, Checked = true };\n    readonly CheckBox autoHeal = new() { Text = "Use detected", AutoSize = true, Checked = true };\n    readonly NumericUpDown healBelow = Number(1, 95), healDelay = Number(1, 120);\n    readonly Label status = new(), position = new(), hint = new();\n    readonly ListView list = new() { View = View.Details, FullRowSelect = true, GridLines = false, HideSelection = false, Dock = DockStyle.Fill };\n    readonly ListView lootList = new() { View = View.Details, FullRowSelect = true, Dock = DockStyle.Fill };\n    readonly TabPage lootPage = new("Ground loot");\n    readonly ListView hotbarList = new() { View = View.Details, FullRowSelect = true, Dock = DockStyle.Fill };\n    readonly TabPage hotbarPage = new("Hotbar");\n    readonly TextBox hotbarDescription = new() { Multiline=true, ReadOnly=true, Dock=DockStyle.Fill, ScrollBars=ScrollBars.Vertical, Text="Select a slotted item to read its description." };\n    readonly TextBox lootDescription = new() { Multiline=true, ReadOnly=true, Dock=DockStyle.Fill, ScrollBars=ScrollBars.Vertical, Text="Select a ground item to read its description." };\n    string? selectedHotbarKey;\n    string? selectedGroundKey;\n    readonly Button connect = new() { Text = "Connect / refresh", AutoSize = true }, start = new() { Text = "Start Â· F8", AutoSize = true }, stop = new() { Text = "Stop Â· F9", AutoSize = true };\n    readonly TableLayoutPanel settings = new() { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 4, Padding = new Padding(0, 8, 0, 12) };\n    readonly Dictionary<Threat, CheckBox> difficultyBoxes = new();\n    bool connected, busy, working, hotkeys;
    bool deathRecoveryActive, deathRecoveryRequested;
    sealed record HotkeyRegistration(string Key, int Id, bool Registered, int WindowsError);\n    readonly List<HotkeyRegistration> hotkeyRegistrations = new();\n    string? hotkeyFailure;\n    CancellationTokenSource? cancel;\n    Movement? movement;\n    List<Entity> entities = new();\n    List<GroundItem> groundLoot = new();\n    Dictionary<uint, Health> latestHealth = new();\n    long lastEvidence;\n    long startVersion;\n    string message = "Connect to read the gameâ€™s live creature list.";\n    string DisplayMessage => (hotkeyFailure == null || message == hotkeyFailure ? message : hotkeyFailure + " Â· " + message) +\n        (working && healingWarning!=null ? " Â· " + healingWarning : "");\n    string? lastCalibrationError;\n    Entity? lockedTarget;\n    long nextHealAt;\n    int? runHotbarPage;\n    HotbarSnapshot? currentHotbar;\n    int recoveryCursor;\n    HealTarget? activeHealTarget;\n    string? lastHealingSkill;\n    static NumericUpDown Number(decimal min, decimal max, int decimals = 0) => new() { Minimum = min, Maximum = max, DecimalPlaces = decimals, Increment = decimals > 0 ? .5m : 1, Width = 130 };\n    public HunterForm() : this(false) { }\n    internal HunterForm(bool offlinePreview)\n    {\n        offlinePreviewMode=offlinePreview;\n        try{var o=Options.Read();maintainBuffs.Checked=o.MaintainAreaBuffs;encourageDuration.Value=Math.Clamp(o.EncourageDurationSeconds,0,7200);hardenSkinDuration.Value=Math.Clamp(o.HardenSkinDurationSeconds,0,7200);}catch{maintainBuffs.Checked=false;}\n        Text = PoteMemoryProbe.WindowsClientRead.Enabled ? "POTE Hunter Â· Fixed detection" : "POTE Hunter Â· Memory controller"; Size = new Size(1080, 1200); MinimumSize = new Size(920, 1000);\n        StartPosition = FormStartPosition.CenterScreen; Font = new Font("Segoe UI", 10); BackColor = Color.FromArgb(20, 25, 34); ForeColor = Color.WhiteSmoke;\n        var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 7, ColumnCount = 1, Padding = new Padding(22) };\n        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));\n        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 55)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));\n        Controls.Add(root);\n        root.Controls.Add(new Label { Text = "POTE HUNTER", Font = new Font("Segoe UI Semibold", 21), AutoSize = true }, 0, 0);\n        settings.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 22)); settings.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28)); settings.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 22)); settings.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28));\n        void Field(string label, Control control, int row, int col) { settings.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 9, 5, 9) }, col, row); control.Anchor = AnchorStyles.Left | AnchorStyles.Right; settings.Controls.Add(control, col + 1, row); }\n        Field("Character (automatic)", player, 0, 0); Field("Name filter (optional)", filter, 0, 2); filter.PlaceholderText = "Blank = any allowed monster";\n        Field("Hunt radius", radius, 1, 0);\n        var attackRangeLabel = new Label { Text = "Melee distance", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 9, 5, 9) };\n        var attackRangeRow = new FlowLayoutPanel { AutoSize = true, WrapContents = true, MaximumSize = new Size(270, 0), Margin = Padding.Empty };\n        attackRangeRow.Controls.AddRange([ranged, archerClass, melee]); melee.Anchor = AnchorStyles.Left;\n        settings.Controls.Add(attackRangeLabel, 2, 1); settings.Controls.Add(attackRangeRow, 3, 1);\n        void ApplyRangedMode()\n        {\n            if (archerClass.Checked) ranged.Checked = true;\n            melee.Maximum = ranged.Checked ? 30m : 10m;\n            melee.Value = Math.Clamp(melee.Value, melee.Minimum, melee.Maximum);\n            double minimumRange = archerClass.Checked ? Targeting.ArcherAttackRange : Targeting.BowAttackRange;\n            if (ranged.Checked && (double)melee.Value < minimumRange) melee.Value = (decimal)minimumRange;\n            if (!ranged.Checked && (double)melee.Value > 10) melee.Value = 2m;\n            attackRangeLabel.Text = ranged.Checked ? "Attack range" : "Melee distance";\n            priorityHint.SetToolTip(attackRangeLabel, ranged.Checked\n                ? (archerClass.Checked ? "Archer class: 24-unit engine range. The bot stops at this distance and uses Firing skills while closing." : "Bow/crossbow: 18-unit engine range. The bot stops at this distance and uses Firing skills while closing.")\n                : "Melee approach distance. The client's own gate is 1.5 units (ATTACKABLE_RANGE 150).");\n        }\n        ranged.Click += (_, _) => { if (!ranged.Checked) archerClass.Checked = false; ApplyRangedMode(); };\n        archerClass.Click += (_, _) => ApplyRangedMode();\n        var skillSelection=new FlowLayoutPanel {AutoSize=true,WrapContents=false,Margin=Padding.Empty};\n        skillKeys.Width=145;skillKeys.ReadOnly=true;skillSelection.Controls.AddRange([autoSkills,skillKeys]);\n        Field("Skill keys", skillSelection, 2, 0); Field("Skill retry fallback (s)", skillSeconds, 2, 2);\n        Field("Loot: hold E (ms)", lootHold, 3, 0);\n        settings.Controls.Add(new Label { Text = "Hold W to approach Â· Hold left-click to combo", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 9, 0, 9) }, 2, 3); settings.SetColumnSpan(settings.GetControlFromPosition(2, 3)!, 2);\n        var difficultyRow = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };\n        foreach (var kind in new[] { Threat.Grey, Threat.Green, Threat.Yellow, Threat.Orange, Threat.Red, Threat.Gold, Threat.Magenta, Threat.Cyan })\n        {\n            var box = new CheckBox { Text = kind.ToString(), AutoSize = true, ForeColor = MonsterDefinition.DisplayColor(kind), Margin = new Padding(0, 8, 18, 8) };\n            difficultyBoxes.Add(kind, box); difficultyRow.Controls.Add(box);\n        }\n        settings.Controls.Add(new Label { Text = "Attack these colors", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 4);\n        settings.Controls.Add(difficultyRow, 1, 4); settings.SetColumnSpan(difficultyRow, 3);\n        healBelow.Width = 52;\n        var healingRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };\n        healingRow.Controls.AddRange([autoHeal, new Label { Text = "below", AutoSize = true, Padding = new Padding(0, 4, 0, 0) }, healBelow, new Label { Text = "%", AutoSize = true, Padding = new Padding(0, 4, 0, 0) }]);\n        Field("Automatic healing", healingRow, 5, 0); Field("Heal delay (seconds)", healDelay, 5, 2);\n        Field("Clear nearby enemies",clearNearby,6,0); Field("Nearby enemy radius",nearbyRadius,6,2);\n        var priorityOptions=new FlowLayoutPanel {AutoSize=true,WrapContents=true,Margin=Padding.Empty};\n        priorityOptions.Controls.AddRange([prioritizeGamekeeper,stationaryGamekeeperPriority,prioritizeBreakables,returnToHuntLocation]);
        Field("Target priority",priorityOptions,7,0);\n        priorityHint.SetToolTip(prioritizeGamekeeper,"Gamekeeper interrupts other fights, healing, sitting, buffs, pickup and returning home. It is pursued within the response radius; other work resumes afterward.");
        priorityHint.SetToolTip(stationaryGamekeeperPriority,"Keeps the character planted during Gamekeeper priority. It attacks the Gamekeeper or already engaged targets only when they are inside the attack range, then resumes normal movement after priority handling.");
        priorityHint.SetToolTip(returnToHuntLocation,"Saves the original hunt location when a Gamekeeper is handled, then routes back there before selecting the next target.");
        priorityHint.SetToolTip(prioritizeBreakables,"When enabled, verified boxes, barrels, and treasure boxes take priority over ordinary monsters. When disabled, they are used only if no valid monster is available.");\n        Field("Gamekeeper response radius",gamekeeperRadius,7,2);\n        var pickupOptions=new FlowLayoutPanel {AutoSize=true,WrapContents=true,Margin=Padding.Empty};\n        pickupOptions.Controls.AddRange([nearbyLootPickup,combatPickup]);\n        Field("Automatic pickup",pickupOptions,8,0); settings.SetColumnSpan(pickupOptions,3);\n        priorityHint.SetToolTip(nearbyLootPickup,"Hold E while any ground loot is inside Nearby enemy radius, measured from your character. Movement and attacks continue. This replaces timed E presses while enabled.");\n        priorityHint.SetToolTip(combatPickup,"Optional cooldown pickup when nearby-loot pickup is disabled.");\n        Field("Hunting area",leaveAreaWhenEmpty,9,0); settings.SetColumnSpan(leaveAreaWhenEmpty,3);
        var deathRecoveryRow=new FlowLayoutPanel{AutoSize=true,WrapContents=false,Margin=Padding.Empty};
        revivalDelaySeconds.Width=60;
        deathRecoveryRow.Controls.AddRange([autoRevive,new Label{Text="delay",AutoSize=true,Padding=new Padding(8,4,0,0)},revivalDelaySeconds,new Label{Text="s",AutoSize=true,Padding=new Padding(0,4,4,0)},new Label{Text="key",AutoSize=true,Padding=new Padding(4,4,0,0)},reviveKey,farmOnArrival]);
        Field("Death recovery",deathRecoveryRow,14,0); settings.SetColumnSpan(deathRecoveryRow,3);
        priorityHint.SetToolTip(autoRevive,"After stable zero HP, release combat input, send the configured revive key, then return to the saved activation anchor.");
        priorityHint.SetToolTip(revivalDelaySeconds,"Wait this many seconds after confirmed death before sending the revive key. The value is stored in the saved route profile and clamped to 0-600 seconds.");
        priorityHint.SetToolTip(reviveKey,"Client-specific death-screen key, for example R or Enter. The setting is saved with the hunt profile.");
        priorityHint.SetToolTip(farmOnArrival,"Keep hunting after returning to this saved route. Clear it to use the route only as a recovery destination.");
        AddManaSettings();
        var healingSkillSelection=new FlowLayoutPanel {AutoSize=true,WrapContents=false,Margin=Padding.Empty};\n        healingSkillSelection.Controls.AddRange([autoHealingSkills,healingSkillKeys]);\n        root.Controls.Add(settings, 0, 1);\n        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill };\n        buttons.Controls.AddRange([connect, start, stop, new Label { Text = "F8 calibrates + hunts Â· F9 stops", AutoSize = true, Padding = new Padding(12, 8, 0, 0) }]); root.Controls.Add(buttons, 0, 2);\n        position.Dock = DockStyle.Fill; position.ForeColor = Color.FromArgb(93, 222, 179); position.TextAlign = ContentAlignment.MiddleLeft; root.Controls.Add(position, 0, 3);\n        list.Columns.Add("Target", 225); list.Columns.Add("Color / type", 95); list.Columns.Add("HP", 105); list.Columns.Add("Allowed", 85); list.Columns.Add("Map X", 75); list.Columns.Add("Map Y", 75); list.Columns.Add("Distance", 75); list.Columns.Add("Entity ID", 95);\n        list.BackColor = Color.FromArgb(28, 35, 47); list.ForeColor = ForeColor;\n        var tabs = new HeaderlessTabControl { Dock = DockStyle.Fill }; var monstersPage = new TabPage("Targets"); monstersPage.Controls.Add(list); tabs.TabPages.Add(monstersPage);\n        supportPage.BackColor=list.BackColor;supportPage.ForeColor=ForeColor;\n        foreach(float width in new[]{22f,28f,22f,28f})supportSettings.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,width));\n        void SupportField(string label,Control control,int row,int col)\n        {\n            supportSettings.Controls.Add(new Label{Text=label,AutoSize=true,Anchor=AnchorStyles.Left,Margin=new Padding(0,7,5,7)},col,row);\n            control.Anchor=AnchorStyles.Left|AnchorStyles.Right;supportSettings.Controls.Add(control,col+1,row);\n        }\n        SupportField("Role",healerMode,0,0);SupportField("Buff upkeep",maintainBuffs,0,2);\n        SupportField("Healing skill keys",healingSkillSelection,1,0);SupportField("Charged skill hold (ms)",healCharge,1,2);\n        SupportField("Heal party below %",partyHealBelow,2,0);SupportField("Party heal range",partyHealRange,2,2);\n        var buffHelp=new Label{Text="Buffs follow their hotbar slots automatically. Live effects control upkeep: INSTANCE = click, CAST = charge, CHANT = turn on once. Party buffs do not use target F-keys. Unknown effects/types are not auto-maintained.",AutoSize=true,MaximumSize=new Size(750,0),Margin=new Padding(3,8,3,8)};\n        supportSettings.Controls.Add(buffHelp,0,3);supportSettings.SetColumnSpan(buffHelp,4);\n        supportSettings.Controls.Add(buffStatus,0,4);supportSettings.SetColumnSpan(buffStatus,4);\n        var saveSupport=new Button{Text="Save support settings",AutoSize=true,FlatStyle=FlatStyle.Flat};\n        saveSupport.Click+=(_,_)=>{try{CurrentOptions().Save();message="Healer and buff settings saved.";}catch(Exception ex){message=ex.Message;}};\n        AddPotionSupport();\n        supportSettings.Controls.Add(saveSupport,0,8);supportSettings.SetColumnSpan(saveSupport,4);\n        supportPage.Controls.Add(supportSettings);tabs.TabPages.Add(supportPage);\n        lootList.Columns.Add("Ground item", 340); lootList.Columns.Add("Distance", 100); lootList.Columns.Add("Map X", 100); lootList.Columns.Add("Map Y", 100);\n        lootList.BackColor = list.BackColor; lootList.ForeColor = ForeColor;\n        void ItemPane(TabPage page, ListView rows, TextBox description)\n        {\n            var pane=new TableLayoutPanel { Dock=DockStyle.Fill, RowCount=2, ColumnCount=1 }; pane.RowStyles.Add(new RowStyle(SizeType.Percent,100)); pane.RowStyles.Add(new RowStyle(SizeType.Absolute,80));\n            description.BackColor=list.BackColor; description.ForeColor=ForeColor; pane.Controls.Add(rows,0,0); pane.Controls.Add(description,0,1); page.Controls.Add(pane);\n        }\n        ItemPane(lootPage,lootList,lootDescription); tabs.TabPages.Add(lootPage);\n        hotbarList.Columns.Add("Key", 45); hotbarList.Columns.Add("Detected skill / item", 215); hotbarList.Columns.Add("Type", 65); hotbarList.Columns.Add("Role", 120); hotbarList.Columns.Add("Ready / remaining", 135); hotbarList.Columns.Add("Total cooldown", 110); hotbarList.Columns.Add("ID", 65);\n        hotbarList.BackColor = list.BackColor; hotbarList.ForeColor = ForeColor; ItemPane(hotbarPage,hotbarList,hotbarDescription); tabs.TabPages.Add(hotbarPage); root.Controls.Add(tabs, 0, 4);\n        protectionPage.BackColor=list.BackColor; protectionPage.ForeColor=ForeColor;\n        protectionPanel.RowStyles.Add(new RowStyle(SizeType.Absolute,40)); protectionPanel.RowStyles.Add(new RowStyle(SizeType.Absolute,28));\n        protectionPanel.RowStyles.Add(new RowStyle(SizeType.Percent,100)); protectionPanel.RowStyles.Add(new RowStyle(SizeType.Absolute,36));\n        var protectionHeader = new FlowLayoutPanel { Dock=DockStyle.Fill, WrapContents=false, Padding=new Padding(6) };\n        playerBuffer.Width=70;\n        protectionHeader.Controls.AddRange([antiKillSteal,new Label { Text="Keep targets away from other players:",AutoSize=true,Padding=new Padding(12,4,0,0) },playerBuffer,new Label {Text="map units",AutoSize=true,Padding=new Padding(0,4,0,0)}]);\n        protectionPanel.Controls.Add(protectionHeader,0,0);\n        protectionPanel.Controls.Add(new Label { Text="Do not go near: names match without case; partial monster/player names are supported.",Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft,Padding=new Padding(6,0,0,0)},0,1);\n        avoidGrid.Columns.Add(new DataGridViewTextBoxColumn { Name="Name",HeaderText="Monster / player name",FillWeight=75 });\n        avoidGrid.Columns.Add(new DataGridViewTextBoxColumn { Name="Radius",HeaderText="Keep away (map units)",FillWeight=25 });\n        avoidGrid.BackgroundColor=list.BackColor; avoidGrid.EnableHeadersVisualStyles=false;\n        avoidGrid.DefaultCellStyle.BackColor=list.BackColor; avoidGrid.DefaultCellStyle.ForeColor=ForeColor;\n        avoidGrid.ColumnHeadersDefaultCellStyle.BackColor=Color.FromArgb(42,54,72); avoidGrid.ColumnHeadersDefaultCellStyle.ForeColor=ForeColor;\n        avoidGrid.RowHeadersDefaultCellStyle.BackColor=list.BackColor; avoidGrid.RowHeadersDefaultCellStyle.ForeColor=ForeColor;\n        avoidGrid.DefaultValuesNeeded += (_,e) => e.Row.Cells["Radius"].Value=15;\n        protectionPanel.Controls.Add(avoidGrid,0,2);\n        var saveProtection = new Button { Text="Save protection settings",AutoSize=true,FlatStyle=FlatStyle.Flat,BackColor=Color.FromArgb(42,54,72),ForeColor=ForeColor };\n        saveProtection.Click += (_,_) => { try { CurrentOptions().Save(); message="Protection settings saved."; } catch(Exception ex) { message=ex.Message; } };\n        protectionPanel.Controls.Add(saveProtection,0,3); protectionPage.Controls.Add(protectionPanel); tabs.TabPages.Add(protectionPage);\n        navigationPage.BackColor=list.BackColor; navigationPage.ForeColor=ForeColor;\n        var navLayout=new TableLayoutPanel {Dock=DockStyle.Fill,RowCount=3,ColumnCount=1};\n        navLayout.RowStyles.Add(new RowStyle(SizeType.Absolute,34)); navLayout.RowStyles.Add(new RowStyle(SizeType.Percent,100)); navLayout.RowStyles.Add(new RowStyle(SizeType.Absolute,44));\n        var navControls=new FlowLayoutPanel {Dock=DockStyle.Fill,WrapContents=false};\n        clearNavigation.FlatStyle=FlatStyle.Flat; clearNavigation.BackColor=Color.FromArgb(42,54,72); clearNavigation.ForeColor=ForeColor;\n        navControls.Controls.AddRange([automaticRouting,clearNavigation]);\n        InitializeNavigationOverlay(navControls);\n        clearNavigation.Click+=(_,_)=> {navigation.Clear();unreachableTargets.Clear();navigationCanvas.Invalidate();};\n        automaticRouting.CheckedChanged+=(_,_)=> {if(!working && !busy) {try{CurrentOptions().Save();}catch(Exception ex){message=ex.Message;}}};\n        navigationCanvas.BackColor=Color.FromArgb(18,24,32); navigationCanvas.Paint+=PaintNavigation;\n        navLayout.Controls.Add(navControls,0,0); navLayout.Controls.Add(navigationCanvas,0,1); navLayout.Controls.Add(navigationLabel,0,2);\n        navigationPage.Controls.Add(navLayout);tabs.TabPages.Add(navigationPage);\n        list.ShowItemToolTips=true;\n        hotbarList.SelectedIndexChanged += (_,_) => { if (hotbarList.SelectedItems.Count>0) { selectedHotbarKey=hotbarList.SelectedItems[0].Text; UpdateItemDescriptions(); } };\n        lootList.SelectedIndexChanged += (_,_) => { if (lootList.SelectedItems.Count>0) { selectedGroundKey=lootList.SelectedItems[0].Tag as string; UpdateItemDescriptions(); } };\n        var groupLayout=new TableLayoutPanel{Dock=DockStyle.Fill,RowCount=3,ColumnCount=1};\n        groupLayout.RowStyles.Add(new RowStyle(SizeType.Absolute,42));groupLayout.RowStyles.Add(new RowStyle(SizeType.Percent,100));groupLayout.RowStyles.Add(new RowStyle(SizeType.Absolute,58));\n        var groupHeader=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false,Padding=new Padding(4)};\n        groupPage.BackColor=list.BackColor;groupPage.ForeColor=ForeColor;\n        groupFollow.Width=60;groupAttack.Width=60;groupLimit.Width=60;tankPicker.Width=160;\n        groupHeader.Controls.AddRange([groupEnabled,new Label{Text="Tank:",AutoSize=true,Padding=new Padding(0,5,0,0)},tankPicker,new Label{Text="Follow:",AutoSize=true,Padding=new Padding(0,5,0,0)},groupFollow,new Label{Text="Attack radius:",AutoSize=true,Padding=new Padding(0,5,0,0)},groupAttack,new Label{Text="Follow limit:",AutoSize=true,Padding=new Padding(0,5,0,0)},groupLimit]);\n        partyList.Columns.Add("Party member",230);partyList.Columns.Add("Role",90);partyList.Columns.Add("Nearby",85);partyList.Columns.Add("Distance",90);partyList.Columns.Add("Character ID",120);\n        partyList.BackColor=list.BackColor;partyList.ForeColor=ForeColor;\n        groupLayout.Controls.Add(groupHeader,0,0);groupLayout.Controls.Add(partyList,0,1);groupLayout.Controls.Add(partyStatus,0,2);\n        groupPage.Controls.Add(groupLayout);tabs.TabPages.Add(groupPage);\n        AddRangedPullSettings(tabs);\n        tankPicker.SelectionChangeCommitted+=(_,_)=> {if(tankPicker.SelectedItem is PartyMember member){selectedTankName=member.Name;if(!working)CurrentOptions().Save();}};\n        try{var groupOptions=Options.Read();selectedTankName=groupOptions.GroupTankName;groupEnabled.Checked=groupOptions.GroupMode;groupFollow.Value=Math.Clamp(groupOptions.GroupFollowDistance,2,20);groupAttack.Value=Math.Clamp(groupOptions.GroupAttackRadius,1,30);groupLimit.Value=Math.Clamp(groupOptions.GroupFollowLimit,20,300);}catch{selectedTankName="";groupFollow.Value=4;groupAttack.Value=6;groupLimit.Value=150;}\n        hint.Text = "Named keep-away threats trigger retreat, recovery, then automatic resume when safe.\nF8 calibrates + hunts Â· F9 stops. Edit names and distances on the Protection tab.";\n        hint.Dock = DockStyle.Fill; hint.ForeColor = Color.Silver; hint.TextAlign = ContentAlignment.MiddleLeft; root.Controls.Add(hint, 0, 5);\n        status.Dock = DockStyle.Fill; status.TextAlign = ContentAlignment.MiddleLeft; root.Controls.Add(status, 0, 6);\n        foreach (var b in new[] { connect, start, stop }) { b.FlatStyle = FlatStyle.Flat; b.BackColor = Color.FromArgb(42, 54, 72); b.ForeColor = ForeColor; b.Padding = new Padding(8, 2, 8, 2); }\n        try { var o = Options.Read(); player.Text = ""; filter.Text = o.Target; radius.Value = Math.Clamp(o.HuntRadius, radius.Minimum, radius.Maximum); leaveAreaWhenEmpty.Checked=o.LeaveAreaWhenEmpty; autoRevive.Checked=o.AutoReviveAfterDeath; revivalDelaySeconds.Value=Math.Clamp(o.RevivalDelaySeconds,revivalDelaySeconds.Minimum,revivalDelaySeconds.Maximum); farmOnArrival.Checked=o.FarmOnArrival; reviveKey.Text=string.IsNullOrWhiteSpace(o.ReviveKey)?"R":o.ReviveKey; archerClass.Checked=o.ArcherClass; ranged.Checked = o.Ranged || archerClass.Checked; melee.Maximum = ranged.Checked ? 30m : 10m; double minimumRange = archerClass.Checked ? Targeting.ArcherAttackRange : Targeting.BowAttackRange; melee.Value = Math.Clamp(ranged.Checked ? Math.Max(o.MeleeRange, (decimal)minimumRange) : Math.Min(o.MeleeRange, 10m), melee.Minimum, melee.Maximum); attackRangeLabel.Text = ranged.Checked ? "Attack range" : "Melee distance"; autoSkills.Checked=o.AutoDetectSkills;skillKeys.ReadOnly=o.AutoDetectSkills;skillKeys.Text = o.SkillKeys; autoHealingSkills.Checked=o.AutoDetectHealingSkills;healingSkillKeys.ReadOnly=o.AutoDetectHealingSkills;healingSkillKeys.Text=o.HealingSkillKeys; healCharge.Value=Math.Clamp(o.HealChargeMilliseconds,healCharge.Minimum,healCharge.Maximum); partyHealBelow.Value=Math.Clamp(o.PartyHealBelowPercent,partyHealBelow.Minimum,partyHealBelow.Maximum); partyHealRange.Value=Math.Clamp(o.PartyHealRange,partyHealRange.Minimum,partyHealRange.Maximum); healerMode.Checked=o.HealerMode; skillSeconds.Value = Math.Clamp(o.SkillSeconds, skillSeconds.Minimum, skillSeconds.Maximum); lootHold.Value = Math.Clamp(o.LootHoldMs, lootHold.Minimum, lootHold.Maximum); foreach (var (kind, box) in difficultyBoxes) box.Checked = (o.AllowedDifficulties ?? []).Contains(kind.ToString()); gamekeeperRadius.Value=Math.Clamp(o.GamekeeperResponseRadius,gamekeeperRadius.Minimum,gamekeeperRadius.Maximum); combatPickup.Checked = o.LootDuringSkillCooldowns; nearbyLootPickup.Checked=o.AutoPickupNearbyLoot; combatPickup.Enabled=!nearbyLootPickup.Checked; prioritizeGamekeeper.Checked = o.PrioritizeGamekeeper; stationaryGamekeeperPriority.Checked=o.StationaryGamekeeperPriority; autoHeal.Checked = o.AutoHeal; healBelow.Value = Math.Clamp(o.HealBelowPercent, healBelow.Minimum, healBelow.Maximum); healDelay.Value = Math.Clamp(o.HealDelaySeconds, healDelay.Minimum, healDelay.Maximum); antiKillSteal.Checked=o.AntiKillSteal; playerBuffer.Value=Math.Clamp(o.OtherPlayerRadius,playerBuffer.Minimum,playerBuffer.Maximum); greetPlayers.Checked=o.GreetPlayers; avoidRules=o.AvoidNames ?? new(); Avoidance.Validate(avoidRules); foreach(var rule in avoidRules) avoidGrid.Rows.Add(rule.Name,rule.Radius); clearNearby.Checked=o.ClearNearbyEnemies; nearbyRadius.Value=Math.Clamp(o.NearbyEnemyRadius,nearbyRadius.Minimum,nearbyRadius.Maximum); automaticRouting.Checked=o.AutomaticRouting; }
        catch { player.Text = ""; filter.Text = "Ichman Villager"; message = "Settings could not be loaded. Check the fields before use."; }\n        try { var savedOptions=Options.Read(); prioritizeBreakables.Checked=savedOptions.PrioritizeBreakables; returnToHuntLocation.Checked=savedOptions.ReturnToHuntLocationAfterGamekeeper; } catch { }
        prioritizeGamekeeper.CheckedChanged += (_,_) => { if(!working && !busy) { try { CurrentOptions().Save(); } catch(Exception ex) { message=ex.Message; } } };
        stationaryGamekeeperPriority.CheckedChanged += (_,_) => { if(!working && !busy) { try { CurrentOptions().Save(); } catch(Exception ex) { message=ex.Message; } } };
        prioritizeBreakables.CheckedChanged += (_,_) => { if(!working && !busy) { try { CurrentOptions().Save(); } catch(Exception ex) { message=ex.Message; } } };
        returnToHuntLocation.CheckedChanged += (_,_) => { if(!working && !busy) { try { CurrentOptions().Save(); } catch(Exception ex) { message=ex.Message; } } };
        combatPickup.CheckedChanged += (_,_) => {if(!working && !busy) {try{CurrentOptions().Save();}catch(Exception ex){message=ex.Message;}}};\n        combatPickup.Enabled=!nearbyLootPickup.Checked;\n        nearbyLootPickup.CheckedChanged += (_,_) =>\n        {\n            combatPickup.Enabled=!nearbyLootPickup.Checked;\n            if(!working && !busy){try{CurrentOptions().Save();}catch(Exception ex){message=ex.Message;}}\n        };\n        leaveAreaWhenEmpty.CheckedChanged += (_,_) => {if(!working && !busy) {try{CurrentOptions().Save();}catch(Exception ex){message=ex.Message;}}};
        autoRevive.CheckedChanged += (_,_) => {if(!working && !busy) {try{CurrentOptions().Save();}catch(Exception ex){message=ex.Message;}}};
        revivalDelaySeconds.ValueChanged += (_,_) => {if(!working && !busy) {try{CurrentOptions().Save();}catch(Exception ex){message=ex.Message;}}};
        farmOnArrival.CheckedChanged += (_,_) => {if(!working && !busy) {try{CurrentOptions().Save();}catch(Exception ex){message=ex.Message;}}};
        reviveKey.Validated += (_,_) => {if(!working && !busy) {try{CurrentOptions().Save();}catch(Exception ex){message=ex.Message;}}};
        autoSkills.CheckedChanged+=(_,_)=>{skillKeys.ReadOnly=autoSkills.Checked;if(autoSkills.Checked && currentHotbar!=null)UpdateSkillKeys(currentHotbar);if(!busy && !working)CurrentOptions().Save();};
        autoHealingSkills.CheckedChanged+=(_,_)=>{healingSkillKeys.ReadOnly=autoHealingSkills.Checked;if(autoHealingSkills.Checked && currentHotbar!=null)UpdateHealingSkillKeys(currentHotbar);if(!busy && !working)CurrentOptions().Save();};\n        healerMode.CheckedChanged+=(_,_)=>{if(!busy && !working)CurrentOptions().Save();};\n        connect.Click += async (_, _) => await Connect(); stop.Click += (_, _) => Stop("Stopped by user.");\n        start.Click += async (_,_) =>\n        {\n            if(busy || working) return;\n            long version=++startVersion;\n            if(!connected) await Connect();\n            if(!connected || version!=startVersion) return;\n            Input.SetForegroundWindow(world.Window);\n            await Task.Delay(200);\n            if(version==startVersion) await StartHunting(version);\n        };\n        timer.Tick += async (_, _) => {\n            Tick();\n            UpdateNavigationOverlay();\n            if(!connected && !busy && !working && Environment.TickCount64>=nextCharacterReconnect)\n            { nextCharacterReconnect=Environment.TickCount64+5000; await Connect(); }\n        };\n        if(!offlinePreview)Shown += async (_, _) => { RegisterKeys(); timer.Start(); await Connect(); };\n        FormClosing += (_, e) => { Stop("Closed."); if (busy || working) { e.Cancel = true; message = "Stopping. Close again when the current operation has finished."; return; } timer.Stop(); DisposeNavigationOverlay(); zoneMapBackground.Dispose(); world.Dispose(); for (int i = 6; i <= 9; i++) Input.UnregisterHotKey(Handle, i); };\n        Input.Allowed = () => connected && world.CheckInputWindow().Allowed;\n        FormClosed += (_,_)=>{DisposeNavigationOverlay();zoneMapBackground.Dispose();};\n        WindowsClientInput.Bind(world);\n        ApplyModernLayout(root,tabs);\n    }\n    void RegisterKeys()\n    {\n        hotkeyRegistrations.Clear();\n        foreach (var (id, key) in new[] { (6, Keys.F6), (8, Keys.F8), (9, Keys.F9) })\n        {\n            bool registered = Input.RegisterHotKey(Handle, id, 0x4000, (uint)key);\n            int error = registered ? 0 : System.Runtime.InteropServices.Marshal.GetLastWin32Error();\n            var registration = new HotkeyRegistration(key.ToString(), id, registered, error);\n            hotkeyRegistrations.Add(registration);\n            TraceLog.Record("hotkey registration", registration);\n        }\n        hotkeys = hotkeyRegistrations.All(result => result.Registered);\n        hotkeyFailure = hotkeys ? null : "Hotkeys unavailable: " +\n            string.Join(", ", hotkeyRegistrations.Where(result => !result.Registered)\n                .Select(result => $"{result.Key} (Windows error {result.WindowsError})")) +\n            ". Close other bot or hotkey apps, then reopen this bot.";\n        if (hotkeyFailure != null) message = hotkeyFailure;\n    }\n    bool RequireHotkeys()\n    {\n        if (hotkeys) return true;\n        message = hotkeyFailure ?? "Hotkeys are not ready. Reopen this bot before starting.";\n        TraceLog.Record("start blocked by hotkeys", new { Reason = message, Registrations = hotkeyRegistrations });\n        return false;\n    }\n    protected override void WndProc(ref Message m)\n    {\n        if (m.Msg == 0x312)\n        {\n            int id = m.WParam.ToInt32();\n            TraceLog.Record("hotkey received",new {Key=$"F{id}",Connected=connected,Working=working,Busy=busy});\n            if (id == 9 || id == 8 && working) Stop("Stopped by hotkey.");\n            else if (id == 6) _ = Calibrate();\n            else if (id == 8) _ = StartHunting();\n        }\n        base.WndProc(ref m);\n    }\n    Options CurrentOptions()\n    {\n        var options=CurrentOptionsCore();\n        options.MaintainAreaBuffs=maintainBuffs.Checked;
        options.EncourageDurationSeconds=encourageDuration.Value;
        options.HardenSkinDurationSeconds=hardenSkinDuration.Value;
        options.GreetPlayers=greetPlayers.Checked;
        options.GreetingRadius=25;
        return options;
    }\n    Options CurrentOptionsCore()\n    {\n        string keys = new(skillKeys.Text.Where(c => !char.IsWhiteSpace(c) && c != ',').ToArray());
        string healingKeys = new(healingSkillKeys.Text.Where(c => !char.IsWhiteSpace(c) && c != ',').ToArray());
        string configuredReviveKey=reviveKey.Text.Trim();
        if(!Enum.TryParse<Keys>(configuredReviveKey,true,out var parsedReviveKey) || parsedReviveKey is Keys.Escape or Keys.F9 or Keys.F8)
            throw new InvalidOperationException("Revive key must be a valid keyboard key other than Escape or the bot hotkeys.");
        configuredReviveKey=parsedReviveKey.ToString();
        // The connected client supplies the character name; no typed name is required.\n        if (keys.Any(c => !"1234567890".Contains(c)) || keys.Distinct().Count() != keys.Length) throw new InvalidOperationException("Skill keys must be unique digits from 1 through 0.");\n        if (healingKeys.Any(c => !"1234567890".Contains(c)) || healingKeys.Distinct().Count() != healingKeys.Length) throw new InvalidOperationException("Healing skill keys must be unique digits from 1 through 0.");\n        if(groupEnabled.Checked && (groupFollow.Value>groupLimit.Value || groupAttack.Value>groupLimit.Value))throw new InvalidOperationException("Group follow distance and attack radius must fit inside the follow limit.");\n        avoidGrid.EndEdit();\n        var rules=new List<AvoidRule>();\n        foreach (DataGridViewRow row in avoidGrid.Rows)\n        {\n            if (row.IsNewRow) continue;\n            string name=Convert.ToString(row.Cells["Name"].Value)?.Trim() ?? "";\n            if(name.Length==0) continue;\n            if (!double.TryParse(Convert.ToString(row.Cells["Radius"].Value),out double distance)) throw new InvalidOperationException("Each avoid name needs a distance from 1 to 150.");\n            rules.Add(new AvoidRule(name,distance));\n        }\n        Avoidance.Validate(rules); avoidRules=rules;\n        return WithOverlaySettings(WithManaSettings(WithRangedPullSettings(new Options { Player = player.Text.Trim(), Target = filter.Text.Trim(), HuntRadius = radius.Value, LeaveAreaWhenEmpty=leaveAreaWhenEmpty.Checked, AutoReviveAfterDeath=autoRevive.Checked, RevivalDelaySeconds=(int)revivalDelaySeconds.Value, FarmOnArrival=farmOnArrival.Checked, ReviveKey=configuredReviveKey, MeleeRange = (decimal)Targeting.AttackRange(ranged.Checked || archerClass.Checked, archerClass.Checked, (double)melee.Value), Ranged = ranged.Checked || archerClass.Checked, ArcherClass = archerClass.Checked, SkillKeys = keys, AutoDetectSkills=autoSkills.Checked, SkillSeconds = skillSeconds.Value, LootHoldMs = lootHold.Value, AllowedDifficulties = difficultyBoxes.Where(kv => kv.Value.Checked).Select(kv => kv.Key.ToString()).ToArray(), GamekeeperResponseRadius = gamekeeperRadius.Value, LootDuringSkillCooldowns = combatPickup.Checked, AutoPickupNearbyLoot=nearbyLootPickup.Checked, HealerMode=healerMode.Checked, AutoDetectHealingSkills=autoHealingSkills.Checked, HealingSkillKeys=healingKeys, HealChargeMilliseconds=healCharge.Value, PartyHealBelowPercent=partyHealBelow.Value, PartyHealRange=partyHealRange.Value, PrioritizeGamekeeper = prioritizeGamekeeper.Checked, StationaryGamekeeperPriority=stationaryGamekeeperPriority.Checked, ReturnToHuntLocationAfterGamekeeper=returnToHuntLocation.Checked, PrioritizeBreakables=prioritizeBreakables.Checked, AutoHeal = autoHeal.Checked, HealBelowPercent = healBelow.Value, HealDelaySeconds = healDelay.Value, AntiKillSteal=antiKillSteal.Checked, OtherPlayerRadius=playerBuffer.Value, AvoidNames=rules, ClearNearbyEnemies=clearNearby.Checked, NearbyEnemyRadius=nearbyRadius.Value, AutomaticRouting=automaticRouting.Checked,GroupTankName=selectedTankName,GroupMode=groupEnabled.Checked,GroupFollowDistance=groupFollow.Value,GroupAttackRadius=groupAttack.Value,GroupFollowLimit=groupLimit.Value })));
    }\n    async Task Connect()\n    {\n        if (busy || working) return;\n        nextCharacterReconnect=Environment.TickCount64+5000;
        busy = true; connected = false; movement = null; connect.Enabled = false; message = "Reading active creaturesâ€¦";
        player.Text="";\n        WriteState(new { TimeUtc=DateTime.UtcNow, Connected=false, Working=false, Calibrated=false, Status=message });\n        try { var options = CurrentOptions(); options.Save(); await Task.Run(world.Connect); WindowsClientInput.ValidateReady(); connected = true; UpdateDetectedCharacter(world.LocalPlayer()); message = (world.AutomaticProfile ? "Updated client detected automatically. " : "Connected. ") + "Press F8 or Start to calibrate and hunt."; }\n        catch (Exception ex) { message = ex.Message; }\n        finally { busy = false; connect.Enabled = true; }\n        if (!connected) {player.Text=""; WriteState(new { TimeUtc = DateTime.UtcNow, Connected = false, Working = false, Calibrated = false, Status = message });}\n        if (connected)\n        {\n            Tick(); await Task.Delay(300);\n            using var preview = new Bitmap(Width, Height); DrawToBitmap(preview, new Rectangle(0, 0, Width, Height));\n            preview.Save(Path.Combine(AppContext.BaseDirectory, "preview.png"));\n            if(supportPage.Parent is TabControl supportTabs)\n            {\n                var selected=supportTabs.SelectedTab;\n                try{supportTabs.SelectedTab=supportPage;PerformLayout();using var supportPreview=new Bitmap(Width,Height);DrawToBitmap(supportPreview,new Rectangle(0,0,Width,Height));supportPreview.Save(Path.Combine(AppContext.BaseDirectory,"preview-support.png"));}\n                finally{supportTabs.SelectedTab=selected;}\n            }\n            if (protectionPage.Parent is TabControl pages)\n            {\n                var selected=pages.SelectedTab;\n                try { pages.SelectedTab=protectionPage; PerformLayout(); using var protectionPreview=new Bitmap(Width,Height); DrawToBitmap(protectionPreview,new Rectangle(0,0,Width,Height)); protectionPreview.Save(Path.Combine(AppContext.BaseDirectory,"preview-protection.png")); }\n                finally { pages.SelectedTab=selected; }\n            }\n            if(navigationPage.Parent is TabControl navigationTabs)\n            {\n                var selected=navigationTabs.SelectedTab;\n                try {navigationTabs.SelectedTab=navigationPage;PerformLayout();using var navPreview=new Bitmap(Width,Height);DrawToBitmap(navPreview,new Rectangle(0,0,Width,Height));navPreview.Save(Path.Combine(AppContext.BaseDirectory,"preview-navigation.png"));}\n                finally {navigationTabs.SelectedTab=selected;}\n            }\n            if(groupPage.Parent is TabControl groupTabs)\n            {\n                var selected=groupTabs.SelectedTab;\n                try{groupTabs.SelectedTab=groupPage;PerformLayout();using var groupPreview=new Bitmap(Width,Height);DrawToBitmap(groupPreview,new Rectangle(0,0,Width,Height));groupPreview.Save(Path.Combine(AppContext.BaseDirectory,"preview-group.png"));}\n                finally{groupTabs.SelectedTab=selected;}\n            }\n        }\n    }\n    void UpdateSkillKeys(HotbarSnapshot bar)\n    {\n        if(!autoSkills.Checked)return;\n        string detected=AttackKeys(SkillRotation.DetectKeys(bar),bar,maintainBuffs.Checked);\n        if(skillKeys.Text==detected)return;\n        skillKeys.Text=detected;\n        if(!busy && !working)CurrentOptions().Save();\n    }\n    void UpdateHealingSkillKeys(HotbarSnapshot bar)\n    {\n        if(!autoHealingSkills.Checked)return;\n        string detected=HealerPolicy.DetectHealingKeys(bar);\n        if(healingSkillKeys.Text==detected)return;\n        healingSkillKeys.Text=detected;\n        if(!busy && !working)CurrentOptions().Save();\n    }\n    void UpdateDetectedCharacter(Entity self)\n    {\n        bool changed=detectedCharacter!=null && !LocalCharacter.Same(detectedCharacter,self);\n        if(changed)\n        {\n            movement=null;\n            if(working)Stop("Character changed. Press F8 to start with "+self.Name+".");\n        }\n        bool nameChanged=player.Text!=self.Name;\n        detectedCharacter=self;player.Text=self.Name;\n        if(nameChanged)CurrentOptions().Save();\n    }
    void Tick()
    {
        status.Text = DisplayMessage + (recordingError == null ? "" : " Â· Data recording: " + recordingError);
        lootTracker.ObserveActivity(working && connected && activeGuardOptions!=null);
        if (!connected || busy) return;
        try\n        {\n            if (working && !Input.Allowed()) Stop("Stopped: switched away from the game. Recalibrate before starting again.");\n            int beforeZone=world.ActiveZone();\n            entities = world.Poll();\n            var self = world.LocalPlayer();\n            UpdateDetectedCharacter(self);\n            Vec pos = self.Position;\n            var health = world.HealthSnapshot();\n            latestHealth = health;\n            navigationZone=world.ActiveZone(); navigationPosition=pos;\n            chestCatalog.Observe(navigationZone,entities.Where(entity=>Targeting.IsChest(entity) && !health.GetValueOrDefault(entity.Id).Dead));
            lootTracker.ObserveZone(navigationZone);
            if(beforeZone!=navigationZone) {navigation.Clear();if(working)Stop("Map zone changed; stopped.");return;}
            navigation.Observe(world.NavigationContext(self),pos,self.Height);
            if(working && runZone.HasValue && navigationZone!=runZone) Stop("Map zone changed; stopped.");
            navigationLabel.Text=$"Zone {navigationZone} · {navigation.Status}\nGreen: observed movement · Orange: temporary blocked · Blue: route · Gold: treasure boxes (live) · Amber: remembered";
            RefreshNavigationRecordingControls();
            navigationCanvas.Invalidate();
            guardSelfId=self.Id;\n            UpdateParty(self);\n            avoidZones=Avoidance.BuildZones(avoidRules,entities,self.Id);\n            int level = world.PlayerLevel();\n            if(working && activeGuardOptions?.GroupMode==true)RefreshGroupDecision();\n            if(working && activeGuardOptions is {GroupMode:false} combatOptions) ObserveEncounter(combatOptions,health,pos,level);\n            if (working && health.GetValueOrDefault(self.Id).Dead)
            {
                SaveLootLog("Death");
                Stop("Character died; stopped.");
            }
            var selfHealth = health.GetValueOrDefault(self.Id);\n            var selfMana = world.ReadMana();\n            int chestCount = entities.Count(e => Targeting.IsChest(e) && !health.GetValueOrDefault(e.Id).Dead);\n            position.Text = $"HP {(selfHealth.Known ? $"{Math.Max(0,selfHealth.Current)}/{selfHealth.Maximum}" : "unknown")} · MP {(selfMana.Known ? $"{selfMana.Current}/{selfMana.Maximum}" : "unknown")}";\n            priorityHint.SetToolTip(position,$"{self.Name} · Lv. {level} · {pos.X:F2}, {pos.Y:F2} · {entities.Count(e=>e.Monster)} monsters");\n            string[] allowedColors = difficultyBoxes.Where(kv => kv.Value.Checked).Select(kv => kv.Key.ToString()).ToArray();\n            list.BeginUpdate(); list.Items.Clear();\n            foreach (var e in entities.Where(e => e.Targetable).OrderByDescending(e => Targeting.PriorityRank(e,prioritizeGamekeeper.Checked,prioritizeBreakables.Checked)).ThenBy(e => (e.Position - pos).Length).Take(30))\n            {\n                Threat threat = world.Difficulty(e, level);\n                var hp = health.GetValueOrDefault(e.Id);\n                string? protectedReason=TargetGuardReason(e,hp,pos);\n                bool engaged = encounter.IsEngaged(e);\n                bool allowed = (engaged && hp.Known && !hp.Dead || Targeting.Eligible(e, hp, threat, filter.Text, allowedColors,prioritizeGamekeeper.Checked)) && protectedReason==null;\n                double targetLimit=prioritizeGamekeeper.Checked && Targeting.IsGamekeeper(e) ? Targeting.ResponseRadius((double)radius.Value,(double)gamekeeperRadius.Value) :\n                    leaveAreaWhenEmpty.Checked ? (double)radius.Value : Targeting.TargetRadius(e,prioritizeGamekeeper.Checked,(double)radius.Value,(double)gamekeeperRadius.Value);\n                bool outsideArea=working && activeGuardOptions?.GroupMode!=true && activeHuntAnchor is Vec areaCenter && !engaged && (e.Position-areaCenter).Length>targetLimit;\n                string? areaReason=outsideArea && allowed ? $"{(e.Position-activeHuntAnchor!.Value).Length:F1} units from original hunt center; home radius {targetLimit:F0}."+\n                    (leaveAreaWhenEmpty.Checked ? $" Outside targets up to {targetLimit*2:F0} are considered only when no approved targets remain inside; return follows each outside fight." : " Distance column is from your character.") : null;\n                string protectedLabel=protectedReason?.StartsWith("Player nearby") == true ? "Player near" : protectedReason?.StartsWith("Already damaged") == true ? "Damaged" : "Avoid";\n                var row = new ListViewItem([e.DisplayName, e.PriorityLootObject ? "Breakable" : threat.ToString(), hp.Known ? $"{Math.Max(0,hp.Current)}/{hp.Maximum}" : "Unknown", hp.Dead ? "Dead" : protectedReason!=null ? protectedLabel : allowed ? engaged ? "Engaged" : outsideArea ? "Outside area" : Targeting.PriorityRank(e,prioritizeGamekeeper.Checked,prioritizeBreakables.Checked)>0 ? "Priority" : "Yes" : "No", e.Position.X.ToString("F2"), e.Position.Y.ToString("F2"), (e.Position - pos).Length.ToString("F1"), $"{e.Id:X8}"]) {ToolTipText=protectedReason??areaReason??""};\n                row.ForeColor = e.PriorityLootObject ? Color.LightSkyBlue : MonsterDefinition.DisplayColor(threat);\n                if (lockedTarget?.Id == e.Id) row.BackColor = Color.FromArgb(53, 61, 78);\n                list.Items.Add(row);\n            }\n            list.EndUpdate();\n            groundLoot = world.Loot();
            Vec? lootCenter=working?activeHuntAnchor:null;
            double? lootRadius=working?(double?)(activeGuardOptions?.HuntRadius ?? radius.Value):null;
            lootTracker.ObserveDrops(groundLoot,navigationZone,lootCenter,lootRadius); lootPage.Text = $"Ground loot ({groundLoot.Count})";
            string? lootTop = lootList.TopItem?.Tag as string;\n            lootList.BeginUpdate(); lootList.Items.Clear();\n            foreach (var item in groundLoot.OrderBy(item => (item.Position-pos).Length).Take(100))\n            {\n                string key=$"{item.KeyA}:{item.KeyB}"; var row=new ListViewItem([item.Name,(item.Position-pos).Length.ToString("F1"),item.Position.X.ToString("F2"),item.Position.Y.ToString("F2")]) {Tag=key};\n                lootList.Items.Add(row); if (key==selectedGroundKey) row.Selected=true;\n            }\n            if (lootTop!=null) { var top=lootList.Items.Cast<ListViewItem>().FirstOrDefault(r=>(r.Tag as string)==lootTop); if (top!=null) lootList.TopItem=top; }\n            lootList.EndUpdate();\n            currentHotbar = world.Hotbar(); UpdateSkillKeys(currentHotbar); UpdateHealingSkillKeys(currentHotbar); hotbarPage.Text = $"Hotbar Â· {currentHotbar.Page}";\n            ObserveBuffs(activeGuardOptions??CurrentOptions(),currentHotbar);\n            supportSettings.Enabled=!working;\n            if (working && runHotbarPage.HasValue && currentHotbar.PageBase != runHotbarPage.Value) Stop("Hotbar page changed; stopped. Check the selected keys before restarting.");\n            string? hotbarTop = hotbarList.TopItem?.Text;\n            hotbarList.BeginUpdate(); hotbarList.Items.Clear();\n            foreach (var slot in currentHotbar.Slots)\n            {\n                string state = slot.Kind == SlotKind.Empty ? "Empty" : slot.Locked ? $"Locked ({Math.Max(0,slot.LockRemaining)/1000.0:F1}s)" : slot.Ready ? "Ready" : $"{slot.RemainingCooldown/1000.0:F1}s";\n                string role = PotionItems.Role(slot);\n                if(role.Length==0 && slot.Kind==SlotKind.Skill && skillKeys.Text.Contains(slot.Key))role="Attack skill";\n                var buff=buffDecisions.FirstOrDefault(b=>b.Key==slot.Key && b.SkillId==slot.Id);\n                if(buff!=null)role=buff.Eligible?$"Party {(slot.SkillUse==SkillUseKind.Chant?"chant":"buff")}":"Effect: monitor only";\n                else if(HealerPolicy.IsHealingSkill(slot))role="Healing skill";\n                var row = new ListViewItem([slot.Key, slot.Name, slot.Kind==SlotKind.Skill?slot.SkillUse.ToString():slot.Kind.ToString(), role, state, slot.TotalCooldown > 0 ? $"{slot.TotalCooldown/1000.0:F1}s" : "None reported", slot.Id == 0 ? "" : slot.Id.ToString()]);\n                if (slot.Ready) row.ForeColor = Color.FromArgb(93,222,179);\n                hotbarList.Items.Add(row);\n                if (slot.Key==selectedHotbarKey) row.Selected=true;\n            }\n            if (hotbarTop!=null) { var top=hotbarList.Items.Cast<ListViewItem>().FirstOrDefault(r=>r.Text==hotbarTop); if (top!=null) hotbarList.TopItem=top; }\n            hotbarList.EndUpdate();\n            UpdateItemDescriptions();\n            if (Environment.TickCount64 - lastEvidence > 1000)\n            {\n                lastEvidence = Environment.TickCount64;\n                RecordObservations(self, level, health);\n                var targetState=world.TargetState();\n                WriteState(new { TimeUtc = DateTime.UtcNow, Connected = true, Working = working, Calibrated = movement != null, Player = self.Name, PlayerHP = selfHealth, PlayerMP = selfMana, ManaRecoveryStatus=manaRecoveryStatus, CameraSupported=world.CameraSupported, CameraStatus=world.CameraStatus, TargetState=new {targetState.Available,targetState.Status,TargetIds=targetState.Ids.Select(id=>$"0x{id:X8}").ToArray()}, Level = level, Position = pos, Hotbar = currentHotbar, DetectedHealingItems = currentHotbar.Slots.Where(RecoveryItems.Recognized), DetectedManaItems=currentHotbar.Slots.Where(ManaRecovery.Recognized), Radar=new{Enabled=showNavigationOverlay.Checked,Visible=navigationOverlay is {Visible:true},Size=(int)navigationOverlaySize.Value}, LootTrackerOverlay=new{Enabled=showLootTrackerOverlay.Checked,Visible=lootTrackerOverlay is {Visible:true},Position=lootTrackerOverlay?.Location}, LootTracker=lootTracker.Snapshot(), Healer = HealerState(), Recording = new { Enabled = true, ObjectCount = entities.Count, Error = recordingError }, Protection = ProtectionState(), Combat = CombatState(), Group = GroupState(), Navigation = navigation.Snapshot(), Gamekeepers=entities.Where(Targeting.IsGamekeeper).Select(e=>new {
                    e.Id,e.Position,HP=health.GetValueOrDefault(e.Id),Distance=(e.Position-pos).Length,\n                    AnchorDistance=(e.Position-(activeHuntAnchor ?? pos)).Length,\n                    ResponseRadius=Targeting.ResponseRadius((double)(activeGuardOptions?.HuntRadius ?? radius.Value),(double)(activeGuardOptions?.GamekeeperResponseRadius ?? gamekeeperRadius.Value)),\n                    Protection=TargetGuardReason(e,health.GetValueOrDefault(e.Id),pos)\n                }), LockedTarget = lockedTarget == null ? null : new { lockedTarget.Name, lockedTarget.DisplayName, lockedTarget.Id, lockedTarget.Generation, lockedTarget.PriorityLootObject }, Status = message, GroundLoot = groundLoot.OrderBy(i => (i.Position-pos).Length).Take(20), PriorityObjects = entities.Where(e => e.PriorityLootObject).OrderBy(e => (e.Position-pos).Length).Select(e => new { e.DisplayName, e.Name, e.Id, e.Model, e.Position, HP = health.GetValueOrDefault(e.Id), Allowed = Targeting.Eligible(e, health.GetValueOrDefault(e.Id), Threat.Unknown, filter.Text, allowedColors) && TargetGuardReason(e,health.GetValueOrDefault(e.Id),pos)==null, Distance = (e.Position-pos).Length }), Monsters = entities.Where(e => e.Monster).OrderBy(e => (e.Position - pos).Length).Select(e => new { e.Name, e.Id, e.Position, HP = health.GetValueOrDefault(e.Id), Difficulty = world.Difficulty(e, level).ToString(), Distance = (e.Position - pos).Length }) });\n            }\n        }\n        catch (Exception ex) { connected = false; player.Text=""; nextCharacterReconnect=Environment.TickCount64+3000; Stop(ex.Message); WriteState(new { TimeUtc = DateTime.UtcNow, Connected = false, Working = false, Calibrated = false, Status = message }); }\n    }\n    void RecordObservations(Entity self, int level, Dictionary<uint, Health> health)\n    {\n        try\n        {\n            var observed = entities.Select(e =>\n            {\n                uint? prototype = (e.Id & 0xf0000000) == 0x80000000 ? e.Id & 0xffff : null;\n                MonsterDefinition definition = prototype.HasValue ? world.Definition(prototype.Value) : default;\n                return new { e.Id, e.Generation, e.Name, e.DisplayName, e.Model, e.Position, e.Height, e.Heading,\n                    HP = health.GetValueOrDefault(e.Id), PrototypeId = prototype, Definition = definition,\n                    e.Monster, e.PriorityLootObject, e.Targetable };\n            }).ToArray();\n            recorder.Capture(new { TimeUtc = DateTime.UtcNow, ClientSha256 = world.ClientHash, ProcessId = world.Pid,
                Working = working, PlayerId = self.Id, PlayerLevel = level, LoadedObjectCount = world.CandidateCount,
                ReadableObjectCount = observed.Length, Objects = observed, GroundItems = groundLoot, LootTracker = lootTracker.Snapshot(), Hotbar = currentHotbar,
                LockedTargetId = lockedTarget?.Id, Status = message, Protection = ProtectionState(), Combat = CombatState(), Group = GroupState(), Navigation = navigation.Snapshot() },\n                observed.Select(e => new ObjectTypeObservation($"{e.Id >> 28:X}:{e.PrototypeId?.ToString() ?? e.Name}:{e.Model}",\n                    e.PrototypeId, e.Name, e.Model, e.Definition.Name ?? "", e.PrototypeId.HasValue ? e.Definition.Level : null,\n                    e.PrototypeId.HasValue ? e.Definition.Category : null, e.PriorityLootObject)));\n            recordingError = recorder.LastError;\n        }\n        catch (Exception ex) { recordingError = ex.Message; }\n    }\n    object ProtectionState() => new { AntiKillSteal=activeGuardOptions?.AntiKillSteal ?? antiKillSteal.Checked,\n        OtherPlayerRadius=activeGuardOptions?.OtherPlayerRadius ?? playerBuffer.Value, AvoidNames=avoidRules, ActiveAvoidZones=avoidZones,\n        Recovery=new {Enabled=true,Phase=retreatRecovery?.Phase.ToString() ?? "Inactive",retreatRecovery?.HealthTarget,ClearanceBeyondRule=RetreatPlanner.Clearance,\n            world.RestSupported,RestPosture=world.RestSupported?world.RestState().Posture.ToString():"Unavailable"},\n        OtherPlayers=entities.Where(e=>CombatCourtesy.IsOtherPlayer(e,guardSelfId)).Select(e=>new {e.Id,e.Name,e.Model,e.Position}).ToArray() };\n    void UpdateParty(Entity self)\n    {\n        currentParty=world.Party();\n        ApplyPartyNames();\n        var candidates=currentParty.Members.Where(m=>m.Id!=self.Id).ToArray();\n        if(string.IsNullOrWhiteSpace(selectedTankName))selectedTankName=candidates.FirstOrDefault(m=>m.Leader)?.Name ?? "";\n        if(!tankPicker.Items.Cast<PartyMember>().SequenceEqual(candidates))\n        {\n            tankPicker.Items.Clear();tankPicker.Items.AddRange(candidates);\n            tankPicker.SelectedItem=candidates.FirstOrDefault(m=>m.Name.Equals(selectedTankName,StringComparison.OrdinalIgnoreCase));\n        }\n        tankPicker.Enabled=!working && currentParty.Available;\n        groupEnabled.Enabled=!working&&!rangedPullEnabled.Checked;groupFollow.Enabled=!working&&!rangedPullEnabled.Checked;groupAttack.Enabled=!working&&!rangedPullEnabled.Checked;groupLimit.Enabled=!working&&!rangedPullEnabled.Checked;
        partyList.BeginUpdate();partyList.Items.Clear();\n        foreach(var member in currentParty.Members)\n        {\n            var entity=entities.FirstOrDefault(e=>e.Id==member.Id);\n            partyList.Items.Add(new ListViewItem([member.Name,member.Leader?"Leader":"Member",entity!=null?"Yes":"Not loaded",entity==null?"â€”":(entity.Position-self.Position).Length.ToString("F1"),$"{member.Id:X8}"]));\n        }\n        partyList.EndUpdate();\n        partyStatus.Text=currentParty.Status+" Â· "+(string.IsNullOrWhiteSpace(selectedTankName)?"Select a tank from the party list.":"Selected tank: "+selectedTankName)+Environment.NewLine+\n            (working && activeGuardOptions?.GroupMode==true?groupDecision.Status:healerMode.Checked?"Healbot: follows the selected tank and heals nearby party members. Attack radius is unused; pickup is off.":"Follows the tank and attacks allowed targets inside the attack radius. Other-party players and avoidance rules still apply. Group pickup is off.");\n    }\n    object GroupState()=>new {Enabled=activeGuardOptions?.GroupMode ?? groupEnabled.Checked,Tank=activeGuardOptions?.GroupTankName ?? selectedTankName,\n        Party=currentParty,Action=groupDecision.Action.ToString(),Status=working?groupDecision.Status:groupEnabled.Checked?"Ready to follow selected tank":"Group mode is off",TargetId=groupDecision.Target?.Id,TargetSelection="Nearby tank",FollowDistance=activeGuardOptions?.GroupFollowDistance ?? groupFollow.Value,AttackRadius=activeGuardOptions?.GroupAttackRadius ?? groupAttack.Value,FollowLimit=activeGuardOptions?.GroupFollowLimit ?? groupLimit.Value};\n    void ApplyPartyNames()\n    {\n        if(!currentParty.Available)return;\n        entities=entities.Select(e=>string.IsNullOrWhiteSpace(e.Name) && currentParty.Members.FirstOrDefault(m=>m.Id==e.Id) is PartyMember member?e with{Name=member.Name}:e).ToList();\n    }\n    void RefreshGroupDecision()\n    {\n        var options=activeGuardOptions;\n        if(options?.GroupMode!=true)return;\n        currentParty=world.Party();var self=world.LocalPlayer();\n        var member=currentParty.Members.FirstOrDefault(m=>m.Name.Equals(options.GroupTankName,StringComparison.OrdinalIgnoreCase));\n        var tank=member==null?null:entities.FirstOrDefault(e=>e.Id==member.Id && CombatCourtesy.IsOtherPlayer(e,self.Id));\n        var health=world.HealthSnapshot();\n        if(options.HealerMode)\n        {\n            groupDecision=GroupHealerPolicy.Follow(currentParty,options.GroupTankName,self,entities,health,\n                (double)options.GroupFollowDistance,(double)options.GroupFollowLimit);\n            return;\n        }\n        groupDecision=GroupPolicy.Decide(currentParty,options.GroupTankName,self,entities,health,(double)options.GroupFollowDistance,(double)options.GroupAttackRadius,(double)options.HuntRadius,\n            (entity,hp)=>GroupCandidateReason(entity,hp,self.Position,options)==null,lockedTarget,options.PrioritizeGamekeeper);\n    }\n    string? GroupCandidateReason(Entity target,Health hp,Vec position,Options options)\n    {\n        if(!Targeting.Eligible(target,hp,world.Difficulty(target,world.PlayerLevel()),options.Target,options.AllowedDifficulties,options.PrioritizeGamekeeper))return "Excluded by target filters";\n        return (unreachableTargets.TryGetValue(TargetIdentity(target),out long until) && Environment.TickCount64<until?"Temporarily unreachable":null) ??\n            (options.AutomaticRouting?Avoidance.BlockedPoint(target.Position,avoidZones):Avoidance.BlockedSegment(position,target.Position,avoidZones)) ??\n            courtesy.Blocked(target,hp,entities.Where(e=>!currentParty.Members.Any(m=>m.Id==e.Id)),guardSelfId,options.AntiKillSteal,(double)options.OtherPlayerRadius,true);\n    }\n    void WriteState(object value)\n    {\n        string path = Path.Combine(AppContext.BaseDirectory, "live-status.json");\n        var state=JsonSerializer.SerializeToNode(value)!;\n        state["ReaderPid"]=Environment.ProcessId;\n        state["MemoryBackend"]=PoteMemoryProbe.WindowsClientRead.Backend;\n        state["InputBackend"]=WindowsClientInput.Backend;\n        state["InputCompatibilityEnabled"]=WindowsClientInput.Enabled;\n        state["Hotkeys"]=JsonSerializer.SerializeToNode(new { Ready=hotkeys, Failure=hotkeyFailure, Registrations=hotkeyRegistrations });\n        state["Status"]=DisplayMessage;\n        state["TargetSearch"]=working ? JsonSerializer.SerializeToNode(targetSearch) : null;\n        state["HuntingArea"]=JsonSerializer.SerializeToNode(new {Mode=(activeGuardOptions?.GroupMode ?? groupEnabled.Checked)?"Group":(activeGuardOptions?.LeaveAreaWhenEmpty ?? leaveAreaWhenEmpty.Checked)?"Fixed with outside trips":"Fixed",Center=activeHuntAnchor,Radius=activeGuardOptions?.HuntRadius ?? radius.Value,OutsideSearchRadius=(activeGuardOptions?.HuntRadius ?? radius.Value)*2,OutsideTrip=working && activeExcursion?.OutsideTrip==true,ReturnPending=working && completionReturnPending,CompletionRadius=working?activeCompletionBoundary:(double?)null});\n        File.WriteAllText(path + ".tmp", state.ToJsonString(new JsonSerializerOptions { WriteIndented = true })); File.Move(path + ".tmp", path, true);\n    }\n    void UpdateItemDescriptions()\n    {\n        var slot=currentHotbar?.Slots.FirstOrDefault(s=>s.Key==selectedHotbarKey);\n        if (slot!=null) hotbarDescription.Text=$"{slot.Name}" + (slot.Category.Length>0?$" Â· {slot.Category}":"") + Environment.NewLine + (slot.Description.Length>0?slot.Description:slot.Kind==SlotKind.Skill?"Skill assignment. Its ready state and cooldown are shown above.":"No base description is available.");\n        var item=groundLoot.FirstOrDefault(i=>$"{i.KeyA}:{i.KeyB}"==selectedGroundKey);\n        if (item!=null) lootDescription.Text=item.Name+Environment.NewLine+(item.Description.Length>0?item.Description:"No base description is available.");\n        else if (selectedGroundKey!=null) lootDescription.Text="The selected item is no longer on the ground.";\n    }\n    void Stop(string reason)
    {
        lootTracker.ObserveActivity(false);
        startVersion++;
        rangedPull.Reset(); rangedTagging = false;\n        Input.PickupHoldProvider=null;nearbyPickupSnapshot.Clear();nearbyPickupCount=0;\n        cancel?.Cancel(); ReleaseCombatPickup(); Input.Release(); movement = null; lockedTarget = null; message = reason;
    }
    bool SaveLootLog(string reason)
    {
        bool saved = lootTrackerLog.TrySave(lootTracker.Snapshot(), reason);
        if (!saved) message = $"Loot event could not be saved ({reason}).";
        return saved;
    }
    void PaintNavigation(object? sender,PaintEventArgs e)\n    {\n        DrawNavigation(e.Graphics,navigationCanvas.ClientSize);\n    }\n    void DrawNavigation(Graphics g,Size canvasSize)\n    {\n        g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;\n        float span=(float)NavigationViewRadius(),scale=Math.Min(canvasSize.Width,canvasSize.Height)/(span*2);\n        float cx=canvasSize.Width/2f, cy=canvasSize.Height/2f;\n        PointF Project(Vec v)=>new(cx+(float)(v.X-navigationPosition.X)*scale,cy-(float)(v.Y-navigationPosition.Y)*scale);\n        if(zoneMapBackground.TryGet(navigationZone,out var mapImage,out var mapBounds))\n        {\n            float left=cx+(float)(mapBounds.MinX-navigationPosition.X)*scale;\n            float right=cx+(float)(mapBounds.MaxX-navigationPosition.X)*scale;\n            float top=cy-(float)(mapBounds.MaxY-navigationPosition.Y)*scale;\n            float bottom=cy-(float)(mapBounds.MinY-navigationPosition.Y)*scale;\n            g.DrawImage(mapImage,RectangleF.FromLTRB(left,top,right,bottom));\n        }\n        using var trailPen=new Pen(Color.SeaGreen,1.5f);using var routePen=new Pen(Color.DeepSkyBlue,2);using var obstaclePen=new Pen(Color.Orange,2);using var avoidPen=new Pen(Color.IndianRed,1.5f);
        var trail=navigation.Trail.Where(p=>(p-navigationPosition).Length<span*2).Select(Project).ToArray();if(trail.Length>1)g.DrawLines(trailPen,trail);
        foreach(var obstacle in navigation.Blocked) {var p=Project(obstacle.Center);float r=(float)obstacle.Radius*scale;g.DrawEllipse(obstaclePen,p.X-r,p.Y-r,r*2,r*2);}
        foreach(var zone in avoidZones) {var p=Project(zone.Center);float r=(float)(zone.Radius+1)*scale;g.DrawEllipse(avoidPen,p.X-r,p.Y-r,r*2,r*2);}
        var savedPens=new[]{new Pen(Color.MediumPurple,2),new Pen(Color.Gold,2),new Pen(Color.Coral,2)};
        var savedBrushes=new[]{new SolidBrush(Color.MediumPurple),new SolidBrush(Color.Gold),new SolidBrush(Color.Coral)};
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
\n        DrawDirectionCone(g,canvasSize);
        DrawRadarMonsters(g,canvasSize);
        if(!showTreasureChestMarkers.Checked)
        {
            var selfMarker=Project(navigationPosition);g.FillEllipse(Brushes.White,selfMarker.X-4,selfMarker.Y-4,8,8);
            return;
        }
        var liveChests=entities.Where(ent=>ent.Position.Finite && Targeting.IsChest(ent) && !latestHealth.GetValueOrDefault(ent.Id).Dead).ToList();
        var liveChestIds=liveChests.Select(chest=>chest.Id).ToHashSet();\n        var chests=liveChests.Select(chest=>new {chest.Position,Label=Targeting.ChestLabel(chest),Live=true}).Concat(\n            chestCatalog.ForZone(navigationZone).Where(chest=>!liveChestIds.Contains(chest.Id)).Select(chest=>new {chest.Position,chest.Label,Live=false})).ToList();\n        if(chests.Count>0)\n        {\n            using var chestFill=new SolidBrush(Color.FromArgb(255,215,0));\n            using var rememberedFill=new SolidBrush(Color.FromArgb(155,215,165,32));\n            using var chestOutline=new Pen(Color.FromArgb(140,90,0),1.4f);\n            using var claspBrush=new SolidBrush(Color.WhiteSmoke);\n            using var font=new Font("Segoe UI",7.5f,FontStyle.Bold);\n            using var textBrush=new SolidBrush(Color.FromArgb(255,238,130));\n            using var tagBgBrush=new SolidBrush(Color.FromArgb(210,16,22,32));\n            using var tagBorderPen=new Pen(Color.FromArgb(180,218,165,32),1f);\n            using var offscreenBrush=new SolidBrush(Color.FromArgb(255,215,0));\n\n            int w=canvasSize.Width, h=canvasSize.Height;\n            foreach(var chest in chests)\n            {\n                double dist=(chest.Position-navigationPosition).Length;\n                var p=Project(chest.Position);\n                bool onScreen=p.X>=12 && p.X<=w-12 && p.Y>=12 && p.Y<=h-12;\n                if(onScreen)\n                {\n                    float bx=p.X-6, by=p.Y-4.5f;\n                    g.FillRectangle(chest.Live?chestFill:rememberedFill,bx,by,12,9);\n                    g.DrawRectangle(chestOutline,bx,by,12,9);\n                    g.DrawLine(chestOutline,bx,by+3.5f,bx+12,by+3.5f);\n                    g.FillRectangle(claspBrush,p.X-1f,by+2.5f,2.5f,2.5f);\n                    g.DrawRectangle(Pens.Black,p.X-1f,by+2.5f,2.5f,2.5f);\n\n                    string label=$"{chest.Label}{(chest.Live?"":" seen")} ({dist:F0}m)";\n                    while(label.Length>1 && g.MeasureString(label,font).Width>w-12)label=label[..^2]+"…";\n                    var sz=g.MeasureString(label,font);\n                    float lx=Math.Clamp(p.X-sz.Width/2f,3,w-sz.Width-3);\n                    float ly=by-sz.Height-2;\n                    if(ly<2) ly=by+11;\n                    g.FillRectangle(tagBgBrush,lx-2,ly-1,sz.Width+4,sz.Height+2);\n                    g.DrawRectangle(tagBorderPen,lx-2,ly-1,sz.Width+4,sz.Height+2);\n                    g.DrawString(label,font,textBrush,lx,ly);\n                }\n                else\n                {\n                    float dx=p.X-cx, dy=p.Y-cy;\n                    float len=MathF.Sqrt(dx*dx+dy*dy);\n                    if(len>1e-3f)\n                    {\n                        float margin=16f;\n                        float maxX=cx-margin, maxY=cy-margin;\n                        float sx=Math.Abs(dx)>1e-4f?maxX/Math.Abs(dx):float.MaxValue;\n                        float sy=Math.Abs(dy)>1e-4f?maxY/Math.Abs(dy):float.MaxValue;\n                        float s=Math.Min(sx,sy);\n                        float edgeX=cx+dx*s, edgeY=cy+dy*s;\n\n                        float nx=dx/len, ny=dy/len;\n                        float px=-ny, py=nx;\n                        PointF tip=new(edgeX,edgeY);\n                        PointF b1=new(edgeX-nx*9+px*4.5f,edgeY-ny*9+py*4.5f);\n                        PointF b2=new(edgeX-nx*9-px*4.5f,edgeY-ny*9-py*4.5f);\n                        g.FillPolygon(offscreenBrush,new[]{tip,b1,b2});\n                        g.DrawPolygon(chestOutline,new[]{tip,b1,b2});\n\n                        string tag=$"{dist:F0}m";\n                        var tsz=g.MeasureString(tag,font);\n                        float tx=Math.Clamp(edgeX-nx*16-tsz.Width/2f,2,w-tsz.Width-2);\n                        float ty=Math.Clamp(edgeY-ny*16-tsz.Height/2f,2,h-tsz.Height-2);\n                        g.FillRectangle(tagBgBrush,tx-2,ty-1,tsz.Width+4,tsz.Height+2);\n                        g.DrawRectangle(tagBorderPen,tx-2,ty-1,tsz.Width+4,tsz.Height+2);\n                        g.DrawString(tag,font,textBrush,tx,ty);\n                    }\n                }\n            }\n        }\n\n        var self=Project(navigationPosition);g.FillEllipse(Brushes.White,self.X-4,self.Y-4,8,8);\n    }\n    bool RoutingEnabled => activeGuardOptions?.AutomaticRouting ?? automaticRouting.Checked;\n    static (uint,uint,long) TargetIdentity(Entity e)=>(e.Id,e.Generation,e.Address);\n    async Task<bool> NavigateTo(Movement drive,Vec goal,Vec anchor,Options options,CancellationToken token,double bodyReach=0,double? boundaryRadius=null,bool watchTurns=false)\n    {\n        activeMovementBoundary=boundaryRadius ?? (double)options.HuntRadius;\n        if(!goal.Finite || (goal-anchor).Length>activeMovementBoundary)throw new RouteUnavailableException("Destination is beyond the current movement boundary.");\n        var self=world.LocalPlayer();navigationPosition=self.Position;\n        navigation.Observe(world.NavigationContext(self),self.Position,self.Height);\n        if(!options.AutomaticRouting) {await drive.Approach(world,self.Position,goal-self.Position,token,watchTurns);return false;}\n        int version=navigation.RouteVersion;\n        Vec waypoint;\n        try {waypoint=navigation.Waypoint(self.Position,goal,anchor,boundaryRadius ?? (double)options.HuntRadius,avoidZones);}\n        catch(RouteUnavailableException) when(bodyReach>0 && (goal-self.Position).Length<=bodyReach) {drive.StopApproach();return true;}\n        if(version!=navigation.RouteVersion) drive.StopApproach();\n        try {await drive.Approach(world,self.Position,waypoint-self.Position,token,watchTurns);}\n        catch(MovementBlockedException blocked)\n        {\n            drive.StopApproach();Input.HoldMouse(false,false,token);\n            if(bodyReach>0 && (goal-blocked.Position).Length<=bodyReach)\n            {\n                TraceLog.Record("close body collision; checking attack reach",new {blocked.Position,Goal=goal,Distance=(goal-blocked.Position).Length});\n                return true;\n            }\n            navigation.RecordBlock(blocked.Position,blocked.Direction,self.Height);\n            TraceLog.Record("navigation blocked direction",new {Zone=navigationZone,blocked.Position,blocked.Direction,navigation.RecoveryAttempts,Goal=goal});\n            await Input.Delay(100,token);\n        }\n        return false;\n    }\n    async Task StartHunting(long? requestedVersion=null)\n    {\n        if(!RequireHotkeys() || busy || working || !connected) return;\n        long version=requestedVersion ?? ++startVersion;\n        if(version!=startVersion) return;\n        var readiness=world.CheckInputWindow();\n        TraceLog.Record("start requested",readiness);\n        if(!readiness.Allowed) { message=readiness.BlockReason!; return; }\n        var requested=CurrentOptions();\n        if(requested.HealerMode && requested.GroupMode)
        {
            var party=world.Party();var self=world.LocalPlayer();
            if(!party.Available || party.Members.Count(m=>m.Id!=self.Id && m.Name.Equals(requested.GroupTankName,StringComparison.OrdinalIgnoreCase))!=1)
            {message="Select another current party member as tank on the Group page before starting the healbot.";return;}
        }
        if(requested.HealerMode && !requested.GroupMode)\n        {\n            message="Healer mode ready. Press F8 again to stop.";\n            await Hunt();\n            return;\n        }\n        if(movement==null && requested.Ranged && !requested.HealerMode && RangedAimPersistence.TryRestore(Path.Combine(AppContext.BaseDirectory,"calibration.json"),world,out var restored,out var restoreStatus))\n        {\n            movement=restored;\n            message="Live facing ready. "+restoreStatus;\n            TraceLog.Record("3D aim sensitivity restored",new{world.ClientHash,Character=world.LocalPlayer().Name,restoreStatus});\n        }\n        if(movement==null) await Calibrate();\n        if(version!=startVersion || movement==null || !Input.Allowed()) return;\n        message="Calibration ready. Starting huntâ€¦";\n        await Hunt();\n    }\n    string? TargetGuardReason(Entity target, Health hp, Vec position, Options? options=null)\n    {\n        var active=options ?? activeGuardOptions;\n        if(active?.GroupMode==true)\n        {\n            if(hp.Dead)return null;\n            if(active.PrioritizeGamekeeper && Targeting.IsGamekeeper(target))return GroupCandidateReason(target,hp,position,active);\n            if(groupDecision.Action!=GroupAction.Attack || groupDecision.Target is not Entity selected || TargetIdentity(selected)!=TargetIdentity(target))\n                return "Group: "+groupDecision.Status;\n            var tank=groupDecision.Tank;\n            var liveTank=tank==null?null:world.Find(tank.Id);\n            var liveTarget=world.Find(target.Id);\n            if(liveTank==null || tank==null || TargetIdentity(liveTank)!=TargetIdentity(tank) || liveTarget==null || TargetIdentity(liveTarget)!=TargetIdentity(target))\n                return "Group tank or target changed";\n            if((liveTarget.Position-liveTank.Position).Length>(double)active.GroupAttackRadius)return "Target moved outside the tank attack radius";\n            return GroupCandidateReason(target,hp,position,active);\n        }\n        return (unreachableTargets.TryGetValue(TargetIdentity(target),out long until) && Environment.TickCount64<until ? "Temporarily unreachable" : null) ??\n        ((options?.AutomaticRouting ?? RoutingEnabled) ? Avoidance.BlockedPoint(target.Position,avoidZones) : Avoidance.BlockedSegment(position,target.Position,avoidZones)) ??\n        courtesy.Blocked(target,hp,entities,guardSelfId,options?.AntiKillSteal ?? activeGuardOptions?.AntiKillSteal ?? antiKillSteal.Checked,\n            (double)(options?.OtherPlayerRadius ?? activeGuardOptions?.OtherPlayerRadius ?? playerBuffer.Value),\n            encounter.MayHaveReceivedOurDamage(target) && CombatCourtesy.PlayerNear(target.Position,entities,guardSelfId,\n                Math.Max((double)(options?.OtherPlayerRadius ?? activeGuardOptions?.OtherPlayerRadius ?? playerBuffer.Value),6))==null);\n    }\n\n    double BaseTargetRadius(Entity entity,Options options) => options.PrioritizeGamekeeper && Targeting.IsGamekeeper(entity) ?\n        Targeting.ResponseRadius((double)options.HuntRadius,(double)options.GamekeeperResponseRadius) : options.LeaveAreaWhenEmpty && !options.GroupMode ?\n        activeExcursion is {OutsideTrip:true} trip ? trip.OutsideSearchRadius : (double)options.HuntRadius :\n        Targeting.TargetRadius(entity,options.PrioritizeGamekeeper,(double)options.HuntRadius,(double)options.GamekeeperResponseRadius);\n\n    Entity? PriorityGamekeeper(Options options)
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
\n    async Task StandForGamekeeper(CancellationToken token)\n    {\n        ReleaseCombatPickup();movement?.StopApproach();Input.Release(preserveNearbyPickup:true);\n        gamekeeperTransition=true;\n        try {if(world.RestSupported)await EnsurePosture(false,token);}\n        finally {gamekeeperTransition=false;}\n    }\n\n    bool MatchesRequestedTarget(Entity entity,Options options,int level) =>\n        Targeting.Eligible(entity,new Health(1,1),world.Difficulty(entity,level),options.Target,options.AllowedDifficulties,options.PrioritizeGamekeeper);\n\n    void ObserveEncounter(Options options,IReadOnlyDictionary<uint,Health> health,Vec position,int level)\n    {\n        if(options.GroupMode || !encounter.Active) return;\n        bool WithinBoundary(Entity entity,bool finishing=false)\n        {\n            double limit=BaseTargetRadius(entity,options);\n            if(finishing)limit=Math.Max(activeCompletionBoundary,Targeting.CompletionRadius(limit,(double)options.NearbyEnemyRadius,(double)options.MeleeRange));\n            return !encounterAnchor.HasValue || (entity.Position-encounterAnchor.Value).Length<=limit;\n        }\n        encounter.Observe(entities,health,position,(double)options.NearbyEnemyRadius,(entity,hp)=>\n            Targeting.Eligible(entity,hp,world.Difficulty(entity,level),options.Target,options.AllowedDifficulties,options.PrioritizeGamekeeper) &&\n            WithinBoundary(entity) && TargetGuardReason(entity,hp,position,options)==null,\n            mayRemainEngaged:(entity,hp)=>WithinBoundary(entity,true) && TargetGuardReason(entity,hp,position,options)==null,\n            clearNearby:options.ClearNearbyEnemies && !healingRestPending && !(options.LeaveAreaWhenEmpty && completionReturnPending),\n            mayClaimCollateral:entity=>(!RangedPullEnabled(options) || !rangedTagging) && CombatCourtesy.PlayerNear(entity.Position,entities,guardSelfId,Math.Max(6,(double)options.OtherPlayerRadius))==null,\n            attackHeld:Input.BasicAttackHeld,attackReach:Encounter.CollateralReach(RangedPullEnabled(options) ? (double)options.RangedMeleeAttackRange : (double)options.MeleeRange));\n        string state=$"{encounter.EngagedCount}:{encounter.HasUnresolvedEngaged}:"+string.Join(",",encounter.EngagedCandidates.OrderBy(e=>e.Id).Select(e=>$"{e.Id}:{e.Generation}:{e.Address}"));\n        if(state!=lastEngagementState)\n        {\n            lastEngagementState=state;\n            TraceLog.Record("engaged enemies updated",new {Count=encounter.EngagedCount,encounter.HasUnresolvedEngaged,Enemies=encounter.EngagedCandidates.Select(e=>new {e.Id,e.DisplayName,e.Generation}).ToArray()});\n        }\n    }\n\n    object CombatState() => new {BasicAttackHeld=Input.BasicAttackHeld,SkillHealthRule=new{Enabled=activeGuardOptions?.HealthSkillCondition ?? healthSkillCondition.Checked,Percent=activeGuardOptions?.HealthSkillPercent ?? healthSkillPercent.Value,ExtraKeys=activeGuardOptions?.HealthConditionKeys ?? healthConditionKeys.Text,HealthSource=(activeGuardOptions?.HealerMode ?? healerMode.Checked)?"Healing target":"Character"},
        AutoDetectSkills=activeGuardOptions?.AutoDetectSkills ?? autoSkills.Checked,ActiveSkillKeys=activeGuardOptions?.SkillKeys,GamekeeperResponseRadius=activeGuardOptions?.GamekeeperResponseRadius ?? gamekeeperRadius.Value,LootDuringSkillCooldowns=activeGuardOptions?.LootDuringSkillCooldowns ?? combatPickup.Checked,PickupHeld=Input.PickupHeld,NearbyLootPickupEnabled=activeGuardOptions?.AutoPickupNearbyLoot ?? nearbyLootPickup.Checked,NearbyLootCount=nearbyPickupCount,PickupRadius=activeGuardOptions?.NearbyEnemyRadius ?? nearbyRadius.Value,PrioritizeGamekeeper=activeGuardOptions?.PrioritizeGamekeeper ?? prioritizeGamekeeper.Checked,Enabled=activeGuardOptions?.ClearNearbyEnemies ?? clearNearby.Checked,
        Radius=activeGuardOptions?.NearbyEnemyRadius ?? nearbyRadius.Value,encounter.Active,\n        NearbyEnemies=encounter.Candidates.Select(e=>new {e.Id,e.DisplayName,e.Position}).ToArray(),\n        EngagedEnemies=encounter.EngagedCandidates.Select(e=>new {e.Id,e.DisplayName,e.Position}).ToArray(),\n        encounter.HasEngaged,encounter.EngagedCount,encounter.HasUnresolvedEngaged,HealingWarning=healingWarning,RestToFullPending=healingRestPending,RestToFullActive=healingRest!=null,\n        encounter.HasUnresolvedNearby,PendingLoot=deferredLoot.Count,DefensePending=defensePending,LastIncomingDamageAt=combatPressure.LastDamageAt ,\n        RangedPack=new {Enabled=activeGuardOptions?.RangedPullEnabled ?? rangedPullEnabled.Checked,Phase=rangedPull.Phase.ToString(),rangedPull.AttemptedCount}};\n    object HealerState()=>new {Enabled=activeGuardOptions?.HealerMode ?? healerMode.Checked,AutoDetectSkills=activeGuardOptions?.AutoDetectHealingSkills ?? autoHealingSkills.Checked,\n        SkillKeys=activeGuardOptions?.HealingSkillKeys ?? healingSkillKeys.Text,ChargeMilliseconds=activeGuardOptions?.HealChargeMilliseconds ?? healCharge.Value,\n        HealBelowPercent=activeGuardOptions?.PartyHealBelowPercent ?? partyHealBelow.Value,Range=activeGuardOptions?.PartyHealRange ?? partyHealRange.Value,\n        Target=activeHealTarget==null?null:new {activeHealTarget.Member.Id,activeHealTarget.Member.Name,activeHealTarget.PartyIndex,activeHealTarget.Health,activeHealTarget.Percent},LastSkill=lastHealingSkill,\n        Buffs=new{Mode="Live active-effect memory",Enabled=activeGuardOptions?.MaintainAreaBuffs??maintainBuffs.Checked,activeEffects.Available,activeEffects.Status,Decisions=buffDecisions,Active=activeEffects.Effects.Where(e=>e.Active)}};\n\n    string AttackKeys(string keys,HotbarSnapshot bar,bool upkeep)\n    {\n        var effects=world.ActiveEffects();\n        return new(keys.Where(key=>!upkeep || !bar.Slots.Any(s=>s.Key==key.ToString() &&\n            (LiveBuffUpkeep.CanMaintain(s,effects) || s.Kind==SlotKind.Skill && s.SkillTarget==SkillTargetKind.Party))).ToArray());\n    }\n\n    void ObserveBuffs(Options o,HotbarSnapshot bar)\n    {\n        var self=world.LocalPlayer();\n        activeEffects=world.ActiveEffects();\n        ObservePotionBuffs(o,bar);\n        buffDecisions=buffPolicy.Evaluate(world.NavigationContext(self)+$":{bar.PageBase}",bar,activeEffects,o.MaintainAreaBuffs,Environment.TickCount64);\n        buffStatus.Text=!activeEffects.Available?activeEffects.Status:buffDecisions.Count==0?"No slotted skills match the current effect catalog.":\n            string.Join("\n",buffDecisions.Select(b=>$"{b.Key}: {b.Skill} ({b.Use}) â€” {b.Status}"));\n    }\n\n    async Task<bool> TryMaintainBuff(Options o,CancellationToken token)\n    {\n        if(HasActiveFight() || combatPressure.RecentDamage(Environment.TickCount64) || PriorityGamekeeper(o)!=null)return false;\n        if(await TryUseBuffPotion(o,token))return true;\n        if(!o.MaintainAreaBuffs)return false;\n        var bar=CheckedHotbar();ObserveBuffs(o,bar);\n        var decision=buffDecisions.FirstOrDefault(b=>b.ShouldCast && HealthSkillAllowed(bar.Slot(b.Key[0]),o));\n        if(decision==null)return false;\n        var hp=world.HealthSnapshot().GetValueOrDefault(world.LocalPlayer().Id);\n        if(!hp.Known||hp.Dead)return false;\n        movement?.StopApproach();ReleaseCombatPickup();ClearRangedPending();Input.Release(preserveNearbyPickup:true);\n        var slot=bar.Slot(decision.Key[0]);\n        var attempt=Environment.TickCount64;\n        message=$"{(slot.SkillUse==SkillUseKind.Chant?"Turning on chant":"Casting AOE buff")}: {slot.Name}";\n        TraceLog.Record("area buff attempt",new{slot.Key,slot.Id,slot.Name,Mode="live effect",slot.SkillUse,TargetSelection="none",ChargeMilliseconds=slot.SkillUse==SkillUseKind.Cast?o.HealChargeMilliseconds:0});\n        priorityInterruptibleActivity=true;buffInProgress=true;\n        try\n        {\n            await Input.Key((Keys)slot.Key[0],70,token);\n            await Input.Delay(100,token);\n            var selected=CheckedHotbar().Slot(slot.Key[0]);\n            var afterKeyEffect=world.ActiveEffects().Match(slot.Name);\n            if(afterKeyEffect is{Active:true}){ObserveBuffs(o,CheckedHotbar());return true;}\n            if(selected.Id!=slot.Id || selected.Kind!=SlotKind.Skill || !selected.Ready || world.SelectedSkill()!=slot.Id)\n            {\n                buffPolicy.RecordAttempt(decision,attempt,false);\n                TraceLog.Record("area buff selection failed",new{slot.Key,slot.Id,SelectedSkill=world.SelectedSkill(),selected.Ready,selected.RemainingCooldown,selected.Locked});return true;\n            }\n            var effect=world.ActiveEffects().Match(slot.Name);\n            if(effect==null)return false;\n            if(effect.Active)return true;\n            attempt=Environment.TickCount64;\n            buffPolicy.RecordAttempt(decision,attempt);\n            if(!await CastHealthCheckedSkill(slot,o,token))return false;\n            await Input.Delay(180,token);\n            ObserveBuffs(o,CheckedHotbar());\n            TraceLog.Record("area buff activation observation",new{slot.Key,slot.Id,Decision=buffDecisions.FirstOrDefault(b=>b.Key==decision.Key && b.SkillId==decision.SkillId)});\n            return true;\n        }\n        catch {buffPolicy.RecordAttempt(decision,attempt,false);throw;}\n        finally{priorityInterruptibleActivity=false;buffInProgress=false;Input.Release(preserveNearbyPickup:true);}\n    }\n\n    void SupportPreflight()\n    {\n        if(Environment.TickCount64<nextSupportPreflight)return;\n        nextSupportPreflight=Environment.TickCount64+100;\n        if(runCharacter==null || !LocalCharacter.Same(runCharacter,world.LocalPlayer()) || runZone!=world.ActiveZone())\n            throw new OperationCanceledException("Character or zone changed during support casting.");\n        _=CheckedHotbar();\n        var hp=world.HealthSnapshot().GetValueOrDefault(runCharacter.Id);\n        if(!hp.Known||hp.Dead)throw new OperationCanceledException("Support stopped: character health unavailable or character dead.");\n        if(activeGuardOptions is {GroupMode:true,HealerMode:true} groupHealer)GroupHealerPreflight(groupHealer);\n    }\n\n    async Task<bool> RunLootJob(LootJob job,Movement drive,Vec anchor,Options options,CancellationToken token)
    {\n        // Continuous pickup already handles every live drop. Historical corpse\n        // checks must not add another 600+ ms of waiting for each chained kill.\n        if(options.AutoPickupNearbyLoot)return true;\n        navigation.BeginGoal($"loot:{job.Target.Id}:{job.Target.Generation}");\n        lootGuardPosition=job.Position; lootBeforeFight=job.ExistingDrops;\n        try\n        {\n            TraceLog.Record("target loot started",new {job.Target.Id,job.Target.DisplayName,job.Target.PriorityLootObject,job.HoldMs});\n            await CollectLoot(drive,job.Position,anchor,options,token,job.HoldMs,job.Target.PriorityLootObject,job.ExistingDrops);\n            TraceLog.Record("target loot finished",new {job.Target.Id,job.Target.DisplayName,job.Target.PriorityLootObject});\n            return true;\n        }\n        catch(EncounterInterruptedException)\n        {\n            encounterQuietSince=0;\n            TraceLog.Record("loot paused for nearby enemies",new {job.Target.Id,Enemies=encounter.Candidates.Select(e=>e.Id).ToArray()});\n            return false;\n        }\n        catch(TargetProtectionException ex) { message="Pickup skipped: "+ex.Message; TraceLog.Record("loot protection skip",new {job.Target.Id,Reason=ex.Message}); return true; }\n        catch(RouteUnavailableException ex) {message="Pickup route unavailable: "+ex.Message;TraceLog.Record("loot navigation skip",new {job.Target.Id,Reason=ex.Message});return true;}\n        finally {lootGuardPosition=null;lootBeforeFight=null;drive.StopApproach();Input.Release(preserveNearbyPickup:true);}\n    }\n\n    void RefreshGuardScene()\n    {\n        entities=world.Poll(); guardSelfId=world.LocalPlayer().Id;\n        if(activeGuardOptions?.GroupMode==true){currentParty=world.Party();ApplyPartyNames();}\n        avoidZones=Avoidance.BuildZones(avoidRules,entities,guardSelfId);\n        guardRefreshedAt=Environment.TickCount64;\n        RefreshGroupDecision();\n    }\n\n    void ReleaseCombatPickup()
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
\n    void StartNearbyPickup(Options options)
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
    {\n        var options=activeGuardOptions;\n        if(!working || !connected || options==null || deathRecoveryActive || !options.AutoPickupNearbyLoot || cancel?.IsCancellationRequested!=false || returningFromPriority)return false;
        if(RangedPullEnabled(options) && rangedPull.Active && encounter.HasEngaged)return false;\n        var self=world.LocalPlayer();\n        if(runCharacter==null || !LocalCharacter.Same(runCharacter,self) || runZone!=world.ActiveZone())\n            throw new OperationCanceledException("Character or zone changed during ground pickup.");\n        var hp=world.TargetHealth(self.Id);
        if(!hp.Known || hp.Dead)
        {
            if(options.AutoReviveAfterDeath && hp.Dead){deathRecoveryRequested=true;return false;}
            throw new InvalidOperationException("Ground pickup stopped: player health is unavailable or dead.");
        }
        long now=Environment.TickCount64;
        if(now>=nextNearbyPickupRead)
        {
            nearbyPickupSnapshot=world.Loot();
            ObserveLootTrackerDrops(options,nearbyPickupSnapshot);
            nextNearbyPickupRead=now+LootTrackerPollMilliseconds;
        }
        var decision=NearbyLootPickup.Evaluate(self.Position,(double)options.NearbyEnemyRadius,nearbyPickupSnapshot);\n        if(decision.NearbyCount!=nearbyPickupCount)\n        {\n            nearbyPickupCount=decision.NearbyCount;\n            TraceLog.Record("nearby ground loot hold changed",new {Count=nearbyPickupCount,Radius=options.NearbyEnemyRadius,HoldingE=decision.HoldLoot,Position=self.Position});\n        }\n        return decision.HoldLoot;\n    }\n    void SetCombatPickup(bool requested,HashSet<(uint,uint)> baseline,Options options,CancellationToken token)\n    {\n        if(options.AutoPickupNearbyLoot || !requested || PriorityGamekeeper(options)!=null || CombatPickup.Blocked(world.PlayerPosition(),entities,guardSelfId,world.Loot(),baseline,\n            options.AntiKillSteal,(double)options.OtherPlayerRadius)!=null) {ReleaseCombatPickup();return;}\n        combatPickupBaseline=baseline;\n        Input.Hold(Keys.E,true,token);combatPickupHeld=true;\n    }\n\n    void ProtectionPreflight()
    {
        // The death screen still requires one deliberate client input. Do not
        // let the normal combat guard reject that revive key while HP is zero.
        if(deathRecoveryActive)return;
        var o=activeGuardOptions; if (o==null) return;
        if(rangedFireTarget!=null && Input.RightButtonHeld)\n        {\n            var aimed=world.Find(rangedFireTarget.Id);\n            if(rangedPull.Phase!=RangedPullPhase.Tagging || healingRestPending || retreatRecovery!=null ||\n                aimed==null || TargetIdentity(aimed)!=TargetIdentity(rangedFireTarget) || !aimed.Position.Finite ||\n                activeHuntAnchor is not Vec home || (aimed.Position-home).Length>(double)o.HuntRadius ||\n                TargetGuardReason(aimed,world.TargetHealth(aimed.Id),world.PlayerPosition(),o)!=null)\n                ClearRangedPending();\n        }\n        if(runCharacter!=null && !LocalCharacter.Same(runCharacter,world.LocalPlayer()))throw new OperationCanceledException("Character changed during the hunt.");\n        if(runZone.HasValue && world.ActiveZone()!=runZone) throw new InvalidOperationException("Map zone changed; stopped.");\n        if(Environment.TickCount64-guardRefreshedAt>=100) RefreshGuardScene();\n        Vec position=world.PlayerPosition();\n        var playerHealth=world.TargetHealth(guardSelfId);
        if(!playerHealth.Known || playerHealth.Dead)
        {
            if(o.AutoReviveAfterDeath && playerHealth.Dead){deathRecoveryRequested=true;return;}
            throw new InvalidOperationException("Player health is unavailable or the character died; stopped.");
        }
        if(gamekeeperTransition)
        {
            var standingHealth=world.TargetHealth(guardSelfId);
            if(!standingHealth.Known || standingHealth.Dead)
            {
                if(o.AutoReviveAfterDeath && standingHealth.Dead){deathRecoveryRequested=true;return;}
                throw new InvalidOperationException("Cannot stand for Gamekeeper: player health is unavailable or dead.");
            }
            _=CheckedHotbar();return;\n        }\n        var priority=PriorityGamekeeper(o);\n        if(retreatRecovery==null && priority!=null &&\n            (lockedTarget!=null && GamekeeperPriority.ShouldYield(lockedTarget,priority) || returningFromPriority || lootGuardPosition.HasValue || priorityInterruptibleActivity))\n        {\n            pendingPriorityGamekeeper=priority;healingRestPending=false;healingWarning=null;\n            ClearRangedPending();ReleaseCombatPickup();movement?.StopApproach();Input.Release(preserveNearbyPickup:true);throw new PriorityTargetException(priority);\n        }\n        if(combatPickupHeld && (retreatRecovery!=null || combatPickupBaseline==null ||\n            CombatPickup.Blocked(position,entities,guardSelfId,world.Loot(),combatPickupBaseline,o.AntiKillSteal,(double)o.OtherPlayerRadius)!=null))\n            ReleaseCombatPickup();\n        if(retreatRecovery!=null)\n        {\n            var hp=world.TargetHealth(guardSelfId);\n            if(!hp.Known || hp.Dead)
            {
                if(o.AutoReviveAfterDeath && hp.Dead){deathRecoveryRequested=true;return;}
                throw new InvalidOperationException("Retreat stopped: player HP is unavailable or the character died.");
            }
            if(!o.GroupMode && combatPressure.Observe(hp,Environment.TickCount64) && !HasActiveFight())defensePending=true;\n            _=CheckedHotbar();\n            // A moving threat can invalidate held W between controller updates.\n            var drive=retreatDrive;\n            Vec forward=Movement.FromClientHeading(world.PlayerHeading());\n            if(drive?.CanAdvance?.Invoke(position,position+forward*.5)!=true) drive?.StopApproach();\n            return;\n        }\n        if(o.GroupMode && lockedTarget==null && groupDecision.Action!=GroupAction.Follow)movement?.StopApproach();\n        string? inside=Avoidance.BlockedPoint(position,avoidZones);\n        if (inside!=null) throw new RetreatRequiredException(inside);\n        if(!o.GroupMode)\n        {\n            if(Environment.TickCount64>=nextEngagementObservation)\n            {\n                nextEngagementObservation=Environment.TickCount64+100;\n                ObserveEncounter(o,world.HealthSnapshot(),position,world.PlayerLevel());\n                ObserveCombatPressure(o,playerHealth,position);\n            }\n            if(priority==null && encounter.HasEngaged && (defenseRepositioning || lootGuardPosition.HasValue || buffInProgress || !returningFromPriority && !rangedTagging && !(RangedPullEnabled(o) && rangedPull.Phase==RangedPullPhase.Clearing) && lockedTarget!=null && !encounter.IsEngaged(lockedTarget) &&
                !(courtesy.StartedHere(lockedTarget) && world.TargetHealth(lockedTarget.Id).Dead)))\n            {\n                ReleaseCombatPickup();movement?.StopApproach();Input.Release(preserveNearbyPickup:true);\n                throw new EngagedTargetPriorityException();\n            }\n            if(priority==null && defensePending && !HasActiveFight() &&\n                (lockedTarget!=null || lootGuardPosition.HasValue || buffInProgress))throw new RecoverUnderDamageException();\n        }\n        if (lockedTarget!=null)\n        {\n            if(rangedTagging)\n            {\n                var live=world.Find(lockedTarget.Id);\n                if(live==null || TargetIdentity(live)!=TargetIdentity(lockedTarget) || !live.Position.Finite ||\n                    !activeHuntAnchor.HasValue || (live.Position-activeHuntAnchor.Value).Length>(double)o.HuntRadius)\n                    throw new TargetProtectionException("Ranged tag target changed or left the hunt area.");\n                if(!rangedApproaching && (live.Position-position).Length>(double)o.MeleeRange)\n                    throw new RangedTargetOutOfRangeException();\n                lockedTarget=live;\n            }\n            if(priority==null && healingRestPending && !HasActiveFight() && !courtesy.StartedHere(lockedTarget))\n            {\n                ReleaseCombatPickup();movement?.StopApproach();Input.Release(preserveNearbyPickup:true);\n                throw new RecoverBeforeFreshTargetException();\n            }\n            if(priority==null && o.LeaveAreaWhenEmpty && activeExcursion is {OutsideTrip:true} excursion && !encounter.HasEngaged && !courtesy.StartedHere(lockedTarget) &&\n                Environment.TickCount64>=nextInsideTargetCheck)\n            {\n                nextInsideTargetCheck=Environment.TickCount64+100;\n                int level=world.PlayerLevel();\n                if(HuntingArea.HasApprovedInside(entities,world.HealthSnapshot(),excursion,e=>MatchesRequestedTarget(e,o,level)))\n                {\n                    ReleaseCombatPickup();movement?.StopApproach();Input.Release(preserveNearbyPickup:true);\n                    throw new ReturnToHuntingAreaException();\n                }\n            }\n            string? blocked=TargetGuardReason(lockedTarget,world.TargetHealth(lockedTarget.Id),position,o);\n            if (blocked!=null) throw new TargetProtectionException(blocked);\n        }\n        movement?.ValidateTravelStep(world);\n        if (lootGuardPosition is Vec lootPoint)\n        {\n            if(encounter.Active)\n            {\n                ObserveEncounter(o,world.HealthSnapshot(),position,world.PlayerLevel());\n                if(encounter.Candidates.Count>0 || encounter.HasUnresolvedNearby) throw new EncounterInterruptedException();\n            }\n            string? blocked=o.AutomaticRouting ? Avoidance.BlockedPoint(lootPoint,avoidZones) : Avoidance.BlockedSegment(position,lootPoint,avoidZones);\n            if (o.AntiKillSteal) blocked ??= CombatCourtesy.PlayerNear(lootPoint,entities,guardSelfId,(double)o.OtherPlayerRadius)\n                ?? CombatCourtesy.PlayerNear(position,entities,guardSelfId,(double)o.OtherPlayerRadius);\n            if (blocked!=null) throw new TargetProtectionException(blocked);\n            if (o.AntiKillSteal && lootBeforeFight!=null && groundLoot.Any(i=>lootBeforeFight.Contains((i.KeyA,i.KeyB)) && (i.Position-position).Length<=3))\n                throw new TargetProtectionException("Pre-existing drops are inside pickup range");\n        }\n    }

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
    async Task Calibrate()\n    {\n        if (!RequireHotkeys() || busy || working || !connected) return;\n        var readiness=world.CheckInputWindow();\n        if (!readiness.Allowed) { message = readiness.BlockReason!; TraceLog.Record("calibration blocked",readiness); return; }\n        try\n        {\n            CurrentOptions().Save(); working = true; settings.Enabled = false; protectionPanel.Enabled=false; automaticRouting.Enabled=false;clearNavigation.Enabled=false; connect.Enabled = false; start.Enabled=false; cancel = new(); movement = null;\n            if(world.RestSupported) await EnsurePosture(false,cancel.Token);\n            message = "Calibrating aim from live facing angleâ€¦";\n            lastCalibrationError = null;\n            bool rangedAim=CurrentOptions().Ranged && !CurrentOptions().HealerMode;\n            if(rangedAim && !world.CameraSupported)throw new InvalidOperationException("3D ranged aiming is unavailable: "+world.CameraStatus);\n            movement = rangedAim\n                ? await Movement.CalibrateRanged(world,cancel.Token,TraceLog.Record,value=>message=value)\n                : await Movement.Calibrate(world,cancel.Token,TraceLog.Record);\n            message = rangedAim ? "Horizontal and vertical aiming ready. Press F8 in game to hunt." : "Turning ready. Press F8 in game to hunt.";\n            System.Media.SystemSounds.Asterisk.Play();\n            string calibrationPath=Path.Combine(AppContext.BaseDirectory,"calibration.json");\n            if(rangedAim) RangedAimPersistence.Save(calibrationPath,world,movement);\n            else File.WriteAllText(calibrationPath,JsonSerializer.Serialize(new { TimeUtc = DateTime.UtcNow, movement.Forward, movement.RadiansPerPixel, movement.UnitsPerMs }, new JsonSerializerOptions { WriteIndented = true }));\n        }\n        catch (Exception ex) { lastCalibrationError = ex.Message; TraceLog.Record("calibration failed", new { Error = ex.Message }); Stop(ex.Message); System.Media.SystemSounds.Exclamation.Play(); }\n        finally { Input.PickupHoldProvider=null;nearbyPickupCount=0;working = false; settings.Enabled = true; protectionPanel.Enabled=true; automaticRouting.Enabled=true;clearNavigation.Enabled=true; connect.Enabled = true; start.Enabled=true; Input.Release(); cancel?.Dispose(); cancel = null; }\n    }\n    async Task RunHealer()\n    {\n        Options o=CurrentOptions();o.Save();working=true;settings.Enabled=false;protectionPanel.Enabled=false;automaticRouting.Enabled=false;clearNavigation.Enabled=false;connect.Enabled=false;start.Enabled=false;cancel=new();var token=cancel.Token;\n        buffPolicy.Restart(); playerGreeting.Reset();
        var previousAdvance=movement?.CanAdvance;\n        try\n        {\n            if(o.GroupMode)\n            {\n                if(movement==null || !world.PartySupported || string.IsNullOrWhiteSpace(o.GroupTankName))\n                    throw new InvalidOperationException("Select a tank from the live party roster and calibrate movement before starting the healbot.");\n                o.HuntRadius=o.GroupFollowLimit;o.AutoPickupNearbyLoot=false;o.PrioritizeGamekeeper=false;\n                o.ClearNearbyEnemies=false;lockedTarget=null;encounter.Reset();combatPressure.Reset();\n                movement.CanAdvance=(from,to)=>activeHuntAnchor is Vec anchor &&\n                    Targeting.BoundaryStepAllowed(from,to,anchor,(double)o.GroupFollowLimit) &&\n                    (o.AutomaticRouting?navigation.CanAdvance(from,to,avoidZones):Avoidance.BlockedSegment(from,to,avoidZones)==null);\n            }\n            nextSupportPreflight=0;nextHealAt=0;manaRecovery.Reset();\n            runCharacter=world.LocalPlayer();runZone=world.ActiveZone();activeGuardOptions=o;activeHuntAnchor=runCharacter.Position;activeHealTarget=null;lastHealingSkill=null;\n            RefreshGuardScene();\n            currentHotbar=CheckedHotbar();runHotbarPage=currentHotbar.PageBase;\n            Input.Preflight=SupportPreflight;StartNearbyPickup(o);\n            if(world.RestSupported)await EnsurePosture(false,token);\n            while(true)\n            {\n                await Input.Delay(100,token);\n                var self=world.LocalPlayer();UpdateDetectedCharacter(self);entities=world.Poll();guardSelfId=self.Id;currentParty=world.Party();ApplyPartyNames();
                if (TryGreetNearbyPlayer(o, self.Position, entities, self.Id)) { await Input.Delay(250, token); continue; }
                var health=world.HealthSnapshot();currentHotbar=CheckedHotbar();\n                if(o.GroupMode && await TryHealbotRecovery(o,token))continue;\n                if(await TryRestoreMana(o,token))continue;\n                if(o.AutoDetectHealingSkills)o.HealingSkillKeys=HealerPolicy.DetectHealingKeys(currentHotbar);\n                else o.HealingSkillKeys=HealerPolicy.AvailableHealingKeys(o.HealingSkillKeys,currentHotbar);\n                o.HealingSkillKeys=AttackKeys(o.HealingSkillKeys,currentHotbar,o.MaintainAreaBuffs);\n                activeHealTarget=HealerPolicy.Select(currentParty,self,entities,health,(double)o.PartyHealRange,o.PartyHealBelowPercent);\n                var healEffects=world.ActiveEffects();\n                var usableHealingKeys=new string(o.HealingSkillKeys.Where(key=>{\n                    var s=currentHotbar.Slot(key);var effect=healEffects.Match(s.Name);\n                    return (!o.GroupMode || activeHealTarget!=null && GroupHealerPolicy.HealingSkill(s,activeHealTarget.IsSelf(self))) && HealthSkillAllowed(s,o) && (s.SkillUse is SkillUseKind.Instance or SkillUseKind.Cast) && (effect==null || !effect.Active) &&\n                        (s.SkillTarget!=SkillTargetKind.Party || healEffects.Available && effect!=null);\n                }).ToArray());\n                activeHealTarget=HealerPolicy.Select(currentParty,self,entities,health,(double)o.PartyHealRange,o.PartyHealBelowPercent);\n                var skill=activeHealTarget==null?null:HealerPolicy.ChooseReady(usableHealingKeys,currentHotbar,healingSkillCursor);\n                if(skill==null && o.GroupMode && await FollowHealbotTank(o,token))continue;\n                if(skill==null && await TryMaintainBuff(o,token))continue;\n                if(activeHealTarget==null)\n                {\n                    movement?.StopApproach();Input.Release(preserveNearbyPickup:true);message=o.GroupMode?groupDecision.Status:currentParty.Available?"Healer waiting: party members are above the heal threshold or out of range.":"Healer waiting: character is above the heal threshold.";continue;\n                }\n                if(skill==null)\n                {\n                    Input.Release(preserveNearbyPickup:true);message=$"Healer waiting: {activeHealTarget.Member.Name} needs help, but healing skills are cooling down.";continue;\n                }\n                var targetKey=skill.SkillTarget==SkillTargetKind.Party?null:HealerPolicy.PartyTargetKey(currentParty,activeHealTarget.Member.Id);\n                if(targetKey==null && skill.SkillTarget!=SkillTargetKind.Party && !activeHealTarget.IsSelf(self))\n                {\n                    Input.Release(preserveNearbyPickup:true);message=$"Healer waiting: no safe F-key is mapped for {activeHealTarget.Member.Name}.";continue;\n                }\n                movement?.StopApproach();Input.Release(preserveNearbyPickup:true);\n                int chargeMs=skill.SkillUse==SkillUseKind.Cast?(int)o.HealChargeMilliseconds:0;\n                message=$"Healing {activeHealTarget.Member.Name} with {skill.Name} Â· {(chargeMs==0?"instant click":$"charge {chargeMs} ms")}";\n                TraceLog.Record("healer target selected",new {activeHealTarget.Member.Id,activeHealTarget.Member.Name,activeHealTarget.PartyIndex,activeHealTarget.Health,activeHealTarget.Percent,TargetKey=targetKey?.ToString(),SkillKey=skill.Key,Skill=skill.Name,ChargeMilliseconds=chargeMs});\n                healerRecipientKey=HealerPolicy.PartyTargetKey(currentParty,activeHealTarget.Member.Id);\n                healerCasting=true;\n                try\n                {\n                    if(o.GroupMode)GroupHealerPreflight(o);\n                    if(targetKey.HasValue)await Input.Key(targetKey.Value,70,token);\n                    await Input.Key((Keys)skill.Key[0],70,token);\n                    lastHealingSkill=skill.Key;\n                    if(o.GroupMode)GroupHealerPreflight(o);\n                    if(!await CastHealthCheckedSkill(skill,o,token))continue;
                }
                catch(HealerRecipientChangedException)
                {
                    movement?.StopApproach();Input.Release();activeHealTarget=null;
                    message="Healbot: recipient changed or left range; selecting again.";
                    continue;
                }
                finally{healerCasting=false;healerRecipientKey=null;}
                healingSkillCursor=(healingSkillCursor+1)%Math.Max(1,o.HealingSkillKeys.Length);\n                TraceLog.Record("healer cast finished",new {Target=activeHealTarget.Member.Name,TargetKey=targetKey?.ToString(),SkillKey=skill.Key,Skill=skill.Name,ChargeMilliseconds=chargeMs,After=world.HealthSnapshot().GetValueOrDefault(activeHealTarget.Member.Id)});\n            }\n        }\n        catch(OperationCanceledException){TraceLog.Record("healer stopped",new {Reason="Stop/focus/cancellation"});Stop("Healer stopped. Press F8 to start again.");}\n        catch(Exception ex){TraceLog.Record("healer failed",new {Error=ex.Message});Stop(ex.Message);}\n        finally{healerFollowing=false;healerCasting=false;healerRecipientKey=null;movement?.StopApproach();if(movement!=null)movement.CanAdvance=previousAdvance;Input.PickupHoldProvider=null;nearbyPickupCount=0;working=false;settings.Enabled=true;protectionPanel.Enabled=true;automaticRouting.Enabled=true;clearNavigation.Enabled=true;connect.Enabled=true;start.Enabled=true;Input.Release();Input.Preflight=null;activeHealTarget=null;lastHealingSkill=null;runCharacter=null;activeHuntAnchor=null;activeGuardOptions=null;runHotbarPage=null;runZone=null;cancel?.Dispose();cancel=null;}\n    }\n    async Task Hunt()\n    {\n        if (!RequireHotkeys() || busy || working || !connected) return;\n        if (!Input.Allowed()) { message = "Press F8 while the game is in front."; return; }\n        if (CurrentOptions().HealerMode)\n        {\n            if(CurrentOptions().GroupMode && movement==null){message="Calibrate movement before starting group healer mode.";return;}\n            await RunHealer(); return;\n        }\n        if (movement == null) { message = lastCalibrationError == null ? "Press F6 in game to calibrate movement first." : "F8 paused: " + lastCalibrationError; return; }\n        try\n        {\n            Options o = CurrentOptions(); o.Save(); working = true; settings.Enabled = false; protectionPanel.Enabled=false;automaticRouting.Enabled=false;clearNavigation.Enabled=false; connect.Enabled = false; start.Enabled=false; cancel = new(); var token = cancel.Token;\n            if(o.GroupMode)\n            {\n                if(!world.PartySupported || string.IsNullOrWhiteSpace(o.GroupTankName))throw new InvalidOperationException("Select a tank from the live party roster before starting group mode.");\n                o.ClearNearbyEnemies=false; // Group combat is bounded to the tank area; it does not pull unrelated adds or leave for loot.\n                o.HuntRadius=o.GroupFollowLimit;\n            }\n            var drive = movement;
            runCharacter=world.LocalPlayer();
            // Capture the activation point once.  Solo fixed-target combat uses
            // this immutable point as its standing location; only group mode is
            // allowed to replace the working anchor with the tank position.
            Vec activationLocation = world.PlayerPosition();
            Vec anchor = activationLocation;
            double savedHuntHeading=runCharacter.Heading;
            Vec originalActivationLocation=activationLocation;
            double originalHuntHeading=savedHuntHeading;
            SavedNavigationRoute? selectedSavedRoute=null;
            int selectedSavedRouteSlot=-1;
            int activeSavedRouteSlot=0;
            drive.TargetHeightOffset=(double)o.RangedVerticalAimOffset;
            runZone=world.ActiveZone();unreachableTargets.Clear();
            navigation.LoadSavedRoutes();
            // If the primary saved farming point is occupied when the hunt is
            // activated, choose the first free alternative instead of entering
            // another player's spot. The activation point is otherwise kept as
            // the normal anchor so existing profiles behave unchanged.
            entities=world.Poll();
            guardSelfId=runCharacter.Id;
            var primarySavedRoute=navigation.GetSavedRoute(0);
            bool RouteCompatible(SavedNavigationRoute route) =>
                (string.IsNullOrWhiteSpace(route.Character) || string.Equals(route.Character,runCharacter.Name,StringComparison.OrdinalIgnoreCase)) &&
                (route.Height<=0 || Math.Abs(route.Height-runCharacter.Height)<2);
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
                activeFarmOnArrival=route?.FarmOnArrival ?? o.FarmOnArrival;
            }
            NavigationRouteProfile CurrentRouteProfile() => new(
                runCharacter?.Name ?? o.Player,runCharacter?.Height ?? 0,activeRouteProfile is {HuntRadius:>0} ? activeRouteProfile.HuntRadius : (double)o.HuntRadius,
                activeRevivalDelaySeconds,activeFarmOnArrival,false);
            ApplyRouteProfile(primarySavedRoute is {Zone:var savedZone} && savedZone==runZone.Value && RouteCompatible(primarySavedRoute) ? primarySavedRoute : null);
            // A saved anchor is a standing location, so use a small floor
            // radius even when anti-kill-steal is configured narrowly. Prefer
            // the route's farming radius so occupancy follows the saved tab.
            double savedRouteOccupancyRadius=primarySavedRoute is {HuntRadius:>0} ? Math.Max(3,primarySavedRoute.HuntRadius) : Math.Max(3,(double)o.HuntRadius);
            if(!o.GroupMode && o.UseAlternativeHuntRoutes && primarySavedRoute is {Zone:var primaryZone} &&
                primaryZone==runZone.Value && (activationLocation-primarySavedRoute.Anchor).Length<=20 &&
                RouteCompatible(primarySavedRoute) &&
                CombatCourtesy.PlayerNear(primarySavedRoute.Anchor,entities,runCharacter.Id,savedRouteOccupancyRadius)!=null)
            {
                foreach(var (slot,routeCandidate) in navigation.SavedRoutesForZone(runZone.Value).Where(item=>item.Slot>0))
                {
                    if((routeCandidate.Anchor-primarySavedRoute.Anchor).Length<=.5)continue;
                    if(!RouteCompatible(routeCandidate))continue;
                    double candidateRadius=routeCandidate.HuntRadius>0 ? Math.Max(3,routeCandidate.HuntRadius) : Math.Max(3,(double)o.HuntRadius);
                    if(CombatCourtesy.PlayerNear(routeCandidate.Anchor,entities,runCharacter.Id,candidateRadius)!=null)continue;
                    selectedSavedRoute=routeCandidate;selectedSavedRouteSlot=slot;break;
                }
                if(selectedSavedRoute!=null)
                {
                    ApplyRouteProfile(selectedSavedRoute);
                    activationLocation=selectedSavedRoute.Anchor;
                    anchor=activationLocation;
                    savedHuntHeading=selectedSavedRoute.Heading;
                    activeSavedRouteSlot=selectedSavedRouteSlot;
                    message=$"Primary hunt spot is occupied; using alternative route {selectedSavedRouteSlot}.";
                    TraceLog.Record("alternative hunt route selected",new{Slot=selectedSavedRouteSlot,Location=selectedSavedRoute.Anchor,Heading=selectedSavedRoute.Heading,Character=selectedSavedRoute.Character,Height=selectedSavedRoute.Height,HuntRadius=selectedSavedRoute.HuntRadius,Primary=primarySavedRoute.Anchor,PlayerNearPrimary=true,OccupancyRadius=savedRouteOccupancyRadius,Zone=runZone});
                }
                else
                {
                    message="Primary hunt spot is occupied; no free alternative route is available.";
                    TraceLog.Record("all alternative hunt routes occupied",new{Primary=primarySavedRoute.Anchor,OccupancyRadius=savedRouteOccupancyRadius,Zone=runZone});
                }
            }
            activeHuntAnchor=activationLocation;
            activeExcursion=o.GroupMode ? null : new HuntExcursion(anchor,(double)o.HuntRadius);
            navigation.Observe(world.NavigationContext(runCharacter),runCharacter.Position,runCharacter.Height);
            navigation.BeginRecording(anchor);
            pendingPriorityGamekeeper=null;gamekeeperTransition=false;priorityInterruptibleActivity=false;returningFromPriority=false;deathRecoveryActive=false;
            rangedPull.Reset(); rangedTagging=false;
            combatPressure.Reset();defensePending=false;defenseRepositioning=false;defenseStep=null;inferredDefense=null;buffInProgress=false;nextEngagementObservation=0;\n            courtesy.Reset(); playerGreeting.Reset(); encounter.Reset(); deferredLoot.Clear(); encounterExistingDrops=null; encounterAnchor=null; encounterHasAttack=false; encounterQuietSince=0; encounterUnknownSince=0;
            targetSearch=null;lastTargetWait="";\n            activeGuardOptions=o; RefreshGuardScene();\n            combatPressure.Observe(world.TargetHealth(guardSelfId),Environment.TickCount64);\n            if(world.RestSupported) await EnsurePosture(false,token);\n            Input.Preflight=ProtectionPreflight;StartNearbyPickup(o);\n            activeMovementBoundary=(double)o.HuntRadius;healingWarning=null;healingRestPending=false;healingRest=null;\n            drive.CanAdvance=(from,to)=>Targeting.BoundaryStepAllowed(from,to,anchor,activeMovementBoundary) &&
                (o.AutomaticRouting ? navigation.CanAdvance(from,to,avoidZones) : Avoidance.BlockedSegment(from,to,avoidZones)==null);
            nextHealAt = 0;
            recoveryCursor = 0;\n            var startingBar = world.Hotbar(); runHotbarPage = startingBar.PageBase;\n            string configuredKeys=o.SkillKeys; o.SkillKeys=AttackKeys(o.AutoDetectSkills ? SkillRotation.DetectKeys(startingBar) : SkillRotation.AvailableKeys(configuredKeys,startingBar),startingBar,o.MaintainAreaBuffs);\n            if(o.SkillKeys!=configuredKeys)TraceLog.Record("unavailable attack slots skipped",new {Configured=configuredKeys,Using=o.SkillKeys,BasicAttackOnly=o.SkillKeys.Length==0});\n             TraceLog.Record("hunt started", new { Anchor = activationLocation, ActivationLocation = activationLocation, o.HuntRadius,o.LeaveAreaWhenEmpty,o.Player, o.Target, o.SkillKeys, o.LootHoldMs, PriorityLootObjects = true, o.AntiKillSteal, o.OtherPlayerRadius, o.AvoidNames });
            var skillDue = o.SkillKeys.ToDictionary(c => c, _ => 0L);
            int skillCursor = 0; bool gamekeeperExcursion=false;
            // Offensive combat skills are deliberately infrequent.  A cast is
            // permitted only for a wounded five-target pack; this timer is
            // shared across target switches so a retarget cannot bypass the
            // five-second spacing requirement.
            long nextCombatSkillAt=0;
            double responseRadius=Targeting.ResponseRadius((double)o.HuntRadius,(double)o.GamekeeperResponseRadius);
             bool gamekeeperReturnPending=false;
             bool gamekeeperDefeated=false;
             bool gamekeeperReturnRouting=false;
             long nextGamekeeperReturnTrace=0;
              Vec gamekeeperReturnLocation=anchor;
              double gamekeeperReturnHeading=savedHuntHeading;
              bool stationaryAssistReturnPending=false;
              long deathObservedAt=0;
              long deathRecoveryReadyAt=0;
              bool deathRecoveryRunning=false;
            async Task RestoreSavedHuntFacing(CancellationToken restoreToken)
            {
                if(!double.IsFinite(gamekeeperReturnHeading))return;
                Vec desiredForward=Movement.FromClientHeading(gamekeeperReturnHeading);
                for(int attempt=0;attempt<100;attempt++)
                {
                    if(await drive.Face(world,desiredForward,restoreToken))
                    {
                        TraceLog.Record("restored saved hunt facing",new {Heading=gamekeeperReturnHeading,Position=world.PlayerPosition(),Attempts=attempt+1,Zone=runZone});
                        return;
                    }
                }
                throw new TurnUnresponsiveException(world.PlayerPosition(),desiredForward);
            }
            async Task MoveToSelectedHuntAnchor(CancellationToken routeToken)
            {
                if(selectedSavedRoute is null || selectedSavedRouteSlot<1)return;
                Vec current=world.PlayerPosition();
                if((current-anchor).Length<=2.5)return;
                ReleaseCombatPickup();Input.HoldMouse(false,false,routeToken);Input.PickupHoldProvider=null;drive.StopApproach();
                double routeBoundary=Math.Max((double)o.HuntRadius,
                    Math.Max((current-anchor).Length+2,navigation.SavedRouteRadius(selectedSavedRouteSlot,anchor)+2));
                double previousBoundary=activeMovementBoundary;
                activeMovementBoundary=routeBoundary;
                navigation.BeginGoal($"alternative hunt route {selectedSavedRouteSlot}");
                message=$"Moving to alternative hunt route {selectedSavedRouteSlot}";
                try
                {
                    bool usedSavedRoute=false;
                    if(o.UseSavedRecoveryRoute && navigation.TryGetRouteToSavedAnchor(runZone!.Value,current,selectedSavedRouteSlot,out var waypoints))
                    {
                        usedSavedRoute=true;
                        foreach(var waypoint in waypoints)
                        {
                            if((world.PlayerPosition()-waypoint).Length<=.6)continue;
                            await NavigateTo(drive,waypoint,anchor,o,routeToken,boundaryRadius:routeBoundary);
                        }
                    }
                    await NavigateTo(drive,anchor,anchor,o,routeToken,boundaryRadius:routeBoundary);
                    drive.StopApproach();
                    await RestoreSavedHuntFacing(routeToken);
                    TraceLog.Record("alternative hunt route reached",new{Slot=selectedSavedRouteSlot,Location=anchor,Heading=savedHuntHeading,UsedSavedRoute=usedSavedRoute,RouteBoundary=routeBoundary,Zone=runZone});
                }
                catch(RouteUnavailableException ex)
                {
                    drive.StopApproach();
                    TraceLog.Record("alternative hunt route unavailable",new{Slot=selectedSavedRouteSlot,Reason=ex.Message,Location=anchor,Zone=runZone});
                    activationLocation=originalActivationLocation;anchor=originalActivationLocation;savedHuntHeading=originalHuntHeading;
                    activeHuntAnchor=anchor;activeExcursion=o.GroupMode?null:new HuntExcursion(anchor,(double)o.HuntRadius);
                    selectedSavedRoute=null;selectedSavedRouteSlot=-1;activeSavedRouteSlot=0;
                    ApplyRouteProfile(primarySavedRoute is {Zone:var fallbackZone} && fallbackZone==runZone.Value && RouteCompatible(primarySavedRoute) ? primarySavedRoute : null);
                    message="Alternative route was blocked; using the activation point.";
                }
                finally
                {
                    activeMovementBoundary=previousBoundary;StartNearbyPickup(o);
                }
            }
            if(selectedSavedRoute!=null)
            {
                await MoveToSelectedHuntAnchor(token);
                if(!activeFarmOnArrival)
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
                Vec current=world.PlayerPosition();
                if((current-anchor).Length>1.5)
                {
                    ReleaseCombatPickup();
                    Input.HoldMouse(false,false,returnToken);
                    message="Returning to saved hunt point after loot";
                    navigation.BeginGoal("return to saved hunt point after loot");
                    returningFromPriority=true;
                    try { await NavigateTo(drive,anchor,anchor,o,returnToken,boundaryRadius:activeCompletionBoundary); }
                    finally { returningFromPriority=false; }
                }
                drive.StopApproach();
                await RestoreSavedHuntFacing(returnToken);
                TraceLog.Record("returned to saved hunt point after loot",new {Position=world.PlayerPosition(),Location=anchor,Heading=gamekeeperReturnHeading,Zone=runZone});
            }
            async Task<bool> ReturnToSavedHuntPointAfterStationaryAssist(CancellationToken returnToken)
            {
                const double arrivalTolerance=.15;
                ReleaseCombatPickup();Input.HoldMouse(false,false,returnToken);drive.StopApproach();
                message="Returning to saved hunt point after stationary melee assist";
                navigation.BeginGoal("return after stationary melee assist");
                returningFromPriority=true;
                bool arrived=false;
                try
                {
                    for(int attempt=0;attempt<100;attempt++)
                    {
                        Vec current=world.PlayerPosition();
                        if((current-anchor).Length<=arrivalTolerance){arrived=true;break;}
                        try
                        {
                            await NavigateTo(drive,anchor,anchor,o,returnToken,
                                boundaryRadius:Math.Max((double)o.HuntRadius,activeCompletionBoundary));
                        }
                        catch(RouteUnavailableException ex)
                        {
                            drive.StopApproach();
                            TraceLog.Record("stationary assist return route retry",new {Reason=ex.Message,Position=current,Anchor=anchor});
                        }
                        catch(MovementBlockedException ex)
                        {
                            drive.StopApproach();
                            TraceLog.Record("stationary assist return blocked",new {Reason=ex.Message,Position=ex.Position,Anchor=anchor});
                        }
                        await Input.Delay(40,returnToken);
                    }
                    arrived=(world.PlayerPosition()-anchor).Length<=arrivalTolerance;
                    if(!arrived)
                    {
                        drive.StopApproach();
                        TraceLog.Record("stationary assist return still pending",new {Position=world.PlayerPosition(),Anchor=anchor});
                        return false;
                    }
                    drive.StopApproach();
                    await RestoreSavedHuntFacing(returnToken);
                    TraceLog.Record("returned to saved hunt point after stationary melee assist",new {Position=world.PlayerPosition(),Location=anchor,Heading=gamekeeperReturnHeading,Zone=runZone});
                    return true;
                }
                finally { returningFromPriority=false; if(!arrived)drive.StopApproach(); }
            }
            async Task ReturnToSavedHuntPointAfterDeath(CancellationToken returnToken)
            {
                Vec current=world.PlayerPosition();
                double routeBoundary=Math.Max((double)o.HuntRadius,
                    Math.Max((current-anchor).Length+2,navigation.SavedRouteRadius(activeSavedRouteSlot,anchor)+2));
                double previousBoundary=activeMovementBoundary;
                activeMovementBoundary=routeBoundary;
                returningFromPriority=true;
                navigation.BeginGoal("return to saved hunt anchor after death");
                bool usedSavedRoute=false;
                try
                {
                    message="Returning to saved hunt anchor after death";
                    if(o.UseSavedRecoveryRoute && navigation.TryGetRecoveryRoute(navigationZone,current,anchor,out var waypoints))
                    {
                        usedSavedRoute=true;
                        foreach(var waypoint in waypoints)
                        {
                            if((world.PlayerPosition()-waypoint).Length<=.6)continue;
                            await NavigateTo(drive,waypoint,anchor,o,returnToken,boundaryRadius:routeBoundary);
                        }
                    }
                    await NavigateTo(drive,anchor,anchor,o,returnToken,boundaryRadius:routeBoundary);
                    drive.StopApproach();
                    await RestoreSavedHuntFacing(returnToken);
                    message=usedSavedRoute?"Returned to saved anchor using the recorded route":"Returned to saved anchor";
                    TraceLog.Record("returned to saved hunt anchor after death",new{Position=world.PlayerPosition(),Location=anchor,Heading=gamekeeperReturnHeading,Zone=runZone,UsedSavedRoute=usedSavedRoute,RouteBoundary=routeBoundary});
                }
                finally
                {
                    returningFromPriority=false;activeMovementBoundary=previousBoundary;drive.StopApproach();
                }
            }
            static Keys ParseReviveKey(string value)
            {
                return Enum.TryParse<Keys>(value,true,out var key) && key is not (Keys.Escape or Keys.F8 or Keys.F9) ? key : Keys.R;
            }
            async Task<bool> TryRecoverAfterDeath(Movement recoverDrive,Vec recoverAnchor,Options options,CancellationToken recoveryToken)
            {
                if(!options.AutoReviveAfterDeath || deathRecoveryRunning)return false;
                Entity self;Health hp;
                try{self=world.LocalPlayer();hp=world.TargetHealth(self.Id);}
                catch
                {
                    // Some clients briefly hide the local entity while the
                    // death screen is being built. Preserve the request and
                    // retry instead of allowing the normal hunt loop to move
                    // on with combat input still released.
                    if(deathRecoveryRequested){await Task.Delay(100,recoveryToken);return true;}
                    return false;
                }
                if(!hp.Known)
                {
                    if(deathRecoveryRequested){await Task.Delay(100,recoveryToken);return true;}
                    return false;
                }
                if(!hp.Dead)
                {
                    deathObservedAt=0;deathRecoveryReadyAt=0;deathRecoveryRequested=false;return false;
                }
                deathRecoveryActive=true;deathRecoveryRequested=true;
                ReleaseCombatPickup();recoverDrive.StopApproach();Input.Release(preserveNearbyPickup:true);Input.PickupHoldProvider=null;
                long now=Environment.TickCount64;
                if(deathObservedAt==0)
                {
                    deathObservedAt=now;
                    deathRecoveryReadyAt=now+activeRevivalDelaySeconds*1000L;
                    bool routeSaved=navigation.SaveCurrentRoute(navigationZone,recoverAnchor,gamekeeperReturnHeading,activeSavedRouteSlot,profile:CurrentRouteProfile());
                    message=routeSaved?"Death detected. Saved the current navigation route; preparing revive":"Death detected. Preparing revive at the saved anchor";
                    TraceLog.Record("character death detected",new{Position=world.PlayerPosition(),Anchor=recoverAnchor,Zone=runZone,RouteSaved=routeSaved});
                    await Input.Delay(100,recoveryToken);
                    return true;
                }
                if(now-deathObservedAt<500)
                {
                    message="Death detected. Waiting for the revive screen";
                    await Input.Delay(100,recoveryToken);
                    return true;
                }
                if(now<deathRecoveryReadyAt)
                {
                    message=$"Death detected. Waiting {Math.Ceiling((deathRecoveryReadyAt-now)/1000d):0}s before revive";
                    await Input.Delay(Math.Min(250,Math.Max(50,(int)(deathRecoveryReadyAt-now))),recoveryToken);
                    return true;
                }
                deathRecoveryRunning=true;
                try
                {
                    Keys reviveKey=ParseReviveKey(options.ReviveKey);
                    bool revived=false;
                    for(int attempt=0;attempt<3 && !revived;attempt++)
                    {
                        message=$"Reviving character (attempt {attempt+1}/3)";
                        TraceLog.Record("revive key input",new{Key=reviveKey.ToString(),Attempt=attempt+1,Position=world.PlayerPosition()});
                        await Input.Key(reviveKey,100,recoveryToken);
                        await Input.Delay(450,recoveryToken);
                        try{self=world.LocalPlayer();hp=world.TargetHealth(self.Id);revived=hp.Known&&!hp.Dead&&hp.Current>0;}catch{revived=false;}
                        if(!revived && reviveKey!=Keys.Enter)
                        {
                            TraceLog.Record("revive fallback input",new{Key=Keys.Enter.ToString(),Attempt=attempt+1});
                            await Input.Key(Keys.Enter,100,recoveryToken);
                            await Input.Delay(450,recoveryToken);
                            try{self=world.LocalPlayer();hp=world.TargetHealth(self.Id);revived=hp.Known&&!hp.Dead&&hp.Current>0;}catch{revived=false;}
                        }
                    }
                    if(!revived)throw new InvalidOperationException("The configured revive key did not restore the character. Check the death-screen key in Death recovery settings.");
                    self=world.LocalPlayer();
                    runCharacter=self;guardSelfId=self.Id;runZone=world.ActiveZone();navigationZone=runZone.Value;navigationPosition=self.Position;
                    navigation.Observe(world.NavigationContext(self),self.Position,self.Height);
                    encounter.Reset();lockedTarget=null;deferredLoot.Clear();encounterExistingDrops=null;encounterAnchor=null;encounterHasAttack=false;encounterQuietSince=0;encounterUnknownSince=0;
                    pendingPriorityGamekeeper=null;gamekeeperReturnPending=false;gamekeeperDefeated=false;gamekeeperReturnRouting=false;gamekeeperExcursion=false;completionReturnPending=false;
                    rangedPull.Reset();rangedTagging=false;defensePending=false;defenseRepositioning=false;defenseStep=null;inferredDefense=null;healingRestPending=false;healingWarning=null;
                    deathObservedAt=0;deathRecoveryReadyAt=0;deathRecoveryRequested=false;deathRecoveryActive=false;StartNearbyPickup(options);
                    TraceLog.Record("character revived",new{Position=self.Position,Anchor=recoverAnchor,Zone=runZone,HP=hp});
                    await ReturnToSavedHuntPointAfterDeath(recoveryToken);
                    return true;
                }
                finally
                {
                    deathRecoveryActive=false;deathRecoveryRunning=false;
                    if(deathObservedAt!=0)StartNearbyPickup(options);
                }
            }
            void RememberGamekeeperReturn(Vec current)
            {
                if(o.GroupMode || !o.ReturnToHuntLocationAfterGamekeeper || gamekeeperReturnPending)return;
                gamekeeperReturnPending=true;gamekeeperDefeated=false;gamekeeperReturnLocation=activationLocation;
                TraceLog.Record("saved hunt location for Gamekeeper return",new {Location=gamekeeperReturnLocation,Heading=gamekeeperReturnHeading,Current=current,Zone=runZone});
            }
            completionReturnPending=false;
            activeCompletionBoundary=Targeting.CompletionRadius((double)o.HuntRadius,(double)o.NearbyEnemyRadius,(double)o.MeleeRange);\n            while (true)
            {
                try
                {
                if(await TryRecoverAfterDeath(drive,anchor,o,token))continue;
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
                if (await TryHeal(drive, o, token)) continue;
                if (deathRecoveryRequested)continue;
                if (await TryRestoreMana(o,token)) continue;
                if (deathRecoveryRequested)continue;
                if(gamekeeper==null)\n                {\n                    if(defensePending && !HasActiveFight())\n                    {\n                        await RepositionUnderPressure(drive,anchor,o,token);\n                        continue;\n                    }\n                    if(!(RangedPullEnabled(o) && rangedPull.Phase==RangedPullPhase.Tagging) && await TryMaintainBuff(o,token))continue;
                    if(deathRecoveryRequested)continue;
                }\n                var pos = world.PlayerPosition();
                entities = world.Poll();
                if (TryGreetNearbyPlayer(o, pos, entities, guardSelfId)) { await Input.Delay(250, token); continue; }
                long now = Environment.TickCount64;
                if(now>=nextLootTrackerRead)
                {
                    nextLootTrackerRead=now+LootTrackerPollMilliseconds;
                    ObserveLootTrackerDrops(o,world.Loot());
                }
                int level = world.PlayerLevel();
                RefreshGuardScene();\n                var health = world.HealthSnapshot();\n                ObserveEncounter(o,health,pos,level);\n                gamekeeper=PriorityGamekeeper(o);
                if(gamekeeper!=null)
                {
                    RememberGamekeeperReturn(pos);
                    pendingPriorityGamekeeper=gamekeeper;healingRestPending=false;healingWarning=null;
                    activeCompletionBoundary=Math.Max(activeCompletionBoundary,Targeting.CompletionRadius(responseRadius,(double)o.NearbyEnemyRadius,(double)o.MeleeRange));\n                }\n                if(!o.GroupMode)\n                {\n                    if(o.LeaveAreaWhenEmpty && (pos-anchor).Length>(double)o.HuntRadius)completionReturnPending=true;\n                    if(encounter.HasEngaged)\n                    {\n                        double engagedBase=encounter.EngagedCandidates.Select(e=>BaseTargetRadius(e,o))\n                            .DefaultIfEmpty(gamekeeperExcursion?responseRadius:(double)o.HuntRadius).Max();\n                        activeCompletionBoundary=Math.Max(activeCompletionBoundary,Targeting.CompletionRadius(engagedBase,(double)o.NearbyEnemyRadius,(double)o.MeleeRange));\n                        if((pos-anchor).Length>(double)o.HuntRadius || encounter.EngagedCandidates.Any(e=>(e.Position-anchor).Length>(double)o.HuntRadius))completionReturnPending=true;\n                    }\n                    double playerBoundary=gamekeeper!=null || encounter.HasEngaged || completionReturnPending || o.LeaveAreaWhenEmpty && encounter.Active ? activeCompletionBoundary : gamekeeperExcursion ? responseRadius : (double)o.HuntRadius;\n                    if((pos-anchor).Length>playerBoundary+2)throw new InvalidOperationException("Stopped beyond the combat completion boundary.");\n                    if(gamekeeper==null && completionReturnPending && !encounter.HasEngaged &&\n                        (!o.LeaveAreaWhenEmpty || !encounter.Active && deferredLoot.Count==0))\n                    {\n                        if((pos-anchor).Length>(double)o.HuntRadius-1)\n                        {\n                            ReleaseCombatPickup();Input.HoldMouse(false,false,token);\n                            message="Returning inside the original hunting area before choosing another targetâ€¦";\n                            navigation.BeginGoal("return after engaged fight");returningFromPriority=true;\n                            try {await NavigateTo(drive,anchor,anchor,o,token,boundaryRadius:activeCompletionBoundary);}\n                            finally {returningFromPriority=false;}\n                            continue;\n                        }\n                        if(activeExcursion is {OutsideTrip:true} trip && !trip.TryCompleteReturn(pos,encounter.HasEngaged))\n                            throw new InvalidOperationException("Outside trip cannot finish before returning inside the original hunting area.");\n                        drive.StopApproach();completionReturnPending=false;gamekeeperExcursion=false;\n                        TraceLog.Record("returned to original hunting area",new {Position=pos,Anchor=anchor,o.HuntRadius});\n                        activeCompletionBoundary=Targeting.CompletionRadius((double)o.HuntRadius,(double)o.NearbyEnemyRadius,(double)o.MeleeRange);\n                    }\n                    if(!encounter.HasEngaged && healingWarning!=null && await TryHeal(drive,o,token))continue;
                }
                // Complete any return that became observable only after the
                // refreshed world snapshot. This remains before ranged pulls
                // and engaged-target selection.
                if(gamekeeper==null && gamekeeperReturnPending && gamekeeperDefeated && await ReturnAfterGamekeeper(token))continue;
                if(gamekeeper==null && await RunRangedPullStep(drive,anchor,o,health,pos,level,skillDue,token))continue;
                Entity? target=o.GroupMode?gamekeeper:GamekeeperPriority.ChooseFirst(encounter,gamekeeper,()=>null);\n                if(gamekeeper==null && RangedPullEnabled(o) && rangedPull.Phase==RangedPullPhase.Clearing)\n                {\n                    // Confirmed Firing tags define the pack lifetime. Within\n                    // that lifetime, use the ordinary melee candidate set so\n                    // nearby engaged or eligible monsters are cleared like\n                    // melee-only combat instead of waiting on one exact tag.\n                    var meleeFocus=RangedPull.ChooseMeleeCluster(RangedNearbyTargets(o,health,pos,level),pos);\n                    target=meleeFocus?.Target;\n                    if(meleeFocus is { } focus)TraceLog.Record("pack melee focus",new {focus.Target.Id,focus.Target.DisplayName,focus.Density,Direction=focus.Target.Position-pos});\n                }\n                if(!o.GroupMode && RangedPullEnabled(o) && rangedPull.Phase==RangedPullPhase.Clearing && target==null)\n                {\n                    ReleaseCombatPickup();drive.StopApproach();Input.HoldMouse(false,false,token);\n                    message=$"Melee mode: waiting for a nearby combat target to enter {o.RangedMeleeAttackRange:0.#}-unit attack range.";\n                    await Input.Delay(100,token);continue;\n                }\n                if(encounter.HasEngaged && target==null)
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
                if(target!=null && encounter.IsEngaged(target)){encounterQuietSince=0;encounterUnknownSince=0;}\n                if(!o.GroupMode && !o.LeaveAreaWhenEmpty && !encounter.HasEngaged && gamekeeperExcursion && target==null)\n                {\n                    if((pos-anchor).Length>2)\n                    {\n                        ReleaseCombatPickup();Input.HoldMouse(false,false,token);\n                        message="Returning to hunting area after Gamekeeper";\n                        navigation.BeginGoal("return after Gamekeeper");\n                        returningFromPriority=true;\n                        try {await NavigateTo(drive,anchor,anchor,o,token,boundaryRadius:responseRadius);}\n                        finally {returningFromPriority=false;}\n                        continue;\n                    }\n                    drive.StopApproach();gamekeeperExcursion=false;\n                }\n                if(o.GroupMode && gamekeeper==null)\n                {\n                    var decision=groupDecision;\n                    if(decision.Tank!=null)anchor=decision.Tank.Position;\n                    message=decision.Status;\n                    if(decision.Action==GroupAction.Follow && decision.Destination is Vec followGoal && decision.Tank!=null)\n                    {\n                        Input.HoldMouse(false,false,token);\n                        navigation.BeginGoal($"follow:{decision.Tank.Id}:{decision.Tank.Generation}");\n                        try{await NavigateTo(drive,followGoal,anchor,o,token);}\n                        catch(RouteUnavailableException ex){drive.StopApproach();message="Tank follow route blocked: "+ex.Message;await Input.Delay(500,token);}\n                        continue;\n                    }\n                    drive.StopApproach();\n                    if(decision.Action!=GroupAction.Attack || decision.Target==null){Input.Release(preserveNearbyPickup:true);await Input.Delay(150,token);continue;}\n                    target=decision.Target;\n                }\n                if(encounter.Active && target==null)
                {\n                    target=healingRestPending || o.LeaveAreaWhenEmpty && completionReturnPending ? null : Targeting.ChooseEncounter(entities.Where(e=>TargetGuardReason(e,health.GetValueOrDefault(e.Id),pos,o)==null),\n                        encounter.Candidates,health,pos,anchor,o.LeaveAreaWhenEmpty ? (double)o.HuntRadius : responseRadius,o.PrioritizeGamekeeper,o.PrioritizeBreakables);\n                    if(target!=null) { encounterQuietSince=0; encounterUnknownSince=0; }\n                    else if(encounter.HasUnresolvedNearby)\n                    {\n                        if(encounterUnknownSince==0) encounterUnknownSince=Environment.TickCount64;\n                        if(Environment.TickCount64-encounterUnknownSince>1500) throw new InvalidOperationException("Nearby enemy HP is unavailable; pickup paused and hunt stopped.");\n                        encounterQuietSince=0; message="Waiting for nearby enemy HP before pickupâ€¦";\n                        await Input.Delay(100,token); continue;\n                    }\n                    else\n                    {\n                        encounterUnknownSince=0;\n                        if(encounterQuietSince==0) encounterQuietSince=Environment.TickCount64;\n                        if(Environment.TickCount64-encounterQuietSince<500) { message="Checking that nearby enemies are clearâ€¦"; await Input.Delay(100,token); continue; }\n                        if(deferredLoot.Count>0)
                        {
                            if(await RunLootJob(deferredLoot.Peek(),drive,anchor,o,token))
                            {
                                deferredLoot.Dequeue();
                                await ReturnToSavedHuntPointAfterLoot(token);
                            }
                            continue;
                        }
                        TraceLog.Record("encounter cleared",new {Position=pos});\n                        encounter.Reset(); rangedPull.Reset(); encounterExistingDrops=null; encounterAnchor=null; encounterHasAttack=false; encounterQuietSince=0;\n                        if(!completionReturnPending)activeCompletionBoundary=Targeting.CompletionRadius((double)o.HuntRadius,(double)o.NearbyEnemyRadius,(double)o.MeleeRange);\n                        if(RangedPullEnabled(o))continue;\n                        if(o.LeaveAreaWhenEmpty || healingRestPending)continue;\n                    }\n                }
                if(target==null && !o.GroupMode && !encounter.Active && !healingRestPending &&
                    await GuideToTreasureChest(drive,anchor,o,token))continue;
                if(target==null && healingRestPending && !encounter.HasEngaged){await Input.Delay(100,token);continue;}
                if(!o.GroupMode)target ??= o.LeaveAreaWhenEmpty ? HuntingArea.Choose(entities,health,pos,activeExcursion!,\n                    e=>MatchesRequestedTarget(e,o,level),(e,hp)=>TargetGuardReason(e,hp,pos,o)==null,e=>Targeting.PriorityRank(e,o.PrioritizeGamekeeper,o.PrioritizeBreakables),o.PrioritizeBreakables) :\n                    Targeting.Choose(entities.Where(e=>TargetGuardReason(e,health.GetValueOrDefault(e.Id),pos,o)==null), health, pos, anchor, (double)o.HuntRadius, e => world.Difficulty(e, level), o.Target, o.AllowedDifficulties,prioritizeGamekeeper:o.PrioritizeGamekeeper,prioritizeBreakables:o.PrioritizeBreakables);\n                if (target == null)\n                {\n                    drive.StopApproach();Input.HoldMouse(false,false,token);\n                    bool insideApproval=o.LeaveAreaWhenEmpty && !o.GroupMode && HuntingArea.HasApprovedInside(entities,health,activeExcursion!,e=>MatchesRequestedTarget(e,o,level));\n                    double searchRadius=o.LeaveAreaWhenEmpty && !o.GroupMode && !insideApproval ? activeExcursion!.OutsideSearchRadius : (double)o.HuntRadius;\n                    var diagnosticEntities=insideApproval ? entities.Where(e=>(e.Position-anchor).Length<=(double)o.HuntRadius) : entities;\n                    targetSearch=o.GroupMode ? null : TargetSearch.Explain(diagnosticEntities,health,pos,anchor,searchRadius,o.LeaveAreaWhenEmpty ? searchRadius : responseRadius,\n                        e=>world.Difficulty(e,level),o.Target,o.AllowedDifficulties,o.PrioritizeGamekeeper,(e,hp)=>TargetGuardReason(e,hp,pos,o));\n                    message=targetSearch?.Message ?? "Waiting: "+groupDecision.Status;\n                    if(lastTargetWait!=message)\n                    {\n                        lastTargetWait=message;\n                        TraceLog.Record("target search waiting",new {Position=pos,Anchor=anchor,o.HuntRadius,Report=targetSearch,Status=message});\n                    }\n                    await Input.Delay(500,token);continue;\n                }\n                targetSearch=null;lastTargetWait="";\n                if(o.LeaveAreaWhenEmpty && !o.GroupMode && !(o.PrioritizeGamekeeper && Targeting.IsGamekeeper(target)) && !encounter.IsEngaged(target) && (target.Position-anchor).Length>(double)o.HuntRadius)\n                {\n                    activeExcursion!.BeginOutsideTrip(target,(double)o.NearbyEnemyRadius,(double)o.MeleeRange);\n                    nextInsideTargetCheck=0;\n                    completionReturnPending=true;activeCompletionBoundary=Math.Max(activeCompletionBoundary,activeExcursion.MovementBoundary);\n                    TraceLog.Record("outside trip started; home area empty",new {Anchor=anchor,o.HuntRadius,activeExcursion.OutsideSearchRadius,Target=target.Id,target.Position});\n                }\n                TraceLog.Record("target selected", new { target.Id, target.Name, target.DisplayName, target.PriorityLootObject, target.Position, Distance = (target.Position - pos).Length });\n                if(!o.GroupMode && o.PrioritizeGamekeeper && Targeting.IsGamekeeper(target) && (target.Position-anchor).Length>(double)o.HuntRadius)\n                {gamekeeperExcursion=true;completionReturnPending=true;}\n                lockedTarget = target;\n                if(o.PrioritizeGamekeeper && Targeting.IsGamekeeper(target))\n                {\n                    if(o.GroupMode && activeHuntAnchor.HasValue)anchor=activeHuntAnchor.Value;\n                    await StandForGamekeeper(token);\n                }\n                navigation.BeginGoal($"target:{target.Id}:{target.Generation}:{target.Address}");\n                drive.ResetTurnResponse();\n                int turnRecoveryAttempts=0;\n                int? lastCombatHp=null;\n                long approachStarted = now, combatStart = 0; Vec lastTargetPosition = target.Position; int missingHealth = 0;
                long stationaryAttackHeldAt=0; int stationaryAttackBaselineHp=-1;
                bool stationaryAssistUsed=false,stationaryAssistActive=false; Vec stationaryAssistGoal=default;
                long targetMissingSince = 0;
                double bodyAllowance=0; int? bodyProbeHp=null;long bodyProbeAt=0;\n                long nextPriorityCheck = 0;\n                bool collectAfterTarget = false;\n                var existingDrops=encounterExistingDrops ?? world.Loot().Select(i=>(i.KeyA,i.KeyB)).ToHashSet();\n                try\n                {\n                while (true)
                {
                    token.ThrowIfCancellationRequested();
                    if(deathRecoveryRequested)
                    {
                        ReleaseCombatPickup();drive.StopApproach();Input.Release(preserveNearbyPickup:true);lockedTarget=null;
                        break;
                    }
                    if(o.GroupMode && !(o.PrioritizeGamekeeper && Targeting.IsGamekeeper(target)))
                    {\n                        if(Environment.TickCount64-guardRefreshedAt>=100)RefreshGuardScene();\n                        if(groupDecision.Tank!=null)anchor=groupDecision.Tank.Position;\n                    }\n                    if (await TryHeal(drive, o, token)) continue;
                    if(deathRecoveryRequested)
                    {
                        ReleaseCombatPickup();drive.StopApproach();Input.Release(preserveNearbyPickup:true);
                        break;
                    }
                    if (await TryRestoreMana(o,token)) continue;
                    if(deathRecoveryRequested)
                    {
                        ReleaseCombatPickup();drive.StopApproach();Input.Release(preserveNearbyPickup:true);
                        break;
                    }
                    var current = world.Find(target.Id);\n                    if (current == null)\n                    {\n                        // A single failed tree read is not proof that the target\n                        // disappeared. The client briefly removes/rebuilds creature\n                        // nodes while turning, strafing, or correcting a position.\n                        // Keep an engaged swing alive, refresh the scene, and keep
                        // the same identity locked for a short bounded
                        // reacquisition window.  Release only when the encounter
                        // no longer confirms this target.
                        if (targetMissingSince == 0) targetMissingSince = Environment.TickCount64;\n                        ReleaseCombatPickup(); drive.StopApproach();
                        bool preserveMissingSwing=PreserveEngagedSwing(target,"target node refresh",token);
                        if(!preserveMissingSwing)Input.HoldMouse(false,false,token);
                        do\n                        {\n                            await Input.Delay(70, token);\n                            RefreshGuardScene();\n                            current = entities.FirstOrDefault(e => e.Id == target.Id);\n                        }\n                        while (current == null && Environment.TickCount64 - targetMissingSince < 1200);\n                        if (current == null)\n                        {\n                            var missingHealthSnapshot = world.TargetHealth(target.Id);\n                            bool stillEngaged = encounter.IsEngaged(target);\n                            TraceLog.Record("target reacquisition timed out", new { target.Id, target.DisplayName, stillEngaged, missingHealthSnapshot.Known, missingHealthSnapshot.Dead, Elapsed = Environment.TickCount64 - targetMissingSince });\n                            if (stillEngaged && !missingHealthSnapshot.Dead && Environment.TickCount64 - targetMissingSince < 1800)
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
                        }\n                    }\n                    if (current.Address != target.Address || current.Generation != target.Generation || current.Name != target.Name || current.Model != target.Model || !current.Targetable)\n                    {\n                        collectAfterTarget = courtesy.StartedHere(target);\n                        TraceLog.Record("target inactive or replaced", new { target.Id, target.DisplayName, Current = current.Targetable ? current.DisplayName : "non-targetable" });\n                        break;\n                    }\n                    targetMissingSince = 0;\n                    lockedTarget=current;\n                    lastTargetPosition = current.Position;\n                    if(!o.GroupMode && !o.LeaveAreaWhenEmpty && o.PrioritizeGamekeeper && Targeting.IsGamekeeper(current) &&\n                        (current.Position-anchor).Length>(double)o.HuntRadius)gamekeeperExcursion=true;\n                    var hp = world.TargetHealth(target.Id);\n                    if (hp.Dead)
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
                        {\n                            // These 1-HP breakables can lose their HP record before their world object disappears.\n                            // Stop attacking and sweep actual drops; unknown HP is never reported as a confirmed kill.\n                            if (current.PriorityLootObject && courtesy.StartedHere(target))
                            {
                                Input.HoldMouse(false,false,token);
                                collectAfterTarget = true;
                                TraceLog.Record("priority HP removed after attack", new { target.Id, target.DisplayName, Position = current.Position });\n                                break;\n                            }\n                            throw new InvalidOperationException("Target HP is unavailable; stopped.");\n                        }\n                        await Input.Delay(100, token); continue;\n                    }\n                    missingHealth = 0;\n                    if(lastCombatHp.HasValue && hp.Current<lastCombatHp.Value)turnRecoveryAttempts=0;\n                    lastCombatHp=hp.Current;\n                    if(bodyProbeHp.HasValue && hp.Current<bodyProbeHp.Value) {bodyProbeHp=null;bodyProbeAt=0;TraceLog.Record("close collision attack confirmed",new {target.Id,hp.Current});}\n                    if(bodyProbeHp.HasValue && bodyProbeAt!=0 && Environment.TickCount64-bodyProbeAt>3500) throw new RouteUnavailableException("Close target did not take damage; repositioning is needed");\n                    pos = world.PlayerPosition();\n                    bool priorityFight=o.PrioritizeGamekeeper && Targeting.IsGamekeeper(current);\n                    double targetRadius=priorityFight ? BaseTargetRadius(current,o) : o.GroupMode ? (double)o.HuntRadius : BaseTargetRadius(current,o);\n                    if(!o.GroupMode && encounter.IsEngaged(current) || priorityFight && courtesy.StartedHere(current))\n                    {\n                        targetRadius=Math.Max(activeCompletionBoundary,Targeting.CompletionRadius(targetRadius,(double)o.NearbyEnemyRadius,(double)o.MeleeRange));\n                        activeCompletionBoundary=Math.Max(activeCompletionBoundary,targetRadius);\n                        if((pos-anchor).Length>(double)o.HuntRadius || (current.Position-anchor).Length>(double)o.HuntRadius)completionReturnPending=true;\n                    }\n                    // Completion tracking may allow an engaged target to remain
                    // observable beyond the hunt radius, but it must never widen
                    // the character's movement boundary. Outside trips and the
                    // explicit Gamekeeper response are the only exceptions.
                    bool outsideTrip=activeExcursion is {OutsideTrip:true} && !o.GroupMode;
                    double movementBoundary=priorityFight || outsideTrip ? targetRadius : Math.Min(targetRadius,(double)o.HuntRadius);
                    activeMovementBoundary=movementBoundary;
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
                    {\n                        if(encounter.IsEngaged(current))throw new InvalidOperationException("An engaged enemy moved beyond the combat completion area; stopped before pulling another target.");\n                        break;\n                    }\n                    if(o.GroupMode && o.PrioritizeGamekeeper && !Targeting.IsGamekeeper(current) && groupDecision.Target is Entity groupPriority && Targeting.IsGamekeeper(groupPriority))\n                    { TraceLog.Record("target preempted for Gamekeeper",new {PreviousId=current.Id,PriorityId=groupPriority.Id}); break; }\n                    string? protection=TargetGuardReason(current,hp,pos,o);\n                    if (protection!=null) throw new TargetProtectionException(protection);\n                    if (!o.GroupMode && !encounter.HasEngaged && !healingRestPending && (!o.LeaveAreaWhenEmpty || !completionReturnPending) && Targeting.PriorityRank(current,o.PrioritizeGamekeeper,o.PrioritizeBreakables)<2 && Environment.TickCount64 >= nextPriorityCheck)\n                    {\n                        nextPriorityCheck = Environment.TickCount64 + 250;\n                        RefreshGuardScene(); var priorityHealth=world.HealthSnapshot();\n                        var protectedCandidates=entities.Where(e=>TargetGuardReason(e,priorityHealth.GetValueOrDefault(e.Id),pos,o)==null).ToArray();\n                        var priority = Targeting.ChooseUrgent(protectedCandidates,priorityHealth,pos,anchor,o.LeaveAreaWhenEmpty ? (double)o.HuntRadius : responseRadius,o.PrioritizeGamekeeper) ??\n                            (encounter.Active ? null : Targeting.Choose(protectedCandidates, priorityHealth, pos, anchor, (double)o.HuntRadius,\n                            e => world.Difficulty(e,level), o.Target, o.AllowedDifficulties, priorityOnly: true,prioritizeGamekeeper:o.PrioritizeGamekeeper,prioritizeBreakables:o.PrioritizeBreakables));\n                        if (priority != null && Targeting.PriorityRank(priority,o.PrioritizeGamekeeper,o.PrioritizeBreakables)>Targeting.PriorityRank(current,o.PrioritizeGamekeeper,o.PrioritizeBreakables))\n                        {\n                            TraceLog.Record("target preempted for priority", new { PreviousId = target.Id, PreviousName = target.DisplayName, PriorityId = priority.Id, PriorityName = priority.DisplayName });\n                            break;\n                        }\n                    }\n                    Vec delta = current.Position - pos;\n                    message = $"{(turnRecoveryAttempts>0 ? $"Re-aiming after an unresponsive turn (retry {turnRecoveryAttempts})" : encounter.HasEngaged ? $"Finishing engaged ({encounter.EngagedCount})" : encounter.Active ? "Clearing nearby" : Targeting.PriorityRank(current,o.PrioritizeGamekeeper,o.PrioritizeBreakables)>0 ? "Priority" : "Locked")}: {current.DisplayName} Â· HP {hp.Current}/{hp.Maximum} Â· {delta.Length:F1} away";\n                    // Once engaged, small target/animation movements should not break a combo.\n                    // Pack clearing uses its configured melee attack gate; the ranged field is the bow stop distance.\n                    bool packClearing=RangedPullEnabled(o) && rangedPull.Active && rangedPull.Phase==RangedPullPhase.Clearing && !priorityFight;
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
                        bool keepStationarySwing=Input.BasicAttackHeld && delta.Length<=swingWindow;
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
                                try { await drive.Face(world,delta,token,.035); }
                                catch(TurnUnresponsiveException) { TraceLog.Record("stationary target face unavailable",new {current.Id,current.DisplayName}); }
                                message=$"Holding saved hunt point; waiting for {current.DisplayName} to enter melee range ({delta.Length:F1}/{swingWindow:F1})";
                                await Input.Delay(100,token);
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
                    bool holdPriorityPosition=o.StationaryGamekeeperPriority && !o.GroupMode && (priorityFight || encounter.IsEngaged(current));
                    if(holdPriorityPosition && delta.Length>chaseThreshold)
                    {
                        ReleaseCombatPickup();drive.StopApproach();Input.HoldMouse(false,false,token);
                        message=$"Holding position for priority combat; waiting for {current.DisplayName} ({delta.Length:F1}m) to enter attack range.";
                        await Input.Delay(100,token);break;
                    }
                    if(packClearing && !RangedPull.WithinNearby3D(current,pos,world.LocalPlayer().Height,(double)o.RangedGatherRadius))
                    {\n                        // This member left the melee-phase admission circle. Yield\n                        // so the pack policy can select another nearby member or\n                        // resume pulling when the circle is empty.\n                        ReleaseCombatPickup();drive.StopApproach();Input.Release(preserveNearbyPickup:true);break;\n                    }\n                    if(packClearing && delta.Length>(double)o.RangedMeleeAttackRange)\n                    {\n                        // Pack clearing is stationary: monsters come to the\n                        // player. Do not move closer merely to start a swing.\n                        ReleaseCombatPickup();drive.StopApproach();Input.HoldMouse(false,false,token);\n                        message=$"Melee mode: waiting for {current.DisplayName} to enter {o.RangedMeleeAttackRange:0.#}-unit attack range.";\n                        await Input.Delay(100,token);break;\n                    }\n                    // Fixed hunt assignments must never fall through to the
                    // normal chase route.  Their target can be several tenths
                    // beyond the configured stop while still inside the
                    // verified swing window; navigating here would make the
                    // character leave the activation point before it attacks.
                    if (!stationaryFarmTarget && delta.Length > chaseThreshold)
                    {\n                        ReleaseCombatPickup();\n                        Input.HoldMouse(false, false, token);\n                        if (approachStarted == 0) approachStarted = Environment.TickCount64;\n                        if (o.Ranged && !packClearing)\n                        {\n                            // The ranged skill (Firing) is for distance: try it while\n                            // closing, then fall through to the walk and pick up the\n                            // weapon swing once the target is in reach.\n                            var rangedBar = CheckedHotbar();\n                            int rangedIndex = o.SkillKeys.Length > 0 ? SkillRotation.ChooseRanged(o.SkillKeys, rangedBar, skillDue, Environment.TickCount64) : -1;\n                            if (rangedIndex >= 0)\n                            {\n                                char key = o.SkillKeys[rangedIndex];\n                                var slot = rangedBar.Slot(key);\n                                TraceLog.Record("ranged skill while approaching", new { Key = key.ToString(), slot.Name, target.Id, Distance = delta.Length });\n                                await Input.Key((Keys)key, 50, token); await Input.Delay(80, token); if(!await CastHealthCheckedSkill(slot,o,token))continue; await Input.Delay(150, token);\n                                var after = CheckedHotbar().Slot(key);\n                                bool cooldownStarted = after.RemainingCooldown > 0 || after.Locked;\n                                skillDue[key] = Environment.TickCount64 + SkillRotation.RetryDelayMilliseconds(slot, cooldownStarted, o.SkillSeconds);\n                                skillCursor = (rangedIndex + 1) % o.SkillKeys.Length;\n                                TraceLog.Record("ranged skill cooldown observed", new { Key = key.ToString(), Remaining = after.RemainingCooldown, After = after.Locked });\n                                await Input.Delay(150, token);\n                                continue;\n                            }\n                        }\n                        if (Environment.TickCount64 - approachStarted > 20000) throw new RouteUnavailableException("Target could not be reached within the approach limit");\n                        try\n                        {\n                            double completionDistance=packClearing ? attackStop : attackStop+1;\n                            if(await NavigateTo(drive,current.Position,anchor,o,token,completionDistance,boundaryRadius:activeMovementBoundary,watchTurns:true))
                            {\n                                if(!packClearing){bodyAllowance=1;bodyProbeHp=hp.Current;}\n                                bodyProbeAt=0;approachStarted=0;\n                            }\n                        }\n                        catch(TurnUnresponsiveException ex)\n                        {\n                            await RecoverUnresponsiveTurn(drive,current,anchor,o,activeMovementBoundary,++turnRecoveryAttempts,ex,token);\n                            approachStarted=Environment.TickCount64;\n                        }\n                        continue;\n                    }\n                    approachStarted = 0;\n                    if (drive.StopApproach()) { await Input.Delay(120, token); continue; }\n                    now = Environment.TickCount64;\n                    if (combatStart == 0) combatStart = now;\n                    if(!o.GroupMode && !encounter.Active)\n                    {\n                        encounter.Begin(); encounterHasAttack=false; encounterExistingDrops=existingDrops; encounterAnchor=anchor;\n                        RefreshGuardScene(); ObserveEncounter(o,world.HealthSnapshot(),pos,level);\n                    }\n                    if(stationaryAttackReady)
                    {
                        // Keep the basic attack held throughout a stationary
                        // engagement. A few client builds stop advancing a
                        // held swing after a long no-damage interval, so the
                        // watchdog below re-arms it only after the target has
                        // remained at the same HP for a bounded period.
                        long stationaryNow=Environment.TickCount64;
                        if(stationaryAttackBaselineHp<0 || hp.Current!=stationaryAttackBaselineHp)
                        {
                            stationaryAttackBaselineHp=hp.Current;
                            stationaryAttackHeldAt=stationaryNow;
                        }
                        bool rearm=!Input.BasicAttackHeld || stationaryNow-stationaryAttackHeldAt>=2500;
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
                        await Input.Delay(45,token);
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
                        // The five-target gate applies to the combat pack, while
                        // self-heals may bypass only the shared five-second timer.
                        bool stationarySkillGate=stationarySkillGroup.Ready;
                        int stationaryReadyIndex=!current.PriorityLootObject && stationaryNow-combatStart>=1200 && stationarySkillGate ?
                            SkillRotation.Choose(o.SkillKeys,skillCursor,stationaryBar,skillDue,stationaryNow,
                                slot=>(!RangedPullEnabled(o) || !SkillRotation.IsRangedSkill(slot.Name)) && HealthSkillAllowed(slot,o) &&
                                    (stationaryNow>=nextCombatSkillAt || SkillHealthRule.Applies(slot,o))) : -1;
                        if(stationaryReadyIndex>=0)
                        {
                            ReleaseCombatPickup();
                            char key=o.SkillKeys[stationaryReadyIndex];
                            var slot=stationaryBar.Slot(key);
                            bool delayExempt=SkillHealthRule.Applies(slot,o);
                            if(o.SmartSkillTargeting)
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
                                TraceLog.Record("skill input",new{Key=key.ToString(),slot.Name,target.Id,Distance=delta.Length,RemainingBefore=slot.RemainingCooldown,Mode="stationary",PackTargets=stationarySkillGroup.InRangeTargets,HighestHealthPercent=stationarySkillGroup.HighestHealthPercent,DelayExempt=delayExempt,NextSkillAt=nextCombatSkillAt});
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
                            await Input.Delay(75,token);
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
                    {\n                        if(!stationaryAttackReady)
                        {
                            if(packClearing && !await drive.Face(world,delta,token,Input.BasicAttackHeld ? .12 : .035))continue;
                            if(!await (o.Ranged ? drive.FaceTarget3D(world,current,token,Input.BasicAttackHeld ? .025 : .01) :
                                drive.Face(world,delta,token,Input.BasicAttackHeld ? .12 : .035)))continue;
                        }
                        if(packClearing)\n                        {\n                            // Optical aim is necessary for this 3D client, but the\n                            // melee swing also needs the character body to follow it.\n                            Vec liveDelta=current.Position-world.PlayerPosition();\n                            double bodyError=Movement.Angle(Movement.FromClientHeading(world.PlayerHeading()),liveDelta);\n                            if(Math.Abs(bodyError)>.12)\n                            {\n                                ReleaseCombatPickup();Input.HoldMouse(false,false,token);\n                                TraceLog.Record("pack melee body not aligned",new {current.Id,ErrorDegrees=bodyError*180/Math.PI,Distance=liveDelta.Length});\n                                continue;\n                            }\n                        }\n                    }\n                    catch(TurnUnresponsiveException ex)\n                    {\n                        await RecoverUnresponsiveTurn(drive,current,anchor,o,activeMovementBoundary,++turnRecoveryAttempts,ex,token);\n                        continue;\n                    }\n                    Input.HoldMouse(false, true, token);\n                    turnRecoveryAttempts=0;\n                    if(bodyProbeHp.HasValue && bodyProbeAt==0) bodyProbeAt=Environment.TickCount64;\n                    courtesy.MarkAttack(current);\n                    if(!o.GroupMode)\n                    {\n                        activeCompletionBoundary=Math.Max(activeCompletionBoundary,Targeting.CompletionRadius(\n                            BaseTargetRadius(current,o),(double)o.NearbyEnemyRadius,(double)o.MeleeRange));\n                        if((pos-anchor).Length>(double)o.HuntRadius || (current.Position-anchor).Length>(double)o.HuntRadius)completionReturnPending=true;\n                        encounter.MarkAttack(current,hp);\n                    }\n                    if(encounter.Active)\n                    {\n                        if(!encounterHasAttack) TraceLog.Record("encounter started",new {Position=pos,Radius=o.NearbyEnemyRadius,Enemies=encounter.Candidates.Select(e=>e.Id).ToArray()});\n                        encounterHasAttack=true; encounter.NoteAttack(pos,Encounter.CollateralReach(RangedPullEnabled(o) ? (double)o.RangedMeleeAttackRange : (double)o.MeleeRange));\n                    }\n                    var bar = CheckedHotbar();\n                    if(o.AutoDetectSkills)\n                    {\n                        string detected=AttackKeys(SkillRotation.DetectKeys(bar),bar,o.MaintainAreaBuffs);\n                        if(detected!=o.SkillKeys)\n                        {\n                            o.SkillKeys=detected;skillCursor=0;\n                            foreach(char key in detected)skillDue.TryAdd(key,0);\n                            TraceLog.Record("skill slots detected",new {Keys=detected});\n                        }\n                    }\n                    var combatSkillGroup=CombatSkillGroup(current,hp,pos,o);
                    // The five-target gate applies to the combat pack, while
                    // self-heals may bypass only the shared five-second timer.
                    bool combatSkillGate=combatSkillGroup.Ready;
                    int readyIndex = !current.PriorityLootObject && now - combatStart >= 1200 && combatSkillGate ? SkillRotation.Choose(o.SkillKeys,
                        skillCursor,bar,skillDue,now,slot=>(!RangedPullEnabled(o) || !SkillRotation.IsRangedSkill(slot.Name)) && HealthSkillAllowed(slot,o) &&
                            (now>=nextCombatSkillAt || SkillHealthRule.Applies(slot,o))) : -1;
                    if (readyIndex >= 0)\n                    {\n                        ReleaseCombatPickup();\n                        char key = o.SkillKeys[readyIndex];
                        var slot = bar.Slot(key);
                        bool delayExempt=SkillHealthRule.Applies(slot,o);
                        if (o.SmartSkillTargeting)
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
                            TraceLog.Record("skill input", new { Key = key.ToString(), slot.Name, target.Id, Distance = delta.Length, RemainingBefore = slot.RemainingCooldown, PackTargets=combatSkillGroup.InRangeTargets, HighestHealthPercent=combatSkillGroup.HighestHealthPercent, DelayExempt=delayExempt, NextSkillAt=nextCombatSkillAt });
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
                    }\n                    else\n                    {\n                        Input.HoldMouse(false, true, token);\n                        SetCombatPickup(o.LootDuringSkillCooldowns && CombatPickup.SkillsCooling(o.SkillKeys,bar),existingDrops,o,token);\n                        await Input.Delay(50, token);\n                    }\n                }\n                }\n                catch(PriorityTargetException ex)
                {
                    RememberGamekeeperReturn(world.PlayerPosition());
                    collectAfterTarget=courtesy.StartedHere(target) && world.TargetHealth(target.Id).Dead;
                    ReleaseCombatPickup();drive.StopApproach();Input.Release(preserveNearbyPickup:true);\n                    pendingPriorityGamekeeper=ex.Target;healingRestPending=false;healingWarning=null;\n                    message="Prioritizing: "+ex.Target.DisplayName;\n                    TraceLog.Record("immediate Gamekeeper preemption",new {PreviousId=target.Id,PriorityId=ex.Target.Id});\n                }\n                catch(TargetProtectionException ex)\n                {\n                    if(ReleaseUnstartedDefense(target))throw new RecoverUnderDamageException();\n                    if(encounter.IsEngaged(target))throw new InvalidOperationException("Cannot safely finish an engaged enemy: "+ex.Message);\n                    collectAfterTarget=false; courtesy.Forget(target); encounter.Forget(target); Input.Release(preserveNearbyPickup:true);\n                    if(encounter.Active && !encounterHasAttack) {encounter.Reset();encounterExistingDrops=null;encounterAnchor=null;}\n                    message="Skipped: " + ex.Message;\n                    TraceLog.Record("target protection skip",new {target.Id,target.DisplayName,Reason=ex.Message});\n                }\n                catch(RouteUnavailableException ex)\n                {\n                    if(inferredDefense is Entity inferred && TargetIdentity(inferred)==TargetIdentity(target) && !courtesy.StartedHere(target))\n                    {\n                        unreachableTargets[TargetIdentity(target)]=Environment.TickCount64+30000;\n                        ReleaseUnstartedDefense(target);throw new RecoverUnderDamageException();\n                    }\n                    if(encounter.IsEngaged(target))throw new InvalidOperationException("Cannot reach an engaged enemy; stopped before selecting a new target: "+ex.Message);\n                    collectAfterTarget=false;courtesy.Forget(target);encounter.Forget(target);Input.Release(preserveNearbyPickup:true);\n                    unreachableTargets[TargetIdentity(target)]=Environment.TickCount64+30000;\n                    message="Target route skipped: "+ex.Message;\n                    TraceLog.Record("target navigation skip",new {target.Id,target.DisplayName,Reason=ex.Message});\n                    if(encounter.Active && (lastTargetPosition-world.PlayerPosition()).Length<=(double)o.NearbyEnemyRadius)\n                        throw new InvalidOperationException("A nearby enemy could not be reached; stopped before pickup.");\n                }\n                ReleaseCombatPickup();drive.StopApproach();\n                Input.Release(preserveNearbyPickup:true);\n                lockedTarget = null;\n                int pickupHoldMs = o.GroupMode?0:Targeting.LootHoldMilliseconds(target, o.LootHoldMs);\n                if (collectAfterTarget && pickupHoldMs > 0 && !o.AutoPickupNearbyLoot)\n                {\n                    var job=new LootJob(target,lastTargetPosition,pickupHoldMs,existingDrops);\n                    deferredLoot.Enqueue(job); encounterQuietSince=0;\n                    if(!encounter.Active){encounter.Begin();encounterExistingDrops=existingDrops;encounterAnchor=anchor;encounterHasAttack=false;}\n                    TraceLog.Record("loot deferred until enemies clear",new {target.Id,target.DisplayName,Pending=deferredLoot.Count});\n                }\n                if(collectAfterTarget)courtesy.Forget(target);\n                }\n                catch(PriorityTargetException ex)
                {
                    RememberGamekeeperReturn(world.PlayerPosition());
                    ReleaseCombatPickup();drive.StopApproach();Input.Release(preserveNearbyPickup:true);lockedTarget=null;
                    pendingPriorityGamekeeper=ex.Target;healingRestPending=false;healingWarning=null;\n                    await StandForGamekeeper(token);\n                    message="Gamekeeper first: interrupting the current activity.";\n                    TraceLog.Record("activity interrupted for Gamekeeper",new {PriorityId=ex.Target.Id,Engaged=encounter.EngagedCount,PendingLoot=deferredLoot.Count,HP=world.TargetHealth(world.LocalPlayer().Id)});\n                }\n                catch(RecoverBeforeFreshTargetException)\n                {\n                    ReleaseCombatPickup();drive.StopApproach();Input.Release(preserveNearbyPickup:true);lockedTarget=null;\n                    if(encounter.Active && !encounterHasAttack){encounter.Reset();encounterExistingDrops=null;encounterAnchor=null;}\n                    message="Recovering to full health before starting a new fight.";\n                    TraceLog.Record("fresh approach paused for health recovery",new {Position=world.PlayerPosition(),ReturnPending=completionReturnPending});\n                }\n                catch(ReturnToHuntingAreaException)\n                {\n                    ReleaseCombatPickup();drive.StopApproach();Input.Release(preserveNearbyPickup:true);lockedTarget=null;\n                    if(encounter.Active && !encounterHasAttack){encounter.Reset();encounterExistingDrops=null;encounterAnchor=null;}\n                    message="An approved target appeared inside the original area. Returning before starting a new fight.";\n                    TraceLog.Record("outside approach cancelled; home target available",new {Anchor=anchor,Position=world.PlayerPosition()});\n                }\n                catch(EngagedTargetPriorityException)\n                {\n                    ReleaseCombatPickup();drive.StopApproach();Input.Release(preserveNearbyPickup:true);lockedTarget=null;\n                    message="Finishing engaged enemies before moving to a new target.";\n                    TraceLog.Record("fresh target interrupted for engaged enemies",new {Count=encounter.EngagedCount});\n                }\n                catch(RecoverUnderDamageException)\n                {\n                    ReleaseCombatPickup();drive.StopApproach();Input.Release(preserveNearbyPickup:true);lockedTarget=null;\n                    defensePending=true;defenseStep=null;encounterQuietSince=0;\n                    message="Incoming damage: staying upright and looking for a nearby enemy to defend against.";\n                    TraceLog.Record("recovery resumed under incoming damage",new {HP=world.TargetHealth(guardSelfId),Engaged=encounter.EngagedCount});\n                }\n                catch(RetreatRequiredException ex)\n                {\n                    await RetreatAndRecover(drive,anchor,o,token,ex.Message);\n                }\n            }\n        }\n        catch (OperationCanceledException) { TraceLog.Record("hunt stopped", new { Reason = "Stop/focus/cancellation" }); Stop("Stopped. Press F8 to calibrate and start again."); }\n        catch (Exception ex) { TraceLog.Record("hunt failed", new { Error = ex.Message }); Stop(ex.Message); }\n        finally { navigation.EndRecording();deathRecoveryActive=false;deathRecoveryRequested=false;combatPressure.Reset();defensePending=false;defenseRepositioning=false;defenseStep=null;inferredDefense=null;buffInProgress=false;returningFromPriority=false;Input.PickupHoldProvider=null;nearbyPickupCount=0;working = false; settings.Enabled = true; protectionPanel.Enabled=true;automaticRouting.Enabled=true;clearNavigation.Enabled=true; connect.Enabled = true; start.Enabled=true; ReleaseCombatPickup(); Input.Release(); Input.Preflight=null; healingRestPending=false; healingRest=null; runCharacter=null; activeHuntAnchor=null; activeExcursion=null; activeGuardOptions=null; retreatRecovery=null;retreatDrive=null;lootGuardPosition=null; lootBeforeFight=null; encounter.Reset(); deferredLoot.Clear(); encounterExistingDrops=null; encounterAnchor=null; encounterHasAttack=false; courtesy.Reset(); playerGreeting.Reset(); movement = null; runHotbarPage = null;runZone=null; cancel?.Dispose(); cancel = null; }
    }\n\n    async Task RecoverUnresponsiveTurn(Movement drive,Entity target,Vec anchor,Options options,double boundary,int attempt,\n        TurnUnresponsiveException failure,CancellationToken token)\n    {\n        // Keep the encounter and target. Accepted mouse packets alone are not\n        // evidence that the client actually turned; retry using measured state.\n        ReleaseCombatPickup();drive.StopApproach();Input.Release(preserveNearbyPickup:true);\n        drive.ResetTurnResponse();\n        message=$"Game did not respond to turning; re-aiming {target.DisplayName} (retry {attempt}).";\n        TraceLog.Record("unresponsive combat turn recovery",new {target.Id,target.Generation,Attempt=attempt,\n            failure.Position,failure.Forward,TargetPosition=target.Position,HP=world.TargetHealth(target.Id),Engaged=encounter.EngagedCount});\n        await Input.Delay(120,token);\n        var current=world.Find(target.Id);\n        if(current==null || TargetIdentity(current)!=TargetIdentity(target) || world.TargetHealth(target.Id).Dead)return;\n        var position=world.PlayerPosition();\n        double heading=world.PlayerHeading();\n        var forward=Movement.FromClientHeading(heading);\n        var end=position+forward*2.5;\n        bool canStep=Targeting.BoundaryStepAllowed(position,end,anchor,boundary) && drive.CanAdvance?.Invoke(position,end)==true;\n        try\n        {\n            // This is a short straight step, requiring no successful turn. It\n            // remains bounded by the same area, route and input checks as chase.\n            if(canStep)await Input.Key(Keys.W,180,token);\n            await Input.Delay(100,token);\n            var after=world.PlayerPosition();\n            double angle=world.PlayerHeading()-heading;\n            TraceLog.Record("combat turn recovery observed",new {target.Id,Attempt=attempt,ForwardStep=canStep,\n                PositionBefore=position,PositionAfter=after,Moved=(after-position).Length,\n                HeadingChange=Math.Atan2(Math.Sin(angle),Math.Cos(angle))*180/Math.PI,HP=world.TargetHealth(target.Id)});\n        }\n        finally {drive.StopApproach();drive.ResetTurnResponse();}\n    }\n\n    async Task RetreatAndRecover(Movement drive,Vec anchor,Options options,CancellationToken token,string reason)\n    {\n        bool interruptedEngagement=encounter.HasEngaged || encounter.Active && encounterHasAttack;\n        double retreatRadius=options.LeaveAreaWhenEmpty && !options.GroupMode && completionReturnPending ?\n            Math.Max((double)options.HuntRadius,activeCompletionBoundary) : (double)options.HuntRadius;\n        ReleaseCombatPickup(); drive.StopApproach(); Input.Release(preserveNearbyPickup:true);\n        TraceLog.Record("retreat triggered",new {Reason=reason,Position=world.PlayerPosition(),Target=lockedTarget?.Id,PendingLoot=deferredLoot.Count});\n        // Escape first; an interrupted engaged fight must not resume with fresh pulls.\n        lockedTarget=null;lootGuardPosition=null;lootBeforeFight=null;\n        encounterQuietSince=0;encounterUnknownSince=0;\n        navigation.BeginGoal("retreat:"+Environment.TickCount64);\n        var previousAdvance=drive.CanAdvance;\n        var recovery=new RetreatRecovery(Environment.TickCount64,options.HealBelowPercent);\n        retreatRecovery=recovery;retreatDrive=drive;\n        drive.CanAdvance=(from,to)=>RetreatPlanner.CanEscape(from,to,anchor,retreatRadius,avoidZones,navigation.Obstacles([]));\n        long nextRouteTrace=0; RecoveryPhase? lastPhase=null;\n        long lastDamageAt=Environment.TickCount64;\n        int lastHp=world.TargetHealth(world.LocalPlayer().Id).Current;\n        bool CanRestNow()\n        {\n            if(PriorityGamekeeper(options)!=null)return false;\n            var position=world.PlayerPosition();var health=world.HealthSnapshot();\n            var hp=health.GetValueOrDefault(world.LocalPlayer().Id);\n            if(!hp.Known || hp.Dead)return false;\n            if(hp.Current<lastHp)lastDamageAt=Environment.TickCount64;\n            lastHp=hp.Current;\n            return RestToggle.SafeToRest(position,entities,health,avoidZones,(double)options.NearbyEnemyRadius,Environment.TickCount64,lastDamageAt);\n        }\n        try\n        {\n            while(true)\n            {\n                await Input.Delay(25,token);\n                RefreshGuardScene();\n                var self=world.LocalPlayer(); var hp=world.TargetHealth(self.Id);\n                if(PriorityGamekeeper(options) is Entity recoveryPriority && Avoidance.BlockedPoint(self.Position,avoidZones)==null)\n                {\n                    pendingPriorityGamekeeper=recoveryPriority;healingRestPending=false;healingWarning=null;\n                    await StandForGamekeeper(token);\n                    TraceLog.Record("retreat recovery interrupted for Gamekeeper",new {PriorityId=recoveryPriority.Id,HP=hp});\n                    return;\n                }\n                if(hp.Current<lastHp)lastDamageAt=Environment.TickCount64;\n                lastHp=hp.Current;\n                navigation.Observe(world.NavigationContext(self),self.Position,self.Height);\n                var action=recovery.Update(Environment.TickCount64,self.Position,Movement.FromClientHeading(world.PlayerHeading()),anchor,\n                    retreatRadius,avoidZones,navigation.Obstacles([]),hp);\n                if(lastPhase!=action.Phase)\n                {\n                    TraceLog.Record("retreat phase",new {Phase=action.Phase.ToString(),self.Position,HP=hp,recovery.HealthTarget});\n                    drive.StopApproach();lastPhase=action.Phase;\n                }\n                if(action.Phase==RecoveryPhase.Ready)\n                {\n                    if(world.RestSupported) await EnsurePosture(false,token);\n                    RefreshGuardScene();\n                    var afterStand=world.TargetHealth(world.LocalPlayer().Id);\n                    if(!afterStand.Known || afterStand.Dead)throw new InvalidOperationException("Player HP became unavailable during recovery.");\n                    if(Avoidance.BlockedPoint(world.PlayerPosition(),avoidZones,RetreatPlanner.Clearance)!=null ||\n                        afterStand.Current*100.0/afterStand.Maximum<(double)recovery.HealthTarget) continue;\n                    if(interruptedEngagement)throw new InvalidOperationException("Recovered after retreating from an unfinished fight; stopped before selecting a new target.");\n                    TraceLog.Record("retreat recovered; hunt resuming",new {self.Position,HP=hp});\n                    message="Area clear and health recovered. Resuming huntâ€¦";\n                    return;\n                }\n                if(action.Waypoint is Vec waypoint)\n                {\n                    if(world.RestSupported && world.RestState().Posture!=RestPosture.Standing)\n                    {\n                        drive.StopApproach();await EnsurePosture(false,token);continue;\n                    }\n                    navigation.ShowRetreatRoute(waypoint,"Retreating from keep-away threat");\n                    message="Retreating: "+reason;\n                    if(Environment.TickCount64>=nextRouteTrace)\n                    {\n                        nextRouteTrace=Environment.TickCount64+500;\n                        TraceLog.Record("retreat route",new {self.Position,Waypoint=waypoint,Zones=avoidZones});\n                    }\n                    try {await drive.Approach(world,self.Position,waypoint-self.Position,token);}\n                    catch(MovementBlockedException blocked)\n                    {\n                        drive.StopApproach();navigation.RecordBlock(blocked.Position,blocked.Direction,self.Height);\n                        TraceLog.Record("retreat blocked direction",new {blocked.Position,blocked.Direction,navigation.RecoveryAttempts});\n                    }\n                }\n                else\n                {\n                    drive.StopApproach();navigation.ShowRetreatRoute(null,"Recovering outside keep-away zones");\n                    bool needsHealth=hp.Current*100.0/hp.Maximum<(double)recovery.HealthTarget;\n                    if(world.RestSupported)\n                    {\n                        bool missingSupplies=HealingRest.MissingHealingItem(CheckedHotbar());\n                        bool alreadyResting=world.RestState().Posture is RestPosture.Resting or RestPosture.SittingDown;\n                        bool wantRest=needsHealth && CanRestNow() && (!missingSupplies || !interruptedEngagement &&\n                            (alreadyResting || recovery.HealthTarget==100 || HealingRest.ShouldTrigger(hp,options.HealBelowPercent)));\n                        if(wantRest && missingSupplies)recovery.RequireFullHealth();\n                        if(!await EnsurePosture(wantRest,token,CanRestNow))\n                        {\n                            await EnsurePosture(false,token);continue;\n                        }\n                    }\n                    if(!options.AutoHeal && !world.RestSupported && needsHealth)\n                        throw new InvalidOperationException("Retreated to safety. Automatic healing is disabled; recover health before restarting.");\n                    message=$"{(world.RestSupported && world.RestState().Posture==RestPosture.Resting?"Resting with C":"Recovering safely")} Â· HP {hp.Current}/{hp.Maximum} Â· resume at {recovery.HealthTarget}% after 2 clear seconds";\n                }\n                // Respect AutoHeal and the same recognized-item/cooldown rules used during hunting.\n                await TryHeal(drive,options,token,recovery.HealthTarget);\n            }\n        }\n        catch(Exception ex)\n        {\n            TraceLog.Record("retreat stopped",new {Reason=ex.Message,Phase=recovery.Phase.ToString()});\n            throw;\n        }\n        finally\n        {\n            drive.StopApproach();Input.Release(preserveNearbyPickup:true);drive.CanAdvance=previousAdvance;\n            retreatRecovery=null;retreatDrive=null;navigation.BeginGoal("retreat finished:"+Environment.TickCount64);\n        }\n    }\n\n    async Task<bool> EnsurePosture(bool wantRest,CancellationToken token,Func<bool>? canRest=null)\n    {\n        var toggle=new RestToggle(wantRest,Environment.TickCount64);\n        while(true)\n        {\n            await Input.Delay(25,token);\n            if(wantRest && canRest?.Invoke()!=true) return false;\n            var reading=world.RestState();\n            switch(toggle.Next(reading,Environment.TickCount64))\n            {\n                case RestCommand.Complete:return true;\n                case RestCommand.Toggle:\n                    message=wantRest?"Resting safely Â· pressing C":"Standing up before continuing Â· pressing C";\n                    TraceLog.Record("rest toggle input",new {WantRest=wantRest,Before=reading});\n                    await Input.Key(Keys.C,70,token);break;\n                default:await Input.Delay(50,token);break;\n            }\n        }\n    }\n    async Task CollectLoot(Movement drive, Vec deathPosition, Vec anchor, Options options, CancellationToken token, int pickupHoldMs, bool priorityObject,HashSet<(uint,uint)> existingDrops)
    {
        if(deathRecoveryRequested)return;
        double lootRadius=options.LeaveAreaWhenEmpty && !options.GroupMode && completionReturnPending && encounter.Active ? activeCompletionBoundary : (double)options.HuntRadius;
        bool EligibleDrop(GroundItem i) => (!options.AntiKillSteal || !existingDrops.Contains((i.KeyA,i.KeyB))) && (i.Position-deathPosition).Length<=5 && (i.Position-anchor).Length<=lootRadius;
        List<GroundItem> ReadDrops()
        {
            var snapshot=world.Loot();
            ObserveLootTrackerDrops(options,snapshot,anchor,lootRadius);
            return snapshot;
        }
        await Input.Delay(250, token);
        var drops = ReadDrops().Where(EligibleDrop).ToList();
        long waitUntil = Environment.TickCount64 + (priorityObject ? 1500 : 350);
        while (drops.Count == 0 && Environment.TickCount64 < waitUntil) { await Input.Delay((int)Math.Min(100, Math.Max(1, waitUntil-Environment.TickCount64)), token); drops = ReadDrops().Where(EligibleDrop).ToList(); }
        if (drops.Count == 0) { TraceLog.Record("no nearby drops", new { Position = deathPosition }); return; }\n        for (int attempt = 0; attempt < 3 && drops.Count > 0; attempt++)
        {
            if(deathRecoveryRequested)return;
            await TryHeal(drive, options, token);
            if(deathRecoveryRequested)return;
            var item = drops.OrderBy(i => (i.Position-world.PlayerPosition()).Length).First();
            lootGuardPosition=item.Position;\n            long started = Environment.TickCount64;\n            while ((item.Position-world.PlayerPosition()).Length > 2.5 && Environment.TickCount64-started < 5000)
            {
                if(deathRecoveryRequested)return;
                if (await TryHeal(drive, options, token)) continue;
                if(deathRecoveryRequested)return;
                var pos = world.PlayerPosition();\n                if ((pos-anchor).Length > lootRadius) break;\n                message = $"Approaching loot: {item.Name}";\n                await NavigateTo(drive,item.Position,anchor,options,token,boundaryRadius:lootRadius);\n            }\n            drive.StopApproach(); await Input.Delay(100, token);\n            var before = ReadDrops().Where(i => (i.Position-world.PlayerPosition()).Length <= 3).ToList();
            if (options.AntiKillSteal && before.Any(i=>existingDrops.Contains((i.KeyA,i.KeyB)))) throw new TargetProtectionException("Pre-existing drops are inside pickup range");\n            if (before.Count == 0) break;\n            message = $"Picking up {before.Count} nearby drop(s) Â· holding E";\n            TraceLog.Record("loot input", new { Items = before.Select(i => new { i.Name, i.KeyA, i.KeyB }), HoldMs = pickupHoldMs });\n            await Input.Key(Keys.E, pickupHoldMs, token); await Input.Delay(200, token);\n            var after = ReadDrops();
            int removed = before.Count(i => !after.Any(a => a.KeyA == i.KeyA && a.KeyB == i.KeyB));\n            TraceLog.Record("loot result", new { Removed = removed, Before = before.Count, Player = world.PlayerPosition() });\n            drops = after.Where(EligibleDrop).ToList();\n        }\n        drive.StopApproach();\n    }\n    public static bool ShouldHeal(Health hp, decimal threshold, long now, long nextAllowed) => hp.Known && !hp.Dead && now >= nextAllowed && hp.Current * 100.0 / hp.Maximum <= (double)threshold;\n    HotbarSnapshot CheckedHotbar()\n    {\n        var bar = world.Hotbar();\n        if (runHotbarPage.HasValue && bar.PageBase != runHotbarPage.Value) throw new InvalidOperationException("Hotbar page changed; stopped.");\n        return bar;\n    }\n    bool HasActiveFight() => encounter.HasEngaged || lockedTarget is Entity fighting &&\n        courtesy.StartedHere(fighting) && !world.TargetHealth(fighting.Id).Dead;\n\n    bool ReleaseUnstartedDefense(Entity target)\n    {\n        if(inferredDefense is not Entity inferred || TargetIdentity(inferred)!=TargetIdentity(target) || courtesy.StartedHere(target))return false;\n        encounter.Forget(target);inferredDefense=null;\n        defensePending=combatPressure.RecentDamage(Environment.TickCount64);\n        TraceLog.Record("inferred defense unavailable before first attack",new {target.Id,target.Generation});\n        return true;\n    }\n\n    bool DefenseWithinBoundary(Entity entity,Options options) => activeHuntAnchor is Vec anchor &&
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
\n    void ObserveCombatPressure(Options options,Health playerHealth,Vec position)
    {
        if(options.GroupMode)return;
        long now=Environment.TickCount64;
        bool damaged=combatPressure.Observe(playerHealth,now);
        if(inferredDefense is Entity inferred)\n        {\n            if(courtesy.StartedHere(inferred))inferredDefense=null;\n            else\n            {\n                var current=entities.FirstOrDefault(e=>TargetIdentity(e)==TargetIdentity(inferred));\n                var hp=world.TargetHealth(inferred.Id);\n                bool ambiguous=entities.Any(e=>e.Id==inferred.Id && TargetIdentity(e)!=TargetIdentity(inferred));\n                if(current==null || ambiguous || !encounter.IsEngaged(inferred) || !hp.Known || hp.Dead ||
                    !DefenseWithinBoundary(current,options) || TargetGuardReason(current,hp,position,options)!=null)ReleaseUnstartedDefense(inferred);
            }
        }
        if(HoldStationaryAnchorDuringDamage(options,position))
        {
            // Fixed hunt assignments should keep swinging at the activation
            // point instead of taking the generic defensive sidestep. If an
            // earlier damage response already moved us, RepositionUnderPressure
            // will route back to the same anchor before combat resumes.
            defensePending=false;defenseStep=null;movement?.StopApproach();
            if(damaged)TraceLog.Record("stationary anchor held after incoming damage",new {Position=position,Anchor=activeHuntAnchor,Target=lockedTarget?.Id,HP=playerHealth});
            return;
        }
        if(damaged && !HasActiveFight())defensePending=true;
        if(HasActiveFight() || !combatPressure.RecentDamage(now))\n        {\n            defensePending=false;\n            if(defenseStep.HasValue)movement?.StopApproach();\n            defenseStep=null;\n            return;\n        }\n        if(!defensePending || activeHuntAnchor is not Vec anchor)return;\n        var health=world.HealthSnapshot();\n        double radius=Math.Clamp((double)options.NearbyEnemyRadius,5,10);\n        var defender=CombatPressure.ChooseDefense(entities,health,position,radius,(entity,hp)=>\n            DefenseWithinBoundary(entity,options) &&\n            TargetGuardReason(entity,hp,position,options)==null,\n            entity=>encounter.IsEngaged(entity) || courtesy.StartedHere(entity));\n        if(defender==null)return;\n        if(!encounter.Active)\n        {\n            encounter.Begin();encounterAnchor=anchor;\n            encounterExistingDrops=world.Loot().Select(item=>(item.KeyA,item.KeyB)).ToHashSet();\n            encounterHasAttack=false;\n        }\n        encounter.MarkDefensive(defender,health.GetValueOrDefault(defender.Id));\n        inferredDefense=defender;\n        defensePending=false;defenseStep=null;movement?.StopApproach();encounterQuietSince=0;encounterUnknownSince=0;\n        TraceLog.Record("nearby defense after incoming damage",new {defender.Id,defender.DisplayName,defender.Generation,\n            Distance=(defender.Position-position).Length,Radius=radius,HP=playerHealth,\n            Evidence="Player HP fell; nearby candidate inferred, attacker ID unavailable"});\n    }\n\n    async Task RepositionUnderPressure(Movement drive,Vec anchor,Options options,CancellationToken token)
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
        if(defenseStep is Vec reached && (reached-position).Length<=1){drive.StopApproach();defenseStep=null;}\n        if(!defenseStep.HasValue)\n        {\n            var health=world.HealthSnapshot();\n            var nearest=entities.Where(e=>e.Monster && e.Position.Finite && health.GetValueOrDefault(e.Id) is {Known:true,Dead:false})\n                .OrderBy(e=>(e.Position-position).Length).FirstOrDefault();\n            Vec away=nearest!=null && (position-nearest.Position).Length>.1 ?\n                (position-nearest.Position)/(position-nearest.Position).Length : Movement.FromClientHeading(world.PlayerHeading())*-1;\n            if((position-anchor).Length>(double)options.HuntRadius)away=(anchor-position)/(anchor-position).Length;\n            foreach(double turn in new[]{0d,Math.PI/4,-Math.PI/4,Math.PI/2,-Math.PI/2,Math.PI})\n            {\n                var goal=position+Movement.Rotate(away,turn)*2.5;\n                if((goal-anchor).Length>boundary || Avoidance.BlockedSegment(position,goal,avoidZones)!=null)continue;\n                if(options.AutomaticRouting && !navigation.CanAdvance(position,goal,avoidZones))continue;\n                defenseStep=goal;navigation.BeginGoal("reacquire after incoming damage");\n                TraceLog.Record("damage response reposition",new {Position=position,Goal=goal,Boundary=boundary});\n                break;\n            }\n        }\n        message="Incoming damage: moving and checking for a nearby enemy before recovering.";\n        if(defenseStep is not Vec destination)\n        {\n            drive.StopApproach();message="Incoming damage: checking for an enemy or an open escape route.";\n            await Input.Delay(100,token);return;\n        }\n        defenseRepositioning=true;\n        try {await NavigateTo(drive,destination,anchor,options,token,boundaryRadius:boundary);}\n        catch(RouteUnavailableException ex)\n        {\n            drive.StopApproach();defenseStep=null;\n            TraceLog.Record("damage response route unavailable",new {Reason=ex.Message});\n            await Input.Delay(100,token);\n        }\n        finally {defenseRepositioning=false;}\n    }\n\n    async Task RecoverToFullByResting(Movement drive,Options options,CancellationToken token)\n    {\n        var initial=world.TargetHealth(world.LocalPlayer().Id);\n        if(!HealingRest.ShouldTrigger(initial,options.HealBelowPercent)){healingRestPending=false;healingWarning=null;return;}\n        if(!world.RestSupported)throw new InvalidOperationException("No healing item is slotted and the client's sitting state cannot be verified.");\n        ReleaseCombatPickup();drive.StopApproach();Input.Release(preserveNearbyPickup:true);\n        var recovery=new HealingRest(initial,Environment.TickCount64);\n        healingRest=recovery;\n        bool requestingSit=false;\n        Entity? recoveryPriority=null;\n        string? threat=null;\n        Action? normalPreflight=Input.Preflight;\n        HealingRestDecision ObserveRecovery()\n        {\n            if(Environment.TickCount64-guardRefreshedAt>=100)RefreshGuardScene();\n            var self=world.LocalPlayer();var hp=world.TargetHealth(self.Id);\n            if(encounter.Active)ObserveEncounter(options,world.HealthSnapshot(),self.Position,world.PlayerLevel());\n            ObserveCombatPressure(options,hp,self.Position);\n            recoveryPriority ??= PriorityGamekeeper(options);\n            threat=Avoidance.BlockedPoint(self.Position,avoidZones,RetreatPlanner.Clearance);\n            return recovery.Evaluate(hp,world.RestState(),HasActiveFight() || threat!=null || recoveryPriority!=null,false,Environment.TickCount64);\n        }\n        Input.Preflight=()=>\n        {\n            if(runCharacter!=null && !LocalCharacter.Same(runCharacter,world.LocalPlayer()))throw new OperationCanceledException("Character changed during recovery.");\n            if(runZone.HasValue && world.ActiveZone()!=runZone)throw new InvalidOperationException("Map zone changed during recovery; stopped.");\n            _=CheckedHotbar();\n            var decision=ObserveRecovery();\n            // Stand-up input remains available after an interruption. Only a\n            // pending sit request is cancelled if combat starts before its C edge.\n            if(requestingSit && decision.Action is HealingRestAction.Stand or HealingRestAction.Interrupted or HealingRestAction.Complete)\n                throw new HealingRestInterruptedException();\n        };\n        TraceLog.Record("no-item rest recovery started",new {initial.Current,initial.Maximum,TriggerPercent=options.HealBelowPercent,TargetPercent=100,Engaged=encounter.EngagedCount});\n        try\n        {\n            while(true)\n            {\n                await Input.Delay(100,token);\n                var decision=ObserveRecovery();\n                var hp=world.TargetHealth(world.LocalPlayer().Id);\n                message=$"{decision.Reason} HP {hp.Current}/{hp.Maximum}";\n                switch(decision.Action)\n                {\n                    case HealingRestAction.Sit:\n                        requestingSit=true;\n                        try\n                        {\n                            TraceLog.Record("rest recovery posture requested",new {Resting=true,hp.Current,hp.Maximum});\n                            await EnsurePosture(true,token,()=>ObserveRecovery().Action is not (HealingRestAction.Stand or HealingRestAction.Interrupted or HealingRestAction.Complete));\n                        }\n                        catch(HealingRestInterruptedException) { }\n                        finally {requestingSit=false;}\n                        break;\n                    case HealingRestAction.Stand:\n                        TraceLog.Record("rest recovery posture requested",new {Resting=false,hp.Current,hp.Maximum});\n                        await EnsurePosture(false,token);\n                        break;\n                    case HealingRestAction.Complete:\n                        // Recheck both after standing, before clearing the\n                        // requirement that prevents any fresh attack.\n                        if(ObserveRecovery().Action!=HealingRestAction.Complete)continue;\n                        healingRestPending=false;healingWarning=null;\n                        TraceLog.Record("rest recovery full and standing",new {HP=world.TargetHealth(world.LocalPlayer().Id),Posture=world.RestState().Posture.ToString()});\n                        message="Health full. Resuming hunt.";\n                        return;\n                    case HealingRestAction.Interrupted:\n                        TraceLog.Record("rest recovery interrupted after standing",new {hp.Current,hp.Maximum,Engaged=encounter.EngagedCount,Threat=threat});\n                        if(recoveryPriority!=null){healingRestPending=false;healingWarning=null;throw new PriorityTargetException(recoveryPriority);}\n                        if(threat!=null)throw new RetreatRequiredException(threat);\n                        if(HasActiveFight())throw new EngagedTargetPriorityException();\n                        if(options.GroupMode)throw new InvalidOperationException("Rest interrupted by incoming damage outside the selected tank fight; standing confirmed.");\n                        throw new RecoverUnderDamageException();\n                }\n            }\n        }\n        finally {requestingSit=false;Input.Release(preserveNearbyPickup:true);Input.Preflight=normalPreflight;healingRest=null;}\n    }\n\n    async Task<bool> TryHeal(Movement drive, Options options, CancellationToken token,decimal? recoveryTarget=null)\n    {\n        if (!options.AutoHeal) return false;\n        var self = world.LocalPlayer(); var hp = world.TargetHealth(self.Id);\n        if (!hp.Known) throw new InvalidOperationException("Player HP is unavailable; auto-heal cannot be checked.");\n        if(hp.Dead)
        {
            if(options.AutoReviveAfterDeath){deathRecoveryRequested=true;return false;}
            throw new InvalidOperationException("Character died; stopped.");
        }
        if(!recoveryTarget.HasValue && !healingRestPending && hp.Current>=hp.Maximum){healingWarning=null;return false;}\n        var bar = CheckedHotbar();\n        bool missingItem=HealingRest.MissingHealingItem(bar);\n        bool restRequested=missingItem && HealingRest.ShouldTrigger(hp,options.HealBelowPercent);\n        if(!recoveryTarget.HasValue && healingRest==null && !restRequested){healingRestPending=false;healingWarning=null;}\n        if(!recoveryTarget.HasValue && restRequested)\n        {\n            if(!healingRestPending)TraceLog.Record("no healing item; full recovery required",new {hp.Current,hp.Maximum,TriggerPercent=options.HealBelowPercent,Engaged=encounter.EngagedCount});\n            healingRestPending=true;\n            if(defensePending && !HasActiveFight())return false;\n            if(!HealingRest.MayStart(hp,bar,encounter.HasEngaged,completionReturnPending,encounter.Active,deferredLoot.Count,HasActiveFight(),lootGuardPosition.HasValue,options.HealBelowPercent))\n            {\n                healingWarning=HasActiveFight() ? "No healing item: finish the fight, then sit to full HP" :\n                    completionReturnPending ? "No healing item: return home, then sit to full HP" : "No healing item: finish pickup, then sit to full HP";\n                if(lockedTarget!=null && !HasActiveFight() && !courtesy.StartedHere(lockedTarget))throw new RecoverBeforeFreshTargetException();\n                return false;\n            }\n            healingWarning=null;\n            ClearRangedPending();\n            await RecoverToFullByResting(drive,options,token);\n            return true;\n        }\n        decimal threshold=recoveryTarget ?? options.HealBelowPercent;\n        if(recoveryTarget.HasValue && hp.Current*100.0/hp.Maximum >= (double)threshold) return false;\n        if (!ShouldHeal(hp, threshold, Environment.TickCount64, nextHealAt))\n        {\n            if(hp.Known && hp.Current*100.0/hp.Maximum>(double)threshold)healingWarning=null;\n            return false;\n        }\n        if (!bar.Slots.Any(RecoveryItems.Recognized))\n        {\n            // Finish escaping before stopping for missing supplies.\n            if(recoveryTarget.HasValue && (world.RestSupported || retreatRecovery?.Phase==RecoveryPhase.Retreating)) return false;\n            if(encounter.HasEngaged || completionReturnPending || lockedTarget is Entity fighting && courtesy.StartedHere(fighting) && !world.TargetHealth(fighting.Id).Dead)\n            {\n                if(healingWarning==null)TraceLog.Record("healing supplies missing; finishing engaged fight",new {hp.Current,hp.Maximum,Engaged=encounter.EngagedCount});\n                healingWarning=completionReturnPending && !encounter.HasEngaged ? "No healing item: finishing the return to the hunting area" : "No healing item: finishing the current fight";\n                return false;\n            }\n            throw new InvalidOperationException("No food or potion with a supported health-restoration description is slotted.");\n        }\n        healingWarning=null;\n        var slot = RecoveryItems.Choose(bar, recoveryCursor);\n        if (slot == null) return false;\n        ClearRangedPending();ReleaseCombatPickup();recoveryCursor++;\n        message = $"Using {slot.Name} on {slot.Key} Â· HP {hp.Current}/{hp.Maximum}";\n        TraceLog.Record("healing item input", new { Key = slot.Key, Item = slot.Name, hp.Current, hp.Maximum, Threshold = threshold, DuringRetreat=recoveryTarget.HasValue, RemainingBefore = slot.RemainingCooldown });\n        priorityInterruptibleActivity=true;\n        try\n        {\n            await Input.Key((Keys)slot.Key[0], 70, token);\n            nextHealAt = Environment.TickCount64 + (long)options.HealDelaySeconds * 1000;\n            if(ManaRecovery.Recognized(slot))manaRecovery.RecordUse(DateTimeOffset.UtcNow);\n            await Input.Delay(100, token);\n        }\n        finally {priorityInterruptibleActivity=false;}\n        TraceLog.Record("healing health check", new { Before = hp, After = world.TargetHealth(self.Id) });\n        return true;\n    }\n}\n\n\n\n