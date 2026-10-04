namespace PoteHunter;

/// <summary>Tab control whose pages are selected by the application navigation.</summary>
internal sealed class HeaderlessTabControl : TabControl
{
    public HeaderlessTabControl()
    {
        Appearance = TabAppearance.FlatButtons;
        SizeMode = TabSizeMode.Fixed;
        ItemSize = new Size(0, 1);
        Multiline = true;
    }

    public override Rectangle DisplayRectangle => new(0, 0, Width, Height);
}

public sealed partial class HunterForm
{
    const int UiCornerRadius = 13;
    // Wayfinder command: midnight surfaces and warm brass accents.
    static readonly Color UiWindow = ImperialTheme.Window;
    static readonly Color UiSidebar = ImperialTheme.Surface;
    static readonly Color UiSurface = ImperialTheme.Surface;
    static readonly Color UiRaised = ImperialTheme.Raised;
    static readonly Color UiBorder = ImperialTheme.Border;
    static readonly Color UiText = ImperialTheme.Text;
    static readonly Color UiMuted = ImperialTheme.Muted;
    static readonly Color UiAccent = ImperialTheme.Gold;
    static readonly Color UiAccentDark = Color.FromArgb(52, 43, 30);
    static readonly Color UiDanger = Color.FromArgb(226, 146, 121);

    /// <summary>
    /// Rehomes the constructor-built controls in the compact application shell.
    /// Call once, at the end of the constructor, after every page has been added.
    /// </summary>
    void ApplyModernLayout(TableLayoutPanel legacyRoot, TabControl tabs)
    {
        SuspendLayout();

        Text = "PlayPoteBot · Adventurer’s Compass";
        using(var stream=typeof(HunterForm).Assembly.GetManifestResourceStream("PoteHunter.PlayPoteBotIcon"))
            if(stream!=null)Icon=new Icon(stream);
        FormClosed+=(_,_)=>Icon?.Dispose();
        Font = new Font("Segoe UI", 10f);
        SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
        HandleCreated += (_, _) => ApplyDarkTitleBar();
        MinimumSize = new Size(1120, 760);
        Size = new Size(1280, 1120);
        BackColor = UiWindow;
        ForeColor = UiText;
        Padding = Padding.Empty;

        // The setup table remains the same instance, so all existing event handlers,
        // option reads and working-state enable/disable behavior continue to apply.
        settings.Dock = DockStyle.Top;
        settings.AutoSize = true;
        settings.Padding = new Padding(0, 2, 0, 0);
        ArrangeCompactSettings();
        settings.BackColor = UiWindow;
        var setupPage = new TabPage("Hunt setup")
        {
            BackColor = UiWindow,
            ForeColor = UiText,
            AutoScroll = true,
            Padding = new Padding(0)
        };
        setupPage.Controls.Add(settings);
        tabs.TabPages.Insert(0, setupPage);
        var packs=tabs.TabPages.Cast<TabPage>().FirstOrDefault(p=>p.Text=="Ranged packs");
        if(packs!=null){tabs.TabPages.Remove(packs);tabs.TabPages.Insert(1,packs);}

        tabs.Dock = DockStyle.Fill;
        tabs.Margin = Padding.Empty;
        tabs.Padding = Point.Empty;
        tabs.SelectedIndex = 0;
        var indexPage=new TabPage("Index"){AutoScroll=true,Padding=new Padding(16)};
        indexPage.Controls.Add(new Label{AutoSize=true,MaximumSize=new Size(620,0),Text=
            "HOTKEYS\n\nF6 — Calibrate movement and turning (hunt stopped).\nF8 — Calibrate if needed, start hunting; press again to stop.\nF9 — Stop the current operation.\nHome — Start recording the selected route.\nEnd — Finish and save the selected route, including facing.\n\nHome / End require connection, stopped hunting, and the game or PlayPoteBot in front. F6 / F8 require the game in front.\n\nSTOP GUARDS\nEscape stops automated input. Manually pressing Enter for chat or switching away from the game also stops input.\n\nRETURN TO ANCHOR\nChoose a target preset or name filter in Overview first. Each target selection has its own Primary route and two alternatives; custom filters are separate, and capitalization does not matter. Older routes are preserved as unassigned: select the correct target, then use Assign existing routes in Navigation on an empty set. In Navigation, select Primary or an Alternative slot. At the route start press Home, walk to the farming anchor, face the targets, then press End. A saved spot alone is not a route.\n\nTo run the route, stand within the configured route corridor of its recorded path and press F8 with the game in front. Set Route corridor in Overview > Route and anchor radii (default 10 map units, adjustable 0.5–30). The selected compatible slot is preferred; otherwise the nearest compatible route is used. The character joins the path, follows it to the anchor, restores facing, and hunts selected targets. Outside the configured corridor, Start uses the activation location as usual. Solo hunting only.\n\nFor death recovery, enable Setup > Death recovery > Revive + return to anchor and Resume farming on arrival. Enable alternatives in Navigation and record them for the same target selection from the same revival start for occupied-spot fallback. Startup, recovery and fallback only use that target's routes. Changing targets during recording cancels the unfinished route; clearing target routes leaves other selections intact. Test revival leaves hunting stopped; it does not run the return route.\n\nGAME INPUT\nW / S move forward / backward; A / D strafe. Attack, skill, healing, potion and revive keys follow your configured hotbar and recovery settings; they are not global app hotkeys."});
        InitializeSessionLogIndex(indexPage);
        tabs.TabPages.Add(indexPage);
        if(groupPage.Controls.OfType<TableLayoutPanel>().FirstOrDefault() is { } groupLayout &&
            groupLayout.Controls.OfType<FlowLayoutPanel>().FirstOrDefault() is { } groupHeader)
        {
            groupHeader.WrapContents=true;groupHeader.AutoSize=true;groupHeader.Dock=DockStyle.Top;
            groupLayout.RowStyles[0].SizeType=SizeType.AutoSize;
        }

        var shell = new TableLayoutPanel
        {
            Name = "fieldConsoleShell", Dock = DockStyle.Fill, RowCount = 5, ColumnCount = 1,
            Margin = Padding.Empty, Padding = new Padding(14, 0, 14, 10), BackColor = UiWindow
        };
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach(int height in new[]{56, 42, 64})shell.RowStyles.Add(new RowStyle(SizeType.Absolute,height));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute,36));

        var masthead = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = Padding.Empty,
            BackColor = UiWindow
        };
        masthead.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        masthead.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,130));
        masthead.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,92));
        masthead.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        masthead.Controls.Add(new Label
        {
            Name = "ironboundMasthead", Text = "PlayPoteBot   /   ADVENTURER’S COMPASS", Dock = DockStyle.Fill,
            Padding = new Padding(64, 0, 0, 0),
            Font = new Font("Georgia",16f), ForeColor = UiAccent,
            TextAlign = ContentAlignment.MiddleLeft, Margin = Padding.Empty
        },0,0);
        masthead.Controls[0].Paint += (_, e) => {
            e.Graphics.InterpolationMode=System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            e.Graphics.DrawImage(ImperialTheme.Logo.Value,new Rectangle(0,5,58,58));
        };
        masthead.Controls.Add(new Label
        {
            Text = AppUpdates.CurrentVersion, Dock = DockStyle.Fill,
            Font = new Font("Consolas",9f), ForeColor = UiMuted,
            TextAlign = ContentAlignment.MiddleRight, Margin = new Padding(0,0,14,0)
        },1,0);
        var connectionBadge = new Label
        {
            Name = "fieldConnectionStatus", Size = new Size(92,26), Anchor = AnchorStyles.Right,
            TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Consolas",9f),
            BackColor = UiRaised, ForeColor = UiMuted, Text = "Offline", Margin = Padding.Empty
        };
        masthead.Controls.Add(connectionBadge,2,0);shell.Controls.Add(masthead,0,0);

        var nav = new FlowLayoutPanel
        {
            Name = "fieldNavigation", Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false, Margin = Padding.Empty, BackColor = UiWindow
        };
        shell.Controls.Add(nav,0,1);
        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1,
            Margin = new Padding(0,6,0,8), BackColor = UiWindow
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,180));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        var heading = new TableLayoutPanel {Dock=DockStyle.Fill,RowCount=2,ColumnCount=1,Margin=Padding.Empty};
        heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        heading.RowStyles.Add(new RowStyle(SizeType.Percent,55));
        heading.RowStyles.Add(new RowStyle(SizeType.Percent,45));
        var pageTitle = new Label {Dock=DockStyle.Fill,Text="Setup",Font=new Font("Georgia",19f),ForeColor=UiText,TextAlign=ContentAlignment.BottomLeft,Margin=Padding.Empty};
        var pageSubtitle = new Label {Dock=DockStyle.Fill,Font=new Font("Segoe UI",9f),ForeColor=UiMuted,TextAlign=ContentAlignment.TopLeft,Margin=Padding.Empty};
        heading.Controls.Add(pageTitle,0,0);heading.Controls.Add(pageSubtitle,0,1);header.Controls.Add(heading,0,0);
        var pagePickers = new Panel {Dock=DockStyle.Fill,Margin=new Padding(4,12,8,0),BackColor=UiWindow};
        header.Controls.Add(pagePickers,1,0);
        var actions = new FlowLayoutPanel {AutoSize=true,Anchor=AnchorStyles.Right,WrapContents=false,Margin=Padding.Empty};
        actions.Controls.AddRange([connect,start,stop]);connect.Text="Connect";start.Text="Start  F8";stop.Text="Stop  F9";
        foreach(var action in new[]{connect,start,stop})
        {action.AutoSize=false;action.Size=new Size(114,36);action.Margin=new Padding(5,0,0,0);}
        header.Controls.Add(actions,2,0);shell.Controls.Add(header,0,2);shell.Controls.Add(tabs,0,3);

        var footer = new TableLayoutPanel
        {
            Dock=DockStyle.Fill,ColumnCount=5,RowCount=1,Margin=new Padding(0,6,0,0),
            Padding=new Padding(9,0,9,0),BackColor=UiSurface
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,156));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,156));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,144));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,24));
        footer.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        status.Dock=DockStyle.Fill;status.ForeColor=UiMuted;status.TextAlign=ContentAlignment.MiddleLeft;
        status.Font=new Font("Segoe UI",9f);status.AutoEllipsis=true;
        if(string.IsNullOrWhiteSpace(status.Text))status.Text="Connect the game to begin.";
        var hpMeter=new FieldVitalMeter("HP",Color.FromArgb(179,109,91));
        var mpMeter=new FieldVitalMeter("MP",Color.FromArgb(110,150,159));
        footer.Controls.Add(status,0,0);footer.Controls.Add(hpMeter,1,0);footer.Controls.Add(mpMeter,2,0);
        footer.Controls.Add(new Label{Text="F8 START / F9 STOP",Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleCenter,ForeColor=UiMuted,Font=new Font("Consolas",8f)},3,0);
        var help=new Label{Text="?",Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleCenter,ForeColor=UiAccent,Cursor=Cursors.Help};
        priorityHint.SetToolTip(help,SanitizeDisplayText(hint.Text));footer.Controls.Add(help,4,0);shell.Controls.Add(footer,0,4);
        // Keep the original reading label alive for the existing tick handler.
        // Meters only format these readings; they never query or control the game.
        position.Visible=false;footer.Controls.Add(position,0,0);
        void RefreshVitals()
        {
            hpMeter.SetReading(connected?position.Text:"");mpMeter.SetReading(connected?position.Text:"");
            string details=connected?priorityHint.GetToolTip(position)??"":"Connect the game to read vitals.";
            priorityHint.SetToolTip(hpMeter,hpMeter.ReadingText+"\n"+details);
            priorityHint.SetToolTip(mpMeter,mpMeter.ReadingText+"\n"+details);
        }
        position.TextChanged+=(_,_)=>RefreshVitals();
        var subtitles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Hunt setup"] = "Choose targets, ranges and automatic actions.",
            ["Targets"] = "Live creatures and eligibility at a glance.",
            ["Healer / Buffs"] = "Healing, party support and effect upkeep.",
            ["Ground loot"] = "Nearby drops detected from the game client.",
            ["Hotbar"] = "Detected abilities, cooldowns and assigned roles.",
            ["Protection"] = "Keep-away rules and player courtesy settings.",
            ["Navigation"] = "3D terrain, game maps and saved route controls.",
            ["Group"] = "Party roster and follow status. Choose the tank in Setup.",
            ["Ranged packs"] = "Tag several monsters, gather them, then clear."
        };
        var overviewPage=CreateFieldOverview(tabs,setupPage);
        tabs.TabPages.Add(overviewPage);
        var monitors = new[]{monstersPageFor(tabs),lootPage,hotbarPage,groupPage};
        var advancedPages = new[]{protectionPage}.Concat(packs==null?Array.Empty<TabPage>():new[]{packs}).ToArray();
        var sections=new[]{("Overview",new[]{overviewPage}),("Hunt",new[]{overviewPage,setupPage,supportPage}),("Routes",new[]{navigationPage}),("Recovery",new[]{setupPage}),("Settings",monitors.Concat(advancedPages).Append(indexPage).ToArray())};
        string selectedSection="Overview";
        var navButtons=new List<(Button Button,TabPage[] Pages,ComboBox? Picker)>();
        foreach(var (name,pages) in sections)
        {
            var button=new Button{Text=name,Tag=pages[0],FlatStyle=FlatStyle.Flat,BackColor=UiSidebar,ForeColor=UiMuted,
                Name="fieldNav"+name,TextAlign=ContentAlignment.MiddleCenter,Size=new Size(105,34),Margin=new Padding(0,0,5,0),Padding=Padding.Empty,Font=new Font("Segoe UI Semibold",10f)};
            button.FlatAppearance.BorderSize=1;button.FlatAppearance.BorderColor=UiBorder;button.Cursor=Cursors.Hand;
            button.FlatAppearance.MouseOverBackColor=UiRaised;
            RoundControl(button,UiCornerRadius);nav.Controls.Add(button);
            ComboBox? picker=null;
            if(pages.Length>1)
            {
                picker=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,DisplayMember="Text",Name="fieldPicker"+name,Width=166,Visible=false,Dock=DockStyle.Top,Margin=Padding.Empty};
                picker.Items.AddRange(pages);picker.SelectedIndex=0;pagePickers.Controls.Add(picker);
                picker.SelectionChangeCommitted+=(_,_)=>{if(picker.SelectedItem is TabPage page)tabs.SelectedTab=page;};
            }
            button.Click+=(_,_)=>
            {
                selectedSection=name;tabs.SelectedTab=picker?.SelectedItem as TabPage??pages[0];RefreshNavigation();
                if(name=="Recovery")setupPage.ScrollControlIntoView(autoRevive);
            };
            navButtons.Add((button,pages,picker));
        }
        void RefreshNavigation()
        {
            if(tabs.SelectedTab is not TabPage selected)return;
            pageTitle.Text=selected==setupPage?"Setup":FriendlyPageName(selected.Text);
            pageSubtitle.Text=selected==overviewPage?"Your hunt, recovery and session at a glance.":selected==setupPage?"Your mode, recovery and skills.":subtitles.GetValueOrDefault(selected.Text,"Live details and configuration.");
            pageSubtitle.AutoEllipsis=true;
            foreach(var (button,pages,picker) in navButtons)
            {
                bool active=pages.Contains(selected) && (selected==setupPage ? button.Text==(selectedSection=="Recovery"?"Recovery":"Hunt")
                    : selected!=overviewPage || button.Text==(selectedSection=="Hunt"?"Hunt":"Overview"));button.BackColor=active?UiAccentDark:UiSidebar;button.ForeColor=active?UiAccent:UiMuted;
                button.FlatAppearance.BorderColor=active?UiAccent:UiBorder;
                if(picker!=null){picker.Visible=active;if(active)picker.SelectedItem=selected;}
            }
        }
        tabs.SelectedIndexChanged+=(_,_)=>RefreshNavigation();

        ThemeTree(shell);
        StyleActionButton(connect, UiRaised, UiText);
        StyleActionButton(start, UiAccent, UiWindow);
        start.Font=new Font("Segoe UI Semibold",9f);
        RoundControl(connectionBadge,UiCornerRadius);
        RoundControl(footer,UiCornerRadius);
        setupPage.BackColor=UiWindow;
        supportPage.BackColor=UiWindow;
        StyleActionButton(stop, UiRaised, UiDanger);
        WireCompactSaving(shell);

        // Retained controls have already been reparented. Dispose the now-empty
        // legacy composition tree so it cannot affect sizing or tab order.
        Controls.Remove(legacyRoot);
        legacyRoot.Dispose();
        ApplyImperialShell(shell, masthead, nav, header, tabs, footer);
        Controls.Add(shell);
        shell.BringToFront();

        timer.Tick += (_, _) =>
        {
            compactMode.Enabled = !working && !busy;
            RefreshVitals();
            if (working)
            {
                connectionBadge.Text = "Hunting";
                connectionBadge.BackColor = UiAccentDark;
                connectionBadge.ForeColor = Color.White;
            }
            else if (busy)
            {
                connectionBadge.Text = "Working";
                connectionBadge.BackColor = Color.FromArgb(87, 76, 42);
                connectionBadge.ForeColor = Color.White;
            }
            else if (connected)
            {
                connectionBadge.Text = "Connected";
                connectionBadge.BackColor = UiRaised;
                connectionBadge.ForeColor = UiAccent;
            }
            else
            {
                connectionBadge.Text = "Offline";
                connectionBadge.BackColor = UiRaised;
                connectionBadge.ForeColor = UiMuted;
            }
        };

        tabs.SelectedTab=overviewPage;refreshOverview?.Invoke();
        RefreshNavigation();
        SanitizeDisplayTree(shell);
        WireDisplaySanitizer(shell);
        ResumeLayout(true);
    }

    static TabPage monstersPageFor(TabControl tabs)=>tabs.TabPages.Cast<TabPage>().Single(p=>p.Text=="Targets");

    static string FriendlyPageName(string text) => text switch
    {
        "Healer / Buffs" => "Support",
        "Ground loot" => "Loot",
        "Ranged Pull" => "Ranged pull",
        _ => text
    };

    void ThemeTree(Control root)
    {
        foreach (Control control in root.Controls)
        {
            if(control is Button crownButton && control is not CollapsibleSectionHeader)CrownfireControls.Button(crownButton);
            if(control is CheckBox crownToggle)CrownfireControls.Toggle(crownToggle);
            if (control is TabPage or Panel or TableLayoutPanel or FlowLayoutPanel)
            {
                if (control.BackColor == SystemColors.Control || control is TabPage)
                    control.BackColor = UiSurface;
                control.ForeColor = UiText;
            }
            else if (control is TextBoxBase textBox)
            {
                textBox.BackColor = UiRaised;
                textBox.ForeColor = UiText;
                textBox.BorderStyle = BorderStyle.FixedSingle;
            }
            else if (control is ComboBox combo)
            {
                combo.BackColor = UiRaised;
                combo.ForeColor = UiText;
                combo.FlatStyle = FlatStyle.Flat;
                if(combo.DropDownStyle==ComboBoxStyle.DropDownList)
                {
                    combo.DrawMode=DrawMode.OwnerDrawFixed;combo.ItemHeight=22;
                    combo.DrawItem+=(_,e)=>
                    {
                        using var brush=new SolidBrush((e.State&DrawItemState.Selected)!=0?UiRaised:UiSurface);
                        e.Graphics.FillRectangle(brush,e.Bounds);
                        string text=(e.Index>=0?combo.GetItemText(combo.Items[e.Index]):combo.Text)??"";
                        TextRenderer.DrawText(e.Graphics,text,combo.Font,Rectangle.Inflate(e.Bounds,-4,0),UiText,TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);
                        e.DrawFocusRectangle();
                    };
                }
            }
            else if (control is NumericUpDown number)
            {
                number.BackColor = UiRaised;
                number.ForeColor = UiText;
                number.BorderStyle = BorderStyle.FixedSingle;
            }
            else if (control is ListView rows)
            {
                rows.BackColor = UiSurface;
                rows.ForeColor = UiText;
                rows.BorderStyle = BorderStyle.None;
                rows.FullRowSelect = true;
                rows.OwnerDraw=true;
                rows.DrawColumnHeader+=(_,e)=>
                {
                    using var background=new SolidBrush(UiRaised);
                    using var border=new Pen(UiBorder);
                    e.Graphics.FillRectangle(background,e.Bounds);
                    e.Graphics.DrawLine(border,e.Bounds.Right-1,e.Bounds.Top,e.Bounds.Right-1,e.Bounds.Bottom);
                    TextRenderer.DrawText(e.Graphics,e.Header?.Text,rows.Font,Rectangle.Inflate(e.Bounds,-6,0),UiText,
                        TextFormatFlags.VerticalCenter|TextFormatFlags.Left|TextFormatFlags.EndEllipsis);
                };
                rows.DrawItem+=(_,e)=>{if(rows.View!=View.Details)e.DrawDefault=true;};
                rows.DrawSubItem+=(_,e)=>e.DrawDefault=true;
                void FillLastColumn()
                {
                    if(rows.Columns.Count==0)return;
                    int used=rows.Columns.Cast<ColumnHeader>().Take(rows.Columns.Count-1).Sum(c=>c.Width);
                    rows.Columns[rows.Columns.Count-1].Width=Math.Max(90,rows.ClientSize.Width-used-4);
                }
                rows.Resize+=(_,_)=>FillLastColumn();FillLastColumn();
                var empty=new Label{Text="Connect the game to view live data.",AutoSize=false,TextAlign=ContentAlignment.MiddleCenter,
                    ForeColor=UiMuted,BackColor=UiSurface,Height=70,Top=44,Left=12,Width=Math.Max(100,rows.ClientSize.Width-24),Anchor=AnchorStyles.Top|AnchorStyles.Left|AnchorStyles.Right};
                rows.Controls.Add(empty);
                timer.Tick+=(_,_)=>{empty.Visible=rows.Items.Count==0;empty.Text=connected?"No entries in the current scene.":"Connect the game to view live data.";};
            }
            else if(control is CheckBox check)
            {
                check.ForeColor=UiText;check.FlatStyle=FlatStyle.Flat;
                check.FlatAppearance.CheckedBackColor=UiAccentDark;
                check.FlatAppearance.BorderColor=UiBorder;
            }
            else if (control is DataGridView grid)
            {
                grid.BackgroundColor = UiSurface;
                grid.GridColor = UiBorder;
                grid.BorderStyle = BorderStyle.None;
                grid.EnableHeadersVisualStyles = false;
                grid.DefaultCellStyle.BackColor = UiSurface;
                grid.DefaultCellStyle.ForeColor = UiText;
                grid.DefaultCellStyle.SelectionBackColor = UiAccentDark;
                grid.DefaultCellStyle.SelectionForeColor = Color.White;
                grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
                grid.ColumnHeadersDefaultCellStyle.BackColor = UiRaised;
                grid.ColumnHeadersDefaultCellStyle.ForeColor = UiText;
            }
            else if (control is Button button && button.Tag is not TabPage)
            {
                StyleActionButton(button, UiRaised, UiText);
            }
            else if (control is Label label && label.ForeColor == SystemColors.ControlText)
            {
                label.ForeColor = UiText;
            }

            ThemeTree(control);
        }

        // Threat checkboxes intentionally retain their semantic colors.
        foreach (var box in difficultyBoxes)
            box.Value.ForeColor = MonsterDefinition.DisplayColor(box.Key);
    }

    static void StyleActionButton(Button button, Color background, Color foreground)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.BorderColor = background == UiAccent ? UiAccent : UiBorder;
        button.BackColor = background;
        button.ForeColor = foreground;
        button.Padding = new Padding(6, 2, 6, 2);
        button.MinimumSize = new Size(0, 30);
        button.Cursor = Cursors.Hand;
        button.FlatAppearance.MouseOverBackColor=ControlPaint.Light(background,0.12f);
        button.FlatAppearance.MouseDownBackColor=ControlPaint.Dark(background,0.10f);
        RoundControl(button,UiCornerRadius);
    }

    static string SanitizeDisplayText(string text) => text
        .Replace("Â·", "·", StringComparison.Ordinal)
        .Replace("â€¦", "…", StringComparison.Ordinal)
        .Replace("â€™", "’", StringComparison.Ordinal)
        .Replace("â€“", "–", StringComparison.Ordinal)
        .Replace("â€”", "—", StringComparison.Ordinal);

    static void SanitizeDisplayTree(Control root)
    {
        if (root is Label or Button or CheckBox or RadioButton or TabPage)
            root.Text = SanitizeDisplayText(root.Text);
        foreach (Control child in root.Controls)
            SanitizeDisplayTree(child);
    }

    static void WireDisplaySanitizer(Control root)
    {
        if (root is Label or Button or CheckBox or RadioButton or TabPage)
        {
            root.TextChanged += (_, _) =>
            {
                var clean = SanitizeDisplayText(root.Text);
                if (!string.Equals(clean, root.Text, StringComparison.Ordinal)) root.Text = clean;
            };
        }
        foreach (Control child in root.Controls)
            WireDisplaySanitizer(child);
    }
}

