using PS2_DATA_File_Extractor.FileOperations;
using PS2_DATA_File_Extractor.Models;

namespace PS2_DATA_File_Extractor;

public sealed class UnlockEditorForm : Form
{
    private readonly GameSettingsFile _settings;
    private readonly string _settingsPath;
    private readonly CheckedListBox _items = new CheckedListBox();
    private readonly Label _maskLabel = new Label();

    public UnlockEditorForm(GameSettingsFile settings, string settingsPath)
    {
        _settings = settings;
        _settingsPath = settingsPath;

        Text = "Backyard Baseball Unlock Editor";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(640, 570);
        MinimumSize = new Size(560, 480);

        Label instructions = new Label
        {
            AutoSize = false,
            Dock = DockStyle.Top,
            Height = 72,
            Padding = new Padding(12, 10, 12, 4),
            Text = "Edit the exported PS2 memory-card file named Settings. " +
                   "Saving updates the game's CRC-32 and creates a timestamped backup."
        };

        Label pathLabel = new Label
        {
            AutoEllipsis = true,
            Dock = DockStyle.Top,
            Height = 32,
            Padding = new Padding(12, 4, 12, 4),
            Text = settingsPath
        };

        _items.CheckOnClick = true;
        _items.Dock = DockStyle.Fill;
        _items.Font = new Font("Segoe UI", 10);
        _items.ItemCheck += (_, _) => BeginInvoke(UpdateMaskPreview);

        foreach (UnlockableContent item in UnlockableContent.Items)
        {
            int index = _items.Items.Add($"[{item.Category}] {item.Name}");
            if (item.Mask == GameSettingsFile.AquadomeProgressMask)
            {
                uint progress = settings.UnlockMask & item.Mask;
                _items.SetItemCheckState(index, progress == 0
                    ? CheckState.Unchecked
                    : progress == item.Mask
                        ? CheckState.Checked
                        : CheckState.Indeterminate);
            }
            else
            {
                _items.SetItemChecked(index, (settings.UnlockMask & item.Mask) != 0);
            }
        }

        _maskLabel.AutoSize = false;
        _maskLabel.Dock = DockStyle.Bottom;
        _maskLabel.Height = 30;
        _maskLabel.Padding = new Padding(12, 5, 12, 2);

        FlowLayoutPanel buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 52,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(8),
            WrapContents = false
        };

        Button cancelButton = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        Button saveButton = new Button { Text = "Save", AutoSize = true };
        Button clearButton = new Button { Text = "Clear Known", AutoSize = true };
        Button unlockAllButton = new Button { Text = "Unlock All", AutoSize = true };

        saveButton.Click += SaveButton_Click;
        clearButton.Click += (_, _) => SetAll(CheckState.Unchecked);
        unlockAllButton.Click += (_, _) => SetAll(CheckState.Checked);

        buttons.Controls.Add(cancelButton);
        buttons.Controls.Add(saveButton);
        buttons.Controls.Add(clearButton);
        buttons.Controls.Add(unlockAllButton);

        Controls.Add(_items);
        Controls.Add(_maskLabel);
        Controls.Add(buttons);
        Controls.Add(pathLabel);
        Controls.Add(instructions);

        AcceptButton = saveButton;
        CancelButton = cancelButton;
        UpdateMaskPreview();
    }

    private uint BuildMask()
    {
        uint mask = _settings.UnlockMask;
        for (int index = 0; index < UnlockableContent.Items.Count; index++)
        {
            UnlockableContent item = UnlockableContent.Items[index];
            CheckState state = _items.GetItemCheckState(index);
            if (state == CheckState.Checked)
            {
                mask |= item.Mask;
            }
            else if (state == CheckState.Unchecked)
            {
                mask &= ~item.Mask;
            }
        }

        return mask;
    }

    private void SetAll(CheckState state)
    {
        for (int index = 0; index < _items.Items.Count; index++)
        {
            _items.SetItemCheckState(index, state);
        }
        UpdateMaskPreview();
    }

    private void UpdateMaskPreview()
    {
        _maskLabel.Text = $"Unlock mask: 0x{BuildMask():X8}";
    }

    private void SaveButton_Click(object? sender, EventArgs e)
    {
        try
        {
            _settings.UnlockMask = BuildMask();
            string backupPath = _settings.SaveWithBackup(_settingsPath);
            MessageBox.Show(this,
                $"Unlock settings saved.\n\nBackup: {backupPath}",
                "Settings Saved", MessageBoxButtons.OK, MessageBoxIcon.Information);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Unable to Save Settings",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
