using PS2_DATA_File_Extractor.Models;
using System.Text;

namespace PS2_DATA_File_Extractor.FileOperations;

public sealed class GameplayTuningArchive
{
    private static readonly UTF8Encoding Utf8WithoutBom = new UTF8Encoding(false);
    private static readonly HashSet<string> DebugMiscKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "HomeTeamBatsFirst", "DisablePlayTimer", "LoadAmbients", "AudioFlag"
    };

    private static readonly TuningFileDefinition[] Definitions =
    {
        new("Ball", "data/options/ball.ini", (_, _) => true),
        new("Batting & Power-ups", "data/options/bat.ini", (_, _) => true),
        new("Field Physics", "data/options/fields.ini", (_, _) => true),
        new("Simulation", "data/options/simulator.ini", (_, _) => true),
        new("Cheats & Practice", "data/options/debugoptions.ini", IncludeDebugSetting),
        new("Game Defaults", "data/options/menuoptions.ini", IncludeMenuSetting)
    };

    private readonly string _metPath;
    private readonly List<TuningFile> _files;

    private GameplayTuningArchive(string metPath, List<TuningFile> files, List<string> missingFiles)
    {
        _metPath = metPath;
        _files = files;
        MissingFiles = missingFiles;
        Tweaks = files.SelectMany(file => file.CreateTweaks()).ToList();
    }

    public IReadOnlyList<GameplayTweak> Tweaks { get; }
    public IReadOnlyList<string> MissingFiles { get; }

    public static GameplayTuningArchive Load(string metPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(metPath);
        METFileStructure structure = METFileReader.ReadMETFile(metPath);
        Dictionary<string, FileEntry> entries = structure.AllEntries.ToDictionary(
            entry => NormalizePath(entry.Path), StringComparer.OrdinalIgnoreCase);
        List<TuningFile> files = new List<TuningFile>();
        List<string> missingFiles = new List<string>();

        using FileStream stream = new FileStream(metPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        foreach (TuningFileDefinition definition in Definitions)
        {
            if (!entries.TryGetValue(definition.Path, out FileEntry? entry))
            {
                missingFiles.Add(definition.Path);
                continue;
            }

            stream.Position = entry.Offset;
            byte[] payload = new byte[entry.OriginalSize];
            stream.ReadExactly(payload);
            int textLength = payload.Length;
            while (textLength > 0 && payload[textLength - 1] == 0)
            {
                textLength--;
            }

            string text = Utf8WithoutBom.GetString(payload, 0, textLength);
            files.Add(new TuningFile(definition, IniDocument.Parse(text), text));
        }

        if (files.Count == 0)
        {
            throw new InvalidDataException("This MET archive does not contain the supported Backyard Baseball gameplay INI files.");
        }

        return new GameplayTuningArchive(metPath, files, missingFiles);
    }

    public GameplayTuningSaveResult SaveWithBackup(IReadOnlyDictionary<GameplayTweak, string> editedValues)
    {
        ArgumentNullException.ThrowIfNull(editedValues);

        foreach ((GameplayTweak tweak, string value) in editedValues)
        {
            if (!tweak.Document.SetValue(tweak.Section, tweak.Key, value))
            {
                throw new InvalidDataException($"Could not update [{tweak.Section}] {tweak.Key} in {tweak.SourcePath}.");
            }
        }

        List<TuningReplacement> replacements = _files
            .Select(file => new TuningReplacement(file.Definition.Path, Utf8WithoutBom.GetBytes(file.Document.ToString()), file.OriginalText))
            .Where(replacement => !Utf8WithoutBom.GetString(replacement.Data).Equals(replacement.OriginalText, StringComparison.Ordinal))
            .ToList();

        if (replacements.Count == 0)
        {
            return new GameplayTuningSaveResult(null, 0, false);
        }

        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
        string backupPath = $"{_metPath}.backup_{timestamp}";
        string tempPath = Path.Combine(
            Path.GetDirectoryName(_metPath) ?? ".",
            $".{Path.GetFileName(_metPath)}.{Guid.NewGuid():N}.gameplay-tweaks.tmp");
        bool rebuilt = false;

        File.Copy(_metPath, backupPath, overwrite: false);
        try
        {
            foreach (TuningReplacement replacement in replacements)
            {
                METFileStructure structure = METFileReader.ReadMETFile(_metPath);
                FileEntry entry = structure.AllEntries.FirstOrDefault(candidate =>
                    NormalizePath(candidate.Path).Equals(replacement.Path, StringComparison.OrdinalIgnoreCase))
                    ?? throw new InvalidDataException($"The MET entry disappeared while saving: {replacement.Path}");

                if (replacement.Data.Length <= entry.OriginalSize)
                {
                    WriteInPlace(_metPath, entry, replacement.Data);
                }
                else
                {
                    METFileRebuilder.RebuildWithExpandedEntry(_metPath, tempPath, entry, replacement.Data);
                    File.Move(tempPath, _metPath, overwrite: true);
                    rebuilt = true;
                }
            }

            return new GameplayTuningSaveResult(backupPath, replacements.Count, rebuilt);
        }
        catch
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }

            File.Copy(backupPath, _metPath, overwrite: true);
            throw;
        }
    }

    private static void WriteInPlace(string metPath, FileEntry entry, byte[] data)
    {
        using FileStream stream = new FileStream(metPath, FileMode.Open, FileAccess.Write, FileShare.None);
        stream.Position = entry.Offset;
        stream.Write(data);
        if (data.Length < entry.OriginalSize)
        {
            stream.Write(new byte[entry.OriginalSize - data.Length]);
        }
    }

    private static bool IncludeDebugSetting(string section, string key)
    {
        return section.Equals("AI", StringComparison.OrdinalIgnoreCase) ||
               section.Equals("Catches", StringComparison.OrdinalIgnoreCase) ||
               section.Equals("Batting", StringComparison.OrdinalIgnoreCase) ||
               section.Equals("Pitching", StringComparison.OrdinalIgnoreCase) ||
               (section.Equals("Misc", StringComparison.OrdinalIgnoreCase) && DebugMiscKeys.Contains(key));
    }

    private static bool IncludeMenuSetting(string section, string key)
    {
        return section.Equals("Rules", StringComparison.OrdinalIgnoreCase) ||
               section.Equals("Controller", StringComparison.OrdinalIgnoreCase) ||
               section.Equals("Display", StringComparison.OrdinalIgnoreCase) ||
               section.Equals("HomerunDerby", StringComparison.OrdinalIgnoreCase) ||
               section.Equals("Sound", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePath(string path) => path.Replace('\\', '/');

    private sealed record TuningFileDefinition(
        string Category,
        string Path,
        Func<string, string, bool> IncludeSetting);

    private sealed class TuningFile
    {
        public TuningFile(TuningFileDefinition definition, IniDocument document, string originalText)
        {
            Definition = definition;
            Document = document;
            OriginalText = originalText;
        }

        public TuningFileDefinition Definition { get; }
        public IniDocument Document { get; }
        public string OriginalText { get; }

        public IEnumerable<GameplayTweak> CreateTweaks()
        {
            return Document.Settings
                .Where(setting => Definition.IncludeSetting(setting.Section, setting.Key))
                .Select(setting => new GameplayTweak(
                    Document,
                    Definition.Category,
                    Definition.Path,
                    setting.Section,
                    setting.Key,
                    setting.Value,
                    GameplayTweakValue.DetectKind(setting.Value)));
        }
    }

    private sealed record TuningReplacement(string Path, byte[] Data, string OriginalText);

    public sealed class GameplayTweak
    {
        internal IniDocument Document { get; }

        internal GameplayTweak(
            IniDocument document,
            string category,
            string sourcePath,
            string section,
            string key,
            string value,
            GameplayTweakValueKind kind)
        {
            Document = document;
            Category = category;
            SourcePath = sourcePath;
            Section = section;
            Key = key;
            Value = value;
            Kind = kind;
        }

        public string Category { get; }
        public string SourcePath { get; }
        public string Section { get; }
        public string Key { get; }
        public string Value { get; }
        public GameplayTweakValueKind Kind { get; }
    }
}

public sealed record GameplayTuningSaveResult(string? BackupPath, int ChangedFileCount, bool RebuiltArchive);
