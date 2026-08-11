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
    public void MapsTalkieVisemesToRetailMouthTextureSlots()
    {
        Assert.Equal(1, RenderWareSkinnedModel.ResolveFacialTexturePose("CLASS_TALKIES", "STATIC"));
        Assert.Equal(1, RenderWareSkinnedModel.ResolveFacialTexturePose("CLASS_TALKIES", "ROOT"));
        Assert.Equal(2, RenderWareSkinnedModel.ResolveFacialTexturePose("CLASS_TALKIES", "AI"));
        Assert.Equal(3, RenderWareSkinnedModel.ResolveFacialTexturePose("CLASS_TALKIES", "EE"));
        Assert.Equal(4, RenderWareSkinnedModel.ResolveFacialTexturePose("CLASS_TALKIES", "OH"));
        Assert.Equal(5, RenderWareSkinnedModel.ResolveFacialTexturePose("CLASS_TALKIES", "OO"));
        Assert.Equal(6, RenderWareSkinnedModel.ResolveFacialTexturePose("CLASS_TALKIES", "CDG"));
        Assert.Equal(7, RenderWareSkinnedModel.ResolveFacialTexturePose("CLASS_TALKIES", "MM"));
        Assert.Equal(8, RenderWareSkinnedModel.ResolveFacialTexturePose("CLASS_TALKIES", "FV"));
        Assert.Equal(19, RenderWareSkinnedModel.ResolveFacialTexturePose("CLASS_MOUTH", "19"));
        Assert.Null(RenderWareSkinnedModel.ResolveFacialTexturePose("CLASS_TALKIES", "UNKNOWN"));
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

    [Fact]
    public void LoadsNumberedBattingTexturesForTheSelectedCharacter()
    {
        Directory.CreateDirectory(_tempDirectory);
        string metPath = Path.Combine(_tempDirectory, "DATA.MET");
        byte[] eyesOne = { 1, 2, 3 };
        byte[] eyesThree = { 3, 2, 1 };
        byte[] mouthTwo = { 9, 8, 7 };
        CreateArchive(metPath, new[]
        {
            ("data/batting/test/test_swing.evt", Encoding.UTF8.GetBytes(CreateAnimationXml())),
            ("data/batting/test/slugger_eyes_tx.001.png", eyesOne),
            ("data/batting/test/slugger_eyes_tx.003.png", eyesThree),
            ("data/batting/test/slugger_mouth_tx.002.png", mouthTwo),
            ("data/batting/other/other_eyes_tx.001.png", new byte[] { 4, 5, 6 })
        });

        FacialEventArchive archive = FacialEventArchive.Load(metPath);
        FacialEventTextureSet textures = Assert.IsType<FacialEventTextureSet>(
            archive.LoadTextureSet(Assert.Single(archive.Files)));

        Assert.Equal("test", textures.CharacterCode);
        Assert.Equal("slugger", textures.CharacterName);
        Assert.Equal(new[] { 1, 3 }, textures.Eyes.Keys.OrderBy(value => value));
        Assert.Equal(new[] { 2 }, textures.Mouths.Keys);
        Assert.Equal(eyesThree, textures.Eyes[3].Data);
        Assert.Equal("data/batting/test/slugger_mouth_tx.002.png", textures.Mouths[2].SourcePath);
        Assert.False(textures.TryGetMouth(1, out _));
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
        const string entryPath = "data/audio/talkies/test/test_00001.evt";
        CreateArchive(path, new[] { (entryPath, evt) });
    }

    private static void CreateArchive(string path, IReadOnlyList<(string Path, byte[] Data)> entries)
    {
        const int dataOffset = 2048;
        int totalLength = dataOffset + (entries.Count - 1) * 2048 + entries[^1].Data.Length;
        using FileStream stream = new(path, FileMode.Create, FileAccess.ReadWrite);
        using BinaryWriter writer = new(stream);
        writer.Write(dataOffset);
        writer.Write(totalLength - dataOffset);
        for (int index = 0; index < entries.Count; index++)
        {
            byte[] pathBytes = Encoding.ASCII.GetBytes(entries[index].Path);
            writer.Write(dataOffset + index * 2048);
            writer.Write(entries[index].Data.Length);
            writer.Write(pathBytes.Length);
            writer.Write(pathBytes);
        }
        writer.Write(new byte[dataOffset - checked((int)stream.Position)]);
        for (int index = 0; index < entries.Count; index++)
        {
            stream.Position = dataOffset + index * 2048;
            writer.Write(entries[index].Data);
        }
    }
}
