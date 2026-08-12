using PS2_DATA_File_Extractor.Models;
using System.Text;

namespace PS2_DATA_File_Extractor.FileOperations;

public sealed class StadiumEnvironmentArchive
{
    private readonly string _metPath;

    private StadiumEnvironmentArchive(string metPath, List<StadiumEnvironment> stadiums)
    {
        _metPath = metPath;
        Stadiums = stadiums;
    }

    public IReadOnlyList<StadiumEnvironment> Stadiums { get; }
    public int ChangedStadiumCount => Stadiums.Count(stadium => stadium.IsChanged);

    public static StadiumEnvironmentArchive Load(string metPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(metPath);
        METFileStructure structure = METFileReader.ReadMETFile(metPath);
        List<FileEntry> entries = structure.AllEntries.Where(entry =>
            NormalizePath(entry.Path).StartsWith("data/fields/", StringComparison.OrdinalIgnoreCase) &&
            NormalizePath(entry.Path).EndsWith("/fielddata.txt", StringComparison.OrdinalIgnoreCase) &&
            NormalizePath(entry.Path).Count(character => character == '/') == 3).ToList();
        if (entries.Count == 0)
        {
            throw new InvalidDataException("This MET archive does not contain Backyard Baseball stadium fielddata.txt files.");
        }

        List<StadiumEnvironment> stadiums = new(entries.Count);
        using FileStream stream = new(metPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        foreach (FileEntry entry in entries)
        {
            stream.Position = entry.Offset;
            byte[] data = new byte[entry.OriginalSize];
            stream.ReadExactly(data);
            int length = data.Length;
            while (length > 0 && data[length - 1] == 0) length--;
            if (data.AsSpan(0, length).IndexOfAnyExceptInRange((byte)0x09, (byte)0x7e) >= 0)
            {
                throw new InvalidDataException($"'{entry.Path}' contains unsupported non-ASCII data.");
            }
            string text = Encoding.ASCII.GetString(data, 0, length);
            stadiums.Add(new StadiumEnvironment(entry.Path, text));
        }
        stadiums.Sort((left, right) => StringComparer.OrdinalIgnoreCase.Compare(left.DisplayName, right.DisplayName));
        return new StadiumEnvironmentArchive(metPath, stadiums);
    }

    public StadiumEnvironmentSaveResult SaveWithBackup(
        IReadOnlyDictionary<string, byte[]>? splineReplacements = null)
    {
        Dictionary<string, byte[]> replacements = Stadiums.Where(stadium => stadium.IsChanged)
            .ToDictionary(stadium => stadium.SourcePath, stadium => stadium.Serialize(), StringComparer.OrdinalIgnoreCase);
        int changedStadiums = replacements.Count;
        if (splineReplacements != null)
            foreach ((string path, byte[] data) in splineReplacements)
                replacements[path] = data;
        METArchiveBatchSaveResult result = METArchiveBatchEditor.SaveWithBackup(
            _metPath, replacements, "stadium-environment");
        return new StadiumEnvironmentSaveResult(result.BackupPath, changedStadiums, result.RebuiltArchive,
            splineReplacements?.Count ?? 0);
    }

    public void ResetAll()
    {
        foreach (StadiumEnvironment stadium in Stadiums) stadium.Reset();
    }

    private static string NormalizePath(string path) => path.Replace('\\', '/');
}

public sealed class StadiumEnvironment
{
    private readonly string _originalText;

    internal StadiumEnvironment(string sourcePath, string text)
    {
        SourcePath = sourcePath;
        FolderName = sourcePath.Replace('\\', '/').Split('/')[2];
        DisplayName = HumanizeFolder(FolderName);
        _originalText = text;
        Document = FieldDataDocument.Parse(text);
    }

    public string SourcePath { get; }
    public string FolderName { get; }
    public string DisplayName { get; }
    public FieldDataDocument Document { get; private set; }
    public bool IsChanged => !_originalText.Equals(Document.ToString(), StringComparison.Ordinal);

    public byte[] Serialize()
    {
        string text = Document.ToString();
        if (text.Any(character => character > 0x7f))
        {
            throw new InvalidDataException($"'{DisplayName}' contains a character that fielddata.txt cannot encode as ASCII.");
        }
        return Encoding.ASCII.GetBytes(text);
    }

    public void Reset() => Document = FieldDataDocument.Parse(_originalText);

    private static string HumanizeFolder(string folder) => folder.ToLowerInvariant() switch
    {
        "aquadome" => "Aquadome",
        "boardwalk" => "Boardwalk",
        "desert" => "Desert (Day)",
        "desertnight" => "Desert (Night)",
        "drivein" => "Drive-In (Day)",
        "driveinnight" => "Drive-In (Night)",
        "frazier" => "Frazier Field",
        "gatorflats" => "Gator Flats (Day)",
        "gatorflatsnight" => "Gator Flats (Night)",
        "memorial" => "Memorial Stadium",
        "quantum" => "Quantum Field",
        "scrapyard" => "Scrapyard",
        "steele" => "Steele Stadium",
        "wheeler" => "Wheeler Acres (Day)",
        "wheelernight" => "Wheeler Acres (Night)",
        _ => FieldDataValue.Humanize(folder)
    };

    public override string ToString() => DisplayName;
}

public sealed record StadiumEnvironmentSaveResult(
    string? BackupPath, int ChangedStadiumCount, bool RebuiltArchive, int ChangedSplineCount = 0)
{
    public int ChangedEntryCount => ChangedStadiumCount + ChangedSplineCount;
}
