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
        private bool _isLoadingEditorContent;
        private byte[] _currentFileData;
        private byte[] _leadingUnprintableBytes = Array.Empty<byte>();
        private const string ApplicationTitle = "Backyard Baseball PS2 Editor v0.4";

        public Form1()
        {
            InitializeComponent();
            FormClosing += Form1_FormClosing;
            textEditorControl1.SetHighlighting("XML");

            ToolStripMenuItem rebuildIsoMenuItem = new ToolStripMenuItem("Build Modded Game ISO...");
            rebuildIsoMenuItem.Click += rebuildIsoMenuItem_Click;
            int exitIndex = fileToolStripMenuItem.DropDownItems.IndexOf(exitToolStripMenuItem);
            fileToolStripMenuItem.DropDownItems.Insert(exitIndex, new ToolStripSeparator());
            fileToolStripMenuItem.DropDownItems.Insert(exitIndex, rebuildIsoMenuItem);

            BuildTabbedWorkspace();
            UpdateUIState();
        }

        /// <summary>
        /// Updates the UI to reflect the current save state.
        /// </summary>
        private void UpdateUIState()
        {
            // Update window title
            string baseTitle = ApplicationTitle;
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

            UpdateWorkspaceState();
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
            if (_selectedEntry != null && !_isLoadingEditorContent)
            {
                int selectedEntryCurrentSize;
                if (_isHexViewMode)
                {
                    if (!HexDataCodec.TryParse(textEditorControl1.Text, out byte[] data, out string error))
                    {
                        currentFileSizeToolStripMenuItem.ForeColor = Color.Red;
                        currentFileSizeToolStripMenuItem.Text = $"Invalid hex: {error}";
                        _hasUnsavedChanges = true;
                        UpdateUIState();
                        return;
                    }

                    selectedEntryCurrentSize = data.Length;
                }
                else
                {
                    selectedEntryCurrentSize = _leadingUnprintableBytes.Length +
                        Encoding.UTF8.GetByteCount(textEditorControl1.Text);
                }

                _selectedEntry.CurrentSize = selectedEntryCurrentSize;
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
                _leadingUnprintableBytes = Array.Empty<byte>(); // Reset for new file

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
            _isLoadingEditorContent = true;
            try
            {
                LoadDataCore(entry);
            }
            finally
            {
                _isLoadingEditorContent = false;
            }
        }

        private void LoadDataCore(FileEntry entry)
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

                    // Extract and store leading unprintable bytes (like control characters)
                    // These will be preserved when saving, but hidden from the editor
                    byte[] printableData = ExtractLeadingUnprintableBytes(data);

                    string dataText = Encoding.UTF8.GetString(printableData);
                    textEditorControl1.Text = dataText;
                    textEditorControl1.Refresh();

                    _selectedEntry.CurrentSize = data.Length;
                }
                else if (Ps2AudioArchive.IsSupported(entry.Path))
                {
                    if (pictureBox1.Image != null)
                    {
                        pictureBox1.Image.Dispose();
                        pictureBox1.Image = null;
                    }

                    textEditorControl1.IsReadOnly = true;
                    try
                    {
                        Ps2AudioInfo info = Ps2AudioArchive.Inspect(
                            _dataMetPath, entry, _metFileStructure);
                        textEditorControl1.Text = Ps2AudioArchive.FormatDescription(info);
                    }
                    catch (Exception exception)
                    {
                        textEditorControl1.Text =
                            $"[PS2 audio file: {Path.GetFileName(entry.Path)}]\n\n" +
                            $"The audio metadata could not be parsed.\n\n{exception.Message}\n\n" +
                            "Raw import and export remain available.";
                    }
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
            if (pictureBox1.Image != null)
            {
                pictureBox1.Image.Dispose();
                pictureBox1.Image = null;
            }

            textEditorControl1.IsReadOnly = false;
            textEditorControl1.Text = HexDataCodec.Format(data);
            _selectedEntry.CurrentSize = data.Length;
            SetStatusMessage("Hex editor shows payload bytes only. Enter byte pairs separated by spaces or lines.");
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
            if (_hasUnsavedChanges)
            {
                hexViewToolStripMenuItem.Checked = _isHexViewMode;
                MessageBox.Show(
                    "Save or discard the current changes before switching editor modes.",
                    "Unsaved Changes",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

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
            info.AppendLine($"  Bytes 4-7 (Data Section Size):   0x{_metFileStructure.DataSectionSize:X} ({_metFileStructure.DataSectionSize:N0} bytes)");
            info.AppendLine();
            info.AppendLine($"File Entry Headers Section:");
            info.AppendLine($"  Start: 0x00000008 (byte 8)");
            info.AppendLine($"  End:   0x{_metFileStructure.DataSectionOffset:X} (byte {_metFileStructure.DataSectionOffset:N0})");
            info.AppendLine($"  Size:  {_metFileStructure.HeaderSectionSize:N0} bytes");
            info.AppendLine();
            info.AppendLine($"Data Section:");
            info.AppendLine($"  Start: 0x{_metFileStructure.DataSectionOffset:X}");
            info.AppendLine($"  End:   0x{_metFileStructure.TotalFileSize:X}");
            info.AppendLine($"  Size:  {_metFileStructure.DataSectionSize:N0} bytes");

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

        /// <summary>
        /// Extracts leading unprintable bytes from data and returns the printable portion.
        /// Stores the unprintable bytes in _leadingUnprintableBytes for later restoration.
        /// </summary>
        private byte[] ExtractLeadingUnprintableBytes(byte[] data)
        {
            int firstPrintableIndex = 0;

            // Find the first printable character
            // We consider printable: space (32) through ~ (126), plus common whitespace (tab=9, LF=10, CR=13)
            for (int i = 0; i < data.Length; i++)
            {
                byte b = data[i];
                bool isPrintable = (b >= 32 && b <= 126) || b == 9 || b == 10 || b == 13;

                if (isPrintable)
                {
                    firstPrintableIndex = i;
                    break;
                }
            }

            // If no printable characters found, return empty array
            if (firstPrintableIndex == 0 && data.Length > 0)
            {
                byte b = data[0];
                bool isPrintable = (b >= 32 && b <= 126) || b == 9 || b == 10 || b == 13;
                if (!isPrintable)
                {
                    // All data is unprintable
                    _leadingUnprintableBytes = data;
                    return Array.Empty<byte>();
                }
            }

            // Extract leading unprintable bytes
            if (firstPrintableIndex > 0)
            {
                _leadingUnprintableBytes = new byte[firstPrintableIndex];
                Array.Copy(data, 0, _leadingUnprintableBytes, 0, firstPrintableIndex);

                // Return the printable portion
                byte[] printableData = new byte[data.Length - firstPrintableIndex];
                Array.Copy(data, firstPrintableIndex, printableData, 0, printableData.Length);
                return printableData;
            }
            else
            {
                // No leading unprintable bytes
                _leadingUnprintableBytes = Array.Empty<byte>();
                return data;
            }
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

        private void rebuildIsoMenuItem_Click(object? sender, EventArgs e)
        {
            string? initialDirectory = string.IsNullOrWhiteSpace(_dataMetPath)
                ? null
                : Path.GetDirectoryName(_dataMetPath);
            using IsoRebuildForm dialog = new IsoRebuildForm(initialDirectory);
            dialog.ShowDialog(this);
        }

        private void openmetFileToolStripMenuItem_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog openFileDialog = new OpenFileDialog())
            {
                openFileDialog.Filter = "Backyard Baseball DATA.MET (DATA.MET)|DATA.MET|MET files (*.met)|*.met|All files (*.*)|*.*";
                openFileDialog.Title = "Open extracted Backyard Baseball DATA.MET";
                openFileDialog.FileName = "DATA.MET";

                if (openFileDialog.ShowDialog(this) == DialogResult.OK)
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
                byte[] contentBytes;
                if (_isHexViewMode)
                {
                    if (!HexDataCodec.TryParse(textEditorControl1.Text, out contentBytes, out string error))
                    {
                        MessageBox.Show(
                            error,
                            "Invalid Hex Data",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Error);
                        return;
                    }
                }
                else
                {
                    if (textEditorControl1.IsReadOnly)
                    {
                        MessageBox.Show(
                            "This binary preview cannot be saved as text. Use Hex Editor or Import File.",
                            "Binary File",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information);
                        return;
                    }

                    contentBytes = Encoding.UTF8.GetBytes(textEditorControl1.Text);
                    if (_leadingUnprintableBytes.Length > 0)
                    {
                        byte[] fullContent = new byte[_leadingUnprintableBytes.Length + contentBytes.Length];
                        Array.Copy(_leadingUnprintableBytes, 0, fullContent, 0, _leadingUnprintableBytes.Length);
                        Array.Copy(contentBytes, 0, fullContent, _leadingUnprintableBytes.Length, contentBytes.Length);
                        contentBytes = fullContent;
                    }
                }

                int originalSize = _selectedEntry.OriginalSize;
                AssetReplacementValidation? validation =
                    ValidateImportedAsset(_selectedEntry, contentBytes);
                if (validation == null) return;

                // Check if resize is needed
                bool requiresResize = contentBytes.Length > _selectedEntry.OriginalSize;

                if (requiresResize)
                {
                    int sizeDelta = contentBytes.Length - _selectedEntry.OriginalSize;
                    string confirmation =
                        $"The new content is larger than the original ({contentBytes.Length} bytes vs {_selectedEntry.OriginalSize} bytes).\n\n" +
                        $"This will require rebuilding the MET file structure (expanding by {sizeDelta} bytes).\n" +
                        $"A backup will be created automatically.\n\n" +
                        $"Do you want to proceed?";
                    confirmation = AddValidationDetails(confirmation, validation);
                    DialogResult result = MessageBox.Show(
                        confirmation,
                        "Confirm File Resize",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Warning
                    );

                    if (result != DialogResult.Yes)
                    {
                        return; // User canceled
                    }
                }
                else if (validation.IsAsset && validation.Warnings.Count > 0)
                {
                    DialogResult result = MessageBox.Show(
                        $"Format check: {validation.Description}\n\nWarnings:\n" +
                        validation.FormatWarnings() +
                        "\n\nSave this replacement anyway?",
                        "Asset Compatibility Warning",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Warning);
                    if (result != DialogResult.Yes) return;
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
                                       $"New size: {contentBytes.Length} bytes (was {originalSize} bytes)",
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

                    LoadData(_selectedEntry);
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

        private AssetReplacementValidation? ValidateImportedAsset(FileEntry entry, byte[] data)
        {
            AssetReplacementValidation validation =
                FileSaver.ValidateFileEntryReplacement(_dataMetPath, entry, data);
            if (validation.IsValid) return validation;

            MessageBox.Show(
                this,
                $"The selected file is not compatible with {entry.Path}.{Environment.NewLine}{Environment.NewLine}" +
                validation.FormatErrors(),
                "Invalid Asset Replacement",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return null;
        }

        private static string AddValidationDetails(
            string confirmation,
            AssetReplacementValidation validation)
        {
            if (!validation.IsAsset) return confirmation;

            string result = confirmation + $"{Environment.NewLine}{Environment.NewLine}Format check: {validation.Description}";
            if (validation.Warnings.Count > 0)
            {
                result += $"{Environment.NewLine}{Environment.NewLine}Warnings:{Environment.NewLine}" +
                          validation.FormatWarnings();
            }
            return result;
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
                    AssetReplacementValidation? validation =
                        ValidateImportedAsset(_selectedEntry, data);
                    if (validation == null) return;

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

                    confirmMessage = AddValidationDetails(confirmMessage, validation);

                    // Ask for confirmation
                    DialogResult result = MessageBox.Show(
                        confirmMessage,
                        requiresResize ? "Confirm File Resize" : "Confirm Replace",
                        MessageBoxButtons.YesNo,
                        requiresResize ? MessageBoxIcon.Warning : MessageBoxIcon.Question
                    );

                    if (result == DialogResult.Yes)
                    {
                        // Import is always byte-for-byte, regardless of extension.
                        bool success = FileSaver.SaveFileEntryChangesWithResize(_dataMetPath, _selectedEntry, data);

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
                AssetReplacementValidation? validation = ValidateImportedAsset(entry, data);
                if (validation == null)
                    return new Tuple<bool, bool, TreeNode>(true, false, null!);

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

                confirmMessage = AddValidationDetails(confirmMessage, validation);

                // Ask for confirmation before overwriting
                DialogResult result = MessageBox.Show(
                    confirmMessage,
                    requiresResize ? "Confirm File Resize" : "Confirm Overwrite",
                    MessageBoxButtons.YesNo,
                    requiresResize ? MessageBoxIcon.Warning : MessageBoxIcon.Question
                );

                if (result == DialogResult.Yes)
                {
                    // Import is always byte-for-byte, regardless of extension.
                    bool success = FileSaver.SaveFileEntryChangesWithResize(_dataMetPath, entry, data);

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
                FileExport.SaveSelectedFileDialog(_dataMetPath, _selectedEntry, _metFileStructure);
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
        private void patchGameMenuItem_Click(object? sender, EventArgs e)
        {
            using OpenFileDialog dialog = new OpenFileDialog
            {
                Title = "Open extracted USA game executable",
                FileName = "SLUS_208.65",
                Filter = "Backyard Baseball executable (SLUS_208.65)|SLUS_208.65|All files (*.*)|*.*"
            };

            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            try
            {
                GameExecutableUnlockState state =
                    GameExecutableUnlockPatcher.Inspect(dialog.FileName);
                using GameExecutableUnlockForm editor =
                    new GameExecutableUnlockForm(dialog.FileName, state);
                editor.ShowDialog(this);
            }
            catch (Exception exception)
            {
                MessageBox.Show(
                    this,
                    exception.Message,
                    "Unsupported Game Executable",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private void playerEditorMenuItem_Click(object? sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_dataMetPath) || _metFileStructure == null)
            {
                MessageBox.Show(this, "Open DATA.MET before editing players.",
                    "No MET File Loaded", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (_hasUnsavedChanges)
            {
                MessageBox.Show(this,
                    "Save or discard the currently selected file's changes before opening the Player Editor.",
                    "Unsaved File Changes", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                PlayerStatsArchive archive = PlayerStatsArchive.Load(_dataMetPath);
                using PlayerEditorForm editor = new PlayerEditorForm(archive, _dataMetPath);
                DialogResult result = editor.ShowDialog(this);
                if (result == DialogResult.OK)
                {
                    ReloadMetAfterStructuredEdit("Player changes saved; DATA.MET directory reloaded.");
                }
                else if (editor.ArchiveWasModified)
                {
                    ReloadMetAfterStructuredEdit("Player portrait saved; DATA.MET directory reloaded.");
                }
            }
            catch (Exception exception)
            {
                MessageBox.Show(this, exception.Message, "Unable to Open Player Editor",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void gameplayTweaksMenuItem_Click(object? sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_dataMetPath) || _metFileStructure == null)
            {
                MessageBox.Show(this, "Open DATA.MET before editing gameplay tweaks.",
                    "No MET File Loaded", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (_hasUnsavedChanges)
            {
                MessageBox.Show(this,
                    "Save or discard the currently selected file's changes before opening Gameplay Tweaks.",
                    "Unsaved File Changes", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                GameplayTuningArchive archive = GameplayTuningArchive.Load(_dataMetPath);
                using GameplayTweaksForm editor = new GameplayTweaksForm(archive, _dataMetPath);
                if (editor.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                ReloadMetAfterStructuredEdit("Gameplay tweaks saved; DATA.MET directory reloaded.");
            }
            catch (Exception exception)
            {
                MessageBox.Show(this, exception.Message, "Unable to Open Gameplay Tweaks",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ReloadMetAfterStructuredEdit(string statusMessage)
        {
            _metFileStructure = METFileReader.ReadMETFile(_dataMetPath);
            PopulateTreeView();
            _selectedEntry = null!;
            _currentFileData = Array.Empty<byte>();
            _leadingUnprintableBytes = Array.Empty<byte>();
            textEditorControl1.Text = string.Empty;
            richTextBox1.Clear();
            if (pictureBox1.Image != null)
            {
                pictureBox1.Image.Dispose();
                pictureBox1.Image = null;
            }
            _hasUnsavedChanges = false;
            UpdateUIState();
            SetStatusMessage(statusMessage);
        }

        private void editSaveMenuItem_Click(object? sender, EventArgs e)
        {
            using OpenFileDialog dialog = new OpenFileDialog
            {
                Title = "Open exported Backyard Baseball Settings save",
                FileName = "Settings",
                Filter = "Backyard Baseball Settings (Settings)|Settings|All files (*.*)|*.*"
            };

            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            try
            {
                GameSettingsFile settings = GameSettingsFile.Load(dialog.FileName);
                using UnlockEditorForm editor = new UnlockEditorForm(settings, dialog.FileName);
                editor.ShowDialog(this);
            }
            catch (Exception exception)
            {
                MessageBox.Show(
                    this,
                    $"This is not a valid Backyard Baseball Settings file.\n\n{exception.Message}",
                    "Unable to Open Settings",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
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

                saveChangesContextMenuItem.Visible = !textEditorControl1.IsReadOnly && _hasUnsavedChanges;
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
