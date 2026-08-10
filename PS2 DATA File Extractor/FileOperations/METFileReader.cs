using PS2_DATA_File_Extractor.Models;
using System.Text;

namespace PS2_DATA_File_Extractor.FileOperations
{
    public class METFileReader
    {
        public static METFileStructure ReadMETFile(string dataMetPath)
        {
            var structure = new METFileStructure
            {
                FilePath = dataMetPath,
                GroupedEntries = new Dictionary<string, List<FileEntry>>()
            };

            using (FileStream fs = new FileStream(dataMetPath, FileMode.Open, FileAccess.Read))
            using (BinaryReader reader = new BinaryReader(fs))
            {
                structure.TotalFileSize = fs.Length;
                if (structure.TotalFileSize < 8)
                {
                    throw new InvalidDataException("The MET file is too small to contain its 8-byte header.");
                }

                // Read the 8-byte MET header
                // Bytes 0-3: Offset where data section begins (tells us where headers end)
                structure.DataSectionOffset = ReadInt32LE(reader);

                // Bytes 4-7: Total number of bytes in the data section
                structure.DataSectionSize = ReadInt32LE(reader);

                if (structure.DataSectionOffset < 8 || structure.DataSectionOffset > structure.TotalFileSize)
                {
                    throw new InvalidDataException($"Invalid MET data section offset: 0x{structure.DataSectionOffset:X}.");
                }

                // File entries start at byte 8 and continue until dataSectionOffset
                // This is structure-based reading instead of pattern matching
                while (fs.Position < structure.DataSectionOffset && fs.Position < structure.TotalFileSize)
                {
                    try
                    {
                        long entryStart = fs.Position;

                        // Read the offset where the data starts (4 bytes, little-endian)
                        int dataOffset = ReadInt32LE(reader);

                        // Read the size of the data (4 bytes, little-endian)
                        int dataSize = ReadInt32LE(reader);

                        // Read the string length (4 bytes, little-endian)
                        int strLength = ReadInt32LE(reader);

                        // Validate string length
                        if (strLength <= 0 || strLength > byte.MaxValue) // The game reads only the low byte.
                        {
                            break;
                        }

                        // Ensure there are enough bytes left for the path
                        if (fs.Position + strLength > structure.DataSectionOffset)
                        {
                            break;
                        }

                        // Read the path (N bytes)
                        byte[] pathBytes = reader.ReadBytes(strLength);
                        string path = Encoding.UTF8.GetString(pathBytes).Trim('\0');

                        // Log the end of the current header
                        long headerEndPosition = fs.Position;

                        // Create a new FileEntry object and add it to the list
                        FileEntry entry = new FileEntry
                        {
                            HeaderStart = entryStart,
                            HeaderEnd = headerEndPosition,
                            StringLength = strLength,
                            Path = path,
                            Offset = dataOffset,
                            OriginalSize = dataSize,
                            CurrentSize = dataSize
                        };

                        string extension = Path.GetExtension(path);
                        if (!structure.GroupedEntries.ContainsKey(extension))
                        {
                            structure.GroupedEntries[extension] = new List<FileEntry>();
                        }
                        structure.GroupedEntries[extension].Add(entry);

                        // Move to the next entry
                        fs.Seek(headerEndPosition, SeekOrigin.Begin);
                    }
                    catch (EndOfStreamException)
                    {
                        break;
                    }
                    catch (IOException)
                    {
                        break;
                    }
                }
            }
            return structure;
        }

        /// <summary>
        /// Legacy method for backwards compatibility.
        /// </summary>
        public static Dictionary<string, List<FileEntry>> ReadFileEntries(string dataMetPath, Dictionary<string, List<FileEntry>> groupedEntries)
        {
            var structure = ReadMETFile(dataMetPath);
            return structure.GroupedEntries;
        }

        private static int ReadInt32LE(BinaryReader reader)
        {
            byte[] bytes = reader.ReadBytes(4);
            if (bytes.Length != 4)
            {
                throw new EndOfStreamException("Unexpected end of MET file while reading a 32-bit value.");
            }

            if (BitConverter.IsLittleEndian)
                return BitConverter.ToInt32(bytes, 0);
            else
                Array.Reverse(bytes);
            return BitConverter.ToInt32(bytes, 0);
        }
    }
}
