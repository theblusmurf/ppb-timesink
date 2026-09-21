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
    static readonly Color UiWindow = Color.FromArgb(15, 20, 27);
    static readonly Color UiSidebar = Color.FromArgb(18, 25, 34);
    static readonly Color UiSurface = Color.FromArgb(23, 31, 42);
    static readonly Color UiRaised = Color.FromArgb(32, 43, 56);
    static readonly Color UiBorder = Color.FromArgb(48, 63, 78);
    static readonly Color UiText = Color.FromArgb(234, 242, 247);
    static readonly Color UiMuted = Color.FromArgb(155, 174, 191);
    static readonly Color UiAccent = Color.FromArgb(94, 225, 201);
    static readonly Color UiAccentDark = Color.FromArgb(28, 108, 99);
    static readonly Color UiDanger = Color.FromArgb(242, 153, 159);

    /// <summary>
    /// Rehomes the constructor-built controls in the compact application shell.
    /// Call once, at the end of the constructor, after every page has been added.
    /// </summary>
    void ApplyModernLayout(TableLayoutPanel legacyRoot, TabControl tabs)
    {
        SuspendLayout();

        Text = "PoteHunter";
        Font = new Font("Segoe UI", 10f);
        SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
        HandleCreated += (_, _) => ApplyDarkTitleBar();
        MinimumSize = new Size(900, 640);
        Size = new Size(980, 700);
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
        if(groupPage.Controls.OfType<TableLayoutPanel>().FirstOrDefault() is { } groupLayout &&
            groupLayout.Controls.OfType<FlowLayoutPanel>().FirstOrDefault() is { } groupHeader)
        {
            groupHeader.WrapContents=true;groupHeader.AutoSize=true;groupHeader.Dock=DockStyle.Top;
            groupLayout.RowStyles[0].SizeType=SizeType.AutoSize;
        }

        var shell = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 1,
            ColumnCount = 2,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            BackColor = UiWindow
        };
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 164));
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var sidebar = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 3,
            ColumnCount = 1,
            Padding = new Padding(10, 18, 10, 14),
            BackColor = UiSidebar
        };
        sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        sidebar.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
        sidebar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));

        var brand = new Label
        {
            Text = "PoteHunter",
            AutoSize = false,
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI Semibold", 13),
            ForeColor = UiText,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(8, 0, 0, 0)
        };
        sidebar.Controls.Add(brand, 0, 0);

        var nav = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            Padding = new Padding(0, 8, 0, 8),
            Margin = Padding.Empty,
            BackColor = UiSidebar
        };
        sidebar.Controls.Add(nav, 0, 1);

        var shortcut = new Label
        {
            Text = "F8   START\nF9   STOP",
            Dock = DockStyle.Fill,
            ForeColor = UiMuted,
            Font = new Font("Segoe UI", 8.5f),
            Padding = new Padding(8, 7, 0, 0)
        };
        sidebar.Controls.Add(shortcut, 0, 2);

        var main = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 3,
            ColumnCount = 1,
            Padding = new Padding(18, 14, 18, 12),
            BackColor = UiWindow
        };
        main.RowStyles.Add(new RowStyle(SizeType.Absolute, 66));
        main.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        main.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        main.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));

        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            Margin = new Padding(0, 0, 0, 10),
            BackColor = UiWindow
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.RowStyles.Add(new RowStyle(SizeType.Percent,100));

        var heading = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Margin = Padding.Empty };
        heading.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
        heading.RowStyles.Add(new RowStyle(SizeType.Percent, 45));
        heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        var pageTitle = new Label { Dock = DockStyle.Fill, Text = "Hunt setup", Font = new Font("Segoe UI Semibold", 18), ForeColor = UiText, TextAlign = ContentAlignment.BottomLeft };
        var pageSubtitle = new Label { Dock = DockStyle.Fill, Text = "Choose targets, ranges and automatic actions.", Font = new Font("Segoe UI", 9), ForeColor = UiMuted, TextAlign = ContentAlignment.TopLeft };
        heading.Controls.Add(pageTitle, 0, 0);
        heading.Controls.Add(pageSubtitle, 0, 1);
        header.Controls.Add(heading, 0, 0);

        var connectionBadge = new Label
        {
            AutoSize = false,
            Size = new Size(78, 28),
            Margin = new Padding(8, 13, 8, 0),
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI Semibold", 8),
            BackColor = UiRaised,
            ForeColor = UiMuted,
            Text = "Offline"
        };
        header.Controls.Add(connectionBadge, 1, 0);

        var actions = new FlowLayoutPanel
        {
            AutoSize = true,
            Anchor = AnchorStyles.Right,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0, 6, 0, 0)
        };
        actions.Controls.AddRange([connect, start, stop]);
        connect.Text="Connect";start.Text="Start";stop.Text="Stop";
        foreach(var action in new[]{connect,start,stop}){action.AutoSize=false;action.Size=new Size(92,34);action.Margin=new Padding(4,0,0,0);}
        header.Controls.Add(actions, 2, 0);
        main.Controls.Add(header, 0, 0);
        main.Controls.Add(tabs, 0, 1);

        var footer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            Margin = new Padding(0, 8, 0, 0),
            Padding = new Padding(12, 0, 12, 0),
            BackColor = UiSurface
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 64));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 36));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 34));
        status.Dock = DockStyle.Fill;
        status.ForeColor = UiText;
        status.TextAlign = ContentAlignment.MiddleLeft;
        if(string.IsNullOrWhiteSpace(status.Text))status.Text="Connect the game to begin.";
        status.AutoEllipsis=true;
        position.Dock = DockStyle.Fill;
        position.ForeColor = UiMuted;
        position.TextAlign = ContentAlignment.MiddleRight;
        position.AutoEllipsis=true;
        var help = new Label { Text = "?", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Segoe UI Semibold", 11), ForeColor = UiMuted, Cursor = Cursors.Help };
        priorityHint.SetToolTip(help, SanitizeDisplayText(hint.Text));
        footer.Controls.Add(status, 0, 0);
        footer.Controls.Add(position, 1, 0);
        footer.Controls.Add(help, 2, 0);
        main.Controls.Add(footer, 0, 2);

        shell.Controls.Add(sidebar, 0, 0);
        shell.Controls.Add(main, 1, 0);

        var subtitles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Hunt setup"] = "Choose targets, ranges and automatic actions.",
            ["Targets"] = "Live creatures and eligibility at a glance.",
            ["Healer / Buffs"] = "Healing, party support and effect upkeep.",
            ["Ground loot"] = "Nearby drops detected from the game client.",
            ["Hotbar"] = "Detected abilities, cooldowns and assigned roles.",
            ["Protection"] = "Keep-away rules and player courtesy settings.",
            ["Navigation"] = "Observed movement space and obstacle routing.",
            ["Group"] = "Party roster and follow status. Choose the tank in Setup.",
            ["Ranged packs"] = "Tag several monsters, gather them, then clear."
        };
        var monitors = new[]{monstersPageFor(tabs),lootPage,hotbarPage,groupPage};
        var advancedPages = new[]{protectionPage,navigationPage}.Concat(packs==null?Array.Empty<TabPage>():new[]{packs}).ToArray();
        var sections=new[]{("Setup",new[]{setupPage}),("Support",new[]{supportPage}),("Monitor",monitors),("Advanced",advancedPages)};
        var navButtons=new List<(Button Button,TabPage[] Pages,ComboBox? Picker)>();
        foreach(var (name,pages) in sections)
        {
            var button=new Button{Text=name,Tag=pages[0],FlatStyle=FlatStyle.Flat,BackColor=UiSidebar,ForeColor=UiMuted,
                TextAlign=ContentAlignment.MiddleLeft,Size=new Size(138,40),Margin=new Padding(0,2,0,2),Padding=new Padding(10,0,0,0)};
            button.FlatAppearance.BorderSize=0;button.Cursor=Cursors.Hand;
            button.FlatAppearance.MouseOverBackColor=UiRaised;
            RoundControl(button,8);nav.Controls.Add(button);
            ComboBox? picker=null;
            if(pages.Length>1)
            {
                picker=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,DisplayMember="Text",Width=138,Visible=false,Margin=new Padding(0,4,0,12)};
                picker.Items.AddRange(pages);picker.SelectedIndex=0;nav.Controls.Add(picker);
                picker.SelectionChangeCommitted+=(_,_)=>{if(picker.SelectedItem is TabPage page)tabs.SelectedTab=page;};
            }
            button.Click+=(_,_)=>tabs.SelectedTab=picker?.SelectedItem as TabPage??pages[0];
            navButtons.Add((button,pages,picker));
        }
        void RefreshNavigation()
        {
            if(tabs.SelectedTab is not TabPage selected)return;
            pageTitle.Text=selected==setupPage?"Setup":FriendlyPageName(selected.Text);
            pageSubtitle.Text=selected==setupPage?"Your mode, recovery and skills.":subtitles.GetValueOrDefault(selected.Text,"Live details and configuration.");
            pageSubtitle.AutoEllipsis=true;
            foreach(var (button,pages,picker) in navButtons)
            {
                bool active=pages.Contains(selected);button.BackColor=active?UiAccentDark:UiSidebar;button.ForeColor=active?UiText:UiMuted;
                if(picker!=null){picker.Visible=active;if(active)picker.SelectedItem=selected;}
            }
        }
        tabs.SelectedIndexChanged+=(_,_)=>RefreshNavigation();

        ThemeTree(shell);
        StyleActionButton(connect, UiRaised, UiText);
        StyleActionButton(start, UiAccent, UiWindow);
        start.Font=new Font("Segoe UI Semibold",9f);
        RoundControl(connectionBadge,10);
        RoundControl(footer,8);
        setupPage.BackColor=UiWindow;
        supportPage.BackColor=UiWindow;
        StyleActionButton(stop, UiRaised, UiDanger);
        WireCompactSaving(shell);

        // Retained controls have already been reparented. Dispose the now-empty
        // legacy composition tree so it cannot affect sizing or tab order.
        Controls.Remove(legacyRoot);
        legacyRoot.Dispose();
        Controls.Add(shell);
        shell.BringToFront();

        timer.Tick += (_, _) =>
        {
            compactMode.Enabled = !working && !busy;
            if (working)
            {
                connectionBadge.Text = "Hunting";
                connectionBadge.BackColor = UiAccentDark;
                connectionBadge.ForeColor = Color.White;
            }
            else if (busy)
            {
                connectionBadge.Text = "Working";
                connectionBadge.BackColor = Color.FromArgb(116, 88, 36);
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
                    combo.DrawMode=DrawMode.OwnerDrawFixed;combo.ItemHeight=24;
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
                number.BorderStyle = BorderStyle.None;
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
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.BorderColor = background == UiRaised ? UiBorder : background;
        button.BackColor = background;
        button.ForeColor = foreground;
        button.Padding = new Padding(6, 2, 6, 2);
        button.MinimumSize = new Size(0, 34);
        button.Cursor = Cursors.Hand;
        button.FlatAppearance.MouseOverBackColor=ControlPaint.Light(background,0.12f);
        button.FlatAppearance.MouseDownBackColor=ControlPaint.Dark(background,0.10f);
        RoundControl(button,7);
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

