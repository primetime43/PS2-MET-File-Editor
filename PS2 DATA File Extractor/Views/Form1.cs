using PS2_DATA_File_Extractor.Models;
using PS2_DATA_File_Extractor.FileOperations;
using System.Text;
using ICSharpCode.TextEditor;

namespace PS2_DATA_File_Extractor
{
    public partial class Form1 : Form
    {
        private METFileStructure _metFileStructure;
        private string _dataMetPath;
        private bool _hasUnsavedChanges = false;
        private FileEntry _selectedEntry;
        private bool _isHexViewMode = false;
        private byte[] _currentFileData;

        public Form1()
        {
            InitializeComponent();
            // Make sure to hook up the BeforeExpand and AfterSelect events
            treeView1.BeforeExpand += treeView1_BeforeExpand;
            treeView1.AfterSelect += treeView1_AfterSelect;

            // Hook up FormClosing event to check for unsaved changes
            this.FormClosing += Form1_FormClosing;

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

            if (_metFileStructure == null || _metFileStructure.GroupedEntries == null)
            {
                return;
            }

            foreach (var group in _metFileStructure.GroupedEntries)
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
                int selectedEntryCurrentSize = Encoding.UTF8.GetByteCount(textEditorControl1.Text);
                currentFileSizeToolStripMenuItem.ForeColor = selectedEntryCurrentSize > _selectedEntry.OriginalSize ? Color.Red : Color.Black;
                currentFileSizeToolStripMenuItem.Text = $"Current Size: 0x{selectedEntryCurrentSize:X} (hex)";

                // Only mark as unsaved if the control is editable (not in read-only mode)
                // This prevents false positives when switching to hex view or binary file view
                if (!textEditorControl1.IsReadOnly)
                {
                    _hasUnsavedChanges = true;
                    UpdateUIState();
                }
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
                _currentFileData = data; // Store for hex view toggling

                // If in hex view mode, show hex regardless of file type
                if (_isHexViewMode)
                {
                    ShowHexView(data, entry);
                    return;
                }

                // Clear and reset text editor state before loading new content
                textEditorControl1.Text = string.Empty;
                textEditorControl1.Refresh();

                string extension = Path.GetExtension(entry.Path).ToLower();

                // Check if it's an image file
                if (extension == ".png" || extension == ".bmp" || extension == ".ico" || extension == ".mnd")
                {
                    try
                    {
                        textEditorControl1.IsReadOnly = true;

                        // Create a copy of the image to avoid holding the MemoryStream
                        Image image;
                        using (MemoryStream ms = new MemoryStream(data))
                        {
                            // Clone the image to prevent issues with stream disposal
                            using (Image tempImage = Image.FromStream(ms))
                            {
                                image = new Bitmap(tempImage);
                            }
                        }
                        ShowImageInPictureBox(image);
                        _selectedEntry.CurrentSize = data.Length;
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"An error occurred while trying to display the image: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
                // Check if it's a human-readable text file
                else if (IsTextFile(extension))
                {
                    // Clear the image when showing text
                    if (pictureBox1.Image != null)
                    {
                        pictureBox1.Image.Dispose();
                        pictureBox1.Image = null;
                    }

                    textEditorControl1.IsReadOnly = false;
                    data = RemoveZeroPadding(data); // Remove padding only for text files
                    string dataText = Encoding.UTF8.GetString(data);
                    textEditorControl1.Text = dataText;
                    textEditorControl1.Refresh();

                    _selectedEntry.CurrentSize = data.Length;
                }
                // Binary file that's not an image - don't display in text editor
                else
                {
                    // Clear both views
                    if (pictureBox1.Image != null)
                    {
                        pictureBox1.Image.Dispose();
                        pictureBox1.Image = null;
                    }

                    textEditorControl1.IsReadOnly = true;
                    textEditorControl1.Text = $"[Binary File: {Path.GetFileName(entry.Path)}]\n\n" +
                                             $"This file type ({extension}) is not editable as text.\n" +
                                             $"File size: {data.Length} bytes\n\n" +
                                             $"You can still export or import this file using the context menu.";
                    _selectedEntry.CurrentSize = data.Length;
                }
            }
        }

        /// <summary>
        /// Determines if a file extension represents a human-readable text file.
        /// </summary>
        private bool IsTextFile(string extension)
        {
            // Whitelist of known text-based file extensions
            string[] textExtensions = {
                ".txt", ".xml", ".html", ".htm", ".css", ".js", ".json",
                ".cfg", ".ini", ".config", ".log", ".csv", ".md",
                ".lua", ".script", ".shader", ".glsl", ".fx",
                ".c", ".cpp", ".h", ".cs", ".java", ".py",
                ".bat", ".sh", ".cmd", ".ps1",
                ".sql", ".yml", ".yaml", ".toml", ".dat"
            };

            return textExtensions.Contains(extension);
        }

        /// <summary>
        /// Displays file data in hexadecimal format.
        /// </summary>
        private void ShowHexView(byte[] data, FileEntry entry)
        {
            // Clear image if present
            if (pictureBox1.Image != null)
            {
                pictureBox1.Image.Dispose();
                pictureBox1.Image = null;
            }

            // Use read-only mode instead of disabling to allow scrolling
            textEditorControl1.IsReadOnly = true;

            // Read the header data from the MET file
            byte[] headerData = ReadHeaderData(entry);

            // Build complete hex view with header and data
            StringBuilder fullHexView = new StringBuilder();

            // Add file information
            fullHexView.AppendLine($"File: {Path.GetFileName(entry.Path)}");
            fullHexView.AppendLine($"Full Path: {entry.Path}");
            fullHexView.AppendLine();

            // Add header section
            fullHexView.AppendLine("═══════════════════════════════════════════════════════════════════════════");
            fullHexView.AppendLine("                           FILE ENTRY HEADER");
            fullHexView.AppendLine($"  Location: 0x{entry.HeaderStart:X} - 0x{entry.HeaderEnd:X}");
            fullHexView.AppendLine($"  Size: {headerData.Length} bytes (0x{headerData.Length:X})");
            fullHexView.AppendLine("═══════════════════════════════════════════════════════════════════════════");
            fullHexView.AppendLine();
            fullHexView.Append(FormatAsHexSection(headerData, (int)entry.HeaderStart));

            // Add separator
            fullHexView.AppendLine();
            fullHexView.AppendLine("═══════════════════════════════════════════════════════════════════════════");
            fullHexView.AppendLine("                              FILE DATA");
            fullHexView.AppendLine($"  Location: 0x{entry.Offset:X} - 0x{(entry.Offset + data.Length):X}");
            fullHexView.AppendLine($"  Size: {data.Length} bytes (0x{data.Length:X})");
            fullHexView.AppendLine("═══════════════════════════════════════════════════════════════════════════");
            fullHexView.AppendLine();
            fullHexView.Append(FormatAsHexSection(data, entry.Offset));

            textEditorControl1.Text = fullHexView.ToString();
            _selectedEntry.CurrentSize = data.Length;
        }

        /// <summary>
        /// Reads the header data for a file entry from the MET file.
        /// </summary>
        private byte[] ReadHeaderData(FileEntry entry)
        {
            int headerLength = (int)(entry.HeaderEnd - entry.HeaderStart);
            byte[] headerData = new byte[headerLength];

            using (FileStream fs = new FileStream(_dataMetPath, FileMode.Open, FileAccess.Read))
            using (BinaryReader reader = new BinaryReader(fs))
            {
                fs.Seek(entry.HeaderStart, SeekOrigin.Begin);
                headerData = reader.ReadBytes(headerLength);
            }

            return headerData;
        }

        /// <summary>
        /// Formats byte array as hexadecimal dump section with offset, hex, and ASCII columns.
        /// </summary>
        /// <param name="data">The byte data to format.</param>
        /// <param name="baseOffset">The starting offset address to display (actual position in MET file).</param>
        private string FormatAsHexSection(byte[] data, int baseOffset)
        {
            StringBuilder sb = new StringBuilder();
            int bytesPerLine = 16;

            sb.AppendLine("Offset(h) 00 01 02 03 04 05 06 07 08 09 0A 0B 0C 0D 0E 0F  ASCII");
            sb.AppendLine("───────────────────────────────────────────────────────────────────────────");

            for (int i = 0; i < data.Length; i += bytesPerLine)
            {
                // Show actual offset in the MET file
                sb.AppendFormat("{0:X8}  ", baseOffset + i);

                // Hex values
                int lineLength = Math.Min(bytesPerLine, data.Length - i);
                for (int j = 0; j < bytesPerLine; j++)
                {
                    if (j < lineLength)
                    {
                        sb.AppendFormat("{0:X2} ", data[i + j]);
                    }
                    else
                    {
                        sb.Append("   "); // Padding for incomplete lines
                    }
                }

                // ASCII representation
                sb.Append(" ");
                for (int j = 0; j < lineLength; j++)
                {
                    byte b = data[i + j];
                    char c = (b >= 32 && b <= 126) ? (char)b : '.';
                    sb.Append(c);
                }

                sb.AppendLine();
            }

            return sb.ToString();
        }

        /// <summary>
        /// Toggles between hex view and normal view.
        /// </summary>
        private void ToggleHexView()
        {
            _isHexViewMode = !_isHexViewMode;

            // Update menu item text
            hexViewToolStripMenuItem.Checked = _isHexViewMode;

            // Reload current file if one is selected
            if (_selectedEntry != null && _currentFileData != null)
            {
                LoadData(_selectedEntry);
            }
        }

        private void hexViewToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ToggleHexView();
        }

        private void fileStructureToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (_metFileStructure == null)
            {
                MessageBox.Show("No MET file is currently loaded.", "No File Loaded", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // Create a custom form to display the structure information
            Form structureForm = new Form
            {
                Text = "MET File Structure Information",
                Width = 700,
                Height = 600,
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.Sizable,
                MinimizeBox = false,
                MaximizeBox = true
            };

            // Create a RichTextBox to display the information
            RichTextBox infoTextBox = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                Font = new Font("Consolas", 10),
                BackColor = Color.White,
                Padding = new Padding(10)
            };

            // Build the structure information
            StringBuilder info = new StringBuilder();

            // Statistics from METFileStructure
            info.AppendLine(new string('═', 70));
            info.AppendLine("                    MET FILE STRUCTURE INFORMATION");
            info.AppendLine(new string('═', 70));
            info.AppendLine();
            info.AppendLine(_metFileStructure.GetStatistics());

            // Header information
            info.AppendLine();
            info.AppendLine(new string('═', 70));
            info.AppendLine("                         HEADER DETAILS");
            info.AppendLine(new string('═', 70));
            info.AppendLine();
            info.AppendLine($"MET Header (8 bytes):");
            info.AppendLine($"  Bytes 0-3 (Data Section Offset): 0x{_metFileStructure.DataSectionOffset:X} ({_metFileStructure.DataSectionOffset:N0} bytes)");
            info.AppendLine($"  Bytes 4-7 (Unknown Value):       0x{_metFileStructure.UnknownHeaderValue:X}");
            info.AppendLine();
            info.AppendLine($"File Entry Headers Section:");
            info.AppendLine($"  Start: 0x00000008 (byte 8)");
            info.AppendLine($"  End:   0x{_metFileStructure.DataSectionOffset:X} (byte {_metFileStructure.DataSectionOffset:N0})");
            info.AppendLine($"  Size:  {_metFileStructure.HeaderSectionSize:N0} bytes");
            info.AppendLine();
            info.AppendLine($"Data Section:");
            info.AppendLine($"  Start: 0x{_metFileStructure.DataSectionOffset:X}");
            info.AppendLine($"  End:   0x{_metFileStructure.TotalFileSize:X}");
            info.AppendLine($"  Size:  {(_metFileStructure.TotalFileSize - _metFileStructure.DataSectionOffset):N0} bytes");

            // Validation
            info.AppendLine();
            info.AppendLine(new string('═', 70));
            info.AppendLine("                      STRUCTURE VALIDATION");
            info.AppendLine(new string('═', 70));
            info.AppendLine();

            var (isValid, errors) = _metFileStructure.ValidateStructure();
            if (isValid)
            {
                info.AppendLine("✓ Structure validation PASSED");
                info.AppendLine("  All offsets and sizes are consistent.");
            }
            else
            {
                info.AppendLine("✗ Structure validation FAILED");
                info.AppendLine();
                info.AppendLine("Errors found:");
                foreach (var error in errors)
                {
                    info.AppendLine($"  • {error}");
                }
            }

            // Entry address ranges (first 10 and last 10)
            info.AppendLine();
            info.AppendLine(new string('═', 70));
            info.AppendLine("                    FILE ENTRY ADDRESS MAP");
            info.AppendLine(new string('═', 70));
            info.AppendLine();

            var allEntries = _metFileStructure.AllEntries;
            int entriesToShow = Math.Min(10, allEntries.Count);

            info.AppendLine($"Showing first {entriesToShow} entries:");
            info.AppendLine();
            info.AppendLine("Entry Path                                    Header Range           Data Range");
            info.AppendLine(new string('─', 70));

            for (int i = 0; i < entriesToShow; i++)
            {
                var entry = allEntries[i];
                string path = entry.Path.Length > 40 ? "..." + entry.Path.Substring(entry.Path.Length - 37) : entry.Path;
                info.AppendLine($"{path,-42} 0x{entry.HeaderStart:X6}-0x{entry.HeaderEnd:X6}  0x{entry.Offset:X8}-0x{(entry.Offset + entry.OriginalSize):X8}");
            }

            if (allEntries.Count > 20)
            {
                info.AppendLine($"... ({allEntries.Count - 20} entries omitted) ...");
                info.AppendLine();
                info.AppendLine($"Showing last 10 entries:");
                info.AppendLine();

                for (int i = allEntries.Count - 10; i < allEntries.Count; i++)
                {
                    var entry = allEntries[i];
                    string path = entry.Path.Length > 40 ? "..." + entry.Path.Substring(entry.Path.Length - 37) : entry.Path;
                    info.AppendLine($"{path,-42} 0x{entry.HeaderStart:X6}-0x{entry.HeaderEnd:X6}  0x{entry.Offset:X8}-0x{(entry.Offset + entry.OriginalSize):X8}");
                }
            }
            else if (allEntries.Count > 10)
            {
                info.AppendLine();
                info.AppendLine($"Showing remaining {allEntries.Count - 10} entries:");
                info.AppendLine();

                for (int i = 10; i < allEntries.Count; i++)
                {
                    var entry = allEntries[i];
                    string path = entry.Path.Length > 40 ? "..." + entry.Path.Substring(entry.Path.Length - 37) : entry.Path;
                    info.AppendLine($"{path,-42} 0x{entry.HeaderStart:X6}-0x{entry.HeaderEnd:X6}  0x{entry.Offset:X8}-0x{(entry.Offset + entry.OriginalSize):X8}");
                }
            }

            infoTextBox.Text = info.ToString();

            // Add close button
            Button closeButton = new Button
            {
                Text = "Close",
                Width = 100,
                Height = 30,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
                Location = new Point(structureForm.ClientSize.Width - 110, structureForm.ClientSize.Height - 40)
            };
            closeButton.Click += (s, ev) => structureForm.Close();

            // Add controls to form
            structureForm.Controls.Add(infoTextBox);
            structureForm.Controls.Add(closeButton);

            // Bring close button to front
            closeButton.BringToFront();

            // Adjust textbox to make room for button
            infoTextBox.Padding = new Padding(10, 10, 10, 50);

            // Show the form as a modal dialog
            structureForm.ShowDialog(this);
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
                // Dispose the old image before assigning a new one to prevent memory leaks
                if (pictureBox1.Image != null)
                {
                    pictureBox1.Image.Dispose();
                    pictureBox1.Image = null;
                }

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
                        _metFileStructure = METFileReader.ReadMETFile(_dataMetPath);
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
                byte[] contentBytes = Encoding.UTF8.GetBytes(content);

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
                        _metFileStructure = METFileReader.ReadMETFile(_dataMetPath);
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
                            byte[] textBytes = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(data));
                            success = FileSaver.SaveFileEntryChangesWithResize(_dataMetPath, _selectedEntry, textBytes);
                        }

                        if (success)
                        {
                            string fileName = Path.GetFileName(_dataMetPath);
                            string listViewItemName = Path.GetFileName(_selectedEntry.Path);

                            if (requiresResize)
                            {
                                // Reload the entire file structure after resize
                                _metFileStructure = METFileReader.ReadMETFile(_dataMetPath);
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
                        byte[] textBytes = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(data));
                        success = FileSaver.SaveFileEntryChangesWithResize(_dataMetPath, entry, textBytes);
                    }

                    if (success && requiresResize)
                    {
                        // Reload the entire file structure after resize
                        _metFileStructure = METFileReader.ReadMETFile(_dataMetPath);
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
            this.Close();
        }

        /// <summary>
        /// Handles the FormClosing event to check for unsaved changes before closing.
        /// </summary>
        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (_hasUnsavedChanges)
            {
                DialogResult result = MessageBox.Show(
                    "You have unsaved changes. Do you want to save before exiting?",
                    "Unsaved Changes",
                    MessageBoxButtons.YesNoCancel,
                    MessageBoxIcon.Warning
                );

                if (result == DialogResult.Yes)
                {
                    // Try to save the changes
                    saveFileChangesToolStripMenuItem_Click(sender, e);

                    // If user still has unsaved changes (save failed or was canceled), don't close
                    if (_hasUnsavedChanges)
                    {
                        e.Cancel = true;
                    }
                }
                else if (result == DialogResult.Cancel)
                {
                    // User canceled, don't close the form
                    e.Cancel = true;
                }
                // If result is No, allow the form to close without saving
            }
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
