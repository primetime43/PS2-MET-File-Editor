using PS2_DATA_File_Extractor.Models;
using System.Buffers.Binary;
using System.Text.RegularExpressions;

namespace PS2_DATA_File_Extractor.FileOperations;

/// <summary>
/// Reads and safely replaces the retail season schedule templates in DATA.MET.
/// Each day contains twelve consecutive two-team matchups using team-slot IDs 0-23.
/// </summary>
public sealed class SeasonScheduleArchive
{
    public const int TeamCount = 24;
    public const int GamesPerRound = TeamCount / 2;
    public const int TemplateByteLength = 3072;
    public const int PaddingValue = unchecked((int)0xcccccccc);

    private static readonly Regex SchedulePathPattern = new(
        @"^data/schedules/templateschedule(?<rounds>18|32)_(?<variant>\d{2})\.dat$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private readonly string _metPath;

    private SeasonScheduleArchive(string metPath, IReadOnlyList<SeasonScheduleTemplate> templates)
    {
        _metPath = metPath;
        Templates = templates;
    }

    public IReadOnlyList<SeasonScheduleTemplate> Templates { get; }
    public bool HasChanges => Templates.Any(template => template.IsChanged);

    public static SeasonScheduleArchive Load(string metPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(metPath);
        METFileStructure structure = METFileReader.ReadMETFile(metPath);
        List<(FileEntry Entry, int Rounds, int Variant)> entries = new();

        foreach (FileEntry entry in structure.AllEntries)
        {
            string path = NormalizePath(entry.Path);
            Match match = SchedulePathPattern.Match(path);
            if (!match.Success) continue;

            entries.Add((entry,
                int.Parse(match.Groups["rounds"].Value),
                int.Parse(match.Groups["variant"].Value)));
        }

        if (entries.Count == 0)
        {
            throw new InvalidDataException(
                "This DATA.MET does not contain the supported Backyard Baseball season schedule templates.");
        }

        List<SeasonScheduleTemplate> templates = new(entries.Count);
        using FileStream stream = new(metPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        foreach ((FileEntry entry, int rounds, int variant) in entries
                     .OrderBy(item => item.Rounds).ThenBy(item => item.Variant))
        {
            if (entry.OriginalSize != TemplateByteLength)
            {
                throw new InvalidDataException(
                    $"{entry.Path} is {entry.OriginalSize:N0} bytes; a schedule template must be " +
                    $"{TemplateByteLength:N0} bytes.");
            }

            stream.Position = entry.Offset;
            byte[] data = new byte[entry.OriginalSize];
            stream.ReadExactly(data);
            templates.Add(SeasonScheduleTemplate.Parse(NormalizePath(entry.Path), rounds, variant, data));
        }

        return new SeasonScheduleArchive(metPath, templates);
    }

    public SeasonScheduleSaveResult SaveWithBackup()
    {
        Dictionary<string, byte[]> replacements = new(StringComparer.OrdinalIgnoreCase);
        foreach (SeasonScheduleTemplate template in Templates.Where(template => template.IsChanged))
        {
            IReadOnlyList<string> errors = template.Validate();
            if (errors.Count > 0)
            {
                throw new InvalidDataException(
                    $"{template.DisplayName} cannot be saved:{Environment.NewLine}" +
                    string.Join(Environment.NewLine, errors.Select(error => $"• {error}")));
            }

            replacements[template.SourcePath] = template.Serialize();
        }

        METArchiveBatchSaveResult result = METArchiveBatchEditor.SaveWithBackup(
            _metPath, replacements, "season-schedules");
        if (result.ChangedEntryCount > 0)
        {
            foreach (SeasonScheduleTemplate template in Templates.Where(template => template.IsChanged))
                template.AcceptChanges();
        }

        return new SeasonScheduleSaveResult(result.BackupPath, result.ChangedEntryCount, result.RebuiltArchive);
    }

    private static string NormalizePath(string path) => path.Replace('\\', '/');
}

public sealed class SeasonScheduleTemplate
{
    private readonly int[] _teams;
    private int[] _originalTeams;
    private byte[] _originalBytes;

    private SeasonScheduleTemplate(
        string sourcePath,
        int roundCount,
        int variantIndex,
        int[] teams,
        byte[] originalBytes)
    {
        SourcePath = sourcePath;
        RoundCount = roundCount;
        VariantIndex = variantIndex;
        _teams = teams;
        _originalTeams = (int[])teams.Clone();
        _originalBytes = (byte[])originalBytes.Clone();
    }

    public string SourcePath { get; }
    public int RoundCount { get; }
    public int VariantIndex { get; }
    public string DisplayName => $"{RoundCount}-game season — template {VariantIndex + 1:00}";
    public bool IsChanged => !_teams.SequenceEqual(_originalTeams);

    public static SeasonScheduleTemplate Parse(
        string sourcePath,
        int roundCount,
        int variantIndex,
        ReadOnlySpan<byte> data)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        if (roundCount is not (18 or 32))
            throw new ArgumentOutOfRangeException(nameof(roundCount), "Only 18- and 32-game templates are supported.");
        if (data.Length != SeasonScheduleArchive.TemplateByteLength)
            throw new InvalidDataException(
                $"{sourcePath} is {data.Length:N0} bytes; expected {SeasonScheduleArchive.TemplateByteLength:N0}.");

        int scheduledValueCount = checked(roundCount * SeasonScheduleArchive.TeamCount);
        int[] teams = new int[scheduledValueCount];
        for (int index = 0; index < teams.Length; index++)
            teams[index] = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(index * sizeof(int), sizeof(int)));

        if (roundCount == 18)
        {
            for (int index = scheduledValueCount; index < data.Length / sizeof(int); index++)
            {
                int value = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(index * sizeof(int), sizeof(int)));
                if (value != SeasonScheduleArchive.PaddingValue)
                    throw new InvalidDataException(
                        $"{sourcePath} has unexpected data in its unused 18-game padding at value {index}.");
            }
        }

        SeasonScheduleTemplate template = new(
            sourcePath, roundCount, variantIndex, teams, data.ToArray());
        IReadOnlyList<string> errors = template.Validate();
        if (errors.Count > 0)
        {
            throw new InvalidDataException(
                $"{sourcePath} is not a valid season schedule:{Environment.NewLine}" +
                string.Join(Environment.NewLine, errors.Select(error => $"• {error}")));
        }

        return template;
    }

    public int GetTeam(int roundIndex, int position)
    {
        ValidateRoundAndPosition(roundIndex, position);
        return _teams[roundIndex * SeasonScheduleArchive.TeamCount + position];
    }

    public SeasonScheduleGame GetGame(int roundIndex, int gameIndex)
    {
        ValidateGame(roundIndex, gameIndex);
        int position = gameIndex * 2;
        return new SeasonScheduleGame(GetTeam(roundIndex, position), GetTeam(roundIndex, position + 1));
    }

    /// <summary>
    /// Assigns a team slot while keeping the day a permutation of all 24 slots. If the requested
    /// team is already in another game, that position receives the previous value automatically.
    /// </summary>
    public void AssignTeam(int roundIndex, int position, int teamSlot)
    {
        ValidateRoundAndPosition(roundIndex, position);
        if (teamSlot is < 0 or >= SeasonScheduleArchive.TeamCount)
            throw new ArgumentOutOfRangeException(nameof(teamSlot));

        int start = roundIndex * SeasonScheduleArchive.TeamCount;
        int target = start + position;
        int previous = _teams[target];
        if (previous == teamSlot) return;

        int existing = Array.IndexOf(_teams, teamSlot, start, SeasonScheduleArchive.TeamCount);
        if (existing < 0)
            throw new InvalidDataException($"Team slot {teamSlot} is missing from round {roundIndex + 1}.");

        _teams[target] = teamSlot;
        _teams[existing] = previous;
    }

    public void SwapGameSides(int roundIndex, int gameIndex)
    {
        ValidateGame(roundIndex, gameIndex);
        int first = roundIndex * SeasonScheduleArchive.TeamCount + gameIndex * 2;
        (_teams[first], _teams[first + 1]) = (_teams[first + 1], _teams[first]);
    }

    public void ResetRound(int roundIndex)
    {
        if (roundIndex < 0 || roundIndex >= RoundCount) throw new ArgumentOutOfRangeException(nameof(roundIndex));
        int start = roundIndex * SeasonScheduleArchive.TeamCount;
        Array.Copy(_originalTeams, start, _teams, start, SeasonScheduleArchive.TeamCount);
    }

    public void Reset()
    {
        Array.Copy(_originalTeams, _teams, _teams.Length);
    }

    public IReadOnlyList<string> Validate()
    {
        List<string> errors = new();
        for (int round = 0; round < RoundCount; round++)
        {
            int[] counts = new int[SeasonScheduleArchive.TeamCount];
            for (int position = 0; position < SeasonScheduleArchive.TeamCount; position++)
            {
                int team = _teams[round * SeasonScheduleArchive.TeamCount + position];
                if (team is < 0 or >= SeasonScheduleArchive.TeamCount)
                {
                    errors.Add($"Round {round + 1} contains out-of-range team slot {team}.");
                    continue;
                }

                counts[team]++;
            }

            int[] missing = Enumerable.Range(0, counts.Length).Where(index => counts[index] == 0).ToArray();
            int[] duplicates = Enumerable.Range(0, counts.Length).Where(index => counts[index] > 1).ToArray();
            if (missing.Length > 0)
                errors.Add($"Round {round + 1} is missing team slot(s): {string.Join(", ", missing)}.");
            if (duplicates.Length > 0)
                errors.Add($"Round {round + 1} repeats team slot(s): {string.Join(", ", duplicates)}.");
        }

        return errors;
    }

    public byte[] Serialize()
    {
        byte[] output = (byte[])_originalBytes.Clone();
        for (int index = 0; index < _teams.Length; index++)
            BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(index * sizeof(int), sizeof(int)), _teams[index]);
        return output;
    }

    internal void AcceptChanges()
    {
        _originalBytes = Serialize();
        _originalTeams = (int[])_teams.Clone();
    }

    private void ValidateGame(int roundIndex, int gameIndex)
    {
        if (roundIndex < 0 || roundIndex >= RoundCount) throw new ArgumentOutOfRangeException(nameof(roundIndex));
        if (gameIndex is < 0 or >= SeasonScheduleArchive.GamesPerRound)
            throw new ArgumentOutOfRangeException(nameof(gameIndex));
    }

    private void ValidateRoundAndPosition(int roundIndex, int position)
    {
        if (roundIndex < 0 || roundIndex >= RoundCount) throw new ArgumentOutOfRangeException(nameof(roundIndex));
        if (position is < 0 or >= SeasonScheduleArchive.TeamCount)
            throw new ArgumentOutOfRangeException(nameof(position));
    }
}

public readonly record struct SeasonScheduleGame(int TeamA, int TeamB);

public sealed record SeasonScheduleSaveResult(string? BackupPath, int ChangedTemplateCount, bool RebuiltArchive);
