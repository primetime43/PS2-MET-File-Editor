using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace PS2_DATA_File_Extractor
{
    public partial class Form1 : Form
    {
        private Dictionary<string, List<FileEntry>> groupedEntries = new Dictionary<string, List<FileEntry>>();
        private string _dataMetPath;
        private ImageViewer _imageViewer; // Single instance of ImageViewer
        private TreeNode _lastSelectedNode;

        public Form1()
        {
            InitializeComponent();
            // Make sure to hook up the BeforeExpand and AfterSelect events
            treeView1.BeforeExpand += treeView1_BeforeExpand;
            treeView1.AfterSelect += treeView1_AfterSelect;

            // Initialize the ImageViewer form
            //_imageViewer = new ImageViewer();
        }

        private void button1_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog openFileDialog = new OpenFileDialog())
            {
                openFileDialog.Filter = "MET files (*.met)|*.met|All files (*.*)|*.*";
                openFileDialog.Title = "Open data.met file";

                if (openFileDialog.ShowDialog() == DialogResult.OK)
                {
                    _dataMetPath = openFileDialog.FileName;
                    try
                    {
                        ReadFileEntries(_dataMetPath);
                        PopulateTreeView();
                        MessageBox.Show($"Successfully read {_dataMetPath}.");
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"An error occurred: {ex.Message}");
                    }
                }
            }
        }

        private void ReadFileEntries(string dataMetPath)
        {
            groupedEntries.Clear();

            using (FileStream fs = new FileStream(dataMetPath, FileMode.Open, FileAccess.Read))
            using (BinaryReader reader = new BinaryReader(fs))
            {
                // Read the initial header (assuming 16 bytes based on provided data)
                long initialHeaderSize = 16;
                byte[] initialHeader = reader.ReadBytes((int)initialHeaderSize);

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
                            break;
                        }

                        // Read string length (4 bytes, little-endian)
                        int strLength = ReadInt32LE(reader);

                        if (strLength == 0) // Reached separator or end of entries
                        {
                            break;
                        }

                        // Ensure there are enough bytes left for the path and additional fields
                        if (fs.Position + strLength + 4 + 4 > fileSize)
                        {
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

                        // Create a new FileEntry object and add it to the list
                        FileEntry entry = new FileEntry
                        {
                            HeaderStart = entryStart,
                            HeaderEnd = headerEndPosition - 1,
                            StringLength = strLength,
                            Path = path,
                            Offset = dataOffset,
                            Size = dataSize
                        };

                        string extension = Path.GetExtension(path);
                        if (!groupedEntries.ContainsKey(extension))
                        {
                            groupedEntries[extension] = new List<FileEntry>();
                        }
                        groupedEntries[extension].Add(entry);

                        // Calculate the next entry start position
                        long nextEntryPosition = headerEndPosition; // This is where the next entry starts

                        if (nextEntryPosition >= fileSize)
                        {
                            break;
                        }

                        // Move to the next entry
                        fs.Seek(nextEntryPosition, SeekOrigin.Begin);
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
        }

        private void PopulateTreeView()
        {
            treeView1.Nodes.Clear();
            foreach (var group in groupedEntries)
            {
                TreeNode extensionNode = new TreeNode(group.Key)
                {
                    Tag = group.Value, // Store the list of FileEntry objects in the Tag property
                    Nodes = { new TreeNode("Loading...") } // Add a dummy node for lazy loading
                };
                treeView1.Nodes.Add(extensionNode);
            }
        }

        private void treeView1_BeforeExpand(object sender, TreeViewCancelEventArgs e)
        {
            if (e.Node.Tag is List<FileEntry> entries)
            {
                e.Node.Nodes.Clear(); // Clear the dummy loading node
                foreach (var entry in entries)
                {
                    TreeNode entryNode = new TreeNode(entry.Path)
                    {
                        Tag = entry
                    };
                    e.Node.Nodes.Add(entryNode);
                }
            }
        }

        private void treeView1_AfterSelect(object sender, TreeViewEventArgs e)
        {
            if (e.Node.Tag is FileEntry entry)
            {
                _lastSelectedNode = e.Node; // Store the selected node
                DisplayEntryInfo(entry);
            }
        }

        private void DisplayEntryInfo(FileEntry entry)
        {
            richTextBox1.Clear();
            richTextBox1.AppendText($"Header starts at address: {entry.HeaderStart} (0x{entry.HeaderStart:X})\n");
            richTextBox1.AppendText($"Header ends at address: {entry.HeaderEnd} (0x{entry.HeaderEnd:X})\n");
            long headerLength = entry.HeaderEnd - entry.HeaderStart + 1; // +1 to include the end byte
            richTextBox1.AppendText($"Length of the header: {headerLength} (0x{headerLength:X})\n");
            richTextBox1.AppendText($"Length of the string: {entry.StringLength} (0x{entry.StringLength:X})\n");
            richTextBox1.AppendText($"Path: {entry.Path}\n");
            richTextBox1.AppendText($"Offset: {entry.Offset} (0x{entry.Offset:X})\n");
            richTextBox1.AppendText($"Size: {entry.Size} (0x{entry.Size:X})\n");
            richTextBox1.AppendText($"Data spans from 0x{entry.Offset:X} to 0x{(entry.Offset + entry.Size):X}\n");

            // Load and display the data in richTextBox2
            LoadData(entry);
        }

        private void LoadData(FileEntry entry)
        {
            using (FileStream fs = new FileStream(_dataMetPath, FileMode.Open, FileAccess.Read))
            using (BinaryReader reader = new BinaryReader(fs))
            {
                fs.Seek(entry.Offset, SeekOrigin.Begin);
                byte[] data = reader.ReadBytes(entry.Size);

                string extension = Path.GetExtension(entry.Path).ToLower();
                if (extension == ".png" || extension == ".bmp" || extension == ".ico" || extension == ".mnd")
                {
                    try
                    {
                        using (MemoryStream ms = new MemoryStream(data))
                        {
                            Image image = Image.FromStream(ms);
                            ShowImageInPictureBox(image);
                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"An error occurred while trying to display the image: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
                else
                {
                    string dataText = Encoding.ASCII.GetString(data);
                    richTextBox2.Text = dataText;
                }
            }
        }

        private void ShowImageInPictureBox(Image image)
        {
            try
            {
                pictureBox1.Image = image;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"An error occurred while trying to display the image: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            /*try
            {
                _imageViewer = new ImageViewer();
                _imageViewer.SetImage(image);
                if (!_imageViewer.Visible)
                {
                    _imageViewer.Show();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"An error occurred while trying to display the image: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }*/
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

        private class FileEntry
        {
            public long HeaderStart { get; set; }
            public long HeaderEnd { get; set; }
            public int StringLength { get; set; }
            public string Path { get; set; }
            public int Offset { get; set; }
            public int Size { get; set; }
        }

        private void richTextBox2_Enter(object sender, EventArgs e)
        {
            if (_lastSelectedNode != null)
            {
                treeView1.SelectedNode = _lastSelectedNode;
                _lastSelectedNode.EnsureVisible(); // Ensure the selected node is visible
            }
        }
    }
}
