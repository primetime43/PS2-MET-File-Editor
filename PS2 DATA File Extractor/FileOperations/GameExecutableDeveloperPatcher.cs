using System.Buffers.Binary;

namespace PS2_DATA_File_Extractor.FileOperations;

/// <summary>
/// Version-checked patches for dormant developer fields in the USA SLUS_208.65 executable.
/// Virtual addresses map through the ELF load segment at file offset 0x80.
/// </summary>
public static class GameExecutableDeveloperPatcher
{
    public const int OneInningGamesFileOffset = 0x000A94E0;
    public const int HitOriginGetterFileOffset = 0x000A9500;
    public const int HitTrajectoryGetterFileOffset = 0x000A9510;
    public const int CheatHitTrajectoryFileOffset = 0x000A9520;
    public const int UserCheatModeFileOffset = 0x000A9530;
    public const int CpuSeasonPlayFileOffset = 0x000A9540;
    public const int HitOriginDataFileOffset = 0x000A8A10;
    public const int HitTrajectoryDataFileOffset = 0x000A8A20;
    public const int HitOriginDataRuntimeAddress = 0x001A8990;
    public const int HitTrajectoryDataRuntimeAddress = 0x001A89A0;

    private static readonly byte[] OriginalOneInning = Getter(0x31, signed: false);
    private static readonly byte[] OriginalHitOrigin = AddressGetter(0x24);
    private static readonly byte[] OriginalHitTrajectory = AddressGetter(0x18);
    private static readonly byte[] OriginalCheatHit = Getter(0x14, signed: false);
    private static readonly byte[] OriginalUserCheat = Getter(0x13, signed: true);
    private static readonly byte[] OriginalCpuSeason = Getter(0x12, signed: false);
    private static readonly byte[] OriginalDataStub =
        { 0x08, 0x00, 0xE0, 0x03, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };

    public static GameExecutableDeveloperState Inspect(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Inspect(File.ReadAllBytes(path));
    }

    public static GameExecutableDeveloperState Inspect(ReadOnlySpan<byte> executable)
    {
        EnsureSize(executable);
        bool oneInning = ReadBooleanGetter(executable, OneInningGamesFileOffset, OriginalOneInning, "GetOneInningGames");
        bool cpuSeason = ReadBooleanGetter(executable, CpuSeasonPlayFileOffset, OriginalCpuSeason, "GetCPUSeasonPlay");
        DeveloperUserCheatMode userCheat = ReadUserCheatGetter(executable);

        ReadOnlySpan<byte> originGetter = executable.Slice(HitOriginGetterFileOffset, 16);
        ReadOnlySpan<byte> trajectoryGetter = executable.Slice(HitTrajectoryGetterFileOffset, 16);
        bool cheatHit = ReadBooleanGetter(executable, CheatHitTrajectoryFileOffset, OriginalCheatHit,
            "GetCheatHitTrajectory");
        bool originalPointers = originGetter.SequenceEqual(OriginalHitOrigin) &&
                                trajectoryGetter.SequenceEqual(OriginalHitTrajectory);
        bool patchedPointers = originGetter.SequenceEqual(BuildAbsoluteAddressGetter(HitOriginDataRuntimeAddress)) &&
                               trajectoryGetter.SequenceEqual(BuildAbsoluteAddressGetter(HitTrajectoryDataRuntimeAddress));
        bool originalData = executable.Slice(HitOriginDataFileOffset, 16).SequenceEqual(OriginalDataStub) &&
                            executable.Slice(HitTrajectoryDataFileOffset, 16).SequenceEqual(OriginalDataStub);

        DeveloperHitOverride? hitOverride = null;
        if (!cheatHit && originalPointers && originalData)
        {
            hitOverride = null;
        }
        else if (cheatHit && patchedPointers)
        {
            hitOverride = new DeveloperHitOverride(
                ReadSingle(executable, HitOriginDataFileOffset),
                ReadSingle(executable, HitOriginDataFileOffset + 4),
                ReadSingle(executable, HitOriginDataFileOffset + 8),
                ReadSingle(executable, HitTrajectoryDataFileOffset),
                ReadSingle(executable, HitTrajectoryDataFileOffset + 4),
                ReadSingle(executable, HitTrajectoryDataFileOffset + 8));
            ValidateHitOverride(hitOverride);
        }
        else
        {
            throw Unsupported("exact hit override");
        }

        return new GameExecutableDeveloperState(oneInning, cpuSeason, userCheat, hitOverride);
    }

    public static string ApplyWithBackup(string path, GameExecutableDeveloperState desired)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(desired);
        if (!Enum.IsDefined(desired.UserCheatMode))
            throw new ArgumentOutOfRangeException(nameof(desired), "Unknown user cheat mode.");
        if (desired.HitOverride != null) ValidateHitOverride(desired.HitOverride);

        byte[] executable = File.ReadAllBytes(path);
        _ = Inspect(executable);
        WriteGetter(executable, OneInningGamesFileOffset, OriginalOneInning, desired.OneInningGames ? 1 : 0);
        WriteGetter(executable, CpuSeasonPlayFileOffset, OriginalCpuSeason, desired.CpuSeasonPlay ? 1 : 0);
        WriteGetter(executable, UserCheatModeFileOffset, OriginalUserCheat, (int)desired.UserCheatMode);

        if (desired.HitOverride == null)
        {
            OriginalHitOrigin.CopyTo(executable, HitOriginGetterFileOffset);
            OriginalHitTrajectory.CopyTo(executable, HitTrajectoryGetterFileOffset);
            OriginalCheatHit.CopyTo(executable, CheatHitTrajectoryFileOffset);
            OriginalDataStub.CopyTo(executable, HitOriginDataFileOffset);
            OriginalDataStub.CopyTo(executable, HitTrajectoryDataFileOffset);
        }
        else
        {
            BuildAbsoluteAddressGetter(HitOriginDataRuntimeAddress).CopyTo(executable, HitOriginGetterFileOffset);
            BuildAbsoluteAddressGetter(HitTrajectoryDataRuntimeAddress).CopyTo(executable, HitTrajectoryGetterFileOffset);
            ForcedReturn(1).CopyTo(executable, CheatHitTrajectoryFileOffset);
            Array.Clear(executable, HitOriginDataFileOffset, 16);
            Array.Clear(executable, HitTrajectoryDataFileOffset, 16);
            WriteSingle(executable, HitOriginDataFileOffset, desired.HitOverride.OriginX);
            WriteSingle(executable, HitOriginDataFileOffset + 4, desired.HitOverride.OriginY);
            WriteSingle(executable, HitOriginDataFileOffset + 8, desired.HitOverride.OriginZ);
            WriteSingle(executable, HitTrajectoryDataFileOffset, desired.HitOverride.VelocityX);
            WriteSingle(executable, HitTrajectoryDataFileOffset + 4, desired.HitOverride.VelocityY);
            WriteSingle(executable, HitTrajectoryDataFileOffset + 8, desired.HitOverride.VelocityZ);
        }

        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
        string backupPath = $"{path}.backup_{timestamp}";
        File.Copy(path, backupPath, overwrite: false);
        ReplacePreservingAttributes(path, executable, backupPath);
        return backupPath;
    }

    private static bool ReadBooleanGetter(ReadOnlySpan<byte> executable, int offset, byte[] original, string name)
    {
        ReadOnlySpan<byte> bytes = executable.Slice(offset, 16);
        if (bytes.SequenceEqual(original)) return false;
        if (bytes.SequenceEqual(ForcedReturn(1))) return true;
        throw Unsupported(name);
    }

    private static DeveloperUserCheatMode ReadUserCheatGetter(ReadOnlySpan<byte> executable)
    {
        ReadOnlySpan<byte> bytes = executable.Slice(UserCheatModeFileOffset, 16);
        if (bytes.SequenceEqual(OriginalUserCheat)) return DeveloperUserCheatMode.Normal;
        if (bytes.SequenceEqual(ForcedReturn(1))) return DeveloperUserCheatMode.ForceWins;
        if (bytes.SequenceEqual(ForcedReturn(2))) return DeveloperUserCheatMode.ForceLosses;
        throw Unsupported("GetUserCheatMode");
    }

    private static void WriteGetter(byte[] executable, int offset, byte[] original, int value) =>
        (value == 0 ? original : ForcedReturn(value)).CopyTo(executable, offset);

    private static byte[] Getter(byte objectOffset, bool signed) =>
        new byte[] { 0x08, 0x00, 0xE0, 0x03, objectOffset, 0x00, 0x82, signed ? (byte)0x80 : (byte)0x90,
            0, 0, 0, 0, 0, 0, 0, 0 };

    private static byte[] AddressGetter(byte objectOffset) =>
        new byte[] { 0x08, 0x00, 0xE0, 0x03, objectOffset, 0x00, 0x82, 0x24,
            0, 0, 0, 0, 0, 0, 0, 0 };

    private static byte[] ForcedReturn(int value) =>
        new byte[] { (byte)value, 0x00, 0x02, 0x24, 0x08, 0x00, 0xE0, 0x03,
            0, 0, 0, 0, 0, 0, 0, 0 };

    private static byte[] BuildAbsoluteAddressGetter(int address)
    {
        ushort high = (ushort)(address >> 16);
        ushort low = (ushort)address;
        return new byte[]
        {
            (byte)high, (byte)(high >> 8), 0x02, 0x3C,       // lui v0, high
            (byte)low, (byte)(low >> 8), 0x42, 0x34,        // ori v0, v0, low
            0x08, 0x00, 0xE0, 0x03, 0, 0, 0, 0             // jr ra; nop
        };
    }

    private static float ReadSingle(ReadOnlySpan<byte> data, int offset) =>
        BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(data.Slice(offset, 4)));

    private static void WriteSingle(Span<byte> data, int offset, float value) =>
        BinaryPrimitives.WriteInt32LittleEndian(data.Slice(offset, 4), BitConverter.SingleToInt32Bits(value));

    private static void ValidateHitOverride(DeveloperHitOverride value)
    {
        float[] values = { value.OriginX, value.OriginY, value.OriginZ,
            value.VelocityX, value.VelocityY, value.VelocityZ };
        if (values.Any(number => !float.IsFinite(number) || MathF.Abs(number) > 100000F))
            throw new InvalidDataException("Exact-hit coordinates and velocities must be finite and between -100,000 and 100,000.");
        if (value.VelocityX == 0 && value.VelocityY == 0 && value.VelocityZ == 0)
            throw new InvalidDataException("Exact-hit velocity cannot be zero on every axis.");
    }

    private static void EnsureSize(ReadOnlySpan<byte> executable)
    {
        int minimum = Math.Max(CpuSeasonPlayFileOffset, HitTrajectoryDataFileOffset) + 16;
        if (executable.Length < minimum)
            throw new InvalidDataException("The selected file is too small to be the supported SLUS_208.65 executable.");
    }

    private static InvalidDataException Unsupported(string feature) => new(
        $"The {feature} instruction signature does not match the verified USA SLUS_208.65 executable. " +
        "The file may be a different revision or contain a conflicting patch.");

    private static void ReplacePreservingAttributes(string path, byte[] data, string backupPath)
    {
        FileAttributes originalAttributes = File.GetAttributes(path);
        bool wasReadOnly = (originalAttributes & FileAttributes.ReadOnly) != 0;
        string temporaryPath = Path.Combine(Path.GetDirectoryName(path) ?? ".",
            $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.developer.tmp");
        try
        {
            File.WriteAllBytes(temporaryPath, data);
            if (wasReadOnly) File.SetAttributes(path, originalAttributes & ~FileAttributes.ReadOnly);
            File.Move(temporaryPath, path, overwrite: true);
            File.SetAttributes(path, originalAttributes);
        }
        catch
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            if (File.Exists(path) && wasReadOnly)
                File.SetAttributes(path, File.GetAttributes(path) & ~FileAttributes.ReadOnly);
            File.Copy(backupPath, path, overwrite: true);
            File.SetAttributes(path, originalAttributes);
            throw;
        }
    }
}

public enum DeveloperUserCheatMode
{
    Normal = 0,
    ForceWins = 1,
    ForceLosses = 2
}

public sealed record DeveloperHitOverride(float OriginX, float OriginY, float OriginZ,
    float VelocityX, float VelocityY, float VelocityZ);

public sealed record GameExecutableDeveloperState(bool OneInningGames, bool CpuSeasonPlay,
    DeveloperUserCheatMode UserCheatMode, DeveloperHitOverride? HitOverride)
{
    public bool IsPatched => OneInningGames || CpuSeasonPlay ||
        UserCheatMode != DeveloperUserCheatMode.Normal || HitOverride != null;
}
