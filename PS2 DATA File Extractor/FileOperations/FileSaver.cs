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
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
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
            // If data fits in existing space, use the standard save method.
            if (data.Length <= entry.OriginalSize)
            {
                return SaveFileEntryChanges(dataMetPath, entry, data);
            }

            string? backupPath = null;
            string tempPath = Path.Combine(
                Path.GetDirectoryName(dataMetPath) ?? ".",
                $".{Path.GetFileName(dataMetPath)}.{Guid.NewGuid():N}.temp");

            try
            {
                backupPath = CreateBackup(dataMetPath);
                METFileRebuilder.RebuildWithExpandedEntry(dataMetPath, tempPath, entry, data);
                File.Move(tempPath, dataMetPath, overwrite: true);

                entry.OriginalSize = data.Length;
                entry.CurrentSize = data.Length;

                MessageBox.Show($"Backup created: {Path.GetFileName(backupPath)}\n\nLocation: {Path.GetDirectoryName(backupPath)}",
                    "Backup Created", MessageBoxButtons.OK, MessageBoxIcon.Information);

                return true;
            }
            catch (Exception ex)
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }

                if (backupPath != null)
                {
                    RestoreBackup(dataMetPath, backupPath);
                }

                string restoreMessage = backupPath == null
                    ? "The archive was not modified."
                    : $"The archive has been restored from backup: {Path.GetFileName(backupPath)}";
                MessageBox.Show($"Error during resize operation: {ex.Message}\n\n{restoreMessage}",
                    "Resize Error", MessageBoxButtons.OK, MessageBoxIcon.Error);

                return false;
            }
        }
    }
}
