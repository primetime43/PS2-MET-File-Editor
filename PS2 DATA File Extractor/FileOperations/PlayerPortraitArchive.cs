using System.Drawing.Imaging;
using System.Text;
using PS2_DATA_File_Extractor.Models;

namespace PS2_DATA_File_Extractor.FileOperations;

public sealed class PlayerPortraitArchive
{
    private const string PortraitDirectory = "data/polaroids/";
    private const string PackedImportPath = "data/menus/polaroids.imp";
    private const string PackedTextureDirectory = "data/menus/";

    private static readonly IReadOnlyDictionary<string, string> CodeAliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["delg"] = "del",
            ["sidn"] = "sydn",
            ["sorr"] = "sori"
        };

    private static readonly IReadOnlyDictionary<string, string> PackedNameAliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["champs"] = "final",
            ["del"] = "delg",
            ["quas"] = "comq",
            ["sydn"] = "sidn"
        };

    private readonly string _metPath;
    private readonly IReadOnlyDictionary<string, FileEntry> _portraitEntries;
    private readonly IReadOnlyDictionary<string, PackedPortraitDefinition> _packedPortraits;

    private PlayerPortraitArchive(
        string metPath,
        IReadOnlyDictionary<string, FileEntry> portraitEntries,
        IReadOnlyDictionary<string, PackedPortraitDefinition> packedPortraits)
    {
        _metPath = metPath;
        _portraitEntries = portraitEntries;
        _packedPortraits = packedPortraits;
    }

    public int PortraitCount => _portraitEntries.Count;
    public int PackedPortraitCount => _packedPortraits.Count;

    public static PlayerPortraitArchive Load(string metPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(metPath);
        METFileStructure structure = METFileReader.ReadMETFile(metPath);
        Dictionary<string, FileEntry> allEntries = structure.AllEntries.ToDictionary(
            entry => NormalizePath(entry.Path), StringComparer.OrdinalIgnoreCase);
        Dictionary<string, FileEntry> portraits = new(StringComparer.OrdinalIgnoreCase);
        foreach (FileEntry entry in structure.AllEntries)
        {
            string path = NormalizePath(entry.Path);
            if (!path.StartsWith(PortraitDirectory, StringComparison.OrdinalIgnoreCase) ||
                !path.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                path.Count(character => character == '/') != 2)
                continue;

            portraits[Path.GetFileNameWithoutExtension(path)] = entry;
        }

        IReadOnlyDictionary<string, PackedPortraitDefinition> packed =
            TryReadPackedPortraits(metPath, allEntries);
        return new PlayerPortraitArchive(metPath, portraits, packed);
    }

    public PlayerPortrait? GetPortrait(PlayerStatsRecord player)
    {
        ArgumentNullException.ThrowIfNull(player);
        if (!TryGetEntry(player, out FileEntry? foundEntry)) return null;
        FileEntry entry = foundEntry!;
        string rawCode = Path.GetFileNameWithoutExtension(entry.Path);

        return new PlayerPortrait(
            entry.Path,
            ReadEntry(_metPath, entry),
            TryGetPackedDefinition(rawCode, out _));
    }

    public PlayerPortraitSaveResult ReplaceWithBackup(PlayerStatsRecord player, byte[] pngData)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(pngData);
        if (!TryGetEntry(player, out FileEntry? foundEntry))
            throw new InvalidOperationException("This player has no replaceable portrait entry in DATA.MET.");
        FileEntry entry = foundEntry!;
        ValidatePng(pngData);

        Dictionary<string, byte[]> replacements = new(StringComparer.OrdinalIgnoreCase)
        {
            [entry.Path] = pngData
        };

        string rawCode = Path.GetFileNameWithoutExtension(entry.Path);
        int packedTextureCount = 0;
        if (TryGetPackedDefinition(rawCode, out PackedPortraitDefinition? definition))
        {
            AddPackedTextureReplacements(pngData, definition!, replacements);
            packedTextureCount = definition!.Pieces.Select(piece => piece.TextureEntry.Path)
                .Distinct(StringComparer.OrdinalIgnoreCase).Count();
        }

        METArchiveBatchSaveResult result = METArchiveBatchEditor.SaveWithBackup(
            _metPath, replacements, "player-portrait");
        return new PlayerPortraitSaveResult(
            entry.Path, result.BackupPath, result.RebuiltArchive, packedTextureCount);
    }

    private void AddPackedTextureReplacements(
        byte[] pngData,
        PackedPortraitDefinition definition,
        IDictionary<string, byte[]> replacements)
    {
        using Bitmap portrait = LoadBitmap(pngData);
        if (portrait.Width != definition.Width || portrait.Height != definition.Height)
        {
            throw new InvalidDataException(
                $"The packed game portrait requires exactly {definition.Width} by {definition.Height} pixels.");
        }

        foreach (IGrouping<string, PackedPortraitPiece> pagePieces in definition.Pieces.GroupBy(
                     piece => piece.TextureEntry.Path, StringComparer.OrdinalIgnoreCase))
        {
            PackedPortraitPiece firstPiece = pagePieces.First();
            using Bitmap page = LoadBitmap(ReadEntry(_metPath, firstPiece.TextureEntry));
            foreach (PackedPortraitPiece piece in pagePieces)
            {
                ValidatePieceBounds(definition, page, piece);
                for (int y = 0; y < piece.Height; y++)
                {
                    for (int x = 0; x < piece.Width; x++)
                    {
                        Color source = portrait.GetPixel(piece.DestinationX + x, piece.DestinationY + y);
                        Color existing = page.GetPixel(piece.SourceX + x, piece.SourceY + y);
                        int alpha = (existing.A * source.A + 127) / 255;
                        page.SetPixel(piece.SourceX + x, piece.SourceY + y,
                            Color.FromArgb(alpha, source.R, source.G, source.B));
                    }
                }
            }

            using MemoryStream stream = new();
            page.Save(stream, ImageFormat.Png);
            replacements[firstPiece.TextureEntry.Path] = stream.ToArray();
        }
    }

    private bool TryGetEntry(PlayerStatsRecord player, out FileEntry? entry)
    {
        entry = null;
        if (player.IsClone) return false;

        string code = Path.GetFileNameWithoutExtension(player.SourceName);
        if (code.EndsWith("_stats", StringComparison.OrdinalIgnoreCase)) code = code[..^6];
        if (CodeAliases.TryGetValue(code, out string? alias)) code = alias;
        return _portraitEntries.TryGetValue(code, out entry);
    }

    private bool TryGetPackedDefinition(string rawCode, out PackedPortraitDefinition? definition)
    {
        string key = NormalizeName(rawCode);
        if (PackedNameAliases.TryGetValue(key, out string? alias)) key = alias;
        return _packedPortraits.TryGetValue(key, out definition);
    }

    private static IReadOnlyDictionary<string, PackedPortraitDefinition> TryReadPackedPortraits(
        string metPath,
        IReadOnlyDictionary<string, FileEntry> allEntries)
    {
        Dictionary<string, PackedPortraitDefinition> result = new(StringComparer.OrdinalIgnoreCase);
        if (!allEntries.TryGetValue(PackedImportPath, out FileEntry? importEntry)) return result;

        try
        {
            byte[] data = ReadEntry(metPath, importEntry);
            if (data.Length < 16 || !data.AsSpan(0, 4).SequenceEqual(new byte[] { 0x49, 0x4d, 0x50, 0x1a }))
                return result;

            int textureCount = ReadInt32(data, 8);
            if (textureCount < 0 || textureCount > 4096) return result;
            int position = 12;
            Dictionary<int, FileEntry> textures = new();
            for (int index = 0; index < textureCount; index++)
            {
                EnsureAvailable(data, position, 64);
                int textureId = ReadInt32(data, position);
                string textureName = ReadFixedAscii(data, position + 4, 60);
                string texturePath = NormalizePath($"{PackedTextureDirectory}{textureName}.png");
                if (allEntries.TryGetValue(texturePath, out FileEntry? textureEntry))
                    textures[textureId] = textureEntry;
                position += 64;
            }

            int portraitCount = ReadInt32(data, position);
            position += 4;
            if (portraitCount < 0 || portraitCount > 4096) return result;
            for (int index = 0; index < portraitCount; index++)
            {
                EnsureAvailable(data, position, 44);
                string name = ReadFixedAscii(data, position, 32);
                int width = ReadInt32(data, position + 32);
                int height = ReadInt32(data, position + 36);
                int pieceCount = ReadInt32(data, position + 40);
                position += 44;
                if (width <= 0 || height <= 0 || width > 4096 || height > 4096 ||
                    pieceCount <= 0 || pieceCount > 64)
                    return new Dictionary<string, PackedPortraitDefinition>(StringComparer.OrdinalIgnoreCase);

                List<PackedPortraitPiece> pieces = new(pieceCount);
                bool complete = true;
                for (int pieceIndex = 0; pieceIndex < pieceCount; pieceIndex++)
                {
                    EnsureAvailable(data, position, 28);
                    int textureId = ReadInt32(data, position);
                    if (!textures.TryGetValue(textureId, out FileEntry? textureEntry)) complete = false;
                    else
                    {
                        pieces.Add(new PackedPortraitPiece(
                            textureEntry,
                            ReadInt32(data, position + 4),
                            ReadInt32(data, position + 8),
                            ReadInt32(data, position + 12),
                            ReadInt32(data, position + 16),
                            ReadInt32(data, position + 20),
                            ReadInt32(data, position + 24)));
                    }
                    position += 28;
                }

                if (complete) result[NormalizeName(name)] = new PackedPortraitDefinition(name, width, height, pieces);
            }
        }
        catch (Exception exception) when (exception is EndOfStreamException or ArgumentException or OverflowException)
        {
            result.Clear();
        }

        return result;
    }

    private static void ValidatePieceBounds(
        PackedPortraitDefinition definition,
        Bitmap page,
        PackedPortraitPiece piece)
    {
        if (piece.SourceX < 0 || piece.SourceY < 0 || piece.Width <= 0 || piece.Height <= 0 ||
            piece.SourceX + piece.Width > page.Width || piece.SourceY + piece.Height > page.Height ||
            piece.DestinationX < 0 || piece.DestinationY < 0 ||
            piece.DestinationX + piece.Width > definition.Width ||
            piece.DestinationY + piece.Height > definition.Height)
            throw new InvalidDataException("The packed portrait map contains an out-of-range texture region.");
    }

    private static Bitmap LoadBitmap(byte[] data)
    {
        using MemoryStream stream = new(data, writable: false);
        using Image source = Image.FromStream(stream);
        return new Bitmap(source);
    }

    private static byte[] ReadEntry(string metPath, FileEntry entry)
    {
        using FileStream stream = new(metPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        stream.Position = entry.Offset;
        byte[] data = new byte[entry.OriginalSize];
        stream.ReadExactly(data);
        return data;
    }

    private static int ReadInt32(byte[] data, int offset)
    {
        EnsureAvailable(data, offset, 4);
        return BitConverter.ToInt32(data, offset);
    }

    private static string ReadFixedAscii(byte[] data, int offset, int length)
    {
        EnsureAvailable(data, offset, length);
        int end = Array.IndexOf(data, (byte)0, offset, length);
        if (end < 0) end = offset + length;
        return Encoding.ASCII.GetString(data, offset, end - offset);
    }

    private static void EnsureAvailable(byte[] data, int offset, int count)
    {
        if (offset < 0 || count < 0 || offset > data.Length - count) throw new EndOfStreamException();
    }

    private static string NormalizePath(string path) => path.Replace('\\', '/');
    private static string NormalizeName(string value) =>
        new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

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

    private sealed record PackedPortraitDefinition(
        string Name, int Width, int Height, IReadOnlyList<PackedPortraitPiece> Pieces);

    private sealed record PackedPortraitPiece(
        FileEntry TextureEntry,
        int SourceX,
        int SourceY,
        int Width,
        int Height,
        int DestinationX,
        int DestinationY);
}

public sealed record PlayerPortrait(string SourcePath, byte[] Data, bool HasPackedGameTexture = false);
public sealed record PlayerPortraitSaveResult(
    string SourcePath, string? BackupPath, bool RebuiltArchive, int PackedTextureCount = 0);
