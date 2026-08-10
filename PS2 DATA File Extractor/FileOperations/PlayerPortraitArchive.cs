using PS2_DATA_File_Extractor.Models;

namespace PS2_DATA_File_Extractor.FileOperations;

public sealed class PlayerPortraitArchive
{
    private const string PortraitDirectory = "data/polaroids/";
    private static readonly IReadOnlyDictionary<string, string> CodeAliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["delg"] = "del",
            ["sidn"] = "sydn",
            ["sorr"] = "sori"
        };

    private readonly string _metPath;
    private readonly IReadOnlyDictionary<string, FileEntry> _entries;

    private PlayerPortraitArchive(string metPath, IReadOnlyDictionary<string, FileEntry> entries)
    {
        _metPath = metPath;
        _entries = entries;
    }

    public int PortraitCount => _entries.Count;

    public static PlayerPortraitArchive Load(string metPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(metPath);
        METFileStructure structure = METFileReader.ReadMETFile(metPath);
        Dictionary<string, FileEntry> entries = new(StringComparer.OrdinalIgnoreCase);
        foreach (FileEntry entry in structure.AllEntries)
        {
            string path = entry.Path.Replace('\\', '/');
            if (!path.StartsWith(PortraitDirectory, StringComparison.OrdinalIgnoreCase) ||
                !path.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                path.Count(character => character == '/') != 2)
                continue;

            entries[Path.GetFileNameWithoutExtension(path)] = entry;
        }
        return new PlayerPortraitArchive(metPath, entries);
    }

    public PlayerPortrait? GetPortrait(PlayerStatsRecord player)
    {
        ArgumentNullException.ThrowIfNull(player);
        if (player.IsClone) return null;

        string code = Path.GetFileNameWithoutExtension(player.SourceName);
        if (code.EndsWith("_stats", StringComparison.OrdinalIgnoreCase)) code = code[..^6];
        if (CodeAliases.TryGetValue(code, out string? alias)) code = alias;
        if (!_entries.TryGetValue(code, out FileEntry? entry)) return null;

        using FileStream stream = new(_metPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        stream.Position = entry.Offset;
        byte[] data = new byte[entry.OriginalSize];
        stream.ReadExactly(data);
        return new PlayerPortrait(entry.Path, data);
    }
}

public sealed record PlayerPortrait(string SourcePath, byte[] Data);
