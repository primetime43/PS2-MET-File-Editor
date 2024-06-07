using System;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace PS2_DATA_File_Extractor
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();
        }

        private void button1_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog openFileDialog = new OpenFileDialog())
            {
                openFileDialog.Filter = "MET files (*.met)|*.met|All files (*.*)|*.*";
                openFileDialog.Title = "Open data.met file";

                if (openFileDialog.ShowDialog() == DialogResult.OK)
                {
                    string dataMetPath = openFileDialog.FileName;
                    try
                    {
                        DisplayFileInfo(dataMetPath, "output.txt");
                        MessageBox.Show($"Successfully read {dataMetPath}. Output written to output.txt.");
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"An error occurred: {ex.Message}");
                    }
                }
            }
        }

        public static void DisplayFileInfo(string dataMetPath, string outputFilePath)
        {
            using (FileStream fs = new FileStream(dataMetPath, FileMode.Open, FileAccess.Read))
            using (BinaryReader reader = new BinaryReader(fs))
            using (StreamWriter writer = new StreamWriter(outputFilePath))
            {
                // Log the start of the file reading process
                writer.WriteLine("Starting to read the file...\n");

                // Read the initial header (assuming 16 bytes based on provided data)
                long initialHeaderSize = 16;
                byte[] initialHeader = reader.ReadBytes((int)initialHeaderSize);
                writer.WriteLine($"Initial header: {BitConverter.ToString(initialHeader)}\n");

                long fileSize = fs.Length;

                // Ensure we don't go beyond the file size
                while (fs.Position < fileSize)
                {
                    try
                    {
                        long entryStart = fs.Position;

                        // Ensure there are enough bytes left for a complete entry
                        if (fs.Position + 4 > fileSize)
                        {
                            writer.WriteLine("Reached end of file before reading complete header.\n");
                            break;
                        }

                        // Read string length (4 bytes, little-endian)
                        int strLength = ReadInt32LE(reader);
                        //writer.WriteLine($"String length: {strLength} (0x{strLength:X})");

                        if (strLength == 0) // Reached separator or end of entries
                        {
                            writer.WriteLine("Reached separator or end of entries.\n");
                            break;
                        }

                        // Ensure there are enough bytes left for the path and additional fields
                        if (fs.Position + strLength + 4 + 4 > fileSize)
                        {
                            writer.WriteLine("Reached end of file before reading complete entry.\n");
                            break;
                        }

                        // Read path (N bytes)
                        byte[] pathBytes = reader.ReadBytes(strLength);
                        string path = Encoding.ASCII.GetString(pathBytes).Trim('\0');

                        // Read offset (4 bytes, little-endian)
                        int dataOffset = ReadInt32LE(reader);

                        // Read size (4 bytes, little-endian)
                        int dataSize = ReadInt32LE(reader);

                        // Log the end of the current header
                        long headerEndPosition = fs.Position;

                        int headerTotalSize = (int)(headerEndPosition - entryStart);

                        // Output the information
                        writer.WriteLine($"Header starts at address: {entryStart} (0x{entryStart:X})");
                        writer.WriteLine($"Header ends at address: {headerEndPosition-1} (0x{headerEndPosition-1:X})");
                        writer.WriteLine($"Length of the header (End - Start Position): {headerTotalSize} (0x{headerTotalSize:X})");
                        writer.WriteLine($"Path (file name): {path}");
                        writer.WriteLine($"Length of the string (file name): {strLength} (0x{strLength:X})");
                        writer.WriteLine($"Offset where data starts: {dataOffset} (0x{dataOffset:X})");
                        writer.WriteLine($"Size: {dataSize} (0x{dataSize:X})\n");

                        // Calculate the next entry start position
                        long nextEntryPosition = headerEndPosition; // This is where the next entry starts

                        // Verify the next entry position
                        //writer.WriteLine($"Next entry position: {nextEntryPosition} (0x{nextEntryPosition:X})");
                        if (nextEntryPosition >= fileSize)
                        {
                            writer.WriteLine("Next entry position exceeds file size.\n");
                            break;
                        }

                        // Move to the next entry
                        fs.Seek(nextEntryPosition, SeekOrigin.Begin);
                    }
                    catch (EndOfStreamException)
                    {
                        writer.WriteLine("Unexpected end of file encountered.\n");
                        break;
                    }
                    catch (IOException ex)
                    {
                        writer.WriteLine($"I/O error occurred: {ex.Message}\n");
                        break;
                    }
                }
            }
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

        private static void AppendText(StreamWriter writer, string text)
        {
            writer.WriteLine(text);
        }
    }
}
