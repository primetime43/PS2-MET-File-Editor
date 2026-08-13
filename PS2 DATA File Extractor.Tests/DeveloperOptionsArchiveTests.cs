using PS2_DATA_File_Extractor.FileOperations;
using PS2_DATA_File_Extractor.Models;
using System.Text;
using System.Windows.Forms;

namespace PS2_DATA_File_Extractor.Tests;

public sealed class DeveloperOptionsArchiveTests : IDisposable
{
    private const int DataOffset = 2048;
    private readonly string _tempDirectory =
        Path.Combine(Path.GetTempPath(), $"developer-options-tests-{Guid.NewGuid():N}");

    [Fact]
    public void LoadDiscoversRetailOptionsAndMarksIgnoredAssertionKey()
    {
        Directory.CreateDirectory(_tempDirectory);
        string metPath = Path.Combine(_tempDirectory, "DATA.MET");
        CreateArchive(metPath);

        DeveloperOptionsArchive archive = DeveloperOptionsArchive.Load(metPath);

        Assert.Equal(9, archive.Options.Count);
        Assert.Equal(8, archive.Options.Count(option => option.RetailSupported));
        DeveloperOption assertions = archive.Options.Single(option => option.Key == "AssertsEnabled");
        Assert.False(assertions.RetailSupported);
        Assert.Equal("Ignored retail keys", assertions.Category);
    }

    [Fact]
    public void RecoveredEnumsExposeNamedChoicesWithExactStoredValues()
    {
        Directory.CreateDirectory(_tempDirectory);
        string metPath = Path.Combine(_tempDirectory, "DATA.MET");
        CreateArchive(metPath);
        DeveloperOptionsArchive archive = DeveloperOptionsArchive.Load(metPath);

        DeveloperOption batType = archive.Options.Single(option => option.Key == "BatType");
        Assert.Equal("0 — Bunt", batType.Choices.Single(choice => choice.Value == "0").Label);
        Assert.Equal("3 — Power", batType.Choices.Single(choice => choice.Value == "3").Label);
        Assert.Equal("10 — Lightning", batType.Choices.Single(choice => choice.Value == "10").Label);
        Assert.Equal("18 — Do not swing", batType.Choices.Single(choice => choice.Value == "18").Label);
        Assert.DoesNotContain(batType.Choices, choice => choice.Value == "12");

        DeveloperOption stance = archive.Options.Single(option => option.Key == "Stance");
        Assert.Equal("-1 — Unselected", stance.Choices.Single(choice => choice.Value == "-1").Label);
        Assert.Equal("2 — Right", stance.Choices.Single(choice => choice.Value == "2").Label);

        DeveloperOption gamepad = archive.Options.Single(option => option.Key == "GamepadType1");
        Assert.Collection(gamepad.Choices,
            choice => Assert.Equal("0 — Gamepad control", choice.Label),
            choice => Assert.Equal("1 — Digital gamepad control", choice.Label));
    }

    [Fact]
    public void SavePreservesCommentsUnknownKeysAndCreatesBackup()
    {
        Directory.CreateDirectory(_tempDirectory);
        string metPath = Path.Combine(_tempDirectory, "DATA.MET");
        CreateArchive(metPath);
        DeveloperOptionsArchive archive = DeveloperOptionsArchive.Load(metPath);
        DeveloperOption alwaysCatch = archive.Options.Single(option => option.Key == "AlwaysCatch");
        DeveloperOption lockAngle = archive.Options.Single(option => option.Key == "LockAngle");

        DeveloperOptionsSaveResult result = archive.SaveWithBackup(
            new Dictionary<DeveloperOption, string>
            {
                [alwaysCatch] = "true",
                [lockAngle] = "-45"
            });

        Assert.NotNull(result.BackupPath);
        Assert.True(File.Exists(result.BackupPath));
        Assert.Equal(2, result.ChangedOptionCount);
        string saved = ReadEntryText(metPath);
        Assert.Contains("AlwaysCatch = True ; catch note", saved);
        Assert.Contains("LockAngle = -45", saved);
        Assert.Contains("UnknownSwitch = untouched", saved);
        Assert.Contains("AssertsEnabled = False", saved);
        Assert.True(METFileReader.ReadMETFile(metPath).ValidateStructure().IsValid);
    }

    [Fact]
    public void SaveRejectsIgnoredKeysAndInvalidRanges()
    {
        Directory.CreateDirectory(_tempDirectory);
        string metPath = Path.Combine(_tempDirectory, "DATA.MET");
        CreateArchive(metPath);
        DeveloperOptionsArchive archive = DeveloperOptionsArchive.Load(metPath);

        DeveloperOption assertions = archive.Options.Single(option => option.Key == "AssertsEnabled");
        InvalidDataException ignored = Assert.Throws<InvalidDataException>(() =>
            archive.SaveWithBackup(new Dictionary<DeveloperOption, string> { [assertions] = "True" }));
        Assert.Contains("ignored", ignored.Message, StringComparison.OrdinalIgnoreCase);

        DeveloperOption angle = archive.Options.Single(option => option.Key == "LockAngle");
        InvalidDataException range = Assert.Throws<InvalidDataException>(() =>
            archive.SaveWithBackup(new Dictionary<DeveloperOption, string> { [angle] = "361" }));
        Assert.Contains("-360 and 360", range.Message);

        DeveloperOption batType = archive.Options.Single(option => option.Key == "BatType");
        InvalidDataException enumValue = Assert.Throws<InvalidDataException>(() =>
            archive.SaveWithBackup(new Dictionary<DeveloperOption, string> { [batType] = "12" }));
        Assert.Contains("named retail choices", enumValue.Message);
    }

    [Fact]
    public void DeveloperToolsWindowShowsRuntimeAndExecutableControls()
    {
        Directory.CreateDirectory(_tempDirectory);
        string metPath = Path.Combine(_tempDirectory, "DATA.MET");
        string executablePath = Path.Combine(_tempDirectory, "SLUS_208.65");
        CreateArchive(metPath);
        File.WriteAllBytes(executablePath, GameExecutableDeveloperPatcherTests.CreateSupportedExecutable());
        Exception? failure = null;
        Thread thread = new(() =>
        {
            try
            {
                using DeveloperToolsForm form = new(DeveloperOptionsArchive.Load(metPath), metPath, executablePath);
                form.Show();
                Application.DoEvents();
                Assert.NotNull(FindControl<TabPage>(form, control => control.Text == "Runtime Options"));
                Assert.NotNull(FindControl<TabPage>(form, control => control.Text == "Executable Modes"));
                Assert.NotNull(FindControl<Button>(form, control => control.Text == "Save Runtime Options to DATA.MET"));
                Assert.NotNull(FindControl<Button>(form, control => control.Text == "Apply to SLUS_208.65"));
                Assert.NotNull(FindControl<Button>(form, control => control.Text == "Restore Retail Developer Modes"));
                DataGridView grid = Assert.Single(FindControls<DataGridView>(form));
                DataGridViewRow batType = grid.Rows.Cast<DataGridViewRow>()
                    .Single(row => row.Tag is DeveloperOption option && option.Key == "BatType");
                DataGridViewComboBoxCell batTypeCell = Assert.IsType<DataGridViewComboBoxCell>(batType.Cells[2]);
                Assert.Contains(batTypeCell.Items.Cast<object>(), item => item.ToString() == "0 — Bunt");
                Assert.Contains(batTypeCell.Items.Cast<object>(), item => item.ToString() == "3 — Power");
                form.Close();
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "Developer Tools UI test did not finish.");
        Assert.Null(failure);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            foreach (string file in Directory.EnumerateFiles(_tempDirectory))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    private static void CreateArchive(string path)
    {
        byte[] data = Encoding.UTF8.GetBytes(
            "[Catches]\r\nAlwaysCatch = False ; catch note\r\nAlwaysMiss = False\r\n" +
            "[Batting]\r\nLockAngle = 0\r\nBatType = 0\r\nStance = 1\r\nNeverMiss = False\r\nUnknownSwitch = untouched\r\n" +
            "[Misc]\r\nGamepadType1 = 1\r\nGamepadType2 = 1\r\nAssertsEnabled = False\r\n");
        int totalLength = DataOffset + data.Length;
        using FileStream stream = new(path, FileMode.Create, FileAccess.ReadWrite);
        using BinaryWriter writer = new(stream);
        writer.Write(DataOffset);
        writer.Write(totalLength - DataOffset);
        WriteEntry(writer, DataOffset, data.Length, DeveloperOptionsArchive.SourcePath);
        writer.Write(new byte[12]);
        stream.Position = DataOffset;
        writer.Write(data);
    }

    private static void WriteEntry(BinaryWriter writer, int offset, int size, string path)
    {
        byte[] pathBytes = Encoding.ASCII.GetBytes(path);
        writer.Write(offset);
        writer.Write(size);
        writer.Write(pathBytes.Length);
        writer.Write(pathBytes);
    }

    private static string ReadEntryText(string metPath)
    {
        FileEntry entry = METFileReader.ReadMETFile(metPath).GetEntryByPath(DeveloperOptionsArchive.SourcePath)!;
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
