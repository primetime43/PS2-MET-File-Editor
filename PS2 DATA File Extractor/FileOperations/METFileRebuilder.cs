using PS2_DATA_File_Extractor.Models;
using System.Text;

namespace PS2_DATA_File_Extractor.FileOperations
{
    /// <summary>
    /// Rebuilds MET archives while preserving their original directory and sector layout.
    /// </summary>
    public static class METFileRebuilder
    {
        public const int SectorSize = 2048;

        /// <summary>
        /// Writes a rebuilt archive containing a larger replacement entry.
        /// The destination must be different from the source.
        /// </summary>
        public static void RebuildWithExpandedEntry(
            string sourcePath,
            string destinationPath,
            FileEntry requestedEntry,
            byte[] replacementData)
        {
            ArgumentNullException.ThrowIfNull(requestedEntry);
            ArgumentNullException.ThrowIfNull(replacementData);

            if (Path.GetFullPath(sourcePath).Equals(Path.GetFullPath(destinationPath), StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("The rebuild destination must be different from the source archive.", nameof(destinationPath));
            }

            METFileStructure structure = METFileReader.ReadMETFile(sourcePath);
            List<FileEntry> entries = structure.AllEntries.OrderBy(entry => entry.Offset).ToList();
            int targetIndex = entries.FindIndex(entry =>
                entry.HeaderStart == requestedEntry.HeaderStart && entry.Path == requestedEntry.Path);

            if (targetIndex < 0)
            {
                throw new InvalidDataException("Could not find the target entry in the MET archive.");
            }

            FileEntry target = entries[targetIndex];
            if (replacementData.Length <= target.OriginalSize)
            {
                throw new ArgumentException("The replacement must be larger than the original entry.", nameof(replacementData));
            }

            using FileStream source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            bool hasFollowingEntry = targetIndex + 1 < entries.Count;
            long oldFollowingOffset = hasFollowingEntry ? entries[targetIndex + 1].Offset : source.Length;
            long replacementEnd = checked((long)target.Offset + replacementData.Length);
            long newFollowingOffset = hasFollowingEntry
                ? Math.Max(oldFollowingOffset, AlignUp(replacementEnd, SectorSize))
                : replacementEnd;
            long offsetShift = newFollowingOffset - oldFollowingOffset;

            using FileStream destination = new FileStream(destinationPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None);

            // Copy the global header and preserve all directory bytes before patching changed fields.
            CopyExactly(source, destination, structure.DataSectionOffset);

            // Preserve every byte before the replacement entry.
            CopyExactly(source, destination, target.Offset - structure.DataSectionOffset);
            destination.Write(replacementData);

            if (hasFollowingEntry)
            {
                WriteZeroPadding(destination, newFollowingOffset - destination.Position);
                source.Position = oldFollowingOffset;
                source.CopyTo(destination);
            }

            long newDataSectionSize = destination.Length - structure.DataSectionOffset;
            using BinaryWriter writer = new BinaryWriter(destination, Encoding.UTF8, leaveOpen: true);

            destination.Position = 4;
            writer.Write(checked((int)newDataSectionSize));

            destination.Position = target.HeaderStart + sizeof(int);
            writer.Write(replacementData.Length);

            for (int i = targetIndex + 1; i < entries.Count; i++)
            {
                destination.Position = entries[i].HeaderStart;
                writer.Write(checked(entries[i].Offset + (int)offsetShift));
            }

            writer.Flush();
        }

        private static long AlignUp(long value, int alignment)
        {
            return checked(((value + alignment - 1) / alignment) * alignment);
        }

        private static void CopyExactly(Stream source, Stream destination, long byteCount)
        {
            byte[] buffer = new byte[81920];
            long remaining = byteCount;

            while (remaining > 0)
            {
                int bytesRead = source.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
                if (bytesRead == 0)
                {
                    throw new EndOfStreamException("The MET archive ended before the expected offset.");
                }

                destination.Write(buffer, 0, bytesRead);
                remaining -= bytesRead;
            }
        }

        private static void WriteZeroPadding(Stream destination, long byteCount)
        {
            if (byteCount < 0)
            {
                throw new InvalidDataException("The replacement overlaps the following MET entry.");
            }

            byte[] zeros = new byte[SectorSize];
            long remaining = byteCount;
            while (remaining > 0)
            {
                int count = (int)Math.Min(zeros.Length, remaining);
                destination.Write(zeros, 0, count);
                remaining -= count;
            }
        }
    }
}
