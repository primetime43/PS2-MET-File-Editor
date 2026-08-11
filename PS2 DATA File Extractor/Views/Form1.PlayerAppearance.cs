using PS2_DATA_File_Extractor.FileOperations;

namespace PS2_DATA_File_Extractor;

public partial class Form1
{
    private void playerAppearanceButton_Click(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_dataMetPath) || _metFileStructure == null)
        {
            MessageBox.Show(this, "Open DATA.MET before editing player appearances.",
                "No MET File Loaded", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (_hasUnsavedChanges)
        {
            MessageBox.Show(this,
                "Save or discard the currently selected raw file changes before opening the Appearance Editor.",
                "Unsaved File Changes", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        try
        {
            RenderWareAnimationArchive animations = RenderWareAnimationArchive.Load(_dataMetPath);
            PlayerStatsArchive players = PlayerStatsArchive.Load(_dataMetPath);
            using PlayerAppearanceEditorForm editor = new(animations, players, _dataMetPath);
            if (editor.ShowDialog(this) != DialogResult.OK) return;
            ReloadMetAfterStructuredEdit("Player textures saved; DATA.MET directory reloaded.");
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Unable to Open Player Appearance Editor",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
