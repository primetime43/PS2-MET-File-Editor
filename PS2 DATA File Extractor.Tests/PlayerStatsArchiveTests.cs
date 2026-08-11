using PS2_DATA_File_Extractor.FileOperations;
using PS2_DATA_File_Extractor.Models;
using System.Text;

namespace PS2_DATA_File_Extractor.Tests;

public sealed class PlayerStatsArchiveTests : IDisposable
{
    private const int FirstOffset = 2048;
    private const int CloneOffset = 4096;
    private const int PortraitOffset = 6144;
    private const int BreatheOffset = 8192;
    private const int BreatheBlinkOffset = 10240;
    private const int PickMeOffset = 12288;
    private static readonly byte[] FakePortrait = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");
    private static readonly byte[] FakePss = CreateFakePss();
    private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), $"player-stats-tests-{Guid.NewGuid():N}");

    [Fact]
    public void NormalRecordRoundTripsAllFieldsAndNames()
    {
        short[] values = Enumerable.Range(0, PlayerStatsRecord.BaseFieldCount).Select(value => (short)value).ToArray();
        byte[] data = CreateRecord(values, Array.Empty<short>(), "Pablo", "Secret Weapon", "Sanchez");

        PlayerStatsRecord player = PlayerStatsRecord.Parse("data/kids/stats/pablo_stats.dat", data);

        Assert.False(player.IsClone);
        Assert.Equal(values, player.BaseValues);
        Assert.Empty(player.CloneAppearance);
        Assert.Equal("Pablo", player.FirstName);
        Assert.Equal("Secret Weapon", player.Nickname);
        Assert.Equal("Sanchez", player.LastName);
        Assert.Equal(data, player.Serialize());
        Assert.False(player.IsChanged);
    }

    [Fact]
    public void CloneRecordHasEightAdditionalAppearanceFields()
    {
        short[] values = Enumerable.Repeat((short)50, PlayerStatsRecord.BaseFieldCount).ToArray();
        short[] appearance = { 1, 2, 0, 4, 5, 6, 3, 2 };
        byte[] data = CreateRecord(values, appearance, "Zena", "", "Fromme");

        PlayerStatsRecord player = PlayerStatsRecord.Parse("data/kids/stats/Clone7_stats.dat", data);

        Assert.True(player.IsClone);
        Assert.Equal(appearance, player.CloneAppearance);
        Assert.Equal(data, player.Serialize());
    }

    [Fact]
    public void DerivedRatingsAndMaxPresetMatchExecutableFormulas()
    {
        short[] values = new short[PlayerStatsRecord.BaseFieldCount];
        values[1] = 82;
        values[3] = 60;
        values[4] = 90;
        values[8] = 30;
        values[2] = 30;
        values[6] = 60;
        values[9] = 90;
        values[5] = 80;
        values[7] = 72;
        values[14] = values[15] = values[16] = values[17] = 120;
        PlayerStatsRecord player = PlayerStatsRecord.Parse(
            "data/kids/stats/test_stats.dat", CreateRecord(values, Array.Empty<short>(), "Test", "", "Player"));

        Assert.Equal(82, player.PowerRating);
        Assert.Equal(60, player.ContactRating);
        Assert.Equal(60, player.FieldingRating);
        Assert.Equal(72, player.RunningRating);
        Assert.Equal(90, player.PitchingRating);

        player.MaximizeSkills();

        Assert.Equal(100, player.PowerRating);
        Assert.Equal(100, player.ContactRating);
        Assert.Equal(100, player.FieldingRating);
        Assert.Equal(100, player.RunningRating);
        Assert.Equal(100, player.PitchingRating);
        Assert.Equal(0, player.BaseValues[12]);
    }

    [Fact]
    public void ParserAcceptsZeroPaddingButRejectsUnexpectedTrailingData()
    {
        short[] values = new short[PlayerStatsRecord.BaseFieldCount];
        byte[] record = CreateRecord(values, Array.Empty<short>(), "A", "", "B");
        byte[] padded = record.Concat(new byte[12]).ToArray();
        Assert.Equal(record, PlayerStatsRecord.Parse("data/kids/stats/a_stats.dat", padded).Serialize());

        padded[^1] = 1;
        Assert.Throws<InvalidDataException>(() =>
            PlayerStatsRecord.Parse("data/kids/stats/a_stats.dat", padded));
    }

    [Fact]
    public void PortraitArchiveMapsStatsNamesAndSkipsClones()
    {
        Directory.CreateDirectory(_tempDirectory);
        string metPath = Path.Combine(_tempDirectory, "DATA.MET");
        CreateArchive(metPath);
        PlayerStatsArchive players = PlayerStatsArchive.Load(metPath);
        PlayerPortraitArchive portraits = PlayerPortraitArchive.Load(metPath);

        PlayerPortraitInfo info = Assert.Single(portraits.Portraits);
        Assert.Equal("abner", info.Code);
        PlayerPortrait portrait = Assert.IsType<PlayerPortrait>(portraits.GetPortrait(info.Code));

        Assert.Equal("data/polaroids/abner.png", portrait.SourcePath);
        Assert.Equal(FakePortrait, portrait.Data);

        PlayerStatsRecord abner = players.Players.Single(player => !player.IsClone);
        Assert.Collection(
            portraits.GetPlayerImages(abner),
            item => Assert.Equal(PlayerImageKind.Polaroid, item.Kind),
            item => Assert.Equal(PlayerImageKind.Breathe, item.Kind),
            item => Assert.Equal(PlayerImageKind.BreatheBlink, item.Kind),
            item => Assert.Equal(PlayerImageKind.PickMe, item.Kind));
        Assert.Empty(portraits.GetPlayerImages(players.Players.Single(player => player.IsClone)));
        Assert.Null(portraits.GetPortrait(players.Players.Single(player => player.IsClone)));
    }

    [Fact]
    public void ReplacingPortraitCreatesBackupAndWritesNewPng()
    {
        Directory.CreateDirectory(_tempDirectory);
        string metPath = Path.Combine(_tempDirectory, "DATA.MET");
        CreateArchive(metPath);
        PlayerStatsRecord player = PlayerStatsArchive.Load(metPath).Players.Single(item => !item.IsClone);
        PlayerPortraitArchive portraits = PlayerPortraitArchive.Load(metPath);
        byte[] replacement = FakePortrait.Concat(new byte[] { 0 }).ToArray();

        PlayerPortraitSaveResult result = portraits.ReplaceWithBackup("abner", replacement);

        Assert.NotNull(result.BackupPath);
        Assert.True(File.Exists(result.BackupPath));
        Assert.Equal("data/polaroids/abner.png", result.SourcePath);
        Assert.Equal(replacement, PlayerPortraitArchive.Load(metPath).GetPortrait(player)!.Data);
        Assert.True(METFileReader.ReadMETFile(metPath).ValidateStructure().IsValid);
    }

    [Fact]
    public void ReplacingSelectionAnimationValidatesAndWritesPss()
    {
        Directory.CreateDirectory(_tempDirectory);
        string metPath = Path.Combine(_tempDirectory, "DATA.MET");
        CreateArchive(metPath);
        PlayerStatsRecord player = PlayerStatsArchive.Load(metPath).Players.Single(item => !item.IsClone);
        PlayerPortraitArchive portraits = PlayerPortraitArchive.Load(metPath);
        PlayerImageInfo breathe = portraits.GetPlayerImages(player)
            .Single(item => item.Kind == PlayerImageKind.Breathe);

        Assert.Throws<InvalidDataException>(() =>
            portraits.ReplacePlayerImageWithBackup(breathe, new byte[128]));

        byte[] replacement = FakePss.Concat(new byte[] { 0x55 }).ToArray();
        PlayerPortraitSaveResult result = portraits.ReplacePlayerImageWithBackup(breathe, replacement);

        Assert.NotNull(result.BackupPath);
        Assert.True(File.Exists(result.BackupPath));
        PlayerPortraitArchive reloaded = PlayerPortraitArchive.Load(metPath);
        PlayerImageInfo savedInfo = reloaded.GetPlayerImages(player)
            .Single(item => item.Kind == PlayerImageKind.Breathe);
        Assert.Equal(replacement, reloaded.GetPlayerImage(savedInfo)!.Data);
        Assert.True(METFileReader.ReadMETFile(metPath).ValidateStructure().IsValid);
    }

    [Fact]
    public void SavingMultiplePlayersCreatesOneBackupAndPreservesArchiveLayout()
    {
        Directory.CreateDirectory(_tempDirectory);
        string metPath = Path.Combine(_tempDirectory, "DATA.MET");
        CreateArchive(metPath);
        PlayerStatsArchive archive = PlayerStatsArchive.Load(metPath);
        PlayerStatsRecord abner = archive.Players.Single(player => player.FirstName == "Abner");
        PlayerStatsRecord clone = archive.Players.Single(player => player.IsClone);
        abner.Nickname = "The Unhittable One";
        abner.BaseValues[1] = 135;
        clone.CloneAppearance[2] = 2;

        PlayerStatsSaveResult result = archive.SaveWithBackup();

        Assert.Equal(2, result.ChangedPlayerCount);
        Assert.True(result.RebuiltArchive);
        Assert.NotNull(result.BackupPath);
        Assert.True(File.Exists(result.BackupPath));
        PlayerStatsArchive saved = PlayerStatsArchive.Load(metPath);
        Assert.Equal("The Unhittable One", saved.Players.Single(player => player.FirstName == "Abner").Nickname);
        Assert.Equal(135, saved.Players.Single(player => player.FirstName == "Abner").BaseValues[1]);
        Assert.Equal(2, saved.Players.Single(player => player.IsClone).CloneAppearance[2]);
        METFileStructure structure = METFileReader.ReadMETFile(metPath);
        Assert.Equal(CloneOffset, structure.GetEntryByPath("data/kids/stats/Clone1_stats.dat")!.Offset);
        Assert.True(structure.ValidateStructure().IsValid);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory)) Directory.Delete(_tempDirectory, recursive: true);
    }

    private static byte[] CreateRecord(short[] values, short[] appearance, string first, string nickname, string last)
    {
        using MemoryStream stream = new();
        using BinaryWriter writer = new(stream, Encoding.ASCII, leaveOpen: true);
        foreach (short value in values) writer.Write(value);
        foreach (short value in appearance) writer.Write(value);
        writer.Write(Encoding.ASCII.GetBytes($"{first},{nickname},{last},"));
        writer.Flush();
        return stream.ToArray();
    }

    private static void CreateArchive(string path)
    {
        short[] normalValues = Enumerable.Repeat((short)50, PlayerStatsRecord.BaseFieldCount).ToArray();
        short[] cloneValues = Enumerable.Repeat((short)40, PlayerStatsRecord.BaseFieldCount).ToArray();
        byte[] normal = CreateRecord(normalValues, Array.Empty<short>(), "Abner", "Ace", "Dubbleplay");
        byte[] clone = CreateRecord(cloneValues, new short[8], "Zena", "", "Fromme");
        int totalLength = PickMeOffset + FakePss.Length;
        using FileStream stream = new(path, FileMode.Create, FileAccess.ReadWrite);
        using BinaryWriter writer = new(stream);
        writer.Write(FirstOffset);
        writer.Write(totalLength - FirstOffset);
        WriteEntry(writer, FirstOffset, normal.Length, "data/kids/stats/Abner_stats.dat");
        WriteEntry(writer, CloneOffset, clone.Length, "data/kids/stats/Clone1_stats.dat");
        WriteEntry(writer, PortraitOffset, FakePortrait.Length, "data/polaroids/abner.png");
        WriteEntry(writer, BreatheOffset, FakePss.Length, "data/video/pickplayer/abner_breathe.pss");
        WriteEntry(writer, BreatheBlinkOffset, FakePss.Length, "data/video/pickplayer/abner_breatheblink.pss");
        WriteEntry(writer, PickMeOffset, FakePss.Length, "data/video/pickplayer/abner_pickme.pss");
        writer.Write(new byte[12]);
        stream.Position = FirstOffset;
        writer.Write(normal);
        stream.Position = CloneOffset;
        writer.Write(clone);
        stream.Position = PortraitOffset;
        writer.Write(FakePortrait);
        stream.Position = BreatheOffset;
        writer.Write(FakePss);
        stream.Position = BreatheBlinkOffset;
        writer.Write(FakePss);
        stream.Position = PickMeOffset;
        writer.Write(FakePss);
    }

    private static byte[] CreateFakePss()
    {
        byte[] data = new byte[2048];
        data[0] = 0x00;
        data[1] = 0x00;
        data[2] = 0x01;
        data[3] = 0xba;
        data[8] = 0x00;
        data[9] = 0x00;
        data[10] = 0x01;
        data[11] = 0xe0;
        data[16] = 0x00;
        data[17] = 0x00;
        data[18] = 0x01;
        data[19] = 0xb3;
        data[20] = 0x10;
        data[21] = 0x01;
        data[22] = 0x00;
        return data;
    }

    private static void WriteEntry(BinaryWriter writer, int offset, int size, string path)
    {
        byte[] pathBytes = Encoding.ASCII.GetBytes(path);
        writer.Write(offset);
        writer.Write(size);
        writer.Write(pathBytes.Length);
        writer.Write(pathBytes);
    }
}
