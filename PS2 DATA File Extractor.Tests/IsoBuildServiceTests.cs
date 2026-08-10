using PS2_DATA_File_Extractor.FileOperations;

namespace PS2_DATA_File_Extractor.Tests;

public sealed class IsoBuildServiceTests : IDisposable
{
    private readonly string _tempDirectory =
        Path.Combine(Path.GetTempPath(), $"ps2-iso-build-tests-{Guid.NewGuid():N}");

    [Fact]
    public void ValidateGameFolderResolvesBootExecutableFromSystemCnf()
    {
        string source = CreateGameFolder();

        GameFolderValidation result = IsoBuildService.ValidateGameFolder(source);

        Assert.Equal(Path.Combine(source, "SYSTEM.CNF"), result.SystemCnfPath);
        Assert.Equal(Path.Combine(source, "DATA.MET"), result.DataMetPath);
        Assert.Equal(Path.Combine(source, "SLUS_208.65"), result.BootExecutablePath);
    }

    [Fact]
    public void ArgumentsUseRequiredPs2FileSystemsAndUdfRevision()
    {
        string source = CreateGameFolder();
        string builder = CreateDummyImgBurn();
        string output = Path.Combine(_tempDirectory, "output", "modded.iso");

        IReadOnlyList<string> arguments = IsoBuildService.CreateImgBurnArguments(
            new IsoBuildRequest(source, output, "Backyard Baseball", builder));

        AssertArgument(arguments, "/MODE", "BUILD");
        AssertArgument(arguments, "/BUILDOUTPUTMODE", "IMAGEFILE");
        AssertArgument(arguments, "/FILESYSTEM", "ISO9660 + UDF");
        AssertArgument(arguments, "/UDFREVISION", "1.02");
        AssertArgument(arguments, "/VOLUMELABEL", "BACKYARD_BASEBALL");
        AssertArgument(arguments, "/ROOTFOLDER", "YES");
        Assert.Contains("/NOIMAGEDETAILS", arguments);
        Assert.Contains("/START", arguments);
        Assert.Contains("/CLOSESUCCESS", arguments);
    }

    [Fact]
    public void OutputInsideSourceFolderIsRejected()
    {
        string source = CreateGameFolder();
        string builder = CreateDummyImgBurn();
        string output = Path.Combine(source, "recursive.iso");

        InvalidDataException error = Assert.Throws<InvalidDataException>(() =>
            IsoBuildService.CreateImgBurnArguments(
                new IsoBuildRequest(source, output, "TEST", builder)));

        Assert.Contains("included in itself", error.Message);
    }

    [Fact]
    public void ValidateIsoRequiresIso9660AndUdfDescriptors()
    {
        Directory.CreateDirectory(_tempDirectory);
        string path = Path.Combine(_tempDirectory, "valid.iso");
        byte[] image = new byte[257 * IsoBuildService.SectorSize];
        "CD001"u8.CopyTo(image.AsSpan((16 * IsoBuildService.SectorSize) + 1));
        image[256 * IsoBuildService.SectorSize] = 0x02;
        File.WriteAllBytes(path, image);

        IsoBuildService.ValidateIsoImage(path);

        image[256 * IsoBuildService.SectorSize] = 0x00;
        File.WriteAllBytes(path, image);
        Assert.Throws<InvalidDataException>(() => IsoBuildService.ValidateIsoImage(path));
    }

    [Theory]
    [InlineData("Backyard Baseball", "BACKYARD_BASEBALL")]
    [InlineData("   ", "GAME_FOLDER")]
    [InlineData("symbols!*", "SYMBOLS")]
    public void VolumeLabelIsNormalizedForIso9660(string input, string expected)
    {
        string source = Path.Combine(_tempDirectory, "game folder");
        Assert.Equal(expected, IsoBuildService.NormalizeVolumeLabel(input, source));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    private string CreateGameFolder()
    {
        string source = Path.Combine(_tempDirectory, "game folder");
        Directory.CreateDirectory(source);
        File.WriteAllText(
            Path.Combine(source, "SYSTEM.CNF"),
            "BOOT2 = cdrom0:\\SLUS_208.65;1\r\nVER = 1.00\r\nVMODE = NTSC");
        File.WriteAllBytes(Path.Combine(source, "DATA.MET"), new byte[] { 1 });
        File.WriteAllBytes(Path.Combine(source, "SLUS_208.65"), new byte[] { 0x7F, 0x45, 0x4C, 0x46 });
        return source;
    }

    private string CreateDummyImgBurn()
    {
        Directory.CreateDirectory(_tempDirectory);
        string path = Path.Combine(_tempDirectory, "ImgBurn.exe");
        File.WriteAllBytes(path, new byte[] { 1 });
        return path;
    }

    private static void AssertArgument(
        IReadOnlyList<string> arguments,
        string name,
        string expectedValue)
    {
        int index = arguments.ToList().IndexOf(name);
        Assert.True(index >= 0, $"Argument {name} was not present.");
        Assert.True(index + 1 < arguments.Count, $"Argument {name} had no value.");
        Assert.Equal(expectedValue, arguments[index + 1]);
    }
}
