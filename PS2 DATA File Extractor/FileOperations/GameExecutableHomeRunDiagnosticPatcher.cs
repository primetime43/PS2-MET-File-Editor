namespace PS2_DATA_File_Extractor.FileOperations;

/// <summary>
/// Applies a version-checked diagnostic patch to the retail USA executable's
/// BaseballField::CheckHomeRun function. This is intentionally not exposed in the UI:
/// it is used to prove which RenderWare collision data the game actually queries.
/// </summary>
public static class GameExecutableHomeRunDiagnosticPatcher
{
    public const int CheckHomeRunRuntimeAddress = 0x0013DF50;
    public const int CheckHomeRunFileOffset = 0x0003DFD0;

    private static readonly byte[] OriginalPrefix =
    {
        0xB0, 0xFF, 0xBD, 0x27, // addiu sp, sp, -0x50
        0x40, 0x00, 0xBF, 0xFF, // sd ra, 0x40(sp)
        0x30, 0x00, 0xB3, 0x7F  // sq s3, 0x30(sp)
    };

    private static readonly byte[] AlwaysTruePrefix =
    {
        0x01, 0x00, 0x02, 0x24, // addiu v0, zero, 1
        0x08, 0x00, 0xE0, 0x03, // jr ra
        0x00, 0x00, 0x00, 0x00  // nop
    };

    public static GameExecutableHomeRunDiagnosticState Inspect(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Inspect(File.ReadAllBytes(path));
    }

    public static GameExecutableHomeRunDiagnosticState Inspect(ReadOnlySpan<byte> executable)
    {
        if (executable.Length < CheckHomeRunFileOffset + OriginalPrefix.Length)
        {
            throw new InvalidDataException(
                "The selected file is too small to be the supported SLUS_208.65 executable.");
        }

        ReadOnlySpan<byte> prefix = executable.Slice(CheckHomeRunFileOffset, OriginalPrefix.Length);
        if (prefix.SequenceEqual(OriginalPrefix))
        {
            return GameExecutableHomeRunDiagnosticState.Original;
        }

        if (prefix.SequenceEqual(AlwaysTruePrefix))
        {
            return GameExecutableHomeRunDiagnosticState.AlwaysHomeRunSurface;
        }

        throw new InvalidDataException(
            "The BaseballField::CheckHomeRun instruction signature does not match the verified " +
            "USA SLUS_208.65 executable. The file may be a different revision or have a conflicting patch.");
    }

    public static string ApplyWithBackup(string path, bool alwaysHomeRunSurface)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        byte[] executable = File.ReadAllBytes(path);
        _ = Inspect(executable);

        (alwaysHomeRunSurface ? AlwaysTruePrefix : OriginalPrefix)
            .CopyTo(executable, CheckHomeRunFileOffset);

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
}

public enum GameExecutableHomeRunDiagnosticState
{
    Original,
    AlwaysHomeRunSurface
}
