using System.Collections.Generic;
using System.Linq;

namespace PS2_DATA_File_Extractor.Models
{
    /// <summary>
    /// Represents the complete structure and address mapping of a MET archive file.
    /// Tracks all offsets, sizes, and provides methods to update when changes occur.
    /// </summary>
    public class METFileStructure
    {
        /// <summary>
        /// Offset where the data section begins (from bytes 0-3 of MET file).
        /// This marks the end of all file entry headers.
        /// </summary>
        public int DataSectionOffset { get; set; }

        /// <summary>
        /// Unknown value from bytes 4-7 of MET file.
        /// Possibly total data size or metadata - preserved for future use.
        /// </summary>
        public int UnknownHeaderValue { get; set; }

        /// <summary>
        /// Total size of the MET file in bytes.
        /// </summary>
        public long TotalFileSize { get; set; }

        /// <summary>
        /// Path to the MET file.
        /// </summary>
        public string FilePath { get; set; }

        /// <summary>
        /// All file entries in the archive, organized by file extension.
        /// </summary>
        public Dictionary<string, List<FileEntry>> GroupedEntries { get; set; }

        /// <summary>
        /// Flat list of all file entries in the order they appear in the archive.
        /// </summary>
        public List<FileEntry> AllEntries
        {
            get
            {
                return GroupedEntries.Values.SelectMany(list => list).OrderBy(e => e.HeaderStart).ToList();
            }
        }

        /// <summary>
        /// Total number of files in the archive.
        /// </summary>
        public int FileCount
        {
            get
            {
                return GroupedEntries.Values.Sum(list => list.Count);
            }
        }

        /// <summary>
        /// Size of the header section (all file entry headers).
        /// </summary>
        public int HeaderSectionSize
        {
            get
            {
                return DataSectionOffset - 8; // Subtract the 8-byte MET header
            }
        }

        public METFileStructure()
        {
            GroupedEntries = new Dictionary<string, List<FileEntry>>();
            FilePath = string.Empty;
        }

        /// <summary>
        /// Updates the data section offset to account for header changes.
        /// Call this when entries are added, removed, or paths change length.
        /// </summary>
        public void RecalculateDataSectionOffset()
        {
            // MET header (8 bytes) + sum of all entry headers
            int totalHeaderSize = 8;

            foreach (var entry in AllEntries)
            {
                // Each entry: offset (4) + size (4) + strLen (4) + path (N)
                totalHeaderSize += 4 + 4 + 4 + entry.StringLength;
            }

            DataSectionOffset = totalHeaderSize;
        }

        /// <summary>
        /// Updates all file offsets after a resize operation.
        /// When a file is resized, all subsequent files need their offsets adjusted.
        /// </summary>
        /// <param name="resizedEntry">The entry that was resized.</param>
        /// <param name="oldSize">The original size before resize.</param>
        /// <param name="newSize">The new size after resize.</param>
        public void UpdateOffsetsAfterResize(FileEntry resizedEntry, int oldSize, int newSize)
        {
            int sizeDifference = newSize - oldSize;

            // Update all entries that come after the resized entry
            foreach (var entry in AllEntries)
            {
                if (entry.Offset > resizedEntry.Offset)
                {
                    entry.Offset += sizeDifference;
                }
            }

            // Update the resized entry itself
            resizedEntry.OriginalSize = newSize;
            resizedEntry.CurrentSize = newSize;
        }

        /// <summary>
        /// Gets a file entry by its path.
        /// </summary>
        public FileEntry? GetEntryByPath(string path)
        {
            return AllEntries.FirstOrDefault(e => e.Path == path);
        }

        /// <summary>
        /// Validates the structure integrity.
        /// Returns true if all offsets and sizes are consistent.
        /// </summary>
        public (bool IsValid, List<string> Errors) ValidateStructure()
        {
            var errors = new List<string>();

            // Check that data section offset is reasonable
            if (DataSectionOffset < 8)
            {
                errors.Add("Data section offset is less than 8 (invalid MET header)");
            }

            // Check that all file entries are within bounds
            var allEntries = AllEntries;
            for (int i = 0; i < allEntries.Count; i++)
            {
                var entry = allEntries[i];

                // Validate offset is within file
                if (entry.Offset < DataSectionOffset || entry.Offset >= TotalFileSize)
                {
                    errors.Add($"Entry '{entry.Path}' has invalid offset: 0x{entry.Offset:X}");
                }

                // Validate size is reasonable
                if (entry.OriginalSize < 0 || entry.Offset + entry.OriginalSize > TotalFileSize)
                {
                    errors.Add($"Entry '{entry.Path}' has invalid size: {entry.OriginalSize} bytes");
                }

                // Check for overlapping data sections
                if (i < allEntries.Count - 1)
                {
                    var nextEntry = allEntries[i + 1];
                    int entryEnd = entry.Offset + entry.OriginalSize;

                    if (entryEnd > nextEntry.Offset)
                    {
                        errors.Add($"Entry '{entry.Path}' overlaps with '{nextEntry.Path}'");
                    }
                }
            }

            return (errors.Count == 0, errors);
        }

        /// <summary>
        /// Gets statistics about the archive.
        /// </summary>
        public string GetStatistics()
        {
            var stats = new System.Text.StringBuilder();
            stats.AppendLine($"MET File: {System.IO.Path.GetFileName(FilePath)}");
            stats.AppendLine($"Total Size: {TotalFileSize:N0} bytes ({TotalFileSize / 1024.0 / 1024.0:F2} MB)");
            stats.AppendLine($"Total Files: {FileCount}");
            stats.AppendLine($"Header Section: {HeaderSectionSize:N0} bytes");
            stats.AppendLine($"Data Section Offset: 0x{DataSectionOffset:X}");
            stats.AppendLine($"Unknown Header Value: 0x{UnknownHeaderValue:X}");
            stats.AppendLine();
            stats.AppendLine("Files by Extension:");

            foreach (var kvp in GroupedEntries.OrderByDescending(g => g.Value.Count))
            {
                long totalSize = kvp.Value.Sum(e => (long)e.OriginalSize);
                stats.AppendLine($"  {kvp.Key}: {kvp.Value.Count} files ({totalSize / 1024.0:F2} KB)");
            }

            return stats.ToString();
        }
    }
}
