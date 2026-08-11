using System.Text;
using PS2_DATA_File_Extractor.FileOperations;

namespace PS2_DATA_File_Extractor.Tests;

public sealed class FacialEventArchiveTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(
        Path.GetTempPath(), $"facial-event-tests-{Guid.NewGuid():N}");

    [Fact]
    public void ParsesTalkieEventsAndResolvesActiveViseme()
    {
        FacialEventFile file = FacialEventFile.Parse(
            "data/audio/talkies/test/test_00001.evt",
            Encoding.UTF8.GetBytes(CreateTalkieXml()));

        Assert.True(file.IsTalkie);
        Assert.Equal("Talkie lip sync", file.Kind);
        Assert.Equal(new[] { "INVALID", "STATIC", "AI", "MM" }, file.GetEventTypes("CLASS_TALKIES"));
        Assert.Equal(3, file.Events.Count);
        Assert.Equal(0.2, file.DurationSeconds, 3);
        Assert.Equal("STATIC", file.GetActiveEvent("CLASS_TALKIES", 0.05)!.EventType);
        Assert.Equal("MM", file.GetActiveEvent("CLASS_TALKIES", 0.15)!.EventType);
        Assert.False(file.IsChanged);
    }

    [Fact]
    public void ReplacesAndSerializesEditedTimeline()
    {
        FacialEventFile file = FacialEventFile.Parse(
            "data/audio/talkies/test/test_00001.evt",
            Encoding.UTF8.GetBytes(CreateTalkieXml()));
        FacialEvent[] replacement =
        {
            new(0, "CLASS_TALKIES", "STATIC", 1, 0),
            new(0.12, "CLASS_TALKIES", "AI", 0.75, 0),
            new(0.25, "CLASS_TALKIES", "STATIC", 1, 0)
        };

        file.ReplaceEvents(replacement);
        FacialEventFile reparsed = FacialEventFile.Parse(file.SourcePath, file.Serialize());

        Assert.True(file.IsChanged);
        Assert.Equal(3, reparsed.Events.Count);
        Assert.Equal("AI", reparsed.Events[1].EventType);
        Assert.Equal(0.12, reparsed.Events[1].Timestamp, 3);
        Assert.Equal(0.75, reparsed.Events[1].Value, 3);
        Assert.Contains("<event_stream>", Encoding.UTF8.GetString(file.Serialize()));
    }

    [Fact]
    public void RejectsOutOfOrderOrUnknownEvents()
    {
        FacialEventFile file = FacialEventFile.Parse(
            "data/audio/talkies/test/test_00001.evt",
            Encoding.UTF8.GetBytes(CreateTalkieXml()));

        Assert.Throws<InvalidDataException>(() => file.ReplaceEvents(new[]
        {
            new FacialEvent(0.2, "CLASS_TALKIES", "STATIC", 1, 0),
            new FacialEvent(0.1, "CLASS_TALKIES", "MM", 1, 0)
        }));
        Assert.Throws<InvalidDataException>(() => file.ReplaceEvents(new[]
        {
            new FacialEvent(0, "CLASS_TALKIES", "NOT_A_SHAPE", 1, 0)
        }));
    }

    [Fact]
    public void ArchiveSaveCreatesBackupAndPersistsTimeline()
    {
        Directory.CreateDirectory(_tempDirectory);
        string metPath = Path.Combine(_tempDirectory, "DATA.MET");
        CreateArchive(metPath, Encoding.UTF8.GetBytes(CreateTalkieXml()));
        FacialEventArchive archive = FacialEventArchive.Load(metPath);
        FacialEventFile file = Assert.Single(archive.Files);
        file.ReplaceEvents(new[]
        {
            new FacialEvent(0, "CLASS_TALKIES", "STATIC", 1, 0),
            new FacialEvent(0.3, "CLASS_TALKIES", "AI", 1, 0)
        });

        FacialEventSaveResult result = archive.SaveWithBackup();
        FacialEventArchive saved = FacialEventArchive.Load(metPath);

        Assert.Equal(1, result.ChangedFileCount);
        Assert.NotNull(result.BackupPath);
        Assert.True(File.Exists(result.BackupPath));
        Assert.Equal(2, Assert.Single(saved.Files).Events.Count);
        Assert.Equal(0.3, saved.Files[0].DurationSeconds, 3);
    }

    [Fact]
    public void PreservesBattingPseudoXmlAndIndependentClassTimelines()
    {
        FacialEventFile file = FacialEventFile.Parse(
            "data/batting/test/test_swing.evt",
            Encoding.UTF8.GetBytes(CreateAnimationXml()));

        Assert.False(file.IsTalkie);
        Assert.Equal(4, file.Events.Count);
        file.ReplaceEvents(new[]
        {
            new FacialEvent(0, "CLASS_MOUTH", "1", 1, 0),
            new FacialEvent(0.1666667, "CLASS_MOUTH", "2", 1, 0),
            new FacialEvent(0, "CLASS_EYES", "1", 1, 0),
            new FacialEvent(0.0666667, "CLASS_EYES", "2", 1, 0)
        });

        string serialized = Encoding.UTF8.GetString(file.Serialize());
        FacialEventFile reparsed = FacialEventFile.Parse(file.SourcePath, file.Serialize());

        Assert.Contains("value value=\"1.0\"/>", serialized);
        Assert.DoesNotContain("<value value=", serialized);
        Assert.Contains("0.1666667", serialized);
        Assert.Equal(4, reparsed.Events.Count);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
            Directory.Delete(_tempDirectory, recursive: true);
    }

    private static string CreateTalkieXml() =>
        "<?xml version=\"1.0\" encoding=\"utf-8\" ?>\r\n" +
        "<event_stream>\r\n" +
        "\t<classes>\r\n" +
        "\t\t<classdef name=\"CLASS_TALKIES\" value=\"31415\">\r\n" +
        "\t\t\t<eventdef name=\"INVALID\" value=\"1\"/>\r\n" +
        "\t\t\t<eventdef name=\"STATIC\" value=\"1\"/>\r\n" +
        "\t\t\t<eventdef name=\"AI\" value=\"2\"/>\r\n" +
        "\t\t\t<eventdef name=\"MM\" value=\"64\"/>\r\n" +
        "\t\t</classdef>\r\n" +
        "\t</classes>\r\n" +
        EventXml("0.00", "STATIC") + EventXml("0.10", "MM") + EventXml("0.20", "AI") +
        "</event_stream>\r\n";

    private static string CreateAnimationXml() =>
        "<?xml version=\"1.0\" encoding=\"utf-8\" ?>\r\n" +
        "<event_stream>\r\n" +
        "\t<classes>\r\n" +
        AnimationClass("CLASS_EYES") +
        AnimationClass("CLASS_MOUTH") +
        "\t</classes>\r\n" +
        AnimationEvent("0", "CLASS_MOUTH", "1") +
        AnimationEvent("0.1", "CLASS_MOUTH", "2") +
        AnimationEvent("0", "CLASS_EYES", "1") +
        AnimationEvent("0.1", "CLASS_EYES", "2") +
        "</event_stream>\r\n";

    private static string AnimationClass(string name) =>
        $"\t\t<classdef name=\"{name}\" value=\"32000\">\r\n" +
        "\t\t\t<eventdef name=\"1\" value=\"1\"/>\r\n" +
        "\t\t\t<eventdef name=\"2\" value=\"2\"/>\r\n" +
        "\t\t</classdef>\r\n";

    private static string AnimationEvent(string timestamp, string eventClass, string type) =>
        "\t<event>\r\n" +
        $"\t\t<timestamp value=\"{timestamp}\"/>\r\n" +
        $"\t\t<eventClass value=\"{eventClass}\"/>\r\n" +
        $"\t\t<eventType value=\"{type}\"/>\r\n" +
        "\t\tvalue value=\"1.0\"/>\r\n" +
        "\t\telementID value=\"0\"/>\r\n" +
        "\t</event>\r\n";

    private static string EventXml(string timestamp, string type) =>
        "\t<event>\r\n" +
        $"\t\t<timestamp value=\"{timestamp}\"/>\r\n" +
        "\t\t<eventClass value=\"CLASS_TALKIES\"/>\r\n" +
        $"\t\t<eventType value=\"{type}\"/>\r\n" +
        "\t\t<value value=\"1.0\"/>\r\n" +
        "\t\t<elementID value=\"0\"/>\r\n" +
        "\t</event>\r\n";

    private static void CreateArchive(string path, byte[] evt)
    {
        const int dataOffset = 2048;
        const string entryPath = "data/audio/talkies/test/test_00001.evt";
        using FileStream stream = new(path, FileMode.Create, FileAccess.ReadWrite);
        using BinaryWriter writer = new(stream);
        writer.Write(dataOffset);
        writer.Write(evt.Length);
        byte[] pathBytes = Encoding.ASCII.GetBytes(entryPath);
        writer.Write(dataOffset);
        writer.Write(evt.Length);
        writer.Write(pathBytes.Length);
        writer.Write(pathBytes);
        writer.Write(new byte[dataOffset - checked((int)stream.Position)]);
        writer.Write(evt);
    }
}
