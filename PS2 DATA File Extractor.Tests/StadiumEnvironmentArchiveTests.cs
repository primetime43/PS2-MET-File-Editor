using PS2_DATA_File_Extractor.FileOperations;
using PS2_DATA_File_Extractor.Models;
using PS2_DATA_File_Extractor;
using System.Text;
using System.Windows.Forms;

namespace PS2_DATA_File_Extractor.Tests;

public sealed class StadiumEnvironmentArchiveTests : IDisposable
{
    private const int FirstOffset = 2048;
    private const int SecondOffset = 4096;
    private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), $"stadium-environment-tests-{Guid.NewGuid():N}");

    private const string Sample =
        "// Test field\r\n" +
        "field {\r\n" +
        "\tambLight 1.0 1.0 1.0 1.0;\r\n" +
        "\tnumAmbs 1;\r\n" +
        "\tcamPos 10.0 20.0 30.0;\r\n" +
        "}\r\n\r\n" +
        "collision {\r\n" +
        "\thomerun HR;\r\n" +
        "}\r\n\r\n" +
        "// Flying object\r\n" +
        "amb {\r\n" +
        "\tpath Fields/Test;;\r\n" +
        "\tmodel plane.dff;\r\n" +
        "\tanim plane.anm; 1.0 2.0;\r\n" +
        "\tpos 1.0 2.0 3.0;\r\n" +
        "\tballSplash;\r\n" +
        "}\r\n";

    [Fact]
    public void ParserRoundTripsCommentsSpacingAndInternalSemicolons()
    {
        FieldDataDocument document = FieldDataDocument.Parse(Sample);

        Assert.Equal(Sample, document.ToString());
        Assert.Equal(3, document.FieldSettings.Count);
        Assert.Single(document.CollisionSettings);
        FieldDataAmbient ambient = Assert.Single(document.Ambients);
        Assert.Equal("Flying object", ambient.DisplayName);
        Assert.Equal("Fields/Test;", ambient.Settings.Single(setting => setting.Key == "path").Value);
        Assert.Equal("plane.anm; 1.0 2.0", ambient.Settings.Single(setting => setting.Key == "anim").Value);
        Assert.Equal(FieldDataValueKind.Flag, ambient.Settings.Single(setting => setting.Key == "ballSplash").Kind);
    }

    [Fact]
    public void ParserRecognizesAmbientBlocksWithInlineComments()
    {
        string text = Sample.Replace("amb {\r\n", "amb { // Red fireworks\r\n");

        FieldDataDocument document = FieldDataDocument.Parse(text);

        FieldDataAmbient ambient = Assert.Single(document.Ambients);
        Assert.Equal("Red fireworks", ambient.DisplayName);
        Assert.Equal(text, document.ToString());
    }

    [Fact]
    public void EditingOneDirectivePreservesEveryOtherLine()
    {
        FieldDataDocument document = FieldDataDocument.Parse(Sample);
        document.Ambients[0].Settings.Single(setting => setting.Key == "pos").Value = "9.0 8.0 7.0";

        string changed = document.ToString();

        Assert.Equal(Sample.Replace("\tpos 1.0 2.0 3.0;", "\tpos 9.0 8.0 7.0;"), changed);
        Assert.Contains("\tanim plane.anm; 1.0 2.0;", changed);
        Assert.Contains("\tpath Fields/Test;;", changed);
    }

    [Fact]
    public void AmbientCountCanExposeBlocksBeyondRetailDeclaredCount()
    {
        string text = Sample + "// Disabled extra\r\namb {\r\n\tmodel extra.dff;\r\n}\r\n";
        FieldDataDocument document = FieldDataDocument.Parse(text);

        Assert.Equal(1, document.DeclaredAmbientCount);
        Assert.Equal(2, document.Ambients.Count);
        Assert.True(document.TrySetDeclaredAmbientCount(document.Ambients.Count));
        Assert.Equal(2, document.DeclaredAmbientCount);
        Assert.Contains("\tnumAmbs 2;", document.ToString());
    }

    [Theory]
    [InlineData(FieldDataValueKind.Integer, "12", true)]
    [InlineData(FieldDataValueKind.Integer, "1.2", false)]
    [InlineData(FieldDataValueKind.Number, "-2.75", true)]
    [InlineData(FieldDataValueKind.NumericList, "1.0 2.0 3.0; 4 5 6", true)]
    [InlineData(FieldDataValueKind.NumericList, "1.0 bad 3.0", false)]
    [InlineData(FieldDataValueKind.Flag, "", true)]
    [InlineData(FieldDataValueKind.Flag, "1", false)]
    public void FieldDataValuesAreValidated(FieldDataValueKind kind, string input, bool expected)
    {
        Assert.Equal(expected, FieldDataValue.TryNormalize(kind, input, out _, out _));
    }

    [Fact]
    public void ArchiveSavesMultipleStadiumsWithOneBackup()
    {
        Directory.CreateDirectory(_tempDirectory);
        string metPath = Path.Combine(_tempDirectory, "DATA.MET");
        CreateArchive(metPath);
        StadiumEnvironmentArchive archive = StadiumEnvironmentArchive.Load(metPath);
        Assert.Equal(2, archive.Stadiums.Count);
        StadiumEnvironment day = archive.Stadiums.Single(stadium => stadium.FolderName == "drivein");
        StadiumEnvironment night = archive.Stadiums.Single(stadium => stadium.FolderName == "driveinnight");
        day.Document.FieldSettings.Single(setting => setting.Key == "camPos").Value = "1000.0 2000.0 3000.0";
        night.Document.Ambients[0].Settings.Single(setting => setting.Key == "model").Value = "a_much_longer_model_name.dff";

        StadiumEnvironmentSaveResult result = archive.SaveWithBackup();

        Assert.Equal(2, result.ChangedStadiumCount);
        Assert.True(result.RebuiltArchive);
        Assert.NotNull(result.BackupPath);
        Assert.True(File.Exists(result.BackupPath));
        StadiumEnvironmentArchive saved = StadiumEnvironmentArchive.Load(metPath);
        Assert.Equal("1000.0 2000.0 3000.0", saved.Stadiums.Single(stadium => stadium.FolderName == "drivein")
            .Document.FieldSettings.Single(setting => setting.Key == "camPos").Value);
        Assert.Equal("a_much_longer_model_name.dff", saved.Stadiums.Single(stadium => stadium.FolderName == "driveinnight")
            .Document.Ambients[0].Settings.Single(setting => setting.Key == "model").Value);
        METFileStructure structure = METFileReader.ReadMETFile(metPath);
        Assert.Equal(SecondOffset, structure.GetEntryByPath("data/fields/driveinnight/fielddata.txt")!.Offset);
        Assert.True(structure.ValidateStructure().IsValid);
    }

    [Fact]
    public void StadiumEditorIncludesResizableLiveScenePreview()
    {
        Directory.CreateDirectory(_tempDirectory);
        string metPath = Path.Combine(_tempDirectory, "DATA.MET");
        CreateArchive(metPath);
        StadiumEnvironmentArchive archive = StadiumEnvironmentArchive.Load(metPath);
        Exception? failure = null;
        Thread thread = new(() =>
        {
            try
            {
                using StadiumEnvironmentEditorForm editor = new(archive, metPath);
                editor.Show();
                Application.DoEvents();
                Assert.True(editor.MinimumSize.Width >= 1100);
                Assert.Contains("Live Preview", editor.Text);
                Assert.True(ContainsControl<RenderWareScenePreviewControl>(editor));
                editor.Close();
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "Stadium editor test did not finish.");
        Assert.Null(failure);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory)) Directory.Delete(_tempDirectory, recursive: true);
    }

    private static void CreateArchive(string path)
    {
        byte[] day = Encoding.ASCII.GetBytes(Sample);
        byte[] night = Encoding.ASCII.GetBytes(Sample.Replace("Test field", "Test night"));
        int totalLength = SecondOffset + night.Length;
        using FileStream stream = new(path, FileMode.Create, FileAccess.ReadWrite);
        using BinaryWriter writer = new(stream);
        writer.Write(FirstOffset);
        writer.Write(totalLength - FirstOffset);
        WriteEntry(writer, FirstOffset, day.Length, "data/fields/drivein/fielddata.txt");
        WriteEntry(writer, SecondOffset, night.Length, "data/fields/driveinnight/fielddata.txt");
        writer.Write(new byte[12]);
        stream.Position = FirstOffset;
        writer.Write(day);
        stream.Position = SecondOffset;
        writer.Write(night);
    }

    private static void WriteEntry(BinaryWriter writer, int offset, int size, string path)
    {
        byte[] pathBytes = Encoding.ASCII.GetBytes(path);
        writer.Write(offset);
        writer.Write(size);
        writer.Write(pathBytes.Length);
        writer.Write(pathBytes);
    }

    private static bool ContainsControl<T>(Control parent) where T : Control
    {
        foreach (Control child in parent.Controls)
        {
            if (child is T || ContainsControl<T>(child)) return true;
        }
        return false;
    }
}
