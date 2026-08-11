using PS2_DATA_File_Extractor.Models;

namespace PS2_DATA_File_Extractor.FileOperations
{
    /// <summary>
    /// Provides methods to export files from a MET file to a specified directory structure.
    /// </summary>
    public class FileExport
    {
        private readonly string _dataMetPath;

        /// <summary>
        /// Initializes a new instance of the <see cref="FileExport"/> class.
        /// </summary>
        /// <param name="dataMetPath">The path to the data.met file.</param>
        public FileExport(string dataMetPath)
        {
            _dataMetPath = dataMetPath;
        }

        /// <summary>
        /// Extracts all files from the TreeView structure to the specified output path, preserving the directory structure.
        /// </summary>
        /// <param name="outputPath">The output path where files will be extracted.</param>
        /// <param name="treeView">The TreeView containing the files to be extracted.</param>
        public void ExtractAllFilesToStructure(string outputPath, TreeView treeView)
        {
            foreach (TreeNode extensionNode in treeView.Nodes)
            {
                // Expand the extension node to load its children
                extensionNode.Expand();
                string extensionFolderPath = Path.Combine(outputPath, extensionNode.Text);
                Directory.CreateDirectory(extensionFolderPath);

                foreach (TreeNode fileNode in extensionNode.Nodes)
                {
                    if (fileNode.Tag is FileEntry entry)
                    {
                        ExtractFile(entry, extensionFolderPath);
                    }
                    else
                    {
                        // Recursively handle nested nodes
                        ExtractFilesFromNode(extensionFolderPath, fileNode);
                    }
                }
            }
        }

        /// <summary>
        /// Recursively extracts files from the given TreeNode and its child nodes.
        /// </summary>
        /// <param name="currentPath">The current directory path where files will be extracted.</param>
        /// <param name="node">The TreeNode to extract files from.</param>
        private void ExtractFilesFromNode(string currentPath, TreeNode node)
        {
            if (node.Tag is FileEntry entry)
            {
                ExtractFile(entry, currentPath);
            }

            string childPath = Path.Combine(currentPath, node.Text);
            Directory.CreateDirectory(childPath);

            foreach (TreeNode childNode in node.Nodes)
            {
                ExtractFilesFromNode(childPath, childNode);
            }
        }

        /// <summary>
        /// Extracts a single file entry to the specified folder path.
        /// </summary>
        /// <param name="entry">The file entry to extract.</param>
        /// <param name="folderPath">The folder path where the file will be extracted.</param>
        private void ExtractFile(FileEntry entry, string folderPath)
        {
            // Replace / with - in the file name to preserve directory structure
            string fileName = entry.Path.Replace('/', '-');
            string filePath = Path.Combine(folderPath, fileName);
            SaveSelectedFileLocally(_dataMetPath, entry, filePath, false);
        }

        /// <summary>
        /// Displays a SaveFileDialog to the user and saves the selected file entry to the chosen location.
        /// </summary>
        /// <param name="dataMetPath">The path to the data.met file.</param>
        /// <param name="entry">The file entry to save.</param>
        public static void SaveSelectedFileDialog(
            string dataMetPath,
            FileEntry entry,
            METFileStructure? structure = null)
        {
            if (structure != null && Ps2AudioArchive.IsSupported(entry.Path))
            {
                SavePs2AudioDialog(dataMetPath, entry, structure);
                return;
            }

            using SaveFileDialog saveFileDialog = new();
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
                SaveSelectedFileLocally(dataMetPath, entry, saveFileDialog.FileName, true);
            }
        }

        private static void SavePs2AudioDialog(
            string dataMetPath,
            FileEntry entry,
            METFileStructure structure)
        {
            try
            {
                Ps2AudioInfo info = Ps2AudioArchive.Inspect(dataMetPath, entry, structure);
                bool pair = info.Kind == Ps2AudioKind.MibMih;
                string baseName = Path.GetFileNameWithoutExtension(entry.Path);
                using SaveFileDialog dialog = new()
                {
                    Title = pair
                        ? "Export streamed PS2 audio"
                        : "Export PlayStation VAG audio",
                    FileName = baseName + ".wav",
                    Filter = pair
                        ? "Decoded PCM WAV (*.wav)|*.wav|Original MIH/MIB pair (*.mib;*.mih)|*.mib"
                        : "Decoded PCM WAV (*.wav)|*.wav|Original VAG file (*.vag)|*.vag",
                    AddExtension = true,
                    DefaultExt = "wav"
                };
                if (dialog.ShowDialog() != DialogResult.OK) return;

                if (dialog.FilterIndex == 1)
                {
                    byte[] wave = Ps2AudioArchive.DecodeToWave(dataMetPath, entry, structure);
                    File.WriteAllBytes(dialog.FileName, wave);
                    MessageBox.Show(
                        $"Decoded {info.Channels}-channel, {info.SampleRate:N0} Hz audio to:\n{dialog.FileName}",
                        "Audio Exported",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    return;
                }

                if (pair)
                {
                    string mibPath = Path.ChangeExtension(dialog.FileName, ".mib");
                    string mihPath = Path.ChangeExtension(dialog.FileName, ".mih");
                    if ((File.Exists(mibPath) || File.Exists(mihPath)) &&
                        MessageBox.Show(
                            $"One or both output files already exist:\n{mibPath}\n{mihPath}\n\nReplace both MIH and MIB files?",
                            "Replace Audio Pair",
                            MessageBoxButtons.YesNo,
                            MessageBoxIcon.Warning) != DialogResult.Yes)
                        return;

                    Ps2AudioArchive.ExportRawPair(dataMetPath, entry, structure, dialog.FileName);
                    MessageBox.Show(
                        $"Original pair exported to:\n{mibPath}\n{mihPath}",
                        "Audio Pair Exported",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }
                else
                {
                    SaveSelectedFileLocally(dataMetPath, entry, dialog.FileName, true);
                }
            }
            catch (Exception exception)
            {
                MessageBox.Show(
                    $"The PS2 audio could not be exported.\n\n{exception.Message}",
                    "Unable to Export Audio",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }
        /// <summary>
        /// Saves the selected file entry to the specified destination path.
        /// </summary>
        /// <param name="dataMetPath">The path to the data.met file.</param>
        /// <param name="entry">The file entry to save.</param>
        /// <param name="destinationPath">The destination path where the file will be saved.</param>
        /// <param name="showSavedFileAlert">Indicates whether to show an alert message after saving the file.</param>
        private static void SaveSelectedFileLocally(string dataMetPath, FileEntry entry, string destinationPath, bool showSavedFileAlert)
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

                if (showSavedFileAlert)
                {
                    MessageBox.Show($"File saved successfully to {destinationPath}", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
        }
    }
}