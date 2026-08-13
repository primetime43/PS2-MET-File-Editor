using PS2_DATA_File_Extractor.Models;
using System.Text;
using System.Text.RegularExpressions;

namespace PS2_DATA_File_Extractor.FileOperations;

/// <summary>
/// Edits the six division lists loaded by SeasonOptions from data/options/menuoptions.ini.
/// Team IDs are stable ETeamID values; they are not the 0-23 slots stored in schedule templates.
/// </summary>
public sealed class TeamLeagueArchive
{
    public const string SourcePath = "data/options/menuoptions.ini";

    private static readonly UTF8Encoding Utf8WithoutBom = new(false);
    private readonly string _metPath;
    private readonly TeamLeagueIniDocument _document;

    private TeamLeagueArchive(string metPath, TeamLeagueIniDocument document, TeamLeagueSetup setup)
    {
        _metPath = metPath;
        _document = document;
        Setup = setup;
    }

    public TeamLeagueSetup Setup { get; }
    public bool HasChanges => Setup.HasChanges;

    public static TeamLeagueArchive Load(string metPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(metPath);
        METFileStructure structure = METFileReader.ReadMETFile(metPath);
        FileEntry entry = structure.AllEntries.FirstOrDefault(candidate =>
            NormalizePath(candidate.Path).Equals(SourcePath, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException($"This DATA.MET does not contain '{SourcePath}'.");

        using FileStream stream = File.OpenRead(metPath);
        stream.Position = entry.Offset;
        byte[] payload = new byte[entry.OriginalSize];
        stream.ReadExactly(payload);
        int textLength = payload.Length;
        while (textLength > 0 && payload[textLength - 1] == 0) textLength--;

        string text = Utf8WithoutBom.GetString(payload, 0, textLength);
        TeamLeagueIniDocument document = TeamLeagueIniDocument.Parse(text);
        return new TeamLeagueArchive(metPath, document, document.CreateSetup());
    }

    public TeamLeagueSaveResult SaveWithBackup()
    {
        IReadOnlyList<string> errors = Setup.Validate();
        if (errors.Count > 0)
        {
            throw new InvalidDataException(
                "The league setup cannot be saved:" + Environment.NewLine +
                string.Join(Environment.NewLine, errors.Select(error => $"• {error}")));
        }

        if (!Setup.HasChanges) return new TeamLeagueSaveResult(null, 0, false);

        byte[] replacement = Utf8WithoutBom.GetBytes(_document.Render(Setup));
        METArchiveBatchSaveResult result = METArchiveBatchEditor.SaveWithBackup(
            _metPath,
            new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase) { [SourcePath] = replacement },
            "team-league-setup");
        Setup.AcceptChanges();
        return new TeamLeagueSaveResult(result.BackupPath, result.ChangedEntryCount, result.RebuiltArchive);
    }

    private static string NormalizePath(string path) => path.Replace('\\', '/');
}

public enum BaseballDivision
{
    ALWest,
    ALCentral,
    ALEast,
    NLWest,
    NLCentral,
    NLEast
}

public static class BaseballDivisionInfo
{
    public static readonly BaseballDivision[] All =
    {
        BaseballDivision.ALWest,
        BaseballDivision.ALCentral,
        BaseballDivision.ALEast,
        BaseballDivision.NLWest,
        BaseballDivision.NLCentral,
        BaseballDivision.NLEast
    };

    public static string IniKey(this BaseballDivision division) => division.ToString();

    public static string DisplayName(this BaseballDivision division) => division switch
    {
        BaseballDivision.ALWest => "AL West",
        BaseballDivision.ALCentral => "AL Central",
        BaseballDivision.ALEast => "AL East",
        BaseballDivision.NLWest => "NL West",
        BaseballDivision.NLCentral => "NL Central",
        BaseballDivision.NLEast => "NL East",
        _ => division.ToString()
    };

    public static string LeagueName(this BaseballDivision division) =>
        division is BaseballDivision.ALWest or BaseballDivision.ALCentral or BaseballDivision.ALEast
            ? "American League"
            : "National League";
}

public sealed record BaseballTeamDefinition(int Id, string Name, string IniSymbol)
{
    private static readonly IReadOnlyDictionary<int, BaseballTeamDefinition> Known =
        new BaseballTeamDefinition[]
        {
            new(0, "Anaheim Angels", "kAngels"),
            new(1, "Baltimore Orioles", "kOrioles"),
            new(2, "Boston Red Sox", "kRedSox"),
            new(3, "Chicago White Sox", "kWhiteSox"),
            new(4, "Cleveland Indians", "kIndians"),
            new(5, "Detroit Tigers", "kTigers"),
            new(6, "Kansas City Royals", "kRoyals"),
            new(7, "Minnesota Twins", "kTwins"),
            new(8, "New York Yankees", "kYankees"),
            new(9, "Oakland Athletics", "kAthletics"),
            new(10, "Seattle Mariners", "kMariners"),
            new(11, "Tampa Bay Devil Rays", "kDRays"),
            new(12, "Texas Rangers", "kRangers"),
            new(13, "Toronto Blue Jays", "kBlueJays"),
            new(14, "Arizona Diamondbacks", "kDiamondbacks"),
            new(15, "Atlanta Braves", "kBraves"),
            new(16, "Chicago Cubs", "kCubs"),
            new(17, "Cincinnati Reds", "kReds"),
            new(18, "Colorado Rockies", "kRockies"),
            new(19, "Florida Marlins", "kMarlins"),
            new(20, "Houston Astros", "kAstros"),
            new(21, "Los Angeles Dodgers", "kDodgers"),
            new(22, "Milwaukee Brewers", "kBrewers"),
            new(23, "Montreal Expos", "kExpos"),
            new(24, "New York Mets", "kMets"),
            new(25, "Philadelphia Phillies", "kPhillies"),
            new(26, "Pittsburgh Pirates", "kPirates"),
            new(27, "San Diego Padres", "kPadres"),
            new(28, "San Francisco Giants", "kGiants"),
            new(29, "St. Louis Cardinals", "kCardinals")
        }.ToDictionary(team => team.Id);

    public static BaseballTeamDefinition ForId(int id) => Known.TryGetValue(id, out BaseballTeamDefinition? team)
        ? team
        : new BaseballTeamDefinition(id, $"Unknown / custom team {id}", $"Team{id}");
}

public sealed record TeamLeaguePlacement(
    int TeamId,
    BaseballTeamDefinition Team,
    BaseballDivision Division,
    bool IsActive,
    int Position);

public sealed class TeamLeagueSetup
{
    private readonly Dictionary<(BaseballDivision Division, bool Active), List<int>> _groups;
    private Dictionary<(BaseballDivision Division, bool Active), int[]> _original;

    internal TeamLeagueSetup(Dictionary<(BaseballDivision Division, bool Active), List<int>> groups)
    {
        _groups = groups;
        _original = Snapshot();
    }

    public bool HasChanges => !SnapshotsEqual(_original, Snapshot());
    public int TeamCount => _groups.Values.Sum(group => group.Count);
    public int ActiveTeamCount => BaseballDivisionInfo.All.Sum(division => GetTeamIds(division, true).Count);
    public int InactiveTeamCount => TeamCount - ActiveTeamCount;

    public IReadOnlyList<int> GetTeamIds(BaseballDivision division, bool active) => _groups[(division, active)];

    public IReadOnlyList<TeamLeaguePlacement> GetPlacements()
    {
        List<TeamLeaguePlacement> placements = new(TeamCount);
        foreach (BaseballDivision division in BaseballDivisionInfo.All)
        {
            AddGroup(division, true);
            AddGroup(division, false);
        }

        return placements;

        void AddGroup(BaseballDivision division, bool active)
        {
            IReadOnlyList<int> ids = GetTeamIds(division, active);
            for (int index = 0; index < ids.Count; index++)
            {
                int id = ids[index];
                placements.Add(new TeamLeaguePlacement(
                    id, BaseballTeamDefinition.ForId(id), division, active, index));
            }
        }
    }

    public TeamLeaguePlacement GetPlacement(int teamId) => GetPlacements().Single(team => team.TeamId == teamId);

    public void MoveTeam(int teamId, BaseballDivision division, bool active)
    {
        Remove(teamId);
        _groups[(division, active)].Add(teamId);
    }

    public bool MoveWithinGroup(int teamId, int delta)
    {
        TeamLeaguePlacement placement = GetPlacement(teamId);
        List<int> group = _groups[(placement.Division, placement.IsActive)];
        int destination = placement.Position + delta;
        if (destination < 0 || destination >= group.Count) return false;
        (group[placement.Position], group[destination]) = (group[destination], group[placement.Position]);
        return true;
    }

    public void RestoreOriginal()
    {
        foreach (((BaseballDivision division, bool active), int[] ids) in _original)
            _groups[(division, active)] = ids.ToList();
    }

    public void RestoreRetailAlignment()
    {
        foreach (BaseballDivision division in BaseballDivisionInfo.All)
        {
            _groups[(division, true)].Clear();
            _groups[(division, false)].Clear();
        }

        Set(BaseballDivision.ALWest, 10, 0, 12, 9);
        Set(BaseballDivision.ALCentral, 4, 7, 3, 5, 6);
        Set(BaseballDivision.ALEast, 8, 2, 13, 1, 11);
        Set(BaseballDivision.NLWest, 14, 28, 21, 27, 18);
        Set(BaseballDivision.NLCentral, 20, 29, 16, 22, 17, 26);
        Set(BaseballDivision.NLEast, 15, 25, 24, 19, 23);

        void Set(BaseballDivision division, params int[] teamIds) =>
            _groups[(division, true)].AddRange(teamIds);
    }

    public IReadOnlyList<string> Validate()
    {
        List<string> errors = new();
        List<int> ids = _groups.Values.SelectMany(group => group).ToList();
        foreach (IGrouping<int, int> duplicate in ids.GroupBy(id => id).Where(group => group.Count() > 1))
            errors.Add($"Team ID {duplicate.Key} appears {duplicate.Count()} times.");
        foreach (int id in ids.Where(id => id < 0).Distinct())
            errors.Add($"Team ID {id} is invalid.");
        foreach (BaseballDivision division in BaseballDivisionInfo.All)
        {
            if (GetTeamIds(division, true).Count == 0)
                errors.Add($"{division.DisplayName()} has no active teams.");
        }
        return errors;
    }

    internal void AcceptChanges() => _original = Snapshot();

    private void Remove(int teamId)
    {
        int removed = 0;
        foreach (List<int> group in _groups.Values)
            if (group.Remove(teamId)) removed++;
        if (removed != 1) throw new InvalidDataException($"Expected team ID {teamId} exactly once, found {removed}.");
    }

    private Dictionary<(BaseballDivision Division, bool Active), int[]> Snapshot() =>
        _groups.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray());

    private static bool SnapshotsEqual(
        IReadOnlyDictionary<(BaseballDivision Division, bool Active), int[]> left,
        IReadOnlyDictionary<(BaseballDivision Division, bool Active), int[]> right) =>
        left.Count == right.Count && left.All(pair =>
            right.TryGetValue(pair.Key, out int[]? values) && pair.Value.SequenceEqual(values));
}

internal sealed class TeamLeagueIniDocument
{
    private static readonly Regex ManagedKey = new(
        @"^\s*(?:ALWest|ALCentral|ALEast|NLWest|NLCentral|NLEast)(?:Active|Inactive)(?:Count|\d+)\s*=",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private readonly List<string> _lines;
    private readonly string _newLine;
    private readonly bool _endsWithNewLine;
    private readonly int _firstManagedLine;
    private readonly int _lastManagedLine;

    private TeamLeagueIniDocument(
        List<string> lines,
        string newLine,
        bool endsWithNewLine,
        int firstManagedLine,
        int lastManagedLine)
    {
        _lines = lines;
        _newLine = newLine;
        _endsWithNewLine = endsWithNewLine;
        _firstManagedLine = firstManagedLine;
        _lastManagedLine = lastManagedLine;
    }

    public static TeamLeagueIniDocument Parse(string text)
    {
        string newLine = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        bool endsWithNewLine = text.EndsWith('\n');
        List<string> lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').ToList();
        if (endsWithNewLine && lines.Count > 0) lines.RemoveAt(lines.Count - 1);

        int seasonStart = lines.FindIndex(line => line.Trim().Equals("[Season]", StringComparison.OrdinalIgnoreCase));
        if (seasonStart < 0) throw new InvalidDataException("menuoptions.ini does not contain a [Season] section.");
        int seasonEnd = lines.FindIndex(seasonStart + 1, line =>
            line.TrimStart().StartsWith("[", StringComparison.Ordinal));
        if (seasonEnd < 0) seasonEnd = lines.Count;

        int first = -1;
        int last = -1;
        for (int index = seasonStart + 1; index < seasonEnd; index++)
        {
            if (!ManagedKey.IsMatch(lines[index])) continue;
            if (first < 0) first = index;
            last = index;
        }
        if (first < 0) throw new InvalidDataException("The [Season] section has no division configuration.");
        return new TeamLeagueIniDocument(lines, newLine, endsWithNewLine, first, last);
    }

    public TeamLeagueSetup CreateSetup()
    {
        Dictionary<string, int> values = new(StringComparer.OrdinalIgnoreCase);
        for (int index = _firstManagedLine; index <= _lastManagedLine; index++)
        {
            string line = _lines[index];
            int equals = line.IndexOf('=');
            if (equals < 0 || !ManagedKey.IsMatch(line)) continue;
            string key = line[..equals].Trim();
            string value = line[(equals + 1)..];
            int comment = value.IndexOf(';');
            if (comment >= 0) value = value[..comment];
            if (!int.TryParse(value.Trim(), out int parsed))
                throw new InvalidDataException($"[Season] {key} is not a valid integer.");
            if (!values.TryAdd(key, parsed))
                throw new InvalidDataException($"[Season] {key} appears more than once.");
        }

        Dictionary<(BaseballDivision Division, bool Active), List<int>> groups = new();
        foreach (BaseballDivision division in BaseballDivisionInfo.All)
        {
            ReadGroup(division, true);
            ReadGroup(division, false);
        }

        TeamLeagueSetup setup = new(groups);
        IReadOnlyList<string> errors = setup.Validate();
        if (errors.Count > 0)
            throw new InvalidDataException(
                "The [Season] division configuration is invalid:" + Environment.NewLine +
                string.Join(Environment.NewLine, errors.Select(error => $"• {error}")));
        return setup;

        void ReadGroup(BaseballDivision division, bool active)
        {
            string prefix = division.IniKey() + (active ? "Active" : "Inactive");
            if (!values.TryGetValue(prefix + "Count", out int count) || count < 0 || count > 100)
                throw new InvalidDataException($"[Season] {prefix}Count is missing or invalid.");
            List<int> teamIds = new(count);
            for (int index = 0; index < count; index++)
            {
                if (!values.TryGetValue(prefix + index.ToString("00"), out int teamId))
                    throw new InvalidDataException($"[Season] {prefix}{index:00} is missing.");
                teamIds.Add(teamId);
            }
            groups[(division, active)] = teamIds;
        }
    }

    public string Render(TeamLeagueSetup setup)
    {
        List<string> replacement = new();
        foreach (BaseballDivision division in BaseballDivisionInfo.All)
        {
            IReadOnlyList<int> active = setup.GetTeamIds(division, true);
            IReadOnlyList<int> inactive = setup.GetTeamIds(division, false);
            string key = division.IniKey();
            replacement.Add($"{key}ActiveCount = {active.Count}");
            replacement.Add($"{key}InactiveCount = {inactive.Count}");
            for (int index = 0; index < active.Count; index++)
                replacement.Add(FormatTeam(key, true, index, active[index]));
            for (int index = 0; index < inactive.Count; index++)
                replacement.Add(FormatTeam(key, false, index, inactive[index]));
            replacement.Add(string.Empty);
        }
        if (replacement.Count > 0) replacement.RemoveAt(replacement.Count - 1);

        List<string> output = new(_lines.Count - (_lastManagedLine - _firstManagedLine + 1) + replacement.Count);
        output.AddRange(_lines.Take(_firstManagedLine));
        output.AddRange(replacement);
        output.AddRange(_lines.Skip(_lastManagedLine + 1));
        string text = string.Join(_newLine, output);
        return _endsWithNewLine ? text + _newLine : text;
    }

    private static string FormatTeam(string divisionKey, bool active, int index, int id)
    {
        BaseballTeamDefinition team = BaseballTeamDefinition.ForId(id);
        return $"{divisionKey}{(active ? "Active" : "Inactive")}{index:00} = {id} ;{team.IniSymbol}";
    }
}

public sealed record TeamLeagueSaveResult(string? BackupPath, int ChangedFileCount, bool RebuiltArchive);
