using PS2_DATA_File_Extractor.FileOperations;
using PS2_DATA_File_Extractor.Models;

namespace PS2_DATA_File_Extractor;

public sealed class GameExecutableUnlockForm : Form
{
    private readonly string _executablePath;
    private readonly CheckedListBox _items = new CheckedListBox();
    private readonly Label _summary = new Label();

    public GameExecutableUnlockForm(string executablePath, GameExecutableUnlockState state)
    {
        _executablePath = executablePath;

        Text = "Patch Game Executable Unlocks";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(680, 610);
        MinimumSize = new Size(580, 500);

        Label instructions = new Label
        {
            Dock = DockStyle.Top,
            Height = 88,
            Padding = new Padding(12, 10, 12, 4),
            Text = "These selections are forced unlocked by patching the extracted USA game executable. " +
                   "They work for every save and do not require a memory card edit. A timestamped backup is created."
        };

        Label path = new Label
        {
            Dock = DockStyle.Top,
            Height = 34,
            AutoEllipsis = true,
            Padding = new Padding(12, 5, 12, 4),
            Text = executablePath
        };

        _items.Dock = DockStyle.Fill;
        _items.CheckOnClick = true;
        _items.Font = new Font("Segoe UI", 10);
        _items.ItemCheck += (_, _) => BeginInvoke((Action)UpdateSummary);

        foreach (UnlockableContent item in UnlockableContent.Items)
        {
            int index = _items.Items.Add($"[{item.Category}] {item.Name}");
            bool isChecked = item.Mask == GameSettingsFile.AquadomeProgressMask
                ? state.AquadomeUnlocked
                : (state.ForcedItemMask & item.Mask) != 0;
            _items.SetItemChecked(index, isChecked);
        }

        _summary.Dock = DockStyle.Bottom;
        _summary.Height = 34;
        _summary.Padding = new Padding(12, 6, 12, 2);

        FlowLayoutPanel buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 56,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(8),
            WrapContents = false
        };

        Button cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        Button apply = new Button { Text = "Apply Patch", AutoSize = true };
        Button restore = new Button { Text = "Restore Original Checks", AutoSize = true };
        Button clear = new Button { Text = "Clear", AutoSize = true };
        Button unlockAll = new Button { Text = "Unlock All", AutoSize = true };

        apply.Click += Apply_Click;
        restore.Click += Restore_Click;
        clear.Click += (_, _) => SetAll(false);
        unlockAll.Click += (_, _) => SetAll(true);

        buttons.Controls.Add(cancel);
        buttons.Controls.Add(apply);
        buttons.Controls.Add(restore);
        buttons.Controls.Add(clear);
        buttons.Controls.Add(unlockAll);

        Controls.Add(_items);
        Controls.Add(_summary);
        Controls.Add(buttons);
        Controls.Add(path);
        Controls.Add(instructions);

        AcceptButton = apply;
        CancelButton = cancel;
        UpdateSummary();
    }

    private (ushort ItemMask, bool Aquadome) BuildSelection()
    {
        ushort itemMask = 0;
        bool aquadome = false;
        for (int index = 0; index < UnlockableContent.Items.Count; index++)
        {
            if (!_items.GetItemChecked(index))
            {
                continue;
            }

            UnlockableContent item = UnlockableContent.Items[index];
            if (item.Mask == GameSettingsFile.AquadomeProgressMask)
            {
                aquadome = true;
            }
            else
            {
                itemMask |= (ushort)item.Mask;
            }
        }

        return (itemMask, aquadome);
    }

    private void SetAll(bool isChecked)
    {
        for (int index = 0; index < _items.Items.Count; index++)
        {
            _items.SetItemChecked(index, isChecked);
        }

        UpdateSummary();
    }

    private void UpdateSummary()
    {
        (ushort itemMask, bool aquadome) = BuildSelection();
        _summary.Text = $"Forced item mask: 0x{itemMask:X4} | Aquadome: {(aquadome ? "unlocked" : "normal")}";
    }

    private void Apply_Click(object? sender, EventArgs e)
    {
        (ushort itemMask, bool aquadome) = BuildSelection();
        Apply(itemMask, aquadome, "Game executable unlock patch applied.");
    }

    private void Restore_Click(object? sender, EventArgs e)
    {
        DialogResult choice = MessageBox.Show(
            this,
            "Restore the game's original unlock checks? A backup of the current executable will still be created.",
            "Restore Original Checks",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);
        if (choice == DialogResult.Yes)
        {
            Apply(0, false, "Original game unlock checks restored.");
        }
    }

    private void Apply(ushort itemMask, bool aquadome, string successMessage)
    {
        try
        {
            string backup = GameExecutableUnlockPatcher.ApplyWithBackup(
                _executablePath, itemMask, aquadome);
            MessageBox.Show(
                this,
                $"{successMessage}\n\nBackup: {backup}\n\nRebuild the ISO using this patched executable.",
                "Executable Updated",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                exception.Message,
                "Unable to Patch Executable",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }
}
