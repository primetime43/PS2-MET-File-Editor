using PS2_DATA_File_Extractor.Models;

namespace PS2_DATA_File_Extractor.FileOperations;

/// <summary>
/// Applies one or more entry replacements to a MET archive with one recoverable backup.
/// </summary>
public static class METArchiveBatchEditor
{
    public static METArchiveBatchSaveResult SaveWithBackup(
        string metPath,
        IReadOnlyDictionary<string, byte[]> replacements,
        string operationName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(metPath);
        ArgumentNullException.ThrowIfNull(replacements);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationName);

        if (replacements.Count == 0)
        {
            return new METArchiveBatchSaveResult(null, 0, false);
        }

        Dictionary<string, byte[]> normalized = replacements.ToDictionary(
            pair => NormalizePath(pair.Key), pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        METFileStructure initialStructure = METFileReader.ReadMETFile(metPath);
        List<string> orderedPaths = initialStructure.AllEntries
            .Select(entry => NormalizePath(entry.Path))
            .Where(normalized.ContainsKey)
            .ToList();

        if (orderedPaths.Count != normalized.Count)
        {
            string missing = normalized.Keys.First(path => !orderedPaths.Contains(path, StringComparer.OrdinalIgnoreCase));
            throw new InvalidDataException($"The MET archive does not contain '{missing}'.");
        }

        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
        string backupPath = $"{metPath}.backup_{timestamp}";
        string tempPath = Path.Combine(
            Path.GetDirectoryName(metPath) ?? ".",
            $".{Path.GetFileName(metPath)}.{Guid.NewGuid():N}.{operationName}.tmp");
        bool rebuilt = false;

        File.Copy(metPath, backupPath, overwrite: false);
        try
        {
            foreach (string path in orderedPaths)
            {
                byte[] data = normalized[path];
                METFileStructure structure = METFileReader.ReadMETFile(metPath);
                FileEntry entry = structure.AllEntries.First(candidate =>
                    NormalizePath(candidate.Path).Equals(path, StringComparison.OrdinalIgnoreCase));

                if (data.Length <= entry.OriginalSize)
                {
                    WriteInPlace(metPath, entry, data);
                }
                else
                {
                    METFileRebuilder.RebuildWithExpandedEntry(metPath, tempPath, entry, data);
                    File.Move(tempPath, metPath, overwrite: true);
                    rebuilt = true;
                }
            }

            return new METArchiveBatchSaveResult(backupPath, replacements.Count, rebuilt);
        }
        catch
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }

            File.Copy(backupPath, metPath, overwrite: true);
            throw;
        }
    }

    private static void WriteInPlace(string metPath, FileEntry entry, byte[] data)
    {
        using FileStream stream = new FileStream(metPath, FileMode.Open, FileAccess.Write, FileShare.None);
        stream.Position = entry.Offset;
        stream.Write(data);
        if (data.Length < entry.OriginalSize)
        {
            stream.Write(new byte[entry.OriginalSize - data.Length]);
        }
    }

    private static string NormalizePath(string path) => path.Replace('\\', '/');
}

public sealed record METArchiveBatchSaveResult(string? BackupPath, int ChangedEntryCount, bool RebuiltArchive);
