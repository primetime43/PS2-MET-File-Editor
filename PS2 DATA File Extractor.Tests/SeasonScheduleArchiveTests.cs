using PS2_DATA_File_Extractor;
using PS2_DATA_File_Extractor.FileOperations;
using PS2_DATA_File_Extractor.Models;
using System.Buffers.Binary;
using System.Text;
using System.Windows.Forms;

namespace PS2_DATA_File_Extractor.Tests;

public sealed class SeasonScheduleArchiveTests : IDisposable
{
    private const int FirstOffset = 2048;
    private const int SecondOffset = 6144;
    private readonly string _temp = Path.Combine(Path.GetTempPath(), $"schedule-tests-{Guid.NewGuid():N}");

    [Fact]
    public void ParsesBothSeasonLengthsAndPreservesUnusedPadding()
    {
        byte[] eighteen = CreateTemplate(18, 2);
        SeasonScheduleTemplate shortTemplate = SeasonScheduleTemplate.Parse(
            "data/schedules/templateschedule18_02.dat", 18, 2, eighteen);
        SeasonScheduleTemplate longTemplate = SeasonScheduleTemplate.Parse(
            "data/schedules/templateschedule32_04.dat", 32, 4, CreateTemplate(32, 4));

        Assert.Equal(18, shortTemplate.RoundCount);
        Assert.Equal(32, longTemplate.RoundCount);
        Assert.Equal(2, shortTemplate.GetGame(0, 0).TeamA);
        Assert.Empty(shortTemplate.Validate());
        Assert.Equal(eighteen, shortTemplate.Serialize());

        byte[] serialized = shortTemplate.Serialize();
        int firstPadding = 18 * SeasonScheduleArchive.TeamCount * sizeof(int);
        Assert.Equal(0xccccccccu,
            BinaryPrimitives.ReadUInt32LittleEndian(serialized.AsSpan(firstPadding, sizeof(int))));
    }

    [Fact]
    public void AssigningUsedTeamAutomaticallySwapsSlotsAndKeepsRoundValid()
    {
        SeasonScheduleTemplate template = SeasonScheduleTemplate.Parse(
            "data/schedules/templateschedule18_00.dat", 18, 0, CreateTemplate(18));

        template.AssignTeam(0, 0, 7);

        Assert.Equal(7, template.GetTeam(0, 0));
        Assert.Equal(0, template.GetTeam(0, 7));
        Assert.Empty(template.Validate());
        Assert.True(template.IsChanged);
        template.ResetRound(0);
        Assert.False(template.IsChanged);
    }

    [Fact]
    public void RejectsDuplicateTeamWithinRound()
    {
        byte[] data = CreateTemplate(32);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(sizeof(int), sizeof(int)), 0);

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            SeasonScheduleTemplate.Parse("data/schedules/templateschedule32_00.dat", 32, 0, data));

        Assert.Contains("repeats team slot", exception.Message);
        Assert.Contains("missing team slot", exception.Message);
    }

    [Fact]
    public void SaveChangesOnlyModifiedTemplateAndCreatesBackup()
    {
        Directory.CreateDirectory(_temp);
        string metPath = Path.Combine(_temp, "DATA.MET");
        CreateArchive(metPath);
        SeasonScheduleArchive archive = SeasonScheduleArchive.Load(metPath);
        SeasonScheduleTemplate changed = archive.Templates.Single(template => template.RoundCount == 18);
        changed.SwapGameSides(0, 0);

        SeasonScheduleSaveResult result = archive.SaveWithBackup();

        Assert.Equal(1, result.ChangedTemplateCount);
        Assert.False(result.RebuiltArchive);
        Assert.NotNull(result.BackupPath);
        Assert.True(File.Exists(result.BackupPath));
        Assert.False(archive.HasChanges);
        SeasonScheduleArchive saved = SeasonScheduleArchive.Load(metPath);
        Assert.Equal(1, saved.Templates.Single(template => template.RoundCount == 18).GetGame(0, 0).TeamA);
        Assert.Equal(0, saved.Templates.Single(template => template.RoundCount == 18).GetGame(0, 0).TeamB);
        Assert.Equal(CreateTemplate(32, 1), ReadEntry(metPath, "data/schedules/templateschedule32_01.dat"));
        Assert.True(METFileReader.ReadMETFile(metPath).ValidateStructure().IsValid);
    }

    [Fact]
    public void EditorOpensWithUsableDefaultSplitterLayout()
    {
        Directory.CreateDirectory(_temp);
        string metPath = Path.Combine(_temp, "DATA.MET");
        CreateArchive(metPath);
        SeasonScheduleArchive archive = SeasonScheduleArchive.Load(metPath);
        Exception? failure = null;
        Thread thread = new(() =>
        {
            try
            {
                using SeasonScheduleEditorForm editor = new(archive, metPath);
                editor.Show();
                Application.DoEvents();
                Assert.Contains("Schedule", editor.Text);
                Assert.True(FindControls<DataGridView>(editor).Count >= 2);
                Assert.NotNull(FindControl<Button>(editor, button => button.Text == "Save Schedules to DATA.MET"));
                editor.Close();
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "Schedule editor test did not finish.");
        Assert.Null(failure);
    }

    public void Dispose()
    {
        if (Directory.Exists(_temp)) Directory.Delete(_temp, recursive: true);
    }

    private static byte[] CreateTemplate(int rounds, int rotation = 0)
    {
        byte[] data = new byte[SeasonScheduleArchive.TemplateByteLength];
        for (int index = 0; index < data.Length / sizeof(int); index++)
            BinaryPrimitives.WriteInt32LittleEndian(
                data.AsSpan(index * sizeof(int), sizeof(int)), SeasonScheduleArchive.PaddingValue);

        for (int round = 0; round < rounds; round++)
        {
            for (int position = 0; position < SeasonScheduleArchive.TeamCount; position++)
            {
                int value = (position + round + rotation) % SeasonScheduleArchive.TeamCount;
                int index = round * SeasonScheduleArchive.TeamCount + position;
                BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(index * sizeof(int), sizeof(int)), value);
            }
        }

        return data;
    }

    private static void CreateArchive(string path)
    {
        byte[] first = CreateTemplate(18);
        byte[] second = CreateTemplate(32, 1);
        int totalLength = SecondOffset + second.Length;
        using FileStream stream = new(path, FileMode.Create, FileAccess.ReadWrite);
        using BinaryWriter writer = new(stream, Encoding.ASCII, leaveOpen: true);
        writer.Write(FirstOffset);
        writer.Write(totalLength - FirstOffset);
        WriteEntry(writer, FirstOffset, first.Length, "data/schedules/templateschedule18_00.dat");
        WriteEntry(writer, SecondOffset, second.Length, "data/schedules/templateschedule32_01.dat");
        writer.Write(new byte[12]);
        stream.Position = FirstOffset;
        writer.Write(first);
        stream.Position = SecondOffset;
        writer.Write(second);
    }

    private static void WriteEntry(BinaryWriter writer, int offset, int size, string path)
    {
        byte[] pathBytes = Encoding.ASCII.GetBytes(path);
        writer.Write(offset);
        writer.Write(size);
        writer.Write(pathBytes.Length);
        writer.Write(pathBytes);
    }

    private static byte[] ReadEntry(string metPath, string path)
    {
        FileEntry entry = METFileReader.ReadMETFile(metPath).GetEntryByPath(path)!;
        using FileStream stream = File.OpenRead(metPath);
        stream.Position = entry.Offset;
        byte[] data = new byte[entry.OriginalSize];
        stream.ReadExactly(data);
        return data;
    }

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
