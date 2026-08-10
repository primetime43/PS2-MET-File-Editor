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
        if (!TryGetEntry(player, out FileEntry? foundEntry)) return null;
        FileEntry entry = foundEntry!;

        using FileStream stream = new(_metPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        stream.Position = entry.Offset;
        byte[] data = new byte[entry.OriginalSize];
        stream.ReadExactly(data);
        return new PlayerPortrait(entry.Path, data);
    }

    public PlayerPortraitSaveResult ReplaceWithBackup(PlayerStatsRecord player, byte[] pngData)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(pngData);
        if (!TryGetEntry(player, out FileEntry? foundEntry))
            throw new InvalidOperationException("This player has no replaceable portrait entry in DATA.MET.");
        FileEntry entry = foundEntry!;
        ValidatePng(pngData);

        Dictionary<string, byte[]> replacement = new(StringComparer.OrdinalIgnoreCase)
        {
            [entry.Path] = pngData
        };
        METArchiveBatchSaveResult result = METArchiveBatchEditor.SaveWithBackup(
            _metPath, replacement, "player-portrait");
        return new PlayerPortraitSaveResult(entry.Path, result.BackupPath, result.RebuiltArchive);
    }

    private bool TryGetEntry(PlayerStatsRecord player, out FileEntry? entry)
    {
        entry = null;
        if (player.IsClone) return false;

        string code = Path.GetFileNameWithoutExtension(player.SourceName);
        if (code.EndsWith("_stats", StringComparison.OrdinalIgnoreCase)) code = code[..^6];
        if (CodeAliases.TryGetValue(code, out string? alias)) code = alias;
        return _entries.TryGetValue(code, out entry);
    }

    private static void ValidatePng(byte[] data)
    {
        ReadOnlySpan<byte> signature = new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a };
        if (data.Length < 24 || !data.AsSpan(0, signature.Length).SequenceEqual(signature) ||
            !data.AsSpan(12, 4).SequenceEqual("IHDR"u8))
            throw new InvalidDataException("The replacement is not a valid PNG image.");

        uint width = ReadBigEndian(data.AsSpan(16, 4));
        uint height = ReadBigEndian(data.AsSpan(20, 4));
        if (width == 0 || height == 0 || width > 4096 || height > 4096)
            throw new InvalidDataException("Portrait dimensions must be between 1 and 4096 pixels.");
    }

    private static uint ReadBigEndian(ReadOnlySpan<byte> value) =>
        ((uint)value[0] << 24) | ((uint)value[1] << 16) | ((uint)value[2] << 8) | value[3];
}

public sealed record PlayerPortrait(string SourcePath, byte[] Data);
public sealed record PlayerPortraitSaveResult(string SourcePath, string? BackupPath, bool RebuiltArchive);
