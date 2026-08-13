using PS2_DATA_File_Extractor;
using PS2_DATA_File_Extractor.FileOperations;
using PS2_DATA_File_Extractor.Models;
using System.Text;
using System.Windows.Forms;

namespace PS2_DATA_File_Extractor.Tests;

public sealed class GameplayTuningArchiveTests : IDisposable
{
    private const int DataOffset = 2048;
    private const int DebugOffset = 4096;
    private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), $"gameplay-tuning-tests-{Guid.NewGuid():N}");

    [Fact]
    public void IniDocumentPreservesFormattingCommentsAndUnknownValues()
    {
        const string original = "[Field]\r\nFriction = .28 ; keep this note\r\nUnknown = untouched\r\n";
        IniDocument document = IniDocument.Parse(original);

        Assert.True(document.SetValue("field", "friction", "1.75"));

        Assert.Equal(
            "[Field]\r\nFriction = 1.75 ; keep this note\r\nUnknown = untouched\r\n",
            document.ToString());
    }

    [Theory]
    [InlineData(GameplayTweakValueKind.Boolean, "true", "True")]
    [InlineData(GameplayTweakValueKind.Integer, "-25", "-25")]
    [InlineData(GameplayTweakValueKind.Decimal, ".45", ".45")]
    public void GameplayValuesNormalizeValidInput(
        GameplayTweakValueKind kind,
        string input,
        string expected)
    {
        Assert.True(GameplayTweakValue.TryNormalize(kind, input, out string normalized, out string error));
        Assert.Equal(expected, normalized);
        Assert.Empty(error);
    }

    [Theory]
    [InlineData(GameplayTweakValueKind.Boolean, "yes")]
    [InlineData(GameplayTweakValueKind.Integer, "1.5")]
    [InlineData(GameplayTweakValueKind.Decimal, "NaN")]
    public void GameplayValuesRejectInvalidInput(GameplayTweakValueKind kind, string input)
    {
        Assert.False(GameplayTweakValue.TryNormalize(kind, input, out _, out string error));
        Assert.NotEmpty(error);
    }

    [Fact]
    public void SavingMultipleTweaksPreservesOtherIniContentAndArchiveLayout()
    {
        Directory.CreateDirectory(_tempDirectory);
        string metPath = Path.Combine(_tempDirectory, "DATA.MET");
        CreateArchive(metPath);
        GameplayTuningArchive archive = GameplayTuningArchive.Load(metPath);
        GameplayTuningArchive.GameplayTweak radius = archive.Tweaks.Single(tweak => tweak.Key == "Radius");
        GameplayTuningArchive.GameplayTweak alwaysCatch = archive.Tweaks.Single(tweak => tweak.Key == "AlwaysCatch");

        GameplayTuningSaveResult result = archive.SaveWithBackup(
            new Dictionary<GameplayTuningArchive.GameplayTweak, string>
            {
                [radius] = "123456789",
                [alwaysCatch] = "True"
            });

        Assert.Equal(2, result.ChangedFileCount);
        Assert.True(result.RebuiltArchive);
        Assert.NotNull(result.BackupPath);
        Assert.True(File.Exists(result.BackupPath));

        GameplayTuningArchive saved = GameplayTuningArchive.Load(metPath);
        Assert.Equal("123456789", saved.Tweaks.Single(tweak => tweak.Key == "Radius").Value);
        Assert.Equal("True", saved.Tweaks.Single(tweak => tweak.Key == "AlwaysCatch").Value);

        METFileStructure structure = METFileReader.ReadMETFile(metPath);
        Assert.Equal(DebugOffset, structure.GetEntryByPath("data/options/debugoptions.ini")!.Offset);
        Assert.True(structure.ValidateStructure().IsValid);
        Assert.Contains("; radius note", ReadEntryText(metPath, "data/options/ball.ini"));
        Assert.Contains("Unknown = preserved", ReadEntryText(metPath, "data/options/ball.ini"));
    }

    [Fact]
    public void PresetsCoverMajorGameplayGroupsAndResolveExactArchiveValues()
    {
        Directory.CreateDirectory(_tempDirectory);
        string metPath = Path.Combine(_tempDirectory, "DATA.MET");
        CreateArchive(metPath);
        GameplayTuningArchive archive = GameplayTuningArchive.Load(metPath);

        Assert.Equal(27, GameplayPresetCatalog.Presets.Count);
        foreach (string group in new[]
                 {
                     "Ball Size", "Bounce & Rolling", "Bunts & Normal Hits", "Special Hits", "Catching",
                     "Complete Game Styles"
                 })
            Assert.Contains(GameplayPresetCatalog.Presets, preset => preset.Group == group);

        GameplayPreset tiny = GameplayPresetCatalog.Presets.Single(preset => preset.Name.StartsWith("Tiny Ball"));
        GameplayPresetChange radius = Assert.Single(tiny.Resolve(archive.Tweaks));
        Assert.Equal("Radius", radius.Tweak.Key);
        Assert.Equal("4", radius.Value);

        GameplayPreset guaranteed = GameplayPresetCatalog.Presets.Single(preset => preset.Name == "Guaranteed Catches");
        IReadOnlyList<GameplayPresetChange> catching = guaranteed.Resolve(archive.Tweaks);
        Assert.Equal("True", catching.Single(change => change.Tweak.Key == "AlwaysCatch").Value);
        Assert.Equal("False", catching.Single(change => change.Tweak.Key == "AlwaysMiss").Value);

        GameplayPreset restore = GameplayPresetCatalog.Presets.Single(preset => preset.RestoreAll);
        Assert.Equal(archive.Tweaks.Count, restore.Resolve(archive.Tweaks).Count);
    }

    [Fact]
    public void GameplayEditorOpensWithVisiblePresetControls()
    {
        Directory.CreateDirectory(_tempDirectory);
        string metPath = Path.Combine(_tempDirectory, "DATA.MET");
        CreateArchive(metPath);
        GameplayTuningArchive archive = GameplayTuningArchive.Load(metPath);
        Exception? failure = null;
        Thread thread = new(() =>
        {
            try
            {
                using GameplayTweaksForm editor = new(archive, metPath);
                editor.Show();
                Application.DoEvents();
                Assert.NotNull(FindControl<GroupBox>(editor, control => control.Text == "Quick Presets"));
                Assert.NotNull(FindControl<Button>(editor, control => control.Text == "Apply Preset"));
                Assert.True(FindControls<ComboBox>(editor).Count >= 2);
                editor.Close();
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "Gameplay preset editor test did not finish.");
        Assert.Null(failure);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    private static void CreateArchive(string path)
    {
        byte[] ball = Encoding.UTF8.GetBytes(
            "[Size]\r\nRadius = 7 ; radius note\r\nUnknown = preserved\r\n");
        byte[] debug = Encoding.UTF8.GetBytes(
            "[Catches]\r\nAlwaysCatch = False\r\nAlwaysMiss = False\r\n");
        int totalLength = DebugOffset + debug.Length;

        using FileStream stream = new FileStream(path, FileMode.Create, FileAccess.ReadWrite);
        using BinaryWriter writer = new BinaryWriter(stream);
        writer.Write(DataOffset);
        writer.Write(totalLength - DataOffset);
        WriteEntry(writer, DataOffset, ball.Length, "data/options/ball.ini");
        WriteEntry(writer, DebugOffset, debug.Length, "data/options/debugoptions.ini");
        writer.Write(new byte[12]);
        stream.Position = DataOffset;
        writer.Write(ball);
        stream.Position = DebugOffset;
        writer.Write(debug);
    }

    private static void WriteEntry(BinaryWriter writer, int offset, int size, string path)
    {
        byte[] pathBytes = Encoding.ASCII.GetBytes(path);
        writer.Write(offset);
        writer.Write(size);
        writer.Write(pathBytes.Length);
        writer.Write(pathBytes);
    }

    private static string ReadEntryText(string metPath, string entryPath)
    {
        METFileStructure structure = METFileReader.ReadMETFile(metPath);
        FileEntry entry = structure.GetEntryByPath(entryPath)!;
        using FileStream stream = File.OpenRead(metPath);
        stream.Position = entry.Offset;
        byte[] data = new byte[entry.OriginalSize];
        stream.ReadExactly(data);
        return Encoding.UTF8.GetString(data).TrimEnd('\0');
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
