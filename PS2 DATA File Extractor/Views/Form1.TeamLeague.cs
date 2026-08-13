using PS2_DATA_File_Extractor.FileOperations;

namespace PS2_DATA_File_Extractor;

public partial class Form1
{
    private void teamLeagueButton_Click(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_dataMetPath) || _metFileStructure == null)
        {
            MessageBox.Show(this, "Open DATA.MET before editing the team and league setup.",
                "No MET File Loaded", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (_hasUnsavedChanges)
        {
            MessageBox.Show(this,
                "Save or discard the selected file's raw changes before opening the Team and League Setup Editor.",
                "Unsaved File Changes", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            TeamLeagueArchive archive = TeamLeagueArchive.Load(_dataMetPath);
            using TeamLeagueSetupEditorForm editor = new(archive, _dataMetPath);
            if (editor.ShowDialog(this) == DialogResult.OK)
                ReloadMetAfterStructuredEdit("Team and league setup saved; DATA.MET directory reloaded.");
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Unable to Open Team and League Setup Editor",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
