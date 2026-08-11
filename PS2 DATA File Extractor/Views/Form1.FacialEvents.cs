using PS2_DATA_File_Extractor.FileOperations;

namespace PS2_DATA_File_Extractor;

public partial class Form1
{
    private void facialEventEditorMenuItem_Click(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_dataMetPath) || _metFileStructure == null)
        {
            MessageBox.Show(this, "Open DATA.MET before editing facial events.",
                "No MET File Loaded", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (_hasUnsavedChanges)
        {
            MessageBox.Show(this,
                "Save or discard the currently selected file's changes before opening the Facial Event Editor.",
                "Unsaved File Changes", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            string? preferredPath = _selectedEntry != null &&
                                    Path.GetExtension(_selectedEntry.Path)
                                        .Equals(".evt", StringComparison.OrdinalIgnoreCase)
                ? _selectedEntry.Path
                : null;
            FacialEventArchive archive = FacialEventArchive.Load(_dataMetPath);
            RenderWareAnimationArchive animations = RenderWareAnimationArchive.Load(_dataMetPath);
            using FacialEventEditorForm editor = new(
                archive, _dataMetPath, preferredPath, animations);
            if (editor.ShowDialog(this) != DialogResult.OK) return;
            ReloadMetAfterStructuredEdit("Facial events saved; DATA.MET directory reloaded.");
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Unable to Open Facial Event Editor",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
