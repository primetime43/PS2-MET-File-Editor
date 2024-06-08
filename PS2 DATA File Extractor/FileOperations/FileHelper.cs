using PS2_DATA_File_Extractor.Models;
using System.Text;

namespace PS2_DATA_File_Extractor.FileOperations
{
    public class FileHelper
    {
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

        public static void SaveSelectedFileLocally(string dataMetPath, FileEntry entry, string destinationPath)
        {
            using (FileStream fs = new FileStream(dataMetPath, FileMode.Open, FileAccess.Read))
            using (BinaryReader reader = new BinaryReader(fs))
            {
                fs.Seek(entry.Offset, SeekOrigin.Begin);
                byte[] data = reader.ReadBytes(entry.OriginalSize);

                using (FileStream destFs = new FileStream(destinationPath, FileMode.Create, FileAccess.Write))
                {
                    destFs.Write(data, 0, data.Length);
                }

                MessageBox.Show($"File saved successfully to {destinationPath}", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        public static void SaveSelectedFileDialog(string dataMetPath, FileEntry entry)
        {
            SaveFileDialog saveFileDialog = new SaveFileDialog();
            // Replace / with - in the file name
            saveFileDialog.FileName = entry.Path.Replace('/', '-');

            // Set the filter based on the file extension
            string extension = Path.GetExtension(entry.Path).ToLower();
            if (!string.IsNullOrEmpty(extension))
            {
                saveFileDialog.Filter = $"{extension.ToUpper().TrimStart('.')} files (*{extension})|*{extension}|All files (*.*)|*.*";
            }
            else
            {
                saveFileDialog.Filter = "All files (*.*)|*.*";
            }

            if (saveFileDialog.ShowDialog() == DialogResult.OK)
            {
                SaveSelectedFileLocally(dataMetPath, entry, saveFileDialog.FileName);
            }
        }
    }
}
