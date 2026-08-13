using PS2_DATA_File_Extractor.Models;
using System.Globalization;
using System.Text;

namespace PS2_DATA_File_Extractor.FileOperations;

/// <summary>
/// Edits the developer switches that the retail executable still reads from debugoptions.ini.
/// Unknown settings and comments are preserved verbatim.
/// </summary>
public sealed class DeveloperOptionsArchive
{
    public const string SourcePath = "data/options/debugoptions.ini";
    private static readonly UTF8Encoding Utf8WithoutBom = new(false);
    private static readonly IReadOnlyDictionary<string, DeveloperOptionDefinition> Definitions = BuildDefinitions();
    private readonly string _metPath;
    private readonly string _originalText;

    private DeveloperOptionsArchive(string metPath, string originalText,
        IReadOnlyList<DeveloperOption> options)
    {
        _metPath = metPath;
        _originalText = originalText;
        Options = options;
    }

    public IReadOnlyList<DeveloperOption> Options { get; }

    public static DeveloperOptionsArchive Load(string metPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(metPath);
        METFileStructure structure = METFileReader.ReadMETFile(metPath);
        FileEntry entry = structure.AllEntries.FirstOrDefault(candidate =>
            NormalizePath(candidate.Path).Equals(SourcePath, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException($"This DATA.MET does not contain '{SourcePath}'.");

        byte[] payload = new byte[entry.OriginalSize];
        using (FileStream stream = new(metPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            stream.Position = entry.Offset;
            stream.ReadExactly(payload);
        }
        int length = payload.Length;
        while (length > 0 && payload[length - 1] == 0) length--;
        string text = Utf8WithoutBom.GetString(payload, 0, length);
        IniDocument document = IniDocument.Parse(text);
        List<DeveloperOption> options = new();
        foreach (IniSetting setting in document.Settings)
        {
            if (!Definitions.TryGetValue(Key(setting.Section, setting.Key), out DeveloperOptionDefinition? definition))
                continue;
            options.Add(new DeveloperOption(setting.Section, setting.Key, setting.Value,
                GameplayTweakValue.DetectKind(setting.Value), definition.Category, definition.Label,
                definition.Description, definition.RetailSupported, definition.Choices));
        }
        return new DeveloperOptionsArchive(metPath, text, options);
    }

    public DeveloperOptionsSaveResult SaveWithBackup(IReadOnlyDictionary<DeveloperOption, string> edits)
    {
        ArgumentNullException.ThrowIfNull(edits);
        Dictionary<DeveloperOption, string> normalizedEdits = new();
        foreach ((DeveloperOption option, string input) in edits)
        {
            if (!option.RetailSupported)
                throw new InvalidDataException($"[{option.Section}] {option.Key} is retained in the file but ignored by the retail executable.");
            if (!GameplayTweakValue.TryNormalize(option.Kind, input, out string normalized, out string error))
                throw new InvalidDataException($"[{option.Section}] {option.Key}: {error}");
            ValidateRange(option, normalized);
            if (!normalized.Equals(option.Value, StringComparison.Ordinal))
                normalizedEdits[option] = normalized;
        }
        if (normalizedEdits.Count == 0)
            return new DeveloperOptionsSaveResult(null, 0, false);

        IniDocument updatedDocument = IniDocument.Parse(_originalText);
        foreach ((DeveloperOption option, string normalized) in normalizedEdits)
        {
            if (!updatedDocument.SetValue(option.Section, option.Key, normalized))
                throw new InvalidDataException($"Could not update [{option.Section}] {option.Key}.");
        }

        string updated = updatedDocument.ToString();
        METArchiveBatchSaveResult result = METArchiveBatchEditor.SaveWithBackup(
            _metPath,
            new Dictionary<string, byte[]> { [SourcePath] = Utf8WithoutBom.GetBytes(updated) },
            "developer-options");
        return new DeveloperOptionsSaveResult(result.BackupPath, normalizedEdits.Count, result.RebuiltArchive);
    }

    private static void ValidateRange(DeveloperOption option, string normalized)
    {
        if (option.Key.Equals("LockAngle", StringComparison.OrdinalIgnoreCase) &&
            (!int.TryParse(normalized, out int angle) || angle is < -360 or > 360))
            throw new InvalidDataException("Batting lock angle must be between -360 and 360 degrees.");
        if (option.Choices.Count > 0 &&
            !option.Choices.Any(choice => choice.Value.Equals(normalized, StringComparison.Ordinal)))
            throw new InvalidDataException($"{option.Label} must be one of the named retail choices.");
    }

    private static IReadOnlyDictionary<string, DeveloperOptionDefinition> BuildDefinitions()
    {
        Dictionary<string, DeveloperOptionDefinition> result = new(StringComparer.OrdinalIgnoreCase);
        void Add(string section, string key, string category, string label, string description,
            bool supported = true, DeveloperOptionChoice[]? choices = null) =>
            result[Key(section, key)] = new(category, label, description, supported, choices ?? []);
        static DeveloperOptionChoice Choice(int value, string label) =>
            new(value.ToString(CultureInfo.InvariantCulture), $"{value} — {label}");

        Add("AI", "SwingingOff", "AI", "Disable AI swinging", "AI batters never attempt a swing.");
        Add("Catches", "AlwaysCatch", "Catching", "Always catch", "Every valid catch attempt succeeds.");
        Add("Catches", "AlwaysMiss", "Catching", "Always miss", "Every catch attempt fails. Do not enable with Always catch.");
        Add("Batting", "SwingLock", "Batting", "Lock swing", "Locks the bat to the configured angle and guarantees contact when the pitch arrives.");
        Add("Batting", "LockAngle", "Batting", "Locked swing angle", "Angle used when Lock swing is enabled.");
        Add("Batting", "TypeLock", "Batting", "Lock AI bat type", "Forces AI batters to use the configured bat type.");
        Add("Batting", "BatType", "Batting", "AI bat type", "Named EBatType used when the AI bat-type lock is enabled.",
            choices:
            [
                Choice(-1, "No bat selected"), Choice(0, "Bunt"), Choice(1, "Grounder"),
                Choice(2, "Line drive"), Choice(3, "Power"), Choice(4, "Jumping Bean"),
                Choice(5, "Butterfingers"), Choice(6, "Sonic Boom"), Choice(7, "Geyser"),
                Choice(8, "Pinata"), Choice(9, "Rubber"), Choice(10, "Lightning"),
                Choice(11, "Aluminum"), Choice(13, "Power-up bat"), Choice(14, "Super bat"),
                Choice(15, "Best bat 1"), Choice(16, "Best bat 2"), Choice(17, "Random bat"),
                Choice(18, "Do not swing")
            ]);
        Add("Batting", "StanceLock", "Batting", "Lock AI stance", "Forces AI batters to use the configured stance.");
        Add("Batting", "Stance", "Batting", "AI stance", "Named EBatterStance used when the AI stance lock is enabled.",
            choices:
            [
                Choice(-1, "Unselected"), Choice(0, "Left"), Choice(1, "Normal"), Choice(2, "Right")
            ]);
        Add("Batting", "NeverMiss", "Batting", "Perfect AI swings", "AI uses perfect aim and timing.");
        Add("Pitching", "ErrorOff", "Pitching", "Disable pitching error", "Removes the normal pitching error calculation.");
        Add("PrintStatus", "PlayerStatus", "Status logging", "Player status", "Writes player-status diagnostics to the debug output.");
        Add("PrintStatus", "PlayerActionState", "Status logging", "Player action state", "Writes player action-state diagnostics.");
        Add("PrintStatus", "PlayerMovementState", "Status logging", "Player movement state", "Writes player movement-state diagnostics.");
        Add("PrintStatus", "PlayerCatchStatus", "Status logging", "Player catch status", "Writes catch decisions and failures.");
        Add("PrintStatus", "BallState", "Status logging", "Ball state", "Writes ball-state diagnostics.");
        Add("PrintStatus", "GameState", "Status logging", "Game state", "Writes game-state transitions.");
        Add("PrintStatus", "SimulationStatus", "Status logging", "Simulation status", "Writes simulator diagnostics.");
        Add("PrintStatus", "ThrowInfo", "Status logging", "Throw information", "Writes throw target and velocity diagnostics.");
        Add("PrintStatus", "MiscInfo", "Status logging", "Miscellaneous information", "Writes miscellaneous gameplay diagnostics.");
        Add("Misc", "HomeTeamBatsFirst", "Game flow", "Home team bats first", "Reverses the normal first batting side.");
        Add("Misc", "DisablePlayTimer", "Game flow", "Disable play timer", "Stops the play clock from expiring.");
        Add("Misc", "LoadAmbients", "Rendering and audio", "Load ambient objects", "Loads stadium ambient models and effects.");
        Add("Misc", "AudioFlag", "Rendering and audio", "Enable debug-controlled audio", "Master audio switch read by field loading and playback code.");
        DeveloperOptionChoice[] controllerChoices =
        [
            Choice(0, "Gamepad control"), Choice(1, "Digital gamepad control")
        ];
        Add("Misc", "GamepadType1", "Controllers", "Gamepad type 1", "PS2-safe EControllerType used for controller 1.",
            choices: controllerChoices);
        Add("Misc", "GamepadType2", "Controllers", "Gamepad type 2", "PS2-safe EControllerType used for controller 2.",
            choices: controllerChoices);
        Add("Misc", "AssertsEnabled", "Ignored retail keys", "Assertions enabled", "Present in the shipped INI but not read by the retail loader.", false);
        return result;
    }

    private static string Key(string section, string key) => section + "\0" + key;
    private static string NormalizePath(string path) => path.Replace('\\', '/');
    private sealed record DeveloperOptionDefinition(string Category, string Label, string Description,
        bool RetailSupported, IReadOnlyList<DeveloperOptionChoice> Choices);
}

public sealed record DeveloperOption(string Section, string Key, string Value, GameplayTweakValueKind Kind,
    string Category, string Label, string Description, bool RetailSupported,
    IReadOnlyList<DeveloperOptionChoice> Choices);

public sealed record DeveloperOptionChoice(string Value, string Label)
{
    public override string ToString() => Label;
}

public sealed record DeveloperOptionsSaveResult(string? BackupPath, int ChangedOptionCount, bool RebuiltArchive);
