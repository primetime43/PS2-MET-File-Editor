using PS2_DATA_File_Extractor.FileOperations;

namespace PS2_DATA_File_Extractor;

public partial class Form1
{
    private void stadiumEnvironmentMenuItem_Click(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_dataMetPath) || _metFileStructure == null)
        {
            MessageBox.Show(this, "Open DATA.MET before editing stadium environments.",
                "No MET File Loaded", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (_hasUnsavedChanges)
        {
            MessageBox.Show(this,
                "Save or discard the currently selected file's changes before opening the Stadium Environment Editor.",
                "Unsaved File Changes", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            StadiumEnvironmentArchive archive = StadiumEnvironmentArchive.Load(_dataMetPath);
            using StadiumEnvironmentEditorForm editor = new(archive, _dataMetPath);
            if (editor.ShowDialog(this) != DialogResult.OK) return;
            ReloadMetAfterStructuredEdit("Stadium environments saved; DATA.MET directory reloaded.");
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Unable to Open Stadium Environment Editor",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
