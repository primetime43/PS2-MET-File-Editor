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
            string filePath = Path.Combine(folderPath, Path.GetFileName(entry.Path));
            SaveSelectedFileLocally(_dataMetPath, entry, filePath, false);
        }

        /// <summary>
        /// Displays a SaveFileDialog to the user and saves the selected file entry to the chosen location.
        /// </summary>
        /// <param name="dataMetPath">The path to the data.met file.</param>
        /// <param name="entry">The file entry to save.</param>
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
                SaveSelectedFileLocally(dataMetPath, entry, saveFileDialog.FileName, true);
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