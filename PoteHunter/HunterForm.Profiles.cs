namespace PoteHunter;

public sealed partial class HunterForm
{
    readonly SettingsProfileStore settingsProfiles=new();
    readonly ComboBox profilePicker=new(){Name="settingsProfilePicker",DropDownStyle=ComboBoxStyle.DropDownList,Width=260};
    readonly TextBox profileName=new(){Name="settingsProfileName",Width=260,MaxLength=48};
    readonly Label profileStatus=new(){Name="settingsProfileStatus",AutoSize=true,MaximumSize=new Size(620,0)};
    readonly Button importMapToolWorld=new(){Name="importCollisionMap",Text="Import collision map…",AutoSize=true,Enabled=false};
    readonly Label collisionMapStatus=new(){Name="collisionMapStatus",AutoSize=true,MaximumSize=new Size(235,0),Text="No imported collision map loaded."};
    TabPage[] InitializeSelectedFeaturePages(TabControl tabs)
    {
        var preferences=new TabPage("Settings"){Name="preferencesPage",AutoScroll=true,Padding=new Padding(12)};
        var stack=CompactTable();
        CompactAdd(stack,new Label{Text="Application settings",AutoSize=true,Font=new Font("Segoe UI Semibold",15)});
        CompactAdd(stack,new Label{Text="Use the grouped menu to open gameplay settings, profiles and overlays.",AutoSize=true,MaximumSize=new Size(620,0)});
        var updates=new Button{Name="settingsUpdates",Text="Updates",AutoSize=true};updates.Click+=(_,_)=>OpenUpdates();
        CompactAdd(stack,updates);preferences.Controls.Add(stack);
        var profiles=new TabPage("Profiles"){Name="profilesPage",AutoScroll=true,Padding=new Padding(12)};
        var body=CompactTable();
        CompactAdd(body,new Label{Text="Settings profiles",AutoSize=true,Font=new Font("Segoe UI Semibold",15)});
        CompactAdd(body,new Label{Text="Save and switch your hunting configuration while the bot is stopped. Applying a profile backs up current settings. Routes, recovery/login profiles, logs, overlay placement and update preferences stay on this PC.",AutoSize=true,MaximumSize=new Size(620,0)});
        CompactAdd(body,CompactRow("Saved profile",profilePicker));CompactAdd(body,CompactRow("Profile name",profileName));
        Button Action(string name,string text,Action action){var b=new Button{Name=name,Text=text,AutoSize=true};b.Click+=(_,_)=>{
            if(working||busy||clientRecoveryRunning||navigation.Recording){profileStatus.Text="Stop hunting, setup and route recording before changing profiles.";return;}
            try{action();}catch(Exception ex){profileStatus.Text="Profile operation failed: "+ex.Message;}};return b;}
        string Selected()=>profilePicker.SelectedItem as string??throw new InvalidOperationException("Choose a saved profile first.");
        void Refresh(string? select=null){profilePicker.Items.Clear();profilePicker.Items.AddRange(settingsProfiles.List());if(select!=null)profilePicker.SelectedItem=select;}
        var save=Action("profileSave","Store current",()=>{
            string name=profileName.Text.Trim();
            if(settingsProfiles.Exists(name)&&MessageBox.Show(this,"Replace the saved profile “"+name+"”? A backup will be kept.","Settings profiles",MessageBoxButtons.YesNo)!=DialogResult.Yes)return;
            settingsProfiles.Save(name,CurrentOptions());Refresh(name);profileStatus.Text="Stored “"+name+"”.";});
        var apply=Action("profileApply","Apply profile",()=>{
            string name=Selected();Options local=CurrentOptions(),next=settingsProfiles.Materialize(name,local);
            string backup=settingsProfiles.Activate(name,Options.PathName,local);
            try{ApplyProfileSettings(next);profileStatus.Text="Applied “"+name+"”. Previous settings backed up.";}
            catch{File.Copy(backup,Options.PathName,true);ApplyProfileSettings(local);throw;}});
        var export=Action("profileExport","Export…",()=>{string name=Selected();using var dialog=new SaveFileDialog{Filter="PlayPoteBot profile|*.playpotebot-profile.json",FileName="settings.playpotebot-profile.json",OverwritePrompt=true};if(dialog.ShowDialog(this)==DialogResult.OK){settingsProfiles.Export(name,dialog.FileName);profileStatus.Text="Exported “"+name+"”.";}});
        var import=Action("profileImport","Import…",()=>{using var dialog=new OpenFileDialog{Filter="PlayPoteBot profile|*.playpotebot-profile.json|JSON files|*.json"};if(dialog.ShowDialog(this)!=DialogResult.OK)return;var doc=settingsProfiles.Inspect(dialog.FileName);if(settingsProfiles.Exists(doc.Name)&&MessageBox.Show(this,"Replace “"+doc.Name+"”? A backup will be kept.","Settings profiles",MessageBoxButtons.YesNo)!=DialogResult.Yes)return;string name=settingsProfiles.Import(dialog.FileName);Refresh(name);profileStatus.Text="Imported “"+name+"”. Select Apply profile to use it.";});
        var delete=Action("profileDelete","Delete",()=>{string name=Selected();if(MessageBox.Show(this,"Delete “"+name+"”? A backup will be kept.","Settings profiles",MessageBoxButtons.YesNo)==DialogResult.Yes){settingsProfiles.Delete(name);Refresh();profileStatus.Text="Deleted “"+name+"”.";}});
        CompactAdd(body,CompactFlow(save,apply,import,export,delete));CompactAdd(body,profileStatus);
        profilePicker.SelectedIndexChanged+=(_,_)=>{if(profilePicker.SelectedItem is string name)profileName.Text=name;};
        profiles.Controls.Add(body);Refresh();
        var grades=AddItemGradeOptions();
        tabs.TabPages.Add(preferences);tabs.TabPages.Add(profiles);tabs.TabPages.Add(grades);
        var routeTools=CompactTable();routeTools.Name="collisionMapTools";routeTools.MaximumSize=new Size(240,0);
        CompactAdd(routeTools,importMapToolWorld);CompactAdd(routeTools,collisionMapStatus);
        var routing=Controls.Find("navigationRoutingOptions",true).OfType<CollapsibleSection>().SingleOrDefault()
            ??navigation3DHint.Parent!.Controls.OfType<CollapsibleSection>().Single(s=>s.Name=="navigationRoutingOptions");
        CompactAdd(routing.Content,routeTools);
        importMapToolWorld.Click+=async(_,_)=>await ImportMapToolWorld();
        timer.Tick+=(_,_)=>{
            foreach(var b in new[]{save,apply,import,export,delete})b.Enabled=!working&&!busy&&!clientRecoveryRunning&&!navigation.Recording;
            importMapToolWorld.Enabled=connected&&!working&&!busy&&!clientRecoveryRunning&&!navigation.Recording;
            RefreshImportedCollisionObstacles();};
        return [preferences,profiles,grades];
    }
    string importedCollisionKey="";
    static readonly RouteObstacle[] NoCollisionObstacles=[];
    void RefreshImportedCollisionObstacles()
    {
        try
        {
            int zone=connected&&world.ConnectionVerified?world.ActiveZone():0;
            string key=zone>0?world.ClientHash+":"+zone:"";
            if(key==importedCollisionKey)return;
            var blockers=key.Length==0?NoCollisionObstacles:zoneMapBackground.NavigationObstacles(zone,world.ClientHash);
            navigation.SetImportedObstacles(blockers);importedCollisionKey=key;
            collisionMapStatus.Text=blockers.Length==0?"No compatible imported collision map loaded.":$"{blockers.Length:N0} imported collision blockers active in Zone {zone}.";
        }
        catch(Exception ex)when(ex is not OutOfMemoryException and not AccessViolationException)
        {navigation.SetImportedObstacles(NoCollisionObstacles);importedCollisionKey="";collisionMapStatus.Text="Collision map unavailable; reconnect to refresh.";}
    }
}
