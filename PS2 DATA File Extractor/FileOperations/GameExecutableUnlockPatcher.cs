namespace PS2_DATA_File_Extractor.FileOperations;

/// <summary>
/// Applies a version-checked unlock patch to the retail USA Backyard Baseball PS2 executable.
/// The file offsets are mapped from R5900 virtual addresses 0x0026B0C0 and 0x0026B0E0.
/// </summary>
public static class GameExecutableUnlockPatcher
{
    public const int IsItemUnlockedFileOffset = 0x0016B140;
    public const int FieldsRemainingForAquadomeFileOffset = 0x0016B160;
    public const ushort SelectableItemMask = 0x0FFF;
    public const string VerifiedRetailSha256 =
        "DCB35FAE266F0D46DCAE7CF605830AC780CF0F199321760B3971F68350BB1FA7";

    private static readonly byte[] OriginalIsItemUnlocked =
    {
        0x1C, 0x00, 0x82, 0x8C, // lw v0, 0x1c(a0)
        0x01, 0x00, 0x03, 0x24, // addiu v1, zero, 1
        0x04, 0x18, 0xA3, 0x00, // sllv v1, v1, a1
        0x24, 0x10, 0x43, 0x00, // and v0, v0, v1
        0x08, 0x00, 0xE0, 0x03, // jr ra
        0x2B, 0x10, 0x02, 0x00, // sltu v0, zero, v0 (delay slot)
        0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00
    };

    private static readonly byte[] OriginalFieldsRemainingPrefix =
    {
        0x1C, 0x00, 0x84, 0x8C,
        0x00, 0x10, 0x83, 0x30,
        0x03, 0x00, 0x60, 0x10
    };

    private static readonly byte[] AquadomeUnlockedPrefix =
    {
        0x00, 0x00, 0x02, 0x24, // addiu v0, zero, 0
        0x08, 0x00, 0xE0, 0x03, // jr ra
        0x00, 0x00, 0x00, 0x00  // nop
    };

    public static GameExecutableUnlockState Inspect(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Inspect(File.ReadAllBytes(path));
    }

    public static GameExecutableUnlockState Inspect(ReadOnlySpan<byte> executable)
    {
        int minimumLength = FieldsRemainingForAquadomeFileOffset + OriginalFieldsRemainingPrefix.Length;
        if (executable.Length < minimumLength)
        {
            throw new InvalidDataException("The selected file is too small to be the supported SLUS_208.65 executable.");
        }

        ReadOnlySpan<byte> itemFunction =
            executable.Slice(IsItemUnlockedFileOffset, OriginalIsItemUnlocked.Length);
        ushort forcedMask;
        bool itemPatched;
        if (itemFunction.SequenceEqual(OriginalIsItemUnlocked))
        {
            forcedMask = 0;
            itemPatched = false;
        }
        else if (TryReadPatchedItemMask(itemFunction, out forcedMask))
        {
            itemPatched = true;
        }
        else
        {
            throw UnsupportedExecutable("IsItemUnlocked");
        }

        ReadOnlySpan<byte> aquadomePrefix =
            executable.Slice(FieldsRemainingForAquadomeFileOffset, OriginalFieldsRemainingPrefix.Length);
        bool aquadomePatched;
        if (aquadomePrefix.SequenceEqual(OriginalFieldsRemainingPrefix))
        {
            aquadomePatched = false;
        }
        else if (aquadomePrefix.SequenceEqual(AquadomeUnlockedPrefix))
        {
            aquadomePatched = true;
        }
        else
        {
            throw UnsupportedExecutable("FieldsRemainingForAquadome");
        }

        return new GameExecutableUnlockState(
            forcedMask,
            aquadomePatched,
            itemPatched || aquadomePatched);
    }

    public static string ApplyWithBackup(string path, ushort forcedItemMask, bool unlockAquadome)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if ((forcedItemMask & ~SelectableItemMask) != 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(forcedItemMask),
                $"Only mask bits 0-11 are supported (0x{SelectableItemMask:X4}).");
        }

        byte[] executable = File.ReadAllBytes(path);
        _ = Inspect(executable);

        byte[] itemInstructions = forcedItemMask == 0
            ? OriginalIsItemUnlocked
            : BuildPatchedItemInstructions(forcedItemMask);
        itemInstructions.CopyTo(executable, IsItemUnlockedFileOffset);

        byte[] aquadomeInstructions = unlockAquadome
            ? AquadomeUnlockedPrefix
            : OriginalFieldsRemainingPrefix;
        aquadomeInstructions.CopyTo(executable, FieldsRemainingForAquadomeFileOffset);

        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
        string backupPath = $"{path}.backup_{timestamp}";
        File.Copy(path, backupPath, overwrite: false);

        FileAttributes originalAttributes = File.GetAttributes(path);
        bool wasReadOnly = (originalAttributes & FileAttributes.ReadOnly) != 0;
        string temporaryPath = Path.Combine(
            Path.GetDirectoryName(path) ?? ".",
            $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.temp");
        try
        {
            File.WriteAllBytes(temporaryPath, executable);
            if (wasReadOnly)
            {
                File.SetAttributes(path, originalAttributes & ~FileAttributes.ReadOnly);
            }

            File.Move(temporaryPath, path, overwrite: true);
            File.SetAttributes(path, originalAttributes);
        }
        catch
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }

            if (File.Exists(path) && wasReadOnly)
            {
                File.SetAttributes(path, File.GetAttributes(path) & ~FileAttributes.ReadOnly);
            }

            File.Copy(backupPath, path, overwrite: true);
            File.SetAttributes(path, originalAttributes);
            throw;
        }

        return backupPath;
    }

    private static byte[] BuildPatchedItemInstructions(ushort forcedMask)
    {
        byte[] instructions =
        {
            0x1C, 0x00, 0x82, 0x8C, // lw v0, 0x1c(a0)
            0x01, 0x00, 0x03, 0x24, // addiu v1, zero, 1 (load-delay slot)
            (byte)forcedMask, (byte)(forcedMask >> 8), 0x42, 0x34, // ori v0, v0, mask
            0x04, 0x18, 0xA3, 0x00, // sllv v1, v1, a1
            0x24, 0x10, 0x43, 0x00, // and v0, v0, v1
            0x08, 0x00, 0xE0, 0x03, // jr ra
            0x2B, 0x10, 0x02, 0x00, // sltu v0, zero, v0 (delay slot)
            0x00, 0x00, 0x00, 0x00
        };
        return instructions;
    }

    private static bool TryReadPatchedItemMask(ReadOnlySpan<byte> instructions, out ushort mask)
    {
        byte[] expected = BuildPatchedItemInstructions(0);
        mask = (ushort)(instructions[8] | (instructions[9] << 8));

        for (int index = 0; index < expected.Length; index++)
        {
            if ((index == 8 || index == 9))
            {
                continue;
            }

            if (instructions[index] != expected[index])
            {
                mask = 0;
                return false;
            }
        }

        if (mask == 0 || (mask & ~SelectableItemMask) != 0)
        {
            mask = 0;
            return false;
        }

        return true;
    }

    private static InvalidDataException UnsupportedExecutable(string functionName)
    {
        return new InvalidDataException(
            $"The {functionName} instruction signature does not match the verified USA SLUS_208.65 executable. " +
            "The file may be a different game revision or already modified by another patch.");
    }
}

public sealed record GameExecutableUnlockState(
    ushort ForcedItemMask,
    bool AquadomeUnlocked,
    bool IsPatched);
