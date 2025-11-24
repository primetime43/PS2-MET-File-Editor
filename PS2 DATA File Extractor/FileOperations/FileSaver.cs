using PS2_DATA_File_Extractor.Models;
using System.Text;

namespace PS2_DATA_File_Extractor.FileOperations
{
    /// <summary>
    /// Provides methods to save changes to file entries within a MET file.
    /// </summary>
    public class FileSaver
    {
        /// <summary>
        /// Creates a backup of the MET file before performing operations.
        /// Backup files are timestamped and kept permanently.
        /// </summary>
        /// <param name="metPath">Path to the MET file.</param>
        /// <returns>Path to the backup file.</returns>
        private static string CreateBackup(string metPath)
        {
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string backupPath = $"{metPath}.backup_{timestamp}";
            File.Copy(metPath, backupPath, overwrite: false);
            return backupPath;
        }

        /// <summary>
        /// Restores the MET file from backup.
        /// </summary>
        /// <param name="metPath">Path to the MET file.</param>
        /// <param name="backupPath">Path to the backup file.</param>
        private static void RestoreBackup(string metPath, string backupPath)
        {
            if (File.Exists(backupPath))
            {
                File.Copy(backupPath, metPath, overwrite: true);
            }
        }

        /// <summary>
        /// Saves changes to a file entry within a MET file.
        /// </summary>
        /// <param name="dataMetPath">The path to the data.met file.</param>
        /// <param name="entry">The file entry to save changes to.</param>
        /// <param name="content">The new content to write to the file entry.</param>
        /// <returns>True if the changes were saved successfully, false otherwise.</returns>
        public static bool SaveFileEntryChanges(string dataMetPath, FileEntry entry, string content)
        {
            byte[] data = Encoding.UTF8.GetBytes(content);
            return SaveFileEntryChanges(dataMetPath, entry, data);
        }

        /// <summary>
        /// Saves binary data to a file entry within a MET file.
        /// </summary>
        /// <param name="dataMetPath">The path to the data.met file.</param>
        /// <param name="entry">The file entry to save changes to.</param>
        /// <param name="data">The binary data to write to the file entry.</param>
        /// <returns>True if the changes were saved successfully, false otherwise.</returns>
        public static bool SaveFileEntryChanges(string dataMetPath, FileEntry entry, byte[] data)
        {
            using (FileStream fs = new FileStream(dataMetPath, FileMode.Open, FileAccess.ReadWrite))
            using (BinaryWriter writer = new BinaryWriter(fs))
            {
                fs.Seek(entry.Offset, SeekOrigin.Begin);

                if (data.Length > entry.OriginalSize)
                {
                    MessageBox.Show("The new data is too large to fit in the existing space.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return false;
                }

                writer.Write(data);

                // Pad with zeros if the new data is shorter than the original data
                if (data.Length < entry.OriginalSize)
                {
                    long paddingPosition = entry.Offset + data.Length;
                    int paddingSize = entry.OriginalSize - data.Length;
                    writer.BaseStream.Position = paddingPosition;
                    writer.Write(new byte[paddingSize]);
                }

                return true;
            }
        }

        /// <summary>
        /// Saves file entry changes with support for dynamic resizing.
        /// If the new data is larger than the original size, the MET file will be rebuilt.
        /// </summary>
        /// <param name="dataMetPath">The path to the data.met file.</param>
        /// <param name="entry">The file entry to save changes to.</param>
        /// <param name="data">The binary data to write.</param>
        /// <returns>True if the changes were saved successfully, false otherwise.</returns>
        public static bool SaveFileEntryChangesWithResize(string dataMetPath, FileEntry entry, byte[] data)
        {
            // If data fits in existing space, use the standard save method
            if (data.Length <= entry.OriginalSize)
            {
                return SaveFileEntryChanges(dataMetPath, entry, data);
            }

            // Data is larger - need to rebuild the MET file
            string backupPath = null;
            try
            {
                // Create backup before making changes
                backupPath = CreateBackup(dataMetPath);

                // Read all file entries to rebuild the structure
                var allEntries = new Dictionary<string, List<FileEntry>>();
                allEntries = METFileReader.ReadFileEntries(dataMetPath, allEntries);

                // Flatten the entries into a single list, sorted by offset
                var sortedEntries = allEntries.Values
                    .SelectMany(list => list)
                    .OrderBy(e => e.Offset)
                    .ToList();

                // Find the entry we're modifying
                var targetEntry = sortedEntries.FirstOrDefault(e =>
                    e.HeaderStart == entry.HeaderStart && e.Path == entry.Path);

                if (targetEntry == null)
                {
                    MessageBox.Show("Could not find the target entry in the MET file.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return false;
                }

                int targetIndex = sortedEntries.IndexOf(targetEntry);
                int sizeDelta = data.Length - targetEntry.OriginalSize;

                // Save original offsets for reading from source file
                var originalOffsets = sortedEntries.Select(e => e.Offset).ToArray();

                // Update the target entry's size
                targetEntry.OriginalSize = data.Length;
                targetEntry.CurrentSize = data.Length;

                // Update offsets for all entries after this one
                for (int i = targetIndex + 1; i < sortedEntries.Count; i++)
                {
                    sortedEntries[i].Offset += sizeDelta;
                }

                // Create a temporary file to rebuild the MET
                string tempPath = dataMetPath + ".temp";
                using (FileStream sourceFs = new FileStream(dataMetPath, FileMode.Open, FileAccess.Read))
                using (FileStream destFs = new FileStream(tempPath, FileMode.Create, FileAccess.Write))
                using (BinaryReader reader = new BinaryReader(sourceFs))
                using (BinaryWriter writer = new BinaryWriter(destFs))
                {
                    // Copy the first 8 bytes (header)
                    sourceFs.Seek(0, SeekOrigin.Begin);
                    byte[] fileHeader = reader.ReadBytes(8);
                    writer.Write(fileHeader);

                    // Write all file entry headers with updated offsets and sizes
                    foreach (var e in sortedEntries)
                    {
                        // Write offset (4 bytes, little-endian)
                        writer.Write(BitConverter.GetBytes(e.Offset));

                        // Write size (4 bytes, little-endian)
                        writer.Write(BitConverter.GetBytes(e.OriginalSize));

                        // Write string length (4 bytes, little-endian)
                        writer.Write(BitConverter.GetBytes(e.StringLength));

                        // Write path string
                        byte[] pathBytes = Encoding.UTF8.GetBytes(e.Path);
                        writer.Write(pathBytes);
                    }

                    // Write separator (12 zero bytes to mark end of headers)
                    writer.Write(new byte[12]);

                    // Pad to align data sections properly
                    long currentPos = destFs.Position;
                    long firstDataOffset = sortedEntries[0].Offset;
                    if (currentPos < firstDataOffset)
                    {
                        writer.Write(new byte[firstDataOffset - currentPos]);
                    }

                    // Write all file data sections
                    for (int i = 0; i < sortedEntries.Count; i++)
                    {
                        var e = sortedEntries[i];

                        // Ensure we're at the correct offset in destination
                        destFs.Seek(e.Offset, SeekOrigin.Begin);

                        if (i == targetIndex)
                        {
                            // Write the new data for the modified entry
                            writer.Write(data);
                        }
                        else
                        {
                            // Copy existing data for other entries using ORIGINAL offset from source
                            sourceFs.Seek(originalOffsets[i], SeekOrigin.Begin);
                            byte[] existingData = reader.ReadBytes(e.OriginalSize);
                            writer.Write(existingData);
                        }
                    }
                }

                // Replace the original file with the rebuilt one
                File.Delete(dataMetPath);
                File.Move(tempPath, dataMetPath);

                // Update the original entry reference with new values
                entry.OriginalSize = data.Length;
                entry.CurrentSize = data.Length;

                // Keep backup permanently - show user where it was saved
                MessageBox.Show($"Backup created: {Path.GetFileName(backupPath)}\n\nLocation: {Path.GetDirectoryName(backupPath)}",
                    "Backup Created", MessageBoxButtons.OK, MessageBoxIcon.Information);

                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error during resize operation: {ex.Message}\n\nThe file has been restored from backup: {Path.GetFileName(backupPath)}",
                    "Resize Error", MessageBoxButtons.OK, MessageBoxIcon.Error);

                // Restore from backup on error
                if (backupPath != null)
                {
                    RestoreBackup(dataMetPath, backupPath);
                }

                return false;
            }
        }
    }
}
