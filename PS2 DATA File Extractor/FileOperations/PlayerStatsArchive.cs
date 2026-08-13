using PS2_DATA_File_Extractor.Models;
using System.Buffers.Binary;
using System.Text;

namespace PS2_DATA_File_Extractor.FileOperations;

public sealed class PlayerStatsArchive
{
    private const string StatsDirectory = "data/kids/stats/";
    private static readonly IReadOnlyDictionary<string, string> BiographyCodeAliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["sorr"] = "sori"
        };
    private readonly string _metPath;
    private readonly IReadOnlyDictionary<string, PlayerBiography> _biographies;

    private PlayerStatsArchive(
        string metPath,
        List<PlayerStatsRecord> players,
        IReadOnlyDictionary<string, PlayerBiography> biographies)
    {
        _metPath = metPath;
        Players = players;
        _biographies = biographies;
    }

    public IReadOnlyList<PlayerStatsRecord> Players { get; }
    public IEnumerable<PlayerBiography> Biographies => _biographies.Values;
    public int ChangedPlayerCount => Players.Count(player => player.IsChanged);
    public int BiographyCount => _biographies.Count;
    public int ChangedBiographyCount => _biographies.Values.Count(biography => biography.IsChanged);
    public bool HasChanges => ChangedPlayerCount > 0 || ChangedBiographyCount > 0;

    public static PlayerStatsArchive Load(string metPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(metPath);
        METFileStructure structure = METFileReader.ReadMETFile(metPath);
        List<FileEntry> entries = structure.AllEntries
            .Where(entry =>
            {
                string path = NormalizePath(entry.Path);
                return path.StartsWith(StatsDirectory, StringComparison.OrdinalIgnoreCase) &&
                       path.EndsWith("_stats.dat", StringComparison.OrdinalIgnoreCase);
            })
            .ToList();
        List<FileEntry> biographyEntries = structure.AllEntries
            .Where(entry => IsBiographyPath(NormalizePath(entry.Path)))
            .ToList();

        if (entries.Count == 0)
        {
            throw new InvalidDataException("This MET archive does not contain Backyard Baseball player _stats.dat files.");
        }

        List<PlayerStatsRecord> players = new(entries.Count);
        using FileStream stream = new FileStream(metPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        foreach (FileEntry entry in entries)
        {
            stream.Position = entry.Offset;
            byte[] data = new byte[entry.OriginalSize];
            stream.ReadExactly(data);
            players.Add(PlayerStatsRecord.Parse(entry.Path, data));
        }

        Dictionary<string, PlayerBiography> biographies = new(StringComparer.OrdinalIgnoreCase);
        foreach (FileEntry entry in biographyEntries)
        {
            stream.Position = entry.Offset;
            byte[] data = new byte[entry.OriginalSize];
            stream.ReadExactly(data);
            PlayerBiography biography = PlayerBiography.Parse(entry.Path, data);
            if (!biographies.TryAdd(biography.PlayerCode, biography))
                throw new InvalidDataException($"More than one biography was found for player code '{biography.PlayerCode}'.");
        }

        players.Sort((left, right) =>
        {
            int cloneOrder = left.IsClone.CompareTo(right.IsClone);
            if (cloneOrder != 0) return cloneOrder;
            int lastName = StringComparer.OrdinalIgnoreCase.Compare(left.LastName, right.LastName);
            return lastName != 0 ? lastName : StringComparer.OrdinalIgnoreCase.Compare(left.FirstName, right.FirstName);
        });
        return new PlayerStatsArchive(metPath, players, biographies);
    }

    public PlayerBiography? GetBiography(PlayerStatsRecord player)
    {
        ArgumentNullException.ThrowIfNull(player);
        string name = Path.GetFileNameWithoutExtension(player.SourcePath);
        string code = name.EndsWith("_stats", StringComparison.OrdinalIgnoreCase) ? name[..^6] : name;
        if (BiographyCodeAliases.TryGetValue(code, out string? alias)) code = alias;
        return _biographies.GetValueOrDefault(code);
    }

    public PlayerStatsSaveResult SaveWithBackup()
    {
        int changedPlayers = ChangedPlayerCount;
        int changedBiographies = ChangedBiographyCount;
        Dictionary<string, byte[]> replacements = Players
            .Where(player => player.IsChanged)
            .ToDictionary(player => player.SourcePath, player => player.Serialize(), StringComparer.OrdinalIgnoreCase);
        foreach (PlayerBiography biography in _biographies.Values.Where(biography => biography.IsChanged))
            replacements.Add(biography.SourcePath, biography.Serialize());

        METArchiveBatchSaveResult result = METArchiveBatchEditor.SaveWithBackup(
            _metPath, replacements, "player-stats");
        foreach (PlayerBiography biography in _biographies.Values.Where(biography => biography.IsChanged))
            biography.AcceptChanges();
        return new PlayerStatsSaveResult(
            result.BackupPath, changedPlayers, changedBiographies, result.ChangedEntryCount, result.RebuiltArchive);
    }

    public void ResetAll()
    {
        foreach (PlayerStatsRecord player in Players) player.Reset();
        foreach (PlayerBiography biography in _biographies.Values) biography.Reset();
    }

    private static string NormalizePath(string path) => path.Replace('\\', '/');

    private static bool IsBiographyPath(string path)
    {
        const string prefix = "data/kids/";
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
            !path.EndsWith("_bio.dat", StringComparison.OrdinalIgnoreCase))
            return false;
        string[] parts = path[prefix.Length..].Split('/');
        if (parts.Length != 2) return false;
        string expectedFile = parts[0] + "_bio.dat";
        return parts[1].Equals(expectedFile, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// A player-card biography. The leading Int32 tells PlayerCard::LoadBioLines how many source lines
/// to read. The game joins those lines with spaces, then wraps the result into display lines.
/// </summary>
public sealed class PlayerBiography
{
    public const int MaximumTextBytes = 4096;
    public const int MaximumSourceLines = 256;
    private static readonly Encoding TextEncoding = Encoding.ASCII;
    private byte[] _originalBytes;
    private string _originalText;

    private PlayerBiography(
        string sourcePath,
        string playerCode,
        int storedSourceLineCount,
        string text,
        byte[] originalBytes)
    {
        SourcePath = sourcePath;
        PlayerCode = playerCode;
        StoredSourceLineCount = storedSourceLineCount;
        Text = text;
        _originalText = text;
        _originalBytes = originalBytes;
    }

    public string SourcePath { get; }
    public string PlayerCode { get; }
    public int StoredSourceLineCount { get; private set; }
    public string Text { get; set; }
    public bool IsChanged => !NormalizeLineEndings(Text).Equals(_originalText, StringComparison.Ordinal);
    public int SourceLineCount => SplitSourceLines(Text).Length;
    public IReadOnlyList<string> GameDisplayLines => WrapForGame(Text);

    public static PlayerBiography Parse(string sourcePath, byte[] data)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length < sizeof(int) + 1)
            throw new InvalidDataException($"'{sourcePath}' is too small to be a player biography.");

        int lineCount = BinaryPrimitives.ReadInt32LittleEndian(data);
        if (lineCount < 1 || lineCount > MaximumSourceLines)
            throw new InvalidDataException($"'{sourcePath}' has invalid source-line count {lineCount}.");
        ReadOnlySpan<byte> body = data.AsSpan(sizeof(int));
        ValidateTextBytes(body, sourcePath);
        int newlineCount = body.Count((byte)'\n');
        if (newlineCount < lineCount)
            throw new InvalidDataException(
                $"'{sourcePath}' says to load {lineCount} lines but stores only {newlineCount} newline terminators.");

        string rawText = TextEncoding.GetString(body).TrimEnd('\r', '\n');
        string text = NormalizeLineEndings(rawText);
        string fileName = Path.GetFileNameWithoutExtension(sourcePath);
        string playerCode = fileName.EndsWith("_bio", StringComparison.OrdinalIgnoreCase)
            ? fileName[..^4]
            : fileName;
        return new PlayerBiography(sourcePath, playerCode, lineCount, text, (byte[])data.Clone());
    }

    public byte[] Serialize()
    {
        string normalized = NormalizeLineEndings(Text).TrimEnd('\n');
        ValidateText(normalized);
        string[] lines = SplitSourceLines(normalized);
        byte[] body = TextEncoding.GetBytes(string.Join("\n", lines) + "\n");
        byte[] output = new byte[sizeof(int) + body.Length];
        BinaryPrimitives.WriteInt32LittleEndian(output, lines.Length);
        body.CopyTo(output, sizeof(int));
        return output;
    }

    public void Reset() => Text = _originalText;

    internal void AcceptChanges()
    {
        _originalBytes = Serialize();
        _originalText = NormalizeLineEndings(Text).TrimEnd('\n');
        Text = _originalText;
        StoredSourceLineCount = SplitSourceLines(Text).Length;
    }

    public static IReadOnlyList<string> WrapForGame(string text)
    {
        string joined = string.Join(" ", SplitSourceLines(text)).Trim();
        if (joined.Length == 0) return new[] { string.Empty };

        List<string> result = new();
        while (joined.Length >= 33)
        {
            int breakAt = joined[..31].LastIndexOf(' ');
            if (breakAt < 0)
            {
                result.Add(joined[..31]);
                joined = joined[31..].TrimStart();
                continue;
            }
            result.Add(joined[..breakAt]);
            joined = joined[(breakAt + 1)..];
        }
        result.Add(joined);
        return result;
    }

    private static string[] SplitSourceLines(string text)
    {
        string normalized = NormalizeLineEndings(text).TrimEnd('\n');
        return normalized.Length == 0 ? new[] { string.Empty } : normalized.Split('\n');
    }

    private static string NormalizeLineEndings(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
    }

    private static void ValidateText(string text)
    {
        string[] lines = SplitSourceLines(text);
        if (lines.Length > MaximumSourceLines)
            throw new InvalidDataException($"A biography cannot contain more than {MaximumSourceLines} source lines.");
        foreach (char character in text)
        {
            if (character != '\n' && (character < 0x20 || character > 0x7e))
                throw new InvalidDataException("Biography text must use printable ASCII characters and line breaks.");
        }
        string? longWord = text.Split(new[] { ' ', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(word => word.Length > 30);
        if (longWord != null)
            throw new InvalidDataException(
                $"Biography words cannot exceed 30 characters because the game cannot wrap '{longWord}'.");
        int bytes = TextEncoding.GetByteCount(string.Join("\n", lines) + "\n");
        if (bytes > MaximumTextBytes)
            throw new InvalidDataException($"Biography text cannot exceed {MaximumTextBytes:N0} bytes.");
    }

    private static void ValidateTextBytes(ReadOnlySpan<byte> data, string sourcePath)
    {
        foreach (byte value in data)
        {
            if (value is not (0x0a or 0x0d or 0x09) && (value < 0x20 || value > 0x7e))
                throw new InvalidDataException($"'{sourcePath}' contains unsupported biography text bytes.");
        }
    }
}

public sealed class PlayerStatsRecord
{
    public const int BaseFieldCount = 31;
    public const int CloneAppearanceFieldCount = 8;
    public const int MaxNameLength = 31;
    private static readonly Encoding NameEncoding = Encoding.ASCII;
    private readonly short[] _originalBaseValues;
    private readonly short[] _originalCloneAppearance;
    private readonly string _originalFirstName;
    private readonly string _originalNickname;
    private readonly string _originalLastName;

    private PlayerStatsRecord(
        string sourcePath,
        bool isClone,
        short[] baseValues,
        short[] cloneAppearance,
        string firstName,
        string nickname,
        string lastName)
    {
        SourcePath = sourcePath;
        IsClone = isClone;
        BaseValues = baseValues;
        CloneAppearance = cloneAppearance;
        FirstName = firstName;
        Nickname = nickname;
        LastName = lastName;
        _originalBaseValues = (short[])baseValues.Clone();
        _originalCloneAppearance = (short[])cloneAppearance.Clone();
        _originalFirstName = firstName;
        _originalNickname = nickname;
        _originalLastName = lastName;
    }

    public string SourcePath { get; }
    public string SourceName => Path.GetFileName(SourcePath);
    public bool IsClone { get; }
    public short[] BaseValues { get; }
    public short[] CloneAppearance { get; }
    public string FirstName { get; set; }
    public string Nickname { get; set; }
    public string LastName { get; set; }
    public string DisplayName
    {
        get
        {
            string nickname = string.IsNullOrWhiteSpace(Nickname) ? string.Empty : $" \"{Nickname}\"";
            string name = $"{FirstName}{nickname} {LastName}".Trim();
            return string.IsNullOrWhiteSpace(name) ? SourceName : name;
        }
    }

    public bool IsChanged =>
        !_originalBaseValues.SequenceEqual(BaseValues) ||
        !_originalCloneAppearance.SequenceEqual(CloneAppearance) ||
        !_originalFirstName.Equals(FirstName, StringComparison.Ordinal) ||
        !_originalNickname.Equals(Nickname, StringComparison.Ordinal) ||
        !_originalLastName.Equals(LastName, StringComparison.Ordinal);

    public int PowerRating => BaseValues[1];
    public int ContactRating => Average(BaseValues[3], BaseValues[4], BaseValues[8]);
    public int FieldingRating => Average(BaseValues[2], BaseValues[6], BaseValues[9]);
    public int RunningRating => BaseValues[7];
    public int PitchingRating => (Math.Min(100, Average(BaseValues[14], BaseValues[15], BaseValues[16], BaseValues[17])) + BaseValues[5]) / 2;

    public static PlayerStatsRecord Parse(string sourcePath, byte[] data)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentNullException.ThrowIfNull(data);
        bool isClone = Path.GetFileName(sourcePath).StartsWith("Clone", StringComparison.OrdinalIgnoreCase);
        int numericFieldCount = BaseFieldCount + (isClone ? CloneAppearanceFieldCount : 0);
        int prefixLength = numericFieldCount * sizeof(short);
        if (data.Length < prefixLength + 3)
        {
            throw new InvalidDataException($"'{sourcePath}' is too small to be a player stats record.");
        }

        short[] values = new short[numericFieldCount];
        for (int index = 0; index < values.Length; index++)
        {
            values[index] = (short)(data[index * 2] | (data[index * 2 + 1] << 8));
        }

        int firstComma = FindComma(data, prefixLength, sourcePath);
        int secondComma = FindComma(data, firstComma + 1, sourcePath);
        int thirdComma = FindComma(data, secondComma + 1, sourcePath);
        if (data.AsSpan(thirdComma + 1).IndexOfAnyExcept((byte)0) >= 0)
        {
            throw new InvalidDataException($"'{sourcePath}' contains unexpected data after its three player names.");
        }

        ReadOnlySpan<byte> firstNameBytes = data.AsSpan(prefixLength, firstComma - prefixLength);
        ReadOnlySpan<byte> nicknameBytes = data.AsSpan(firstComma + 1, secondComma - firstComma - 1);
        ReadOnlySpan<byte> lastNameBytes = data.AsSpan(secondComma + 1, thirdComma - secondComma - 1);
        ValidateNameBytes(firstNameBytes, sourcePath);
        ValidateNameBytes(nicknameBytes, sourcePath);
        ValidateNameBytes(lastNameBytes, sourcePath);
        string firstName = NameEncoding.GetString(firstNameBytes);
        string nickname = NameEncoding.GetString(nicknameBytes);
        string lastName = NameEncoding.GetString(lastNameBytes);
        ValidateName(firstName, nameof(FirstName));
        ValidateName(nickname, nameof(Nickname));
        ValidateName(lastName, nameof(LastName));

        return new PlayerStatsRecord(
            sourcePath,
            isClone,
            values[..BaseFieldCount],
            isClone ? values[BaseFieldCount..] : Array.Empty<short>(),
            firstName,
            nickname,
            lastName);
    }

    public byte[] Serialize()
    {
        ValidateName(FirstName, nameof(FirstName));
        ValidateName(Nickname, nameof(Nickname));
        ValidateName(LastName, nameof(LastName));
        using MemoryStream stream = new MemoryStream();
        using BinaryWriter writer = new BinaryWriter(stream, NameEncoding, leaveOpen: true);
        foreach (short value in BaseValues) writer.Write(value);
        foreach (short value in CloneAppearance) writer.Write(value);
        writer.Write(NameEncoding.GetBytes(FirstName));
        writer.Write((byte)',');
        writer.Write(NameEncoding.GetBytes(Nickname));
        writer.Write((byte)',');
        writer.Write(NameEncoding.GetBytes(LastName));
        writer.Write((byte)',');
        writer.Flush();
        return stream.ToArray();
    }

    public void Reset()
    {
        _originalBaseValues.CopyTo(BaseValues, 0);
        _originalCloneAppearance.CopyTo(CloneAppearance, 0);
        FirstName = _originalFirstName;
        Nickname = _originalNickname;
        LastName = _originalLastName;
    }

    public void MaximizeSkills()
    {
        int[] directSkillFields = { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 13 };
        foreach (int index in directSkillFields) BaseValues[index] = 100;
        BaseValues[12] = 0; // The executable uses 100 - this value for run acceleration.
        for (int index = 14; index <= 25; index++) BaseValues[index] = 100;
    }

    private static int FindComma(byte[] data, int start, string sourcePath)
    {
        int relative = data.AsSpan(start).IndexOf((byte)',');
        if (relative < 0)
        {
            throw new InvalidDataException($"'{sourcePath}' is missing a comma-delimited player name field.");
        }
        return start + relative;
    }

    private static void ValidateNameBytes(ReadOnlySpan<byte> value, string sourcePath)
    {
        foreach (byte character in value)
        {
            if (character < 0x20 || character > 0x7e)
            {
                throw new InvalidDataException($"'{sourcePath}' contains a non-ASCII player name.");
            }
        }
    }

    private static void ValidateName(string value, string fieldName)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length > MaxNameLength)
        {
            throw new InvalidDataException($"{fieldName} cannot be longer than {MaxNameLength} characters.");
        }
        if (value.Any(character => character == ',' || character < 0x20 || character > 0x7e))
        {
            throw new InvalidDataException($"{fieldName} must use printable ASCII characters and cannot contain commas.");
        }
    }

    private static int Average(params short[] values) => values.Sum(value => (int)value) / values.Length;
}

public sealed record PlayerStatsSaveResult(
    string? BackupPath,
    int ChangedPlayerCount,
    int ChangedBiographyCount,
    int ChangedEntryCount,
    bool RebuiltArchive);
