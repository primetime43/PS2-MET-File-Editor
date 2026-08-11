using PS2_DATA_File_Extractor.FileOperations;

namespace PS2_DATA_File_Extractor;

public partial class Form1
{
    private void animationEditorButton_Click(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_dataMetPath) || _metFileStructure == null)
        {
            MessageBox.Show(this, "Open DATA.MET before viewing animations.",
                "No MET File Loaded", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (_hasUnsavedChanges)
        {
            MessageBox.Show(this,
                "Save or discard the currently selected file's changes before opening the Animation Editor.",
                "Unsaved File Changes", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            string? preferredPath = _selectedEntry != null &&
                                    Path.GetExtension(_selectedEntry.Path)
                                        .Equals(".anm", StringComparison.OrdinalIgnoreCase)
                ? _selectedEntry.Path
                : null;
            RenderWareAnimationArchive archive = RenderWareAnimationArchive.Load(_dataMetPath);
            using AnimationEditorForm editor = new(archive, _dataMetPath, preferredPath);
            if (editor.ShowDialog(this) != DialogResult.OK) return;
            ReloadMetAfterStructuredEdit("Animation timing saved; DATA.MET directory reloaded.");
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Unable to Open Animation Editor",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
