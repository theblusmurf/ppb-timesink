using System.Globalization;
using System.IO.Compression;

namespace PoteHunter;

/// <summary>Minimum stat values for each grade of one equipment stat. A null minimum means the grade is not reachable.</summary>
internal sealed record ItemStatBreakpoints(string Stat, int Base, int? MinB, int? MinA, int? MinAA, int? MinAAA, int? MinS)
{
    internal int? Minimum(ItemGrade grade) => grade switch
    {
        ItemGrade.B => MinB, ItemGrade.A => MinA, ItemGrade.AA => MinAA, ItemGrade.AAA => MinAAA, ItemGrade.S => MinS, _ => null
    };

    internal ItemGrade GradeOf(int value)
    {
        foreach (var grade in ItemGradeLadder.Descending)
            if (Minimum(grade) is int minimum && value >= minimum) return grade;
        return ItemGrade.C;
    }
}

/// <summary>Grade breakpoints for one equipment prototype, keyed by the stat labels used in the bundled table.</summary>
internal sealed record ItemGradeProfile(int Id, string Name, string Type, string Category, string RequirementStat, int Requirement,
    int DropOption, IReadOnlyDictionary<string, ItemStatBreakpoints> Stats)
{
    internal ItemGemGroup GemGroup => ItemGradePlanner.GemGroupFor(Category);
}

internal enum ItemGrade { C = 0, B = 1, A = 2, AA = 3, AAA = 4, S = 5 }

internal static class ItemGradeLadder
{
    internal static readonly ItemGrade[] Descending = [ItemGrade.S, ItemGrade.AAA, ItemGrade.AA, ItemGrade.A, ItemGrade.B];
    internal static readonly ItemGrade[] Targets = [ItemGrade.B, ItemGrade.A, ItemGrade.AA, ItemGrade.AAA, ItemGrade.S];

    internal const string AutomaticName = "Auto";
    /// <summary>"Auto" (or anything unrecognised) means the automatic rule; a grade name fixes the target.</summary>
    internal static ItemGrade? ParseTarget(string? text) => TryParse(text, out var grade) ? grade : null;
    internal static string TargetName(ItemGrade? target) => target?.ToString() ?? AutomaticName;

    internal static bool TryParse(string? text, out ItemGrade grade)
    {
        grade = ItemGrade.AAA;
        if (string.IsNullOrWhiteSpace(text)) return false;
        return Enum.TryParse(text.Trim(), ignoreCase: true, out grade) && Enum.IsDefined(grade);
    }

    internal static Color ChipColor(ItemGrade? grade) => grade switch
    {
        ItemGrade.C => Color.FromArgb(0x62, 0xd0, 0x62), ItemGrade.B => Color.FromArgb(0x3c, 0xcf, 0xd8),
        ItemGrade.A => Color.FromArgb(0xf2, 0xea, 0x96), ItemGrade.AA => Color.FromArgb(0xf3, 0x9a, 0x2c),
        ItemGrade.AAA => Color.FromArgb(0xf6, 0xc4, 0x19), ItemGrade.S => Color.FromArgb(0xee, 0xe1, 0x3a),
        _ => Color.FromArgb(0x8d, 0x99, 0xa6)
    };
}

/// <summary>The bundled per-item grade table (Resources/ItemGrades/grade_breakpoints.csv.gz), loaded once on first use.</summary>
internal static class ItemGradeTable
{
    internal const string ResourceName = "PoteHunter.Resources.ItemGrades.grade_breakpoints.csv.gz";
    internal static readonly string[] StatOrder = ["MIN", "MAX", "ACC", "DEF", "EVAS", "MAX HP", "HP REG", "MAX MP", "MP REG", "CRI", "BLOCK", "MAGIC", "MR"];
    static readonly Lazy<IReadOnlyDictionary<int, ItemGradeProfile>> bundled = new(LoadBundled, LazyThreadSafetyMode.ExecutionAndPublication);

    internal static IReadOnlyDictionary<int, ItemGradeProfile> Bundled => bundled.Value;
    internal static ItemGradeProfile? Find(int prototypeId) => Bundled.GetValueOrDefault(prototypeId);

    internal static string DisplayName(string stat) => stat switch
    {
        "MIN" => "Damage min", "MAX" => "Damage max", "ACC" => "Accuracy", "DEF" => "Defense", "EVAS" => "Evasion",
        "MAX HP" => "Max Health", "HP REG" => "Health Regen", "MAX MP" => "Max Mana", "MP REG" => "Mana Regen",
        "CRI" => "Critical", "BLOCK" => "Block", "MR" => "Magic Resist", "MAGIC" => "Magic", _ => stat
    };

    internal static int StatRank(string stat) { int index = Array.IndexOf(StatOrder, stat); return index < 0 ? StatOrder.Length : index; }

    static IReadOnlyDictionary<int, ItemGradeProfile> LoadBundled()
    {
        using var stream = typeof(ItemGradeTable).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new FileNotFoundException("The bundled item grade table is missing: " + ResourceName);
        using var unzipped = new GZipStream(stream, CompressionMode.Decompress);
        using var reader = new StreamReader(unzipped, System.Text.Encoding.UTF8);
        return Parse(reader);
    }

    internal static IReadOnlyDictionary<int, ItemGradeProfile> Parse(TextReader reader)
    {
        string? header = reader.ReadLine() ?? throw new InvalidDataException("The item grade table is empty.");
        var columns = SplitCsv(header);
        int Column(string name)
        {
            int index = Array.IndexOf(columns, name);
            return index < 0 ? throw new InvalidDataException("The item grade table lacks the column " + name + ".") : index;
        }
        int id = Column("id"), name = Column("name"), type = Column("type"), category = Column("category"), reqStat = Column("req_stat"),
            req = Column("req"), dropOption = Column("dropoption"), stat = Column("stat"), baseValue = Column("base"),
            minB = Column("min_B"), minA = Column("min_A"), minAA = Column("min_AA"), minAAA = Column("min_AAA"), minS = Column("min_S");
        var builders = new Dictionary<int, (ItemGradeProfile Profile, Dictionary<string, ItemStatBreakpoints> Stats)>();
        static int Int(string text) => int.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture);
        static int? Optional(string text) => string.IsNullOrWhiteSpace(text) ? null : Int(text);
        for (string? line = reader.ReadLine(); line != null; line = reader.ReadLine())
        {
            if (line.Length == 0) continue;
            var fields = SplitCsv(line);
            if (fields.Length < columns.Length) throw new InvalidDataException("Short item grade row: " + line);
            int itemId = Int(fields[id]);
            if (!builders.TryGetValue(itemId, out var builder))
            {
                var stats = new Dictionary<string, ItemStatBreakpoints>(StringComparer.Ordinal);
                builder = (new ItemGradeProfile(itemId, fields[name], fields[type], fields[category], fields[reqStat],
                    Int(fields[req]), Int(fields[dropOption]), stats), stats);
                builders[itemId] = builder;
            }
            var breakpoints = new ItemStatBreakpoints(fields[stat], Int(fields[baseValue]), Optional(fields[minB]), Optional(fields[minA]),
                Optional(fields[minAA]), Optional(fields[minAAA]), Optional(fields[minS]));
            int?[] ladder = [breakpoints.MinB, breakpoints.MinA, breakpoints.MinAA, breakpoints.MinAAA, breakpoints.MinS];
            int previous = int.MinValue;
            foreach (int? minimum in ladder)
                if (minimum is int value) { if (value < previous) throw new InvalidDataException($"Item {itemId} {breakpoints.Stat} grade minimums are not ascending."); previous = value; }
            if (!builder.Stats.TryAdd(breakpoints.Stat, breakpoints))
                throw new InvalidDataException($"Item {itemId} repeats the stat {breakpoints.Stat}.");
        }
        return builders.ToDictionary(pair => pair.Key, pair => pair.Value.Profile);
    }

    // Names in the table are plain text; a quoted field is still handled so an edited table does not break loading.
    static string[] SplitCsv(string line)
    {
        var fields = new List<string>();
        var current = new System.Text.StringBuilder();
        bool quoted = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < line.Length && line[i + 1] == '"') { current.Append('"'); i++; }
                else if (c == '"') quoted = false;
                else current.Append(c);
            }
            else if (c == '"') quoted = true;
            else if (c == ',') { fields.Add(current.ToString()); current.Clear(); }
            else current.Append(c);
        }
        fields.Add(current.ToString());
        return fields.ToArray();
    }

    internal static void SelfTest()
    {
        var table = Bundled;
        if (table.Count < 1000) throw new Exception("The bundled item grade table loaded too few items: " + table.Count);
        var helmet = table.GetValueOrDefault(101) ?? throw new Exception("Leather Helmet (101) is missing from the bundled grade table.");
        if (helmet.Name != "Leather Helmet" || helmet.RequirementStat != "CON" || helmet.Requirement != 20 || helmet.DropOption != 47 ||
            helmet.Category != "Helm/Gloves/Boots/Pelvis" || helmet.GemGroup != ItemGemGroup.Armor)
            throw new Exception("Leather Helmet identity fields were decoded incorrectly.");
        var defense = helmet.Stats["DEF"];
        if (defense.Base != 17 || defense.MinB != 32 || defense.MinA != 41 || defense.MinAA != 97 || defense.MinAAA != 173 || defense.MinS != 248)
            throw new Exception("Leather Helmet defense breakpoints were decoded incorrectly.");
        if (defense.GradeOf(31) != ItemGrade.C || defense.GradeOf(32) != ItemGrade.B || defense.GradeOf(96) != ItemGrade.A ||
            defense.GradeOf(97) != ItemGrade.AA || defense.GradeOf(200) != ItemGrade.AAA || defense.GradeOf(248) != ItemGrade.S)
            throw new Exception("Grade thresholds are not applied as inclusive minimums.");
        var staff = table.GetValueOrDefault(1501) ?? throw new Exception("Staff (1501) is missing from the bundled grade table.");
        if (staff.GemGroup != ItemGemGroup.Caster || !staff.Stats.ContainsKey("MAGIC") || staff.Stats["MAGIC"].MinAAA != 251)
            throw new Exception("Staff caster stats were decoded incorrectly.");
        if (table.GetValueOrDefault(1301)?.GemGroup != ItemGemGroup.Weapon) throw new Exception("Short Bow should use weapon gems.");
        if (ItemGradeLadder.ParseTarget("Auto") != null || ItemGradeLadder.ParseTarget("S") != ItemGrade.S || ItemGradeLadder.TargetName(null) != "Auto")
            throw new Exception("The automatic target name did not round-trip.");
        if (!ItemGradeLadder.TryParse("aaa", out var parsed) || parsed != ItemGrade.AAA || ItemGradeLadder.TryParse("AAAA", out _) || ItemGradeLadder.TryParse("", out _))
            throw new Exception("Grade parsing accepted or rejected the wrong text.");
        var quoted = Parse(new StringReader("id,name,type,category,req_stat,req,dropoption,stat,base,min_B,min_A,min_AA,min_AAA,min_S\n" +
            "7,\"Blade, Long\",Dagger,Dagger/Claw/Knife Arm,DEX,40,10,MIN,5,10,20,30,40,\n"));
        if (quoted[7].Name != "Blade, Long" || quoted[7].Stats["MIN"].MinS != null || quoted[7].Stats["MIN"].GradeOf(1000) != ItemGrade.AAA)
            throw new Exception("Quoted names or missing grade minimums were parsed incorrectly.");
        bool rejected = false;
        try { Parse(new StringReader("id,name,type,category,req_stat,req,dropoption,stat,base,min_B,min_A,min_AA,min_AAA,min_S\n7,x,t,c,DEX,1,1,MIN,5,40,30,20,10,1\n")); }
        catch (InvalidDataException) { rejected = true; }
        if (!rejected) throw new Exception("Descending grade minimums were accepted.");
    }
}
