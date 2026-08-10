using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace PS2_DATA_File_Extractor.FileOperations;

public static partial class IsoBuildService
{
    public const int SectorSize = 2048;

    public static string? FindImgBurn()
    {
        string[] candidates =
        {
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "ImgBurn", "ImgBurn.exe"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "ImgBurn", "ImgBurn.exe")
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    public static GameFolderValidation ValidateGameFolder(string sourceDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceDirectory);
        string sourcePath = Path.GetFullPath(sourceDirectory);
        if (!Directory.Exists(sourcePath))
        {
            throw new DirectoryNotFoundException($"Game folder not found: {sourcePath}");
        }

        Dictionary<string, string> rootFiles = Directory
            .EnumerateFiles(sourcePath, "*", SearchOption.TopDirectoryOnly)
            .ToDictionary(path => Path.GetFileName(path)!, StringComparer.OrdinalIgnoreCase);

        if (!rootFiles.TryGetValue("SYSTEM.CNF", out string? systemCnfPath))
        {
            throw new InvalidDataException("The selected folder does not contain SYSTEM.CNF at its root.");
        }

        if (!rootFiles.TryGetValue("DATA.MET", out string? dataMetPath))
        {
            throw new InvalidDataException("The selected folder does not contain DATA.MET at its root.");
        }

        string systemCnf = File.ReadAllText(systemCnfPath);
        Match bootMatch = BootExecutableRegex().Match(systemCnf);
        if (!bootMatch.Success)
        {
            throw new InvalidDataException(
                "SYSTEM.CNF does not contain a supported BOOT/BOOT2 cdrom executable entry.");
        }

        string relativeBootPath = bootMatch.Groups["path"].Value
            .Trim()
            .Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar);
        string bootPath = Path.GetFullPath(Path.Combine(sourcePath, relativeBootPath));
        if (!IsWithinDirectory(sourcePath, bootPath) || !File.Exists(bootPath))
        {
            throw new InvalidDataException(
                $"The boot executable referenced by SYSTEM.CNF was not found: {relativeBootPath}");
        }

        return new GameFolderValidation(sourcePath, systemCnfPath, dataMetPath, bootPath);
    }

    public static IReadOnlyList<string> CreateImgBurnArguments(IsoBuildRequest request)
    {
        ValidatedIsoBuildRequest validated = ValidateRequest(request);
        return new[]
        {
            "/MODE", "BUILD",
            "/BUILDINPUTMODE", "STANDARD",
            "/BUILDOUTPUTMODE", "IMAGEFILE",
            "/SRC", validated.SourceDirectory + Path.DirectorySeparatorChar,
            "/DEST", validated.OutputPath,
            "/FILESYSTEM", "ISO9660 + UDF",
            "/UDFREVISION", "1.02",
            "/VOLUMELABEL", validated.VolumeLabel,
            "/OVERWRITE", "YES",
            "/ROOTFOLDER", "YES",
            "/NOIMAGEDETAILS",
            "/START",
            "/CLOSESUCCESS"
        };
    }

    public static async Task<IsoBuildResult> BuildAsync(
        IsoBuildRequest request,
        IProgress<string>? progress = null)
    {
        ValidatedIsoBuildRequest validated = ValidateRequest(request);
        IReadOnlyList<string> arguments = CreateImgBurnArguments(request);
        Directory.CreateDirectory(Path.GetDirectoryName(validated.OutputPath)!);

        string? previousImageBackup = null;
        if (File.Exists(validated.OutputPath))
        {
            previousImageBackup = CreateSiblingPath(validated.OutputPath, "backup");
            File.Move(validated.OutputPath, previousImageBackup);
        }

        try
        {
            progress?.Report("Starting ImgBurn with ISO9660 + UDF 1.02...");
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = validated.ImgBurnPath,
                UseShellExecute = false,
                WorkingDirectory = validated.SourceDirectory
            };
            foreach (string argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            using Process process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("ImgBurn could not be started.");
            await process.WaitForExitAsync();

            progress?.Report("Validating the generated ISO...");
            ValidateIsoImage(validated.OutputPath);

            FileInfo image = new FileInfo(validated.OutputPath);
            return new IsoBuildResult(
                validated.OutputPath,
                image.Length,
                previousImageBackup,
                process.ExitCode);
        }
        catch (Exception exception)
        {
            string recovery = RestorePreviousOutput(validated.OutputPath, previousImageBackup);
            throw new InvalidOperationException(
                $"{exception.Message}{Environment.NewLine}{Environment.NewLine}{recovery}",
                exception);
        }
    }

    public static void ValidateIsoImage(string isoPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(isoPath);
        if (!File.Exists(isoPath))
        {
            throw new InvalidDataException("ImgBurn did not create the requested ISO file.");
        }

        long minimumLength = (257L * SectorSize);
        FileInfo file = new FileInfo(isoPath);
        if (file.Length < minimumLength)
        {
            throw new InvalidDataException("The generated image is too small to contain valid ISO9660/UDF descriptors.");
        }

        using FileStream stream = File.OpenRead(isoPath);
        byte[] identifier = new byte[5];
        stream.Position = (16L * SectorSize) + 1;
        stream.ReadExactly(identifier);
        if (!identifier.AsSpan().SequenceEqual("CD001"u8))
        {
            throw new InvalidDataException("The generated image does not contain a valid ISO9660 primary descriptor.");
        }

        Span<byte> udfTag = stackalloc byte[2];
        stream.Position = 256L * SectorSize;
        stream.ReadExactly(udfTag);
        if (udfTag[0] != 0x02 || udfTag[1] != 0x00)
        {
            throw new InvalidDataException("The generated image does not contain the expected UDF anchor descriptor.");
        }
    }

    public static string NormalizeVolumeLabel(string? volumeLabel, string sourceDirectory)
    {
        string label = string.IsNullOrWhiteSpace(volumeLabel)
            ? Path.GetFileName(Path.TrimEndingDirectorySeparator(sourceDirectory))
            : volumeLabel;

        StringBuilder normalized = new StringBuilder();
        foreach (char value in label.ToUpperInvariant())
        {
            normalized.Append(
                (value is >= 'A' and <= 'Z') ||
                (value is >= '0' and <= '9') ||
                value == '_'
                    ? value
                    : '_');
        }

        string result = normalized.ToString().Trim('_');
        if (result.Length == 0)
        {
            result = "PS2_GAME";
        }

        return result.Length <= 32 ? result : result[..32];
    }

    private static ValidatedIsoBuildRequest ValidateRequest(IsoBuildRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        GameFolderValidation gameFolder = ValidateGameFolder(request.SourceDirectory);

        string outputPath = Path.GetFullPath(request.OutputPath);
        if (!outputPath.EndsWith(".iso", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The output filename must use the .iso extension.");
        }

        if (IsWithinDirectory(gameFolder.SourceDirectory, outputPath))
        {
            throw new InvalidDataException(
                "The output ISO cannot be placed inside the source folder because it would be included in itself.");
        }

        string imgBurnPath = Path.GetFullPath(request.ImgBurnPath);
        if (!File.Exists(imgBurnPath))
        {
            throw new FileNotFoundException("ImgBurn.exe was not found.", imgBurnPath);
        }

        string? outputDirectory = Path.GetDirectoryName(outputPath);
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            throw new InvalidDataException("The output ISO path does not have a valid parent directory.");
        }

        return new ValidatedIsoBuildRequest(
            gameFolder.SourceDirectory,
            outputPath,
            NormalizeVolumeLabel(request.VolumeLabel, gameFolder.SourceDirectory),
            imgBurnPath);
    }

    private static bool IsWithinDirectory(string directory, string candidate)
    {
        string relative = Path.GetRelativePath(
            Path.GetFullPath(directory),
            Path.GetFullPath(candidate));
        return !Path.IsPathRooted(relative) &&
               relative != ".." &&
               !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal);
    }

    private static string CreateSiblingPath(string path, string kind)
    {
        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
        string candidate = $"{path}.{kind}_{timestamp}";
        int suffix = 1;
        while (File.Exists(candidate))
        {
            candidate = $"{path}.{kind}_{timestamp}_{suffix++}";
        }

        return candidate;
    }

    private static string RestorePreviousOutput(string outputPath, string? backupPath)
    {
        string? failedPath = null;
        if (File.Exists(outputPath))
        {
            failedPath = CreateSiblingPath(outputPath, "failed");
            File.Move(outputPath, failedPath);
        }

        if (backupPath != null && File.Exists(backupPath))
        {
            File.Move(backupPath, outputPath);
            return failedPath == null
                ? $"The previous output ISO was restored: {outputPath}"
                : $"The incomplete image was kept at {failedPath}.{Environment.NewLine}" +
                  $"The previous output ISO was restored: {outputPath}";
        }

        return failedPath == null
            ? "No existing output ISO was changed."
            : $"The incomplete image was kept at: {failedPath}";
    }

    [GeneratedRegex(
        @"(?im)^\s*BOOT2?\s*=\s*cdrom0:\\+(?<path>[^;\r\n]+)(?:;\d+)?\s*$",
        RegexOptions.CultureInvariant)]
    private static partial Regex BootExecutableRegex();

    private sealed record ValidatedIsoBuildRequest(
        string SourceDirectory,
        string OutputPath,
        string VolumeLabel,
        string ImgBurnPath);
}

public sealed record IsoBuildRequest(
    string SourceDirectory,
    string OutputPath,
    string VolumeLabel,
    string ImgBurnPath);

public sealed record IsoBuildResult(
    string OutputPath,
    long ImageSize,
    string? PreviousImageBackupPath,
    int ImgBurnExitCode);

public sealed record GameFolderValidation(
    string SourceDirectory,
    string SystemCnfPath,
    string DataMetPath,
    string BootExecutablePath);
