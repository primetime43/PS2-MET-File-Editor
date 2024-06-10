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
        /// Saves changes to a file entry within a MET file.
        /// </summary>
        /// <param name="dataMetPath">The path to the data.met file.</param>
        /// <param name="entry">The file entry to save changes to.</param>
        /// <param name="content">The new content to write to the file entry.</param>
        /// <returns>True if the changes were saved successfully, false otherwise.</returns>
        public static bool SaveFileEntryChanges(string dataMetPath, FileEntry entry, string content)
        {
            using (FileStream fs = new FileStream(dataMetPath, FileMode.Open, FileAccess.ReadWrite))
            using (BinaryWriter writer = new BinaryWriter(fs))
            {
                fs.Seek(entry.Offset, SeekOrigin.Begin);
                byte[] data = Encoding.ASCII.GetBytes(content);

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
    }
}
