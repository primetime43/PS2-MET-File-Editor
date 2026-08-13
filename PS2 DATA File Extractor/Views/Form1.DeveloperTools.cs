using PS2_DATA_File_Extractor.FileOperations;

namespace PS2_DATA_File_Extractor;

public partial class Form1
{
    private void developerToolsButton_Click(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_dataMetPath) || _metFileStructure == null)
        {
            MessageBox.Show(this, "Open DATA.MET before opening Developer Tools.",
                "No MET File Loaded", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (_hasUnsavedChanges)
        {
            MessageBox.Show(this,
                "Save or discard the selected file's raw changes before opening Developer Tools.",
                "Unsaved File Changes", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        try
        {
            DeveloperOptionsArchive archive = DeveloperOptionsArchive.Load(_dataMetPath);
            string siblingExecutable = Path.Combine(Path.GetDirectoryName(_dataMetPath) ?? string.Empty, "SLUS_208.65");
            using DeveloperToolsForm editor = new(archive, _dataMetPath,
                File.Exists(siblingExecutable) ? siblingExecutable : null);
            editor.ShowDialog(this);
            if (editor.ArchiveModified)
                ReloadMetAfterStructuredEdit("Developer runtime options saved; DATA.MET directory reloaded.");
            else if (editor.ExecutableModified)
                SetStatusMessage("Developer executable modes updated; rebuild the ISO to test them.");
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Unable to Open Developer Tools",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
