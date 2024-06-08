using PS2_DATA_File_Extractor.Models;
using PS2_DATA_File_Extractor.FileOperations;
using System.Text;
using ICSharpCode.TextEditor;

namespace PS2_DATA_File_Extractor
{
    public partial class Form1 : Form
    {
        private Dictionary<string, List<FileEntry>> groupedEntries = new Dictionary<string, List<FileEntry>>();
        private string _dataMetPath;
        private ImageViewer _imageViewer; // Single instance of ImageViewer
        private bool _hasUnsavedChanges = false;
        private FileEntry _selectedEntry;

        public Form1()
        {
            InitializeComponent();
            // Make sure to hook up the BeforeExpand and AfterSelect events
            treeView1.BeforeExpand += treeView1_BeforeExpand;
            treeView1.AfterSelect += treeView1_AfterSelect;

            textEditorControl1.SetHighlighting("XML");

            // Initialize the ImageViewer form
            //_imageViewer = new ImageViewer();
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

        private void filesTreeView_BeforeSelect(object sender, TreeViewCancelEventArgs e)
        {
            if (_hasUnsavedChanges)
            {
                DialogResult result = MessageBox.Show("You have unsaved changes. Do you want to save them?", "Unsaved Changes", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (result == DialogResult.Yes)
                {
                    saveFileChangesToolStripMenuItem_Click(sender, e);
                }
                else if (result == DialogResult.Cancel)
                {
                    e.Cancel = true;
                }
            }
        }

        private void textEditorControl1_TextChanged(object sender, EventArgs e)
        {
            if (_selectedEntry != null)
            {
                int selectedEntryCurrentSize = textEditorControl1.Text.Length;
                currentFileSizeToolStripMenuItem.ForeColor = selectedEntryCurrentSize > _selectedEntry.OriginalSize ? Color.Red : Color.Black;
                currentFileSizeToolStripMenuItem.Text = $"Current OriginalSize: 0x{selectedEntryCurrentSize:X} (hex)";
                _hasUnsavedChanges = true;
            }
        }

        private void treeView1_AfterSelect(object sender, TreeViewEventArgs e)
        {
            if (e.Node.Tag is FileEntry entry)
            {
                _selectedEntry = entry; // Store the selected entry

                DisplayEntryInfo(entry);

                // move this eventually
                maxFileSizeToolStripMenuItem.Text = $"Max OriginalSize: 0x{entry.OriginalSize:X} (hex)";
                maxFileSizeToolStripMenuItem.Visible = true;

                currentFileSizeToolStripMenuItem.Text = $"Current OriginalSize: 0x{_selectedEntry.CurrentSize:X} (hex)";
                currentFileSizeToolStripMenuItem.Visible = true;

                _hasUnsavedChanges = false; // Reset the flag after loading new content
            }
        }

        private void DisplayEntryInfo(FileEntry entry)
        {
            richTextBox1.Clear();
            richTextBox1.AppendText($"Header starts at address: {entry.HeaderStart} (0x{entry.HeaderStart:X})\n");
            richTextBox1.AppendText($"Header ends at address: {entry.HeaderEnd} (0x{entry.HeaderEnd:X})\n");
            long headerLength = entry.HeaderEnd - entry.HeaderStart;
            richTextBox1.AppendText($"Length of the header: {headerLength} (0x{headerLength:X})\n");
            richTextBox1.AppendText($"Length of the string: {entry.StringLength} (0x{entry.StringLength:X})\n");
            richTextBox1.AppendText($"Path: {entry.Path}\n");
            richTextBox1.AppendText($"Offset: {entry.Offset} (0x{entry.Offset:X})\n");
            richTextBox1.AppendText($"OriginalSize: {entry.OriginalSize} (0x{entry.OriginalSize:X})\n");
            richTextBox1.AppendText($"Data spans from 0x{entry.Offset:X} to 0x{(entry.Offset + entry.OriginalSize):X}\n");

            // Load and display the data in richTextBox2
            LoadData(entry);
        }

        private void LoadData(FileEntry entry)
        {
            using (FileStream fs = new FileStream(_dataMetPath, FileMode.Open, FileAccess.Read))
            using (BinaryReader reader = new BinaryReader(fs))
            {
                fs.Seek(entry.Offset, SeekOrigin.Begin);
                byte[] data = reader.ReadBytes(entry.OriginalSize);
                data = RemoveZeroPadding(data); // Remove padding

                string extension = Path.GetExtension(entry.Path).ToLower();
                if (extension == ".png" || extension == ".bmp" || extension == ".ico" || extension == ".mnd")
                {
                    try
                    {
                        using (MemoryStream ms = new MemoryStream(data))
                        {
                            Image image = Image.FromStream(ms);
                            ShowImageInPictureBox(image);
                            textEditorControl1.Enabled = false;
                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"An error occurred while trying to display the image: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
                else
                {
                    textEditorControl1.Enabled = true;
                    string dataText = Encoding.ASCII.GetString(data);

                    // Clear the text editor before setting new text
                    textEditorControl1.Text = string.Empty;
                    textEditorControl1.Refresh();

                    // Set the new text
                    textEditorControl1.Text = dataText;

                    // Force the UI to update
                    textEditorControl1.Refresh();

                    _selectedEntry.CurrentSize = data.Length;
                }
            }
        }

        private byte[] RemoveZeroPadding(byte[] data)
        {
            int i = data.Length - 1;
            while (i >= 0 && data[i] == 0x00)
            {
                i--;
            }
            byte[] unpaddedData = new byte[i + 1];
            Array.Copy(data, unpaddedData, i + 1);
            return unpaddedData;
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
        }

        private void openmetFileToolStripMenuItem_Click(object sender, EventArgs e)
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
                        groupedEntries.Clear();
                        groupedEntries = METFileReader.ReadFileEntries(_dataMetPath, groupedEntries);
                        PopulateTreeView();
                        //MessageBox.Show($"Successfully read {_dataMetPath}.");
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"An error occurred: {ex.Message}");
                    }
                }
            }
        }

        private void exportFileToPCToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (_selectedEntry != null)
            {
                FileHelper.SaveSelectedFileDialog(_dataMetPath, _selectedEntry);
            }
            else
            {
                MessageBox.Show("No file selected.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void saveFileChangesToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (_selectedEntry != null)
            {
                string content = textEditorControl1.Text;
                if (FileHelper.SaveFileEntryChanges(_dataMetPath, _selectedEntry, content))
                {
                    string listViewItemName = Path.GetFileName(_selectedEntry.Path);
                    string fileName = Path.GetFileName(_dataMetPath);
                    MessageBox.Show($"'{listViewItemName}' changes successfully saved to '{fileName}'.", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    _hasUnsavedChanges = false;
                }
            }
            else
            {
                MessageBox.Show("No file selected.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void importFileToolStripMenuItem_Click(object sender, EventArgs e)
        {
            _hasUnsavedChanges = false;
            using (OpenFileDialog openFileDialog = new OpenFileDialog())
            {
                openFileDialog.Title = "Import file";
                if (openFileDialog.ShowDialog() == DialogResult.OK)
                {
                    string filePath = openFileDialog.FileName;
                    string importedFileName = Path.GetFileName(filePath);
                    string searchFileName = importedFileName.Replace('-', '/'); // Replace - with / in the file name
                    bool fileFound = false;
                    bool overwriteSuccess = false;
                    TreeNode? matchedNode = null;

                    string fileExtension = Path.GetExtension(searchFileName).ToLower();

                    // Iterate through the tree nodes to find the matching file extension node
                    foreach (TreeNode extensionNode in treeView1.Nodes)
                    {
                        if (extensionNode.Text.ToLower() == fileExtension)
                        {
                            // Expand the extension node to load its children
                            extensionNode.Expand();

                            // Iterate through the child nodes of the extension node to find the matching file name
                            foreach (TreeNode childNode in extensionNode.Nodes)
                            {
                                var result = FindAndReplaceFile(childNode, searchFileName, filePath);
                                fileFound = result.Item1;
                                overwriteSuccess = result.Item2;
                                matchedNode = result.Item3;

                                if (fileFound) break;
                            }

                            if (fileFound) break;
                        }
                    }

                    if (fileFound && overwriteSuccess)
                    {
                        // Update the selected TreeView item
                        treeView1.SelectedNode = matchedNode;
                        MessageBox.Show($"'{searchFileName}' was successfully imported and overwritten.", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    else if (fileFound && !overwriteSuccess)
                    {
                        MessageBox.Show("The imported file is too large to fit in the existing space or the overwrite was canceled.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                    else
                    {
                        MessageBox.Show("No matching file found in the tree.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
        }

        private Tuple<bool, bool, TreeNode> FindAndReplaceFile(TreeNode node, string importedFileName, string filePath)
        {
            if (node.Tag is FileEntry entry && node.Text == importedFileName)
            {
                byte[] data = File.ReadAllBytes(filePath);
                data = RemoveZeroPadding(data); // Remove padding

                // Check if the imported file size fits in the allocated space
                if (data.Length > entry.OriginalSize)
                {
                    MessageBox.Show("The imported file is too large to fit in the existing space.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return new Tuple<bool, bool, TreeNode>(true, false, null); // File found but too large to overwrite
                }

                // Ask for confirmation before overwriting
                DialogResult result = MessageBox.Show(
                    $"Are you sure you want to overwrite '{importedFileName}'?",
                    "Confirm Overwrite",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning
                );

                if (result == DialogResult.Yes)
                {
                    // Overwrite the existing data
                    FileHelper.SaveFileEntryChanges(_dataMetPath, entry, Encoding.ASCII.GetString(data));
                    return new Tuple<bool, bool, TreeNode>(true, true, node); // File found and successfully overwritten
                }
                else
                {
                    return new Tuple<bool, bool, TreeNode>(true, false, null); // File found but overwrite was canceled
                }
            }

            // Recursively search in child nodes
            foreach (TreeNode childNode in node.Nodes)
            {
                var result = FindAndReplaceFile(childNode, importedFileName, filePath);
                if (result.Item1)
                {
                    return result;
                }
            }

            return new Tuple<bool, bool, TreeNode>(false, false, null); // File not found
        }

        private void exitToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Application.Exit();
        }
    }
}
