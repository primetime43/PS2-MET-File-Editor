using PS2_DATA_File_Extractor;
using PS2_DATA_File_Extractor.FileOperations;
using PS2_DATA_File_Extractor.Models;
using System.Text;
using System.Windows.Forms;

namespace PS2_DATA_File_Extractor.Tests;

public sealed class TeamLeagueArchiveTests : IDisposable
{
    private const int DataOffset = 2048;
    private readonly string _temp = Path.Combine(Path.GetTempPath(), $"team-league-tests-{Guid.NewGuid():N}");

    [Fact]
    public void LoadsRetailDivisionOrderAndKnownTeamNames()
    {
        string path = CreateArchive(RetailMenuOptions());

        TeamLeagueArchive archive = TeamLeagueArchive.Load(path);

        Assert.Equal(30, archive.Setup.TeamCount);
        Assert.Equal(30, archive.Setup.ActiveTeamCount);
        Assert.Equal(0, archive.Setup.InactiveTeamCount);
        Assert.Equal(new[] { 10, 0, 12, 9 }, archive.Setup.GetTeamIds(BaseballDivision.ALWest, true));
        Assert.Equal("Seattle Mariners", archive.Setup.GetPlacement(10).Team.Name);
        Assert.False(archive.HasChanges);
    }

    [Fact]
    public void MovesTeamsBetweenDivisionPoolsAndPreservesOrdering()
    {
        TeamLeagueArchive archive = TeamLeagueArchive.Load(CreateArchive(RetailMenuOptions()));

        archive.Setup.MoveTeam(0, BaseballDivision.ALCentral, false);
        archive.Setup.MoveTeam(6, BaseballDivision.ALCentral, false);
        Assert.True(archive.Setup.MoveWithinGroup(6, -1));

        Assert.DoesNotContain(0, archive.Setup.GetTeamIds(BaseballDivision.ALWest, true));
        Assert.Equal(new[] { 6, 0 }, archive.Setup.GetTeamIds(BaseballDivision.ALCentral, false));
        Assert.Equal(28, archive.Setup.ActiveTeamCount);
        Assert.True(archive.HasChanges);
        Assert.Empty(archive.Setup.Validate());

        archive.Setup.RestoreOriginal();
        Assert.False(archive.HasChanges);
    }

    [Fact]
    public void SaveCreatesBackupReloadsChangesAndPreservesOtherSections()
    {
        string path = CreateArchive(RetailMenuOptions());
        TeamLeagueArchive archive = TeamLeagueArchive.Load(path);
        archive.Setup.MoveTeam(0, BaseballDivision.ALWest, false);

        TeamLeagueSaveResult result = archive.SaveWithBackup();

        Assert.Equal(1, result.ChangedFileCount);
        Assert.NotNull(result.BackupPath);
        Assert.True(File.Exists(result.BackupPath));
        Assert.False(archive.HasChanges);
        TeamLeagueArchive saved = TeamLeagueArchive.Load(path);
        Assert.Equal(new[] { 0 }, saved.Setup.GetTeamIds(BaseballDivision.ALWest, false));
        string savedText = ReadMenuOptions(path);
        Assert.Contains("ALWestActiveCount = 3", savedText);
        Assert.Contains("ALWestInactive00 = 0 ;kAngels", savedText);
        Assert.Contains("[Rules]\r\nInnings = 9 ;must be preserved", savedText);
        Assert.True(METFileReader.ReadMETFile(path).ValidateStructure().IsValid);
    }

    [Fact]
    public void UnknownCustomIdsAlreadyInIniArePreservedAndLabeled()
    {
        string text = RetailMenuOptions().Replace(
            "ALWestActiveCount = 4\r\nALWestInactiveCount = 0",
            "ALWestActiveCount = 4\r\nALWestInactiveCount = 1")
            .Replace(
                "ALWestActive03 = 9 ;kAthletics",
                "ALWestActive03 = 9 ;kAthletics\r\nALWestInactive00 = 40 ;kCustom");
        string path = CreateArchive(text);
        TeamLeagueArchive archive = TeamLeagueArchive.Load(path);

        TeamLeaguePlacement custom = archive.Setup.GetPlacement(40);

        Assert.False(custom.IsActive);
        Assert.Equal("Unknown / custom team 40", custom.Team.Name);
        archive.Setup.MoveTeam(40, BaseballDivision.NLEast, false);
        TeamLeagueSaveResult result = archive.SaveWithBackup();
        Assert.Equal(1, result.ChangedFileCount);
        Assert.Equal(40, TeamLeagueArchive.Load(path).Setup.GetTeamIds(BaseballDivision.NLEast, false).Single());
    }

    [Fact]
    public void EditorOpensWithUsableGridAndDefaultSplitterLayout()
    {
        string path = CreateArchive(RetailMenuOptions());
        TeamLeagueArchive archive = TeamLeagueArchive.Load(path);
        Exception? failure = null;
        Thread thread = new(() =>
        {
            try
            {
                using TeamLeagueSetupEditorForm editor = new(archive, path);
                editor.Show();
                Application.DoEvents();
                Assert.Contains("Team and League", editor.Text);
                Assert.True(FindControls<DataGridView>(editor).Count >= 2);
                Assert.NotNull(FindControl<Button>(editor, button => button.Text == "Save League Setup to DATA.MET"));
                SplitContainer split = FindControls<SplitContainer>(editor).Single();
                Assert.InRange(split.SplitterDistance, split.Panel1MinSize,
                    split.ClientSize.Height - split.SplitterWidth - split.Panel2MinSize);
                editor.Close();
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "Team editor test did not finish.");
        Assert.Null(failure);
    }

    public void Dispose()
    {
        if (Directory.Exists(_temp)) Directory.Delete(_temp, recursive: true);
    }

    private string CreateArchive(string text)
    {
        Directory.CreateDirectory(_temp);
        string path = Path.Combine(_temp, "DATA.MET");
        byte[] data = Encoding.UTF8.GetBytes(text);
        int totalLength = DataOffset + data.Length;
        using FileStream stream = new(path, FileMode.Create, FileAccess.ReadWrite);
        using BinaryWriter writer = new(stream, Encoding.ASCII, leaveOpen: true);
        writer.Write(DataOffset);
        writer.Write(totalLength - DataOffset);
        WriteEntry(writer, DataOffset, data.Length, TeamLeagueArchive.SourcePath);
        writer.Write(new byte[12]);
        stream.Position = DataOffset;
        writer.Write(data);
        return path;
    }

    private static void WriteEntry(BinaryWriter writer, int offset, int size, string path)
    {
        byte[] pathBytes = Encoding.ASCII.GetBytes(path);
        writer.Write(offset);
        writer.Write(size);
        writer.Write(pathBytes.Length);
        writer.Write(pathBytes);
    }

    private static string ReadMenuOptions(string metPath)
    {
        FileEntry entry = METFileReader.ReadMETFile(metPath).GetEntryByPath(TeamLeagueArchive.SourcePath)!;
        using FileStream stream = File.OpenRead(metPath);
        stream.Position = entry.Offset;
        byte[] data = new byte[entry.OriginalSize];
        stream.ReadExactly(data);
        return Encoding.UTF8.GetString(data).TrimEnd('\0');
    }

    private static string RetailMenuOptions() =>
        "[Display]\r\nWidescreen = False\r\n\r\n" +
        "[Season]\r\n\r\n; ETeamID\r\n;Team = 10 ;Mariners\r\n\r\n" +
        "ALWestActiveCount = 4\r\nALWestInactiveCount = 0\r\n" +
        "ALWestActive00 = 10 ;kMariners\r\nALWestActive01 = 0 ;kAngels\r\n" +
        "ALWestActive02 = 12 ;kRangers\r\nALWestActive03 = 9 ;kAthletics\r\n\r\n" +
        "ALCentralActiveCount = 5\r\nALCentralInactiveCount = 0\r\n" +
        "ALCentralActive00 = 4 ;kIndians\r\nALCentralActive01 = 7 ;kTwins\r\n" +
        "ALCentralActive02 = 3 ;kWhiteSox\r\nALCentralActive03 = 5 ;kTigers\r\nALCentralActive04 = 6 ;kRoyals\r\n\r\n" +
        "ALEastActiveCount = 5\r\nALEastInactiveCount = 0\r\n" +
        "ALEastActive00 = 8 ;kYankees\r\nALEastActive01 = 2 ;kRedSox\r\nALEastActive02 = 13 ;kBlueJays\r\n" +
        "ALEastActive03 = 1 ;kOrioles\r\nALEastActive04 = 11 ;kDRays\r\n\r\n" +
        "NLWestActiveCount = 5\r\nNLWestInactiveCount = 0\r\n" +
        "NLWestActive00 = 14 ;kDiamondbacks\r\nNLWestActive01 = 28 ;kGiants\r\nNLWestActive02 = 21 ;kDodgers\r\n" +
        "NLWestActive03 = 27 ;kPadres\r\nNLWestActive04 = 18 ;kRockies\r\n\r\n" +
        "NLCentralActiveCount = 6\r\nNLCentralInactiveCount = 0\r\n" +
        "NLCentralActive00 = 20 ;kAstros\r\nNLCentralActive01 = 29 ;kCardinals\r\nNLCentralActive02 = 16 ;kCubs\r\n" +
        "NLCentralActive03 = 22 ;kBrewers\r\nNLCentralActive04 = 17 ;kReds\r\nNLCentralActive05 = 26 ;kPirates\r\n\r\n" +
        "NLEastActiveCount = 5\r\nNLEastInactiveCount = 0\r\n" +
        "NLEastActive00 = 15 ;kBraves\r\nNLEastActive01 = 25 ;kPhillies\r\nNLEastActive02 = 24 ;kMets\r\n" +
        "NLEastActive03 = 19 ;kMarlins\r\nNLEastActive04 = 23 ;kExpos\r\n\r\n" +
        "[Rules]\r\nInnings = 9 ;must be preserved\r\n";

    private static List<T> FindControls<T>(Control root) where T : Control
    {
        List<T> result = new();
        if (root is T match) result.Add(match);
        foreach (Control child in root.Controls) result.AddRange(FindControls<T>(child));
        return result;
    }

    private static T? FindControl<T>(Control root, Func<T, bool> predicate) where T : Control =>
        FindControls<T>(root).FirstOrDefault(predicate);
}
