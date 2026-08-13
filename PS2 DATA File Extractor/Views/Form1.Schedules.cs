using PS2_DATA_File_Extractor.FileOperations;

namespace PS2_DATA_File_Extractor;

public partial class Form1
{
    private void seasonScheduleButton_Click(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_dataMetPath) || _metFileStructure == null)
        {
            MessageBox.Show(this, "Open DATA.MET before editing season schedules.",
                "No MET File Loaded", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (_hasUnsavedChanges)
        {
            MessageBox.Show(this,
                "Save or discard the selected file's raw changes before opening the Season Schedule Editor.",
                "Unsaved File Changes", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            SeasonScheduleArchive archive = SeasonScheduleArchive.Load(_dataMetPath);
            using SeasonScheduleEditorForm editor = new(archive, _dataMetPath);
            if (editor.ShowDialog(this) == DialogResult.OK)
                ReloadMetAfterStructuredEdit("Season schedules saved; DATA.MET directory reloaded.");
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Unable to Open Season Schedule Editor",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
