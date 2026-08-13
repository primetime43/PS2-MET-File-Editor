namespace PS2_DATA_File_Extractor.FileOperations;

/// <summary>
/// User-facing combinations of the retail INI controls exposed by Gameplay Tweaks.
/// Presets stage values in the editor; they never write DATA.MET until the normal Save action.
/// </summary>
public static class GameplayPresetCatalog
{
    private const string Ball = "data/options/ball.ini";
    private const string Bat = "data/options/bat.ini";
    private const string Fields = "data/options/fields.ini";
    private const string Debug = "data/options/debugoptions.ini";

    public static IReadOnlyList<GameplayPreset> Presets { get; } = BuildPresets();

    private static IReadOnlyList<GameplayPreset> BuildPresets() => new GameplayPreset[]
    {
        Preset("Ball Size", "Tiny Ball — Harder to Catch",
            "Shrinks the gameplay collision radius from the retail value of 7 to 4.",
            R(Ball, "Size", "Radius", "4")),
        Preset("Ball Size", "Large Ball — Easier to Catch",
            "Increases the gameplay collision radius to 11.",
            R(Ball, "Size", "Radius", "11")),
        Preset("Ball Size", "Huge Arcade Ball",
            "Uses a deliberately oversized radius of 16. Expect unusual bat and wall contacts.",
            R(Ball, "Size", "Radius", "16")),
        Preset("Ball Size", "Restore Ball Size",
            "Restores the ball radius currently stored in the opened archive.",
            O(Ball, "Size", "Radius")),

        Preset("Bounce & Rolling", "Super Bouncy Fields",
            "Makes every stadium retain much more impact speed, with low friction and continued small bounces.",
            R(Ball, "Collisions", "CollisionEfficiency", "1.0"),
            F("CollisionEfficiency", ".85"), F("Friction", ".05"),
            F("MinBounceSpeed", "8"), F("MinRollSpeed", "10")),
        Preset("Bounce & Rolling", "Pinball Physics",
            "Experimental arcade preset: tiny, extremely lively ball with nearly frictionless stadium surfaces.",
            R(Ball, "Size", "Radius", "5"),
            R(Ball, "Collisions", "CollisionEfficiency", "1.05"),
            R(Ball, "Collisions", "Friction", "0"),
            F("CollisionEfficiency", "1.05"), F("Friction", ".01"),
            F("MinBounceSpeed", "3"), F("MinRollSpeed", "3")),
        Preset("Bounce & Rolling", "Long Rolling Ball",
            "Reduces every field's friction and stopping threshold so ground balls travel much farther.",
            R(Ball, "Collisions", "Friction", "0"),
            F("Friction", ".04"), F("MinRollSpeed", "8")),
        Preset("Bounce & Rolling", "Heavy Dead Ball",
            "Deadens wall and ground impacts and makes the ball stop bouncing and rolling sooner.",
            R(Ball, "Collisions", "CollisionEfficiency", ".35"),
            R(Ball, "Collisions", "Friction", ".50"),
            F("CollisionEfficiency", ".12"), F("Friction", ".55"),
            F("MinBounceSpeed", "60"), F("MinRollSpeed", "55")),
        Preset("Bounce & Rolling", "Low Bounce, Normal Roll",
            "Makes surfaces absorb most bounce energy without heavily changing rolling behavior.",
            F("CollisionEfficiency", ".15"), F("MinBounceSpeed", "55")),
        Preset("Bounce & Rolling", "Restore Ball and Field Physics",
            "Restores ball collision values and all four surface controls for every stadium.",
            O(Ball, "Collisions", null), O(Fields, null, "CollisionEfficiency"),
            O(Fields, null, "Friction"), O(Fields, null, "MinBounceSpeed"),
            O(Fields, null, "MinRollSpeed")),

        Preset("Bunts & Normal Hits", "Powerful Bunts",
            "Makes normal and Crazy Bunts travel much farther while keeping them recognizable as bunts.",
            R(Bat, "Bunt", "BasePower", "350"), R(Bat, "Bunt", "BatterPower", "350"),
            R(Bat, "Bunt", "RandomPower", "125"),
            R(Bat, "CrazyBunt", "BasePower", "300"), R(Bat, "CrazyBunt", "BatterPower", "350"),
            R(Bat, "CrazyBunt", "RandomPower", "150")),
        Preset("Bunts & Normal Hits", "Bunt Home-Run Experiment",
            "Extreme bunt power intended for testing stadium and home-run behavior.",
            R(Bat, "Bunt", "BasePower", "900"), R(Bat, "Bunt", "BatterPower", "700"),
            R(Bat, "Bunt", "BaseAngle", "20"), R(Bat, "Bunt", "RandomPower", "250"),
            R(Bat, "CrazyBunt", "BasePower", "900"), R(Bat, "CrazyBunt", "BatterPower", "700"),
            R(Bat, "CrazyBunt", "BaseAngle", "20"), R(Bat, "CrazyBunt", "RandomPower", "250")),
        Preset("Bunts & Normal Hits", "Stronger Normal Contact",
            "Raises Grounder, Line Drive, and Power hit velocity without changing special-hit power-ups.",
            R(Bat, "Grounder", "BasePower", "1200"), R(Bat, "Grounder", "BatterPower", "800"),
            R(Bat, "LineDrive", "BasePower", "950"), R(Bat, "LineDrive", "BatterPower", "700"),
            R(Bat, "Power", "BasePower", "950"), R(Bat, "Power", "BatterPower", "750")),
        Preset("Bunts & Normal Hits", "Weak Contact Challenge",
            "Reduces ordinary hit power for a defense-heavy game.",
            R(Bat, "Bunt", "BasePower", "60"), R(Bat, "Bunt", "BatterPower", "90"),
            R(Bat, "Grounder", "BasePower", "500"), R(Bat, "Grounder", "BatterPower", "300"),
            R(Bat, "LineDrive", "BasePower", "350"), R(Bat, "LineDrive", "BatterPower", "275"),
            R(Bat, "Power", "BasePower", "400"), R(Bat, "Power", "BatterPower", "300")),
        Preset("Bunts & Normal Hits", "Wild Contact",
            "Adds large power variation and wider horizontal bunt dispersion.",
            R(Bat, "Swing", "HorizontalBuntDispersion", "40"),
            R(Bat, "Bunt", "RandomPower", "600"), R(Bat, "CrazyBunt", "RandomPower", "600"),
            R(Bat, "Grounder", "RandomPower", "650"), R(Bat, "LineDrive", "RandomPower", "650"),
            R(Bat, "Power", "RandomPower", "700")),
        Preset("Bunts & Normal Hits", "Restore Bunts and Normal Hits",
            "Restores the archive values for normal bunt, grounder, line-drive, power, and swing tuning.",
            O(Bat, "Bunt", null), O(Bat, "CrazyBunt", null), O(Bat, "Grounder", null),
            O(Bat, "LineDrive", null), O(Bat, "Power", null), O(Bat, "Swing", null)),

        Preset("Special Hits", "Overpowered Special Hits",
            "Greatly strengthens Aluminum, Lightning, Rubber, Sonic Boom, and the other special hit types.",
            R(Bat, "Aluminum", "BasePower", "2800"), R(Bat, "Aluminum", "BatterPower", "700"),
            R(Bat, "Lightning", "BasePower", "3200"), R(Bat, "Lightning", "BatterPower", "1000"),
            R(Bat, "Butterfingers", "BasePower", "1600"), R(Bat, "Pinata", "BasePower", "1400"),
            R(Bat, "Rubber", "BasePower", "1800"), R(Bat, "SonicBoom", "BasePower", "1900"),
            R(Bat, "UnderGrounder", "BasePower", "1100")),
        Preset("Special Hits", "Tamed Special Hits",
            "Reduces the strongest special-hit launch powers for a less explosive game.",
            R(Bat, "Aluminum", "BasePower", "850"), R(Bat, "Lightning", "BasePower", "950"),
            R(Bat, "Butterfingers", "BasePower", "600"), R(Bat, "Pinata", "BasePower", "500"),
            R(Bat, "Rubber", "BasePower", "650"), R(Bat, "SonicBoom", "BasePower", "700"),
            R(Bat, "UnderGrounder", "BasePower", "450")),
        Preset("Special Hits", "Restore Special Hits",
            "Restores all supported values for the retail special-hit sections.",
            O(Bat, "Aluminum", null), O(Bat, "Lightning", null), O(Bat, "Butterfingers", null),
            O(Bat, "Pinata", null), O(Bat, "Rubber", null), O(Bat, "SonicBoom", null),
            O(Bat, "UnderGrounder", null)),

        Preset("Catching", "Normal Catch Logic",
            "Turns off both catch-debug overrides so player ratings and gameplay determine catches.",
            R(Debug, "Catches", "AlwaysCatch", "False"), R(Debug, "Catches", "AlwaysMiss", "False")),
        Preset("Catching", "Guaranteed Catches",
            "Forces catch attempts to succeed through the game's debug option.",
            R(Debug, "Catches", "AlwaysCatch", "True"), R(Debug, "Catches", "AlwaysMiss", "False")),
        Preset("Catching", "Drop Every Catch",
            "Forces catch attempts to fail. This is an extreme debug preset.",
            R(Debug, "Catches", "AlwaysCatch", "False"), R(Debug, "Catches", "AlwaysMiss", "True")),
        Preset("Catching", "Hard-to-Catch Physics",
            "Combines a small ball with lively, low-friction surfaces; catch ratings still work normally.",
            R(Ball, "Size", "Radius", "4"),
            R(Debug, "Catches", "AlwaysCatch", "False"), R(Debug, "Catches", "AlwaysMiss", "False"),
            F("CollisionEfficiency", ".75"), F("Friction", ".06"),
            F("MinBounceSpeed", "10"), F("MinRollSpeed", "10")),

        Preset("Complete Game Styles", "Arcade Chaos",
            "Small pinball-like ball, strong normal contact, powerful bunts, and highly variable hits.",
            R(Ball, "Size", "Radius", "5"), R(Ball, "Collisions", "CollisionEfficiency", "1.0"),
            F("CollisionEfficiency", ".95"), F("Friction", ".02"),
            F("MinBounceSpeed", "5"), F("MinRollSpeed", "5"),
            R(Bat, "Bunt", "BasePower", "500"), R(Bat, "Bunt", "BatterPower", "450"),
            R(Bat, "Grounder", "BasePower", "1350"), R(Bat, "Grounder", "BatterPower", "850"),
            R(Bat, "LineDrive", "BasePower", "1100"), R(Bat, "LineDrive", "BatterPower", "800"),
            R(Bat, "Power", "BasePower", "1100"), R(Bat, "Power", "BatterPower", "850"),
            R(Bat, "Grounder", "RandomPower", "600"), R(Bat, "LineDrive", "RandomPower", "600"),
            R(Bat, "Power", "RandomPower", "650")),
        Preset("Complete Game Styles", "Big-Ball Slugfest",
            "Large ball, long rolls, and stronger ordinary contact for an offense-heavy game.",
            R(Ball, "Size", "Radius", "11"), F("Friction", ".06"), F("MinRollSpeed", "10"),
            R(Bat, "Bunt", "BasePower", "300"), R(Bat, "Bunt", "BatterPower", "300"),
            R(Bat, "Grounder", "BasePower", "1200"), R(Bat, "Grounder", "BatterPower", "750"),
            R(Bat, "LineDrive", "BasePower", "1000"), R(Bat, "LineDrive", "BatterPower", "750"),
            R(Bat, "Power", "BasePower", "1000"), R(Bat, "Power", "BatterPower", "800")),
        Preset("Complete Game Styles", "Defense Challenge",
            "Small ball, weaker ordinary contact, low bounce, and normal catch logic.",
            R(Ball, "Size", "Radius", "4"), F("CollisionEfficiency", ".18"), F("MinBounceSpeed", "50"),
            R(Bat, "Bunt", "BasePower", "60"), R(Bat, "Bunt", "BatterPower", "90"),
            R(Bat, "Grounder", "BasePower", "550"), R(Bat, "Grounder", "BatterPower", "325"),
            R(Bat, "LineDrive", "BasePower", "400"), R(Bat, "LineDrive", "BatterPower", "300"),
            R(Bat, "Power", "BasePower", "450"), R(Bat, "Power", "BatterPower", "325"),
            R(Debug, "Catches", "AlwaysCatch", "False"), R(Debug, "Catches", "AlwaysMiss", "False")),
        new GameplayPreset("Complete Game Styles", "Restore All Loaded Values",
            "Clears every unsaved gameplay edit and returns all supported settings to the values in the opened archive.",
            Array.Empty<GameplayPresetRule>(), RestoreAll: true)
    };

    private static GameplayPreset Preset(string group, string name, string description,
        params GameplayPresetRule[] rules) => new(group, name, description, rules, RestoreAll: false);

    private static GameplayPresetRule R(string path, string? section, string? key, string value) =>
        new(path, section, key, value, RestoreOriginal: false);

    private static GameplayPresetRule O(string path, string? section, string? key) =>
        new(path, section, key, null, RestoreOriginal: true);

    private static GameplayPresetRule F(string key, string value) => R(Fields, null, key, value);
}

public sealed record GameplayPreset(
    string Group,
    string Name,
    string Description,
    IReadOnlyList<GameplayPresetRule> Rules,
    bool RestoreAll)
{
    public IReadOnlyList<GameplayPresetChange> Resolve(
        IEnumerable<GameplayTuningArchive.GameplayTweak> tweaks)
    {
        ArgumentNullException.ThrowIfNull(tweaks);
        GameplayTuningArchive.GameplayTweak[] available = tweaks.ToArray();
        if (RestoreAll)
            return available.Select(tweak => new GameplayPresetChange(tweak, tweak.Value)).ToArray();

        Dictionary<GameplayTuningArchive.GameplayTweak, string> changes = new();
        foreach (GameplayPresetRule rule in Rules)
        {
            foreach (GameplayTuningArchive.GameplayTweak tweak in available.Where(rule.Matches))
                changes[tweak] = rule.RestoreOriginal ? tweak.Value : rule.Value!;
        }
        return changes.Select(pair => new GameplayPresetChange(pair.Key, pair.Value)).ToArray();
    }

    public override string ToString() => Name;
}

public sealed record GameplayPresetRule(
    string? SourcePath,
    string? Section,
    string? Key,
    string? Value,
    bool RestoreOriginal)
{
    public bool Matches(GameplayTuningArchive.GameplayTweak tweak) =>
        (SourcePath == null || Normalize(tweak.SourcePath).Equals(Normalize(SourcePath), StringComparison.OrdinalIgnoreCase)) &&
        (Section == null || tweak.Section.Equals(Section, StringComparison.OrdinalIgnoreCase)) &&
        (Key == null || tweak.Key.Equals(Key, StringComparison.OrdinalIgnoreCase));

    private static string Normalize(string path) => path.Replace('\\', '/');
}

public sealed record GameplayPresetChange(GameplayTuningArchive.GameplayTweak Tweak, string Value);
