using PS2_DATA_File_Extractor.FileOperations;

namespace PS2_DATA_File_Extractor.Tests;

public sealed class GameExecutableHomeRunDiagnosticPatcherTests : IDisposable
{
    private static readonly byte[] OriginalPrefix =
    {
        0xB0, 0xFF, 0xBD, 0x27,
        0x40, 0x00, 0xBF, 0xFF,
        0x30, 0x00, 0xB3, 0x7F
    };

    private readonly string _tempDirectory =
        Path.Combine(Path.GetTempPath(), $"ps2-homerun-diagnostic-tests-{Guid.NewGuid():N}");

    [Fact]
    public void ApplyAndRestorePreserveBackupsAndExecutableSize()
    {
        Directory.CreateDirectory(_tempDirectory);
        string path = Path.Combine(_tempDirectory, "SLUS_208.65");
        byte[] original = CreateSupportedExecutable();
        File.WriteAllBytes(path, original);

        string patchedBackup = GameExecutableHomeRunDiagnosticPatcher.ApplyWithBackup(
            path, alwaysHomeRunSurface: true);

        Assert.Equal(original, File.ReadAllBytes(patchedBackup));
        Assert.Equal(original.Length, new FileInfo(path).Length);
        Assert.Equal(
            GameExecutableHomeRunDiagnosticState.AlwaysHomeRunSurface,
            GameExecutableHomeRunDiagnosticPatcher.Inspect(path));
        Assert.Equal(
            new byte[] { 0x01, 0x00, 0x02, 0x24, 0x08, 0x00, 0xE0, 0x03, 0, 0, 0, 0 },
            File.ReadAllBytes(path)
                .AsSpan(GameExecutableHomeRunDiagnosticPatcher.CheckHomeRunFileOffset, 12)
                .ToArray());

        string restoredBackup = GameExecutableHomeRunDiagnosticPatcher.ApplyWithBackup(
            path, alwaysHomeRunSurface: false);

        Assert.NotEqual(patchedBackup, restoredBackup);
        Assert.Equal(
            GameExecutableHomeRunDiagnosticState.Original,
            GameExecutableHomeRunDiagnosticPatcher.Inspect(path));
        Assert.Equal(
            OriginalPrefix,
            File.ReadAllBytes(path)
                .AsSpan(GameExecutableHomeRunDiagnosticPatcher.CheckHomeRunFileOffset, 12)
                .ToArray());
    }

    [Fact]
    public void InspectRejectsUnknownInstructionSignature()
    {
        byte[] executable = CreateSupportedExecutable();
        executable[GameExecutableHomeRunDiagnosticPatcher.CheckHomeRunFileOffset] ^= 0xFF;

        InvalidDataException error = Assert.Throws<InvalidDataException>(
            () => GameExecutableHomeRunDiagnosticPatcher.Inspect(executable));

        Assert.Contains("instruction signature", error.Message);
    }

    public void Dispose()
    {
        if (!Directory.Exists(_tempDirectory))
        {
            return;
        }

        foreach (string file in Directory.EnumerateFiles(_tempDirectory))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(_tempDirectory, recursive: true);
    }

    private static byte[] CreateSupportedExecutable()
    {
        byte[] executable =
            new byte[GameExecutableHomeRunDiagnosticPatcher.CheckHomeRunFileOffset + 0x100];
        OriginalPrefix.CopyTo(
            executable, GameExecutableHomeRunDiagnosticPatcher.CheckHomeRunFileOffset);
        return executable;
    }
}
