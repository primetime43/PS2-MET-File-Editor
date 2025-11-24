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
        private bool _hasUnsavedChanges = false;
        private FileEntry _selectedEntry;

        public Form1()
        {
            InitializeComponent();
            // Make sure to hook up the BeforeExpand and AfterSelect events
            treeView1.BeforeExpand += treeView1_BeforeExpand;
            treeView1.AfterSelect += treeView1_AfterSelect;

            textEditorControl1.SetHighlighting("XML");
        }

        /// <summary>
        /// Updates the UI to reflect the current save state.
        /// </summary>
        private void UpdateUIState()
        {
            // Update window title
            string baseTitle = "PS2 MET File Editor";
            if (!string.IsNullOrEmpty(_dataMetPath))
            {
                string fileName = Path.GetFileName(_dataMetPath);
                if (_hasUnsavedChanges)
                {
                    this.Text = $"{baseTitle} - {fileName} *";
                }
                else
                {
                    this.Text = $"{baseTitle} - {fileName}";
                }
            }
            else
            {
                this.Text = baseTitle;
            }

            // Update status bar
            if (_hasUnsavedChanges)
            {
                statusLabel.Text = "Unsaved changes in editor";
            }
            else if (!string.IsNullOrEmpty(_dataMetPath))
            {
                statusLabel.Text = $"Ready - {Path.GetFileName(_dataMetPath)}";
            }
            else
            {
                statusLabel.Text = "Ready";
            }

            // Update Save menu item text
            if (_hasUnsavedChanges)
            {
                saveFileChangesToolStripMenuItem.Text = "Save File Changes *";
            }
            else
            {
                saveFileChangesToolStripMenuItem.Text = "Save File Changes";
            }
        }

        /// <summary>
        /// Sets a temporary status message that will be displayed briefly.
        /// </summary>
        private void SetStatusMessage(string message, int durationMs = 3000)
        {
            statusLabel.Text = message;

            // Use a timer to reset the message after the duration
            var timer = new System.Windows.Forms.Timer();
            timer.Interval = durationMs;
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                timer.Dispose();
                UpdateUIState(); // Reset to normal state
            };
            timer.Start();
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

                // Build hierarchical structure
                foreach (var entry in entries)
                {
                    BuildHierarchicalNode(e.Node, entry);
                }
            }
        }

        /// <summary>
        /// Builds a hierarchical tree structure for a file entry, creating folder nodes as needed.
        /// </summary>
        private void BuildHierarchicalNode(TreeNode parentNode, FileEntry entry)
        {
            // Split the path into parts (e.g., "data/batting/abne/file.png" -> ["data", "batting", "abne", "file.png"])
            string[] pathParts = entry.Path.Split('/');

            // Start from index 1 to skip "data" (as requested)
            TreeNode currentNode = parentNode;
            for (int i = 1; i < pathParts.Length; i++)
            {
                string part = pathParts[i];
                bool isLastPart = (i == pathParts.Length - 1);

                // Try to find existing node with this name
                TreeNode? existingNode = null;
                foreach (TreeNode child in currentNode.Nodes)
                {
                    if (child.Text == part)
                    {
                        existingNode = child;
                        break;
                    }
                }

                if (existingNode != null)
                {
                    // Node already exists, use it
                    currentNode = existingNode;
                }
                else
                {
                    // Create new node
                    TreeNode newNode = new TreeNode(part);

                    // Only attach FileEntry tag to leaf nodes (actual files)
                    if (isLastPart)
                    {
                        newNode.Tag = entry;
                    }

                    currentNode.Nodes.Add(newNode);
                    currentNode = newNode;
                }
            }
        }

        private void textEditorControl1_TextChanged(object sender, EventArgs e)
        {
            if (_selectedEntry != null)
            {
                int selectedEntryCurrentSize = Encoding.ASCII.GetByteCount(textEditorControl1.Text);
                currentFileSizeToolStripMenuItem.ForeColor = selectedEntryCurrentSize > _selectedEntry.OriginalSize ? Color.Red : Color.Black;
                currentFileSizeToolStripMenuItem.Text = $"Current Size: 0x{selectedEntryCurrentSize:X} (hex)";
                _hasUnsavedChanges = true;
                UpdateUIState();
            }
        }

        private void treeView1_AfterSelect(object sender, TreeViewEventArgs e)
        {
            if (e.Node.Tag is FileEntry entry)
            {
                _selectedEntry = entry; // Store the selected entry

                DisplayEntryInfo(entry);

                // move this eventually
                maxFileSizeToolStripMenuItem.Text = $"Max Size: 0x{entry.OriginalSize:X} (hex)";
                maxFileSizeToolStripMenuItem.Visible = true;

                currentFileSizeToolStripMenuItem.Text = $"Current Size: 0x{_selectedEntry.CurrentSize:X} (hex)";
                currentFileSizeToolStripMenuItem.Visible = true;

                _hasUnsavedChanges = false; // Reset the flag after loading new content
                UpdateUIState();
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
                        _selectedEntry.CurrentSize = data.Length;
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"An error occurred while trying to display the image: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
                else
                {
                    textEditorControl1.Enabled = true;
                    data = RemoveZeroPadding(data); // Remove padding only for text files
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
                        _hasUnsavedChanges = false;
                        UpdateUIState();
                        SetStatusMessage($"Opened {Path.GetFileName(_dataMetPath)} successfully");
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"An error occurred: {ex.Message}");
                    }
                }
            }
        }

        private void saveFileChangesToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (_selectedEntry != null)
            {
                string content = textEditorControl1.Text;
                byte[] contentBytes = Encoding.ASCII.GetBytes(content);

                // Check if resize is needed
                bool requiresResize = contentBytes.Length > _selectedEntry.OriginalSize;

                if (requiresResize)
                {
                    int sizeDelta = contentBytes.Length - _selectedEntry.OriginalSize;
                    DialogResult result = MessageBox.Show(
                        $"The new content is larger than the original ({contentBytes.Length} bytes vs {_selectedEntry.OriginalSize} bytes).\n\n" +
                        $"This will require rebuilding the MET file structure (expanding by {sizeDelta} bytes).\n" +
                        $"A backup will be created automatically.\n\n" +
                        $"Do you want to proceed?",
                        "Confirm File Resize",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Warning
                    );

                    if (result != DialogResult.Yes)
                    {
                        return; // User canceled
                    }
                }

                if (FileSaver.SaveFileEntryChangesWithResize(_dataMetPath, _selectedEntry, contentBytes))
                {
                    string listViewItemName = Path.GetFileName(_selectedEntry.Path);
                    string fileName = Path.GetFileName(_dataMetPath);

                    if (requiresResize)
                    {
                        MessageBox.Show($"✓ Changes written to {fileName}\n\n" +
                                       $"File: {listViewItemName}\n" +
                                       $"Action: MET file resized and rebuilt\n" +
                                       $"New size: {contentBytes.Length} bytes (was {_selectedEntry.OriginalSize} bytes)",
                            "Successfully Written to MET File", MessageBoxButtons.OK, MessageBoxIcon.Information);

                        // Reload the entire file structure after resize
                        groupedEntries.Clear();
                        groupedEntries = METFileReader.ReadFileEntries(_dataMetPath, groupedEntries);
                        PopulateTreeView();

                        // Reselect the current entry
                        // Find and select the node in the tree
                        foreach (TreeNode extensionNode in treeView1.Nodes)
                        {
                            if (extensionNode.Text == Path.GetExtension(_selectedEntry.Path))
                            {
                                extensionNode.Expand();
                                foreach (TreeNode fileNode in extensionNode.Nodes)
                                {
                                    if (fileNode.Tag is FileEntry entry && entry.Path == _selectedEntry.Path)
                                    {
                                        treeView1.SelectedNode = fileNode;
                                        break;
                                    }
                                }
                                break;
                            }
                        }
                    }
                    else
                    {
                        MessageBox.Show($"✓ Changes written to {fileName}\n\n" +
                                       $"File: {listViewItemName}\n" +
                                       $"Size: {contentBytes.Length} bytes",
                            "Successfully Written to MET File", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }

                    _hasUnsavedChanges = false;
                    UpdateUIState();
                    SetStatusMessage($"Changes saved to {fileName}");
                }
            }
            else
            {
                MessageBox.Show("No file selected.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void importFileToolStripMenuItem_Click(object sender, EventArgs e)
        {
            // Check if a file is selected
            if (_selectedEntry == null)
            {
                MessageBox.Show("Please select a file in the tree view first.", "No File Selected", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _hasUnsavedChanges = false;
            using (OpenFileDialog openFileDialog = new OpenFileDialog())
            {
                openFileDialog.Title = $"Import file to replace '{Path.GetFileName(_selectedEntry.Path)}'";

                // Set file filter based on the selected file's extension
                string extension = Path.GetExtension(_selectedEntry.Path).ToLower();
                if (!string.IsNullOrEmpty(extension))
                {
                    openFileDialog.Filter = $"{extension.ToUpper().TrimStart('.')} files (*{extension})|*{extension}|All files (*.*)|*.*";
                }

                if (openFileDialog.ShowDialog() == DialogResult.OK)
                {
                    string filePath = openFileDialog.FileName;
                    byte[] data = File.ReadAllBytes(filePath);

                    // Check if this is a binary file (image) or text file
                    bool isBinaryFile = extension == ".png" || extension == ".bmp" || extension == ".ico" || extension == ".mnd";

                    // Only remove padding for text files
                    if (!isBinaryFile)
                    {
                        data = RemoveZeroPadding(data);
                    }

                    // Check if the imported file requires resizing the MET file
                    bool requiresResize = data.Length > _selectedEntry.OriginalSize;
                    string confirmMessage;

                    if (requiresResize)
                    {
                        int sizeDelta = data.Length - _selectedEntry.OriginalSize;
                        confirmMessage = $"The imported file is larger than the original ({data.Length} bytes vs {_selectedEntry.OriginalSize} bytes).\n\n" +
                                       $"This will require rebuilding the MET file structure (expanding by {sizeDelta} bytes).\n" +
                                       $"A backup will be created automatically.\n\n" +
                                       $"Replace '{Path.GetFileName(_selectedEntry.Path)}' with the imported file?";
                    }
                    else
                    {
                        confirmMessage = $"Replace '{Path.GetFileName(_selectedEntry.Path)}' with the imported file?";
                    }

                    // Ask for confirmation
                    DialogResult result = MessageBox.Show(
                        confirmMessage,
                        requiresResize ? "Confirm File Resize" : "Confirm Replace",
                        MessageBoxButtons.YesNo,
                        requiresResize ? MessageBoxIcon.Warning : MessageBoxIcon.Question
                    );

                    if (result == DialogResult.Yes)
                    {
                        // Use the resize-capable save method
                        bool success;
                        if (isBinaryFile)
                        {
                            success = FileSaver.SaveFileEntryChangesWithResize(_dataMetPath, _selectedEntry, data);
                        }
                        else
                        {
                            byte[] textBytes = Encoding.ASCII.GetBytes(Encoding.ASCII.GetString(data));
                            success = FileSaver.SaveFileEntryChangesWithResize(_dataMetPath, _selectedEntry, textBytes);
                        }

                        if (success)
                        {
                            string fileName = Path.GetFileName(_dataMetPath);
                            string listViewItemName = Path.GetFileName(_selectedEntry.Path);

                            if (requiresResize)
                            {
                                // Reload the entire file structure after resize
                                groupedEntries.Clear();
                                groupedEntries = METFileReader.ReadFileEntries(_dataMetPath, groupedEntries);
                                PopulateTreeView();

                                // Reselect the current entry
                                foreach (TreeNode extensionNode in treeView1.Nodes)
                                {
                                    if (extensionNode.Text == Path.GetExtension(_selectedEntry.Path))
                                    {
                                        extensionNode.Expand();
                                        foreach (TreeNode fileNode in extensionNode.Nodes)
                                        {
                                            if (fileNode.Tag is FileEntry entry && entry.Path == _selectedEntry.Path)
                                            {
                                                treeView1.SelectedNode = fileNode;
                                                break;
                                            }
                                        }
                                        break;
                                    }
                                }

                                MessageBox.Show($"✓ File imported and written to {fileName}\n\n" +
                                               $"File: {listViewItemName}\n" +
                                               $"Action: MET file resized and rebuilt\n" +
                                               $"New size: {data.Length} bytes (was {_selectedEntry.OriginalSize} bytes)",
                                    "Successfully Written to MET File", MessageBoxButtons.OK, MessageBoxIcon.Information);
                            }
                            else
                            {
                                MessageBox.Show($"✓ File imported and written to {fileName}\n\n" +
                                               $"File: {listViewItemName}\n" +
                                               $"Size: {data.Length} bytes",
                                    "Successfully Written to MET File", MessageBoxButtons.OK, MessageBoxIcon.Information);
                            }

                            // Reload the file data in the editor
                            LoadData(_selectedEntry);
                            _hasUnsavedChanges = false;
                            UpdateUIState();
                            SetStatusMessage($"File imported and saved to {fileName}");
                        }
                    }
                }
            }
        }

        private Tuple<bool, bool, TreeNode> FindAndReplaceFile(TreeNode node, string importedFileName, string filePath)
        {
            System.Diagnostics.Debug.WriteLine($"  Comparing: '{node.Text}' == '{importedFileName}' ? {node.Text == importedFileName}");

            if (node.Tag is FileEntry entry && node.Text == importedFileName)
            {
                System.Diagnostics.Debug.WriteLine($"  MATCH FOUND! Entry path: {entry.Path}");

                byte[] data = File.ReadAllBytes(filePath);

                // Check if this is a binary file (image) or text file
                string extension = Path.GetExtension(entry.Path).ToLower();
                bool isBinaryFile = extension == ".png" || extension == ".bmp" || extension == ".ico" || extension == ".mnd";

                // Only remove padding for text files
                if (!isBinaryFile)
                {
                    data = RemoveZeroPadding(data);
                }

                // Check if the imported file requires resizing the MET file
                bool requiresResize = data.Length > entry.OriginalSize;
                string confirmMessage;

                if (requiresResize)
                {
                    int sizeDelta = data.Length - entry.OriginalSize;
                    confirmMessage = $"The imported file is larger than the original ({data.Length} bytes vs {entry.OriginalSize} bytes).\n\n" +
                                   $"This will require rebuilding the MET file structure (expanding by {sizeDelta} bytes).\n" +
                                   $"A backup will be created automatically.\n\n" +
                                   $"Do you want to proceed with importing '{importedFileName}'?";
                }
                else
                {
                    confirmMessage = $"Are you sure you want to overwrite '{importedFileName}'?";
                }

                // Ask for confirmation before overwriting
                DialogResult result = MessageBox.Show(
                    confirmMessage,
                    requiresResize ? "Confirm File Resize" : "Confirm Overwrite",
                    MessageBoxButtons.YesNo,
                    requiresResize ? MessageBoxIcon.Warning : MessageBoxIcon.Question
                );

                if (result == DialogResult.Yes)
                {
                    // Use the resize-capable save method for all imports
                    bool success;
                    if (isBinaryFile)
                    {
                        // Save binary files directly without string conversion
                        success = FileSaver.SaveFileEntryChangesWithResize(_dataMetPath, entry, data);
                    }
                    else
                    {
                        // Convert text to bytes for the resize method
                        byte[] textBytes = Encoding.ASCII.GetBytes(Encoding.ASCII.GetString(data));
                        success = FileSaver.SaveFileEntryChangesWithResize(_dataMetPath, entry, textBytes);
                    }

                    if (success && requiresResize)
                    {
                        // Reload the entire file structure after resize
                        groupedEntries.Clear();
                        groupedEntries = METFileReader.ReadFileEntries(_dataMetPath, groupedEntries);
                        PopulateTreeView();
                    }

                    return new Tuple<bool, bool, TreeNode>(true, success, success ? node : null);
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

        private void exportSelectFileToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (_selectedEntry != null)
            {
                FileExport.SaveSelectedFileDialog(_dataMetPath, _selectedEntry);
            }
            else
            {
                MessageBox.Show("No file selected.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void exportAllFilesToolStripMenuItem_Click(object sender, EventArgs e)
        {
            using (FolderBrowserDialog folderBrowserDialog = new FolderBrowserDialog())
            {
                if (folderBrowserDialog.ShowDialog() == DialogResult.OK)
                {
                    string outputPath = folderBrowserDialog.SelectedPath;
                    FileExport fileExport = new FileExport(_dataMetPath);
                    fileExport.ExtractAllFilesToStructure(outputPath, treeView1);
                    MessageBox.Show("All files have been successfully exported.", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    CollapseAllNodes(treeView1); // Collapse all nodes after extraction
                }
            }
        }

        /// <summary>
        /// Recursively collapses all nodes in the TreeView.
        /// </summary>
        /// <param name="treeView">The TreeView to collapse.</param>
        private void CollapseAllNodes(TreeView treeView)
        {
            foreach (TreeNode node in treeView.Nodes)
            {
                CollapseNode(node);
            }
        }

        /// <summary>
        /// Recursively collapses the given TreeNode and all its child nodes.
        /// </summary>
        /// <param name="node">The TreeNode to collapse.</param>
        private void CollapseNode(TreeNode node)
        {
            foreach (TreeNode childNode in node.Nodes)
            {
                CollapseNode(childNode);
            }
            node.Collapse();
        }

        /// <summary>
        /// Handles the context menu opening event to show/hide menu items based on selection.
        /// </summary>
        private void treeViewContextMenu_Opening(object sender, System.ComponentModel.CancelEventArgs e)
        {
            // Only show context menu if a file is selected (not an extension group)
            if (treeView1.SelectedNode != null && treeView1.SelectedNode.Tag is FileEntry)
            {
                // Show all menu items
                importFileContextMenuItem.Visible = true;
                exportFileContextMenuItem.Visible = true;

                // Only show "Save Changes" if there are unsaved changes and it's a text file
                string extension = Path.GetExtension(_selectedEntry?.Path ?? "").ToLower();
                bool isTextFile = extension != ".png" && extension != ".bmp" && extension != ".ico" && extension != ".mnd";
                saveChangesContextMenuItem.Visible = isTextFile && _hasUnsavedChanges;
            }
            else
            {
                // No file selected, cancel the context menu
                e.Cancel = true;
            }
        }

        /// <summary>
        /// Handles the Import File context menu click.
        /// </summary>
        private void importFileContextMenuItem_Click(object sender, EventArgs e)
        {
            // Reuse the existing import logic
            importFileToolStripMenuItem_Click(sender, e);
        }

        /// <summary>
        /// Handles the Export File context menu click.
        /// </summary>
        private void exportFileContextMenuItem_Click(object sender, EventArgs e)
        {
            // Reuse the existing export logic
            exportSelectFileToolStripMenuItem_Click(sender, e);
        }

        /// <summary>
        /// Handles the Save Changes context menu click.
        /// </summary>
        private void saveChangesContextMenuItem_Click(object sender, EventArgs e)
        {
            // Reuse the existing save logic
            saveFileChangesToolStripMenuItem_Click(sender, e);
        }
    }
}
