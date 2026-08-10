using PS2_DATA_File_Extractor.Models;
using System.Text;

namespace PS2_DATA_File_Extractor.FileOperations;

public sealed class PlayerStatsArchive
{
    private const string StatsDirectory = "data/kids/stats/";
    private readonly string _metPath;

    private PlayerStatsArchive(string metPath, List<PlayerStatsRecord> players)
    {
        _metPath = metPath;
        Players = players;
    }

    public IReadOnlyList<PlayerStatsRecord> Players { get; }
    public int ChangedPlayerCount => Players.Count(player => player.IsChanged);

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

        players.Sort((left, right) =>
        {
            int cloneOrder = left.IsClone.CompareTo(right.IsClone);
            if (cloneOrder != 0) return cloneOrder;
            int lastName = StringComparer.OrdinalIgnoreCase.Compare(left.LastName, right.LastName);
            return lastName != 0 ? lastName : StringComparer.OrdinalIgnoreCase.Compare(left.FirstName, right.FirstName);
        });
        return new PlayerStatsArchive(metPath, players);
    }

    public PlayerStatsSaveResult SaveWithBackup()
    {
        Dictionary<string, byte[]> replacements = Players
            .Where(player => player.IsChanged)
            .ToDictionary(player => player.SourcePath, player => player.Serialize(), StringComparer.OrdinalIgnoreCase);

        METArchiveBatchSaveResult result = METArchiveBatchEditor.SaveWithBackup(
            _metPath, replacements, "player-stats");
        return new PlayerStatsSaveResult(result.BackupPath, result.ChangedEntryCount, result.RebuiltArchive);
    }

    public void ResetAll()
    {
        foreach (PlayerStatsRecord player in Players) player.Reset();
    }

    private static string NormalizePath(string path) => path.Replace('\\', '/');
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

public sealed record PlayerStatsSaveResult(string? BackupPath, int ChangedPlayerCount, bool RebuiltArchive);
