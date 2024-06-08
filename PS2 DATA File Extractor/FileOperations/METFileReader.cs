using PS2_DATA_File_Extractor.Models;
using System.Text;

namespace PS2_DATA_File_Extractor.FileOperations
{
    public class METFileReader
    {
        public static Dictionary<string, List<FileEntry>> ReadFileEntries(string dataMetPath, Dictionary<string, List<FileEntry>> groupedEntries)
        {
            using (FileStream fs = new FileStream(dataMetPath, FileMode.Open, FileAccess.Read))
            using (BinaryReader reader = new BinaryReader(fs))
            {
                long fileSize = fs.Length;

                // Skip the first 8 bytes
                fs.Seek(8, SeekOrigin.Begin);

                while (fs.Position < fileSize)
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

                        if (strLength == 0) // Reached separator or end of entries
                        {
                            break;
                        }

                        // Ensure there are enough bytes left for the path
                        if (fs.Position + strLength > fileSize)
                        {
                            break;
                        }

                        // Read the path (N bytes)
                        byte[] pathBytes = reader.ReadBytes(strLength);
                        string path = Encoding.ASCII.GetString(pathBytes).Trim('\0');

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
                        if (!groupedEntries.ContainsKey(extension))
                        {
                            groupedEntries[extension] = new List<FileEntry>();
                        }
                        groupedEntries[extension].Add(entry);

                        // Move to the next entry
                        fs.Seek(headerEndPosition, SeekOrigin.Begin);
                    }
                    catch (EndOfStreamException)
                    {
                        break;
                    }
                    catch (IOException ex)
                    {
                        break;
                    }
                }
            }
            return groupedEntries;
        }

        private static int ReadInt32LE(BinaryReader reader)
        {
            byte[] bytes = reader.ReadBytes(4);
            if (BitConverter.IsLittleEndian)
                return BitConverter.ToInt32(bytes, 0);
            else
                Array.Reverse(bytes);
            return BitConverter.ToInt32(bytes, 0);
        }
    }
}
