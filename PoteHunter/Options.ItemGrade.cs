namespace PoteHunter;

public sealed partial class Options
{
    /// <summary>Item grades on: hovering an item shows its stat grades and the gems needed for the target grade.</summary>
    public bool ItemGradeOverlayEnabled {get;set;}=true;
    /// <summary>Show the box by itself whenever the game shows an item tooltip (no key needed).</summary>
    public bool ItemGradeAutoShow {get;set;}=true;
    /// <summary>"Auto": a stat at AAA shows the gap to S, every other stat the gap to AAA. Or a fixed grade: B, A, AA, AAA, S.</summary>
    public string ItemGradeTarget {get;set;}="Auto";
    /// <summary>Optional key that reads the hovered item on demand. "None" or any key from ItemGradeHotkeys.Choices.</summary>
    public string ItemGradeHotkey {get;set;}="None";
    /// <summary>Focus stat in Upgrade Desk; Auto picks the unfinished stat closest to its target threshold by percentage.</summary>
    public string ItemGradeFocusStat {get;set;}="Auto";
}

internal static class ItemGradeHotkeys
{
    internal const int Id=12;
    internal const string None="None";
    internal static readonly string[] Choices=[None,"F1","F2","F3","F4","F5","F7","F10","F11","F12","Pause","ScrollLock","PageUp","PageDown"];

    static readonly Dictionary<string,Keys> Map=new(StringComparer.OrdinalIgnoreCase)
    {
        [None]=Keys.None,["F1"]=Keys.F1,["F2"]=Keys.F2,["F3"]=Keys.F3,["F4"]=Keys.F4,["F5"]=Keys.F5,["F6"]=Keys.F6,["F7"]=Keys.F7,["F8"]=Keys.F8,
        ["F9"]=Keys.F9,["F10"]=Keys.F10,["F11"]=Keys.F11,["F12"]=Keys.F12,["End"]=Keys.End,["Pause"]=Keys.Pause,
        ["ScrollLock"]=Keys.Scroll,["PageUp"]=Keys.PageUp,["PageDown"]=Keys.PageDown
    };

    /// <summary>Unknown names (and the bot's own keys) mean no key.</summary>
    internal static Keys Parse(string? name)=>!string.IsNullOrWhiteSpace(name)&&Map.TryGetValue(name.Trim(),out var key)&&!Reserved(key)?key:Keys.None;

    internal static string Name(Keys key)=>Choices.FirstOrDefault(choice=>Parse(choice)==key)??None;

    internal static bool Reserved(Keys key)=>key is Keys.F6 or Keys.F8 or Keys.F9 or Keys.Home or Keys.End or Keys.Insert or Keys.Delete;
    internal static void SelfTest()
    {
        foreach(var key in new[]{"F6","Home","Insert","Delete"})
            if(Parse(key)!=Keys.None)throw new Exception("Item grade hotkeys conflict with existing controls.");
        foreach(var choice in Choices)if(Name(Parse(choice))!=choice)throw new Exception("Item grade key round trip failed.");
    }
}
