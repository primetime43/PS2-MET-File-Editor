using PS2_DATA_File_Extractor.FileOperations;

namespace PS2_DATA_File_Extractor.Tests;

public sealed class GameExecutableUnlockPatcherTests : IDisposable
{
    private static readonly byte[] OriginalIsItemUnlocked =
    {
        0x1C, 0x00, 0x82, 0x8C, 0x01, 0x00, 0x03, 0x24,
        0x04, 0x18, 0xA3, 0x00, 0x24, 0x10, 0x43, 0x00,
        0x08, 0x00, 0xE0, 0x03, 0x2B, 0x10, 0x02, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00
    };

    private static readonly byte[] OriginalAquadomePrefix =
    {
        0x1C, 0x00, 0x84, 0x8C, 0x00, 0x10, 0x83, 0x30,
        0x03, 0x00, 0x60, 0x10
    };

    private readonly string _tempDirectory =
        Path.Combine(Path.GetTempPath(), $"ps2-executable-tests-{Guid.NewGuid():N}");

    [Fact]
    public void ApplyForcesSelectedBitsAndAquadomeAndPreservesBackup()
    {
        Directory.CreateDirectory(_tempDirectory);
        string path = Path.Combine(_tempDirectory, "SLUS_208.65");
        byte[] original = CreateSupportedExecutable();
        File.WriteAllBytes(path, original);
        const ushort forcedMask = (1 << 9) | (1 << 11);

        string backup = GameExecutableUnlockPatcher.ApplyWithBackup(
            path, forcedMask, unlockAquadome: true);

        Assert.Equal(original, File.ReadAllBytes(backup));
        Assert.Equal(original.Length, new FileInfo(path).Length);

        GameExecutableUnlockState state = GameExecutableUnlockPatcher.Inspect(path);
        Assert.True(state.IsPatched);
        Assert.Equal(forcedMask, state.ForcedItemMask);
        Assert.True(state.AquadomeUnlocked);

        byte[] patched = File.ReadAllBytes(path);
        int itemOffset = GameExecutableUnlockPatcher.IsItemUnlockedFileOffset;
        Assert.Equal((byte)(forcedMask & 0xFF), patched[itemOffset + 8]);
        Assert.Equal((byte)(forcedMask >> 8), patched[itemOffset + 9]);
        Assert.Equal(
            new byte[] { 0x00, 0x00, 0x02, 0x24, 0x08, 0x00, 0xE0, 0x03, 0, 0, 0, 0 },
            patched.AsSpan(GameExecutableUnlockPatcher.FieldsRemainingForAquadomeFileOffset, 12).ToArray());
    }

    [Fact]
    public void ApplyingEmptySelectionRestoresOriginalInstructions()
    {
        Directory.CreateDirectory(_tempDirectory);
        string patchedPath = Path.Combine(_tempDirectory, "patched-SLUS_208.65");
        File.WriteAllBytes(patchedPath, CreateSupportedExecutable());
        GameExecutableUnlockPatcher.ApplyWithBackup(
            patchedPath, GameExecutableUnlockPatcher.SelectableItemMask, unlockAquadome: true);

        string restorePath = Path.Combine(_tempDirectory, "restore-SLUS_208.65");
        File.Copy(patchedPath, restorePath);
        GameExecutableUnlockPatcher.ApplyWithBackup(restorePath, 0, unlockAquadome: false);

        GameExecutableUnlockState state = GameExecutableUnlockPatcher.Inspect(restorePath);
        Assert.False(state.IsPatched);
        Assert.Equal((ushort)0, state.ForcedItemMask);
        Assert.False(state.AquadomeUnlocked);

        byte[] restored = File.ReadAllBytes(restorePath);
        Assert.Equal(
            OriginalIsItemUnlocked,
            restored.AsSpan(GameExecutableUnlockPatcher.IsItemUnlockedFileOffset, 32).ToArray());
        Assert.Equal(
            OriginalAquadomePrefix,
            restored.AsSpan(GameExecutableUnlockPatcher.FieldsRemainingForAquadomeFileOffset, 12).ToArray());
    }

    [Fact]
    public void ApplySupportsAndPreservesReadOnlyExtractedExecutable()
    {
        Directory.CreateDirectory(_tempDirectory);
        string path = Path.Combine(_tempDirectory, "readonly-SLUS_208.65");
        File.WriteAllBytes(path, CreateSupportedExecutable());
        File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.ReadOnly);

        GameExecutableUnlockPatcher.ApplyWithBackup(path, 1 << 9, unlockAquadome: false);

        Assert.True((File.GetAttributes(path) & FileAttributes.ReadOnly) != 0);
        Assert.Equal((ushort)(1 << 9), GameExecutableUnlockPatcher.Inspect(path).ForcedItemMask);
    }

    [Fact]
    public void InspectRejectsUnknownExecutableRevision()
    {
        byte[] executable = CreateSupportedExecutable();
        executable[GameExecutableUnlockPatcher.IsItemUnlockedFileOffset] ^= 0xFF;

        InvalidDataException error = Assert.Throws<InvalidDataException>(
            () => GameExecutableUnlockPatcher.Inspect(executable));

        Assert.Contains("instruction signature", error.Message);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            foreach (string file in Directory.EnumerateFiles(_tempDirectory))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    private static byte[] CreateSupportedExecutable()
    {
        byte[] executable =
            new byte[GameExecutableUnlockPatcher.FieldsRemainingForAquadomeFileOffset + 0x100];
        OriginalIsItemUnlocked.CopyTo(
            executable, GameExecutableUnlockPatcher.IsItemUnlockedFileOffset);
        OriginalAquadomePrefix.CopyTo(
            executable, GameExecutableUnlockPatcher.FieldsRemainingForAquadomeFileOffset);
        return executable;
    }
}
