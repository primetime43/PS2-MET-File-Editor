using PS2_DATA_File_Extractor.FileOperations;

namespace PS2_DATA_File_Extractor;

public partial class Form1
{
    private void renderWareViewerButton_Click(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_dataMetPath) || _metFileStructure == null)
        {
            MessageBox.Show(this, "Open DATA.MET before viewing models and stadiums.",
                "No MET File Loaded", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (_hasUnsavedChanges)
        {
            MessageBox.Show(this,
                "Save or discard the selected file's raw changes before opening the Model and Stadium Viewer.",
                "Unsaved File Changes", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        try
        {
            string? extension = _selectedEntry == null ? null : Path.GetExtension(_selectedEntry.Path);
            string? preferredPath = extension != null &&
                                    (extension.Equals(".dff", StringComparison.OrdinalIgnoreCase) ||
                                     extension.Equals(".rws", StringComparison.OrdinalIgnoreCase))
                ? _selectedEntry!.Path : null;
            RenderWareSceneArchive archive = RenderWareSceneArchive.Load(_dataMetPath);
            using RenderWareModelViewerForm viewer = new(archive, _dataMetPath, preferredPath);
            viewer.ShowDialog(this);
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Unable to Open Model and Stadium Viewer",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
