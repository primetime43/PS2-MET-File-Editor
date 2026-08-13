using PS2_DATA_File_Extractor.FileOperations;

namespace PS2_DATA_File_Extractor.Tests;

public sealed class GameExecutableDeveloperPatcherTests : IDisposable
{
    private static readonly byte[] OriginalOneInning = Getter(0x31, false);
    private static readonly byte[] OriginalHitOrigin = AddressGetter(0x24);
    private static readonly byte[] OriginalHitTrajectory = AddressGetter(0x18);
    private static readonly byte[] OriginalCheatHit = Getter(0x14, false);
    private static readonly byte[] OriginalUserCheat = Getter(0x13, true);
    private static readonly byte[] OriginalCpuSeason = Getter(0x12, false);
    private static readonly byte[] OriginalDataStub =
        { 0x08, 0x00, 0xE0, 0x03, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
    private readonly string _tempDirectory =
        Path.Combine(Path.GetTempPath(), $"developer-executable-tests-{Guid.NewGuid():N}");

    [Fact]
    public void InspectRecognizesUnmodifiedRetailInstructions()
    {
        GameExecutableDeveloperState state = GameExecutableDeveloperPatcher.Inspect(CreateSupportedExecutable());

        Assert.False(state.IsPatched);
        Assert.False(state.OneInningGames);
        Assert.False(state.CpuSeasonPlay);
        Assert.Equal(DeveloperUserCheatMode.Normal, state.UserCheatMode);
        Assert.Null(state.HitOverride);
    }

    [Fact]
    public void ApplyRoundTripsEveryModeAndPreservesBackup()
    {
        Directory.CreateDirectory(_tempDirectory);
        string path = Path.Combine(_tempDirectory, "SLUS_208.65");
        byte[] original = CreateSupportedExecutable();
        File.WriteAllBytes(path, original);
        DeveloperHitOverride hit = new(1, 2, 3, 400, 500, -600);
        GameExecutableDeveloperState desired = new(true, true, DeveloperUserCheatMode.ForceWins, hit);

        string backup = GameExecutableDeveloperPatcher.ApplyWithBackup(path, desired);

        Assert.Equal(original, File.ReadAllBytes(backup));
        Assert.Equal(desired, GameExecutableDeveloperPatcher.Inspect(path));
        Assert.Equal(original.Length, new FileInfo(path).Length);
    }

    [Fact]
    public void RestoreReturnsEveryControlledRegionToOriginalBytes()
    {
        Directory.CreateDirectory(_tempDirectory);
        string path = Path.Combine(_tempDirectory, "SLUS_208.65");
        byte[] original = CreateSupportedExecutable();
        original[^1] = 0x7B;
        File.WriteAllBytes(path, original);
        GameExecutableDeveloperPatcher.ApplyWithBackup(path,
            new GameExecutableDeveloperState(true, true, DeveloperUserCheatMode.ForceLosses,
                new DeveloperHitOverride(10, 70, -5, -250, 700, -900)));

        GameExecutableDeveloperPatcher.ApplyWithBackup(path,
            new GameExecutableDeveloperState(false, false, DeveloperUserCheatMode.Normal, null));

        Assert.Equal(original, File.ReadAllBytes(path));
        Assert.False(GameExecutableDeveloperPatcher.Inspect(path).IsPatched);
    }

    [Fact]
    public void ApplySupportsReadOnlyExtractedExecutable()
    {
        Directory.CreateDirectory(_tempDirectory);
        string path = Path.Combine(_tempDirectory, "SLUS_208.65");
        File.WriteAllBytes(path, CreateSupportedExecutable());
        File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.ReadOnly);

        GameExecutableDeveloperPatcher.ApplyWithBackup(path,
            new GameExecutableDeveloperState(true, false, DeveloperUserCheatMode.Normal, null));

        Assert.True((File.GetAttributes(path) & FileAttributes.ReadOnly) != 0);
        Assert.True(GameExecutableDeveloperPatcher.Inspect(path).OneInningGames);
    }

    [Fact]
    public void InspectRejectsConflictingInstructionPatch()
    {
        byte[] executable = CreateSupportedExecutable();
        executable[GameExecutableDeveloperPatcher.OneInningGamesFileOffset] ^= 0xFF;

        InvalidDataException error = Assert.Throws<InvalidDataException>(() =>
            GameExecutableDeveloperPatcher.Inspect(executable));

        Assert.Contains("instruction signature", error.Message);
    }

    [Fact]
    public void ApplyRejectsZeroExactHitVelocityBeforeWriting()
    {
        Directory.CreateDirectory(_tempDirectory);
        string path = Path.Combine(_tempDirectory, "SLUS_208.65");
        byte[] original = CreateSupportedExecutable();
        File.WriteAllBytes(path, original);

        InvalidDataException error = Assert.Throws<InvalidDataException>(() =>
            GameExecutableDeveloperPatcher.ApplyWithBackup(path,
                new GameExecutableDeveloperState(false, false, DeveloperUserCheatMode.Normal,
                    new DeveloperHitOverride(0, 70, 0, 0, 0, 0))));

        Assert.Contains("velocity", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(original, File.ReadAllBytes(path));
        Assert.Empty(Directory.EnumerateFiles(_tempDirectory, "*.backup_*"));
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

    internal static byte[] CreateSupportedExecutable()
    {
        byte[] executable = new byte[GameExecutableDeveloperPatcher.CpuSeasonPlayFileOffset + 0x100];
        OriginalDataStub.CopyTo(executable, GameExecutableDeveloperPatcher.HitOriginDataFileOffset);
        OriginalDataStub.CopyTo(executable, GameExecutableDeveloperPatcher.HitTrajectoryDataFileOffset);
        OriginalOneInning.CopyTo(executable, GameExecutableDeveloperPatcher.OneInningGamesFileOffset);
        OriginalHitOrigin.CopyTo(executable, GameExecutableDeveloperPatcher.HitOriginGetterFileOffset);
        OriginalHitTrajectory.CopyTo(executable, GameExecutableDeveloperPatcher.HitTrajectoryGetterFileOffset);
        OriginalCheatHit.CopyTo(executable, GameExecutableDeveloperPatcher.CheatHitTrajectoryFileOffset);
        OriginalUserCheat.CopyTo(executable, GameExecutableDeveloperPatcher.UserCheatModeFileOffset);
        OriginalCpuSeason.CopyTo(executable, GameExecutableDeveloperPatcher.CpuSeasonPlayFileOffset);
        return executable;
    }

    private static byte[] Getter(byte objectOffset, bool signed) =>
        new byte[] { 0x08, 0x00, 0xE0, 0x03, objectOffset, 0x00, 0x82, signed ? (byte)0x80 : (byte)0x90,
            0, 0, 0, 0, 0, 0, 0, 0 };

    private static byte[] AddressGetter(byte objectOffset) =>
        new byte[] { 0x08, 0x00, 0xE0, 0x03, objectOffset, 0x00, 0x82, 0x24,
            0, 0, 0, 0, 0, 0, 0, 0 };
}
