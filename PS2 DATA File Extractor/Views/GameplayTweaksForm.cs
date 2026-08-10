using PS2_DATA_File_Extractor.FileOperations;

namespace PS2_DATA_File_Extractor;

public sealed class GameplayTweaksForm : Form
{
    private readonly GameplayTuningArchive _archive;
    private readonly TabControl _tabs = new() { Dock = DockStyle.Fill };
    private readonly Label _status = new() { Dock = DockStyle.Bottom, Height = 30, Padding = new Padding(12, 5, 12, 2) };
    private readonly List<DataGridView> _grids = new();
    private bool _loading;

    public GameplayTweaksForm(GameplayTuningArchive archive, string metPath)
    {
        _archive = archive;
        Text = "Gameplay Tweaks - DATA.MET";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(980, 680);
        MinimumSize = new Size(760, 520);
        AutoScaleMode = AutoScaleMode.Dpi;

        Label instructions = new()
        {
            Dock = DockStyle.Top,
            Height = 64,
            Padding = new Padding(12, 9, 12, 4),
            Text = "Edit gameplay values stored in DATA.MET. Numeric fields accept modded values beyond the retail defaults. " +
                   "Saving preserves comments and unlisted settings and creates a timestamped archive backup."
        };
        Label path = new()
        {
            Dock = DockStyle.Top,
            Height = 32,
            Padding = new Padding(12, 4, 12, 4),
            AutoEllipsis = true,
            Text = metPath
        };

        BuildTabs();
        _status.Text = archive.MissingFiles.Count == 0
            ? $"Loaded {archive.Tweaks.Count} supported gameplay values."
            : $"Loaded {archive.Tweaks.Count} values. Missing {archive.MissingFiles.Count} optional tuning file(s).";

        FlowLayoutPanel buttons = new()
        {
            Dock = DockStyle.Bottom,
            Height = 56,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(8),
            WrapContents = false
        };
        Button cancel = new() { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        Button save = new() { Text = "Save to DATA.MET", AutoSize = true };
        Button reset = new() { Text = "Reset Unsaved Values", AutoSize = true };
        save.Click += Save_Click;
        reset.Click += (_, _) => ResetValues();
        buttons.Controls.AddRange(new Control[] { cancel, save, reset });

        Controls.Add(_tabs);
        Controls.Add(_status);
        Controls.Add(buttons);
        Controls.Add(path);
        Controls.Add(instructions);
        AcceptButton = save;
        CancelButton = cancel;
    }

    private void BuildTabs()
    {
        _loading = true;
        foreach (IGrouping<string, GameplayTuningArchive.GameplayTweak> category in
                 _archive.Tweaks.GroupBy(tweak => tweak.Category))
        {
            TabPage page = new(category.Key) { Padding = new Padding(6) };
            DataGridView grid = CreateGrid();
            foreach (GameplayTuningArchive.GameplayTweak tweak in category)
            {
                int index = grid.Rows.Add(tweak.Section, Humanize(tweak.Key), tweak.Value, KindName(tweak.Kind));
                DataGridViewRow row = grid.Rows[index];
                row.Tag = tweak;
                row.Cells[1].ToolTipText = tweak.Key;
                row.Cells[2].ToolTipText = $"INI key: [{tweak.Section}] {tweak.Key}\nSource: {tweak.SourcePath}";
                if (tweak.Kind == GameplayTweakValueKind.Boolean)
                {
                    DataGridViewComboBoxCell cell = new()
                    {
                        FlatStyle = FlatStyle.Flat,
                        DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton
                    };
                    cell.Items.AddRange("False", "True");
                    cell.Value = tweak.Value.Equals("True", StringComparison.OrdinalIgnoreCase) ? "True" : "False";
                    row.Cells[2] = cell;
                }
            }

            grid.CellValueChanged += (_, args) => ValueChanged(args);
            grid.CurrentCellDirtyStateChanged += (_, _) =>
            {
                if (grid.IsCurrentCellDirty)
                    grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };
            _grids.Add(grid);
            page.Controls.Add(grid);
            _tabs.TabPages.Add(page);
        }
        _loading = false;
    }

    private static DataGridView CreateGrid()
    {
        DataGridView grid = new()
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            BackgroundColor = SystemColors.Window,
            EditMode = DataGridViewEditMode.EditOnEnter,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.CellSelect,
            AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle { BackColor = Color.FromArgb(246, 248, 250) }
        };
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Section / Item", ReadOnly = true, Width = 180 });
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Setting", ReadOnly = true,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, MinimumWidth = 220
        });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Value", Width = 180 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Format", ReadOnly = true, Width = 95 });
        return grid;
    }

    private void ValueChanged(DataGridViewCellEventArgs args)
    {
        if (_loading || args.RowIndex < 0 || args.ColumnIndex != 2) return;
        int changed = GetRows().Count(row =>
        {
            GameplayTuningArchive.GameplayTweak tweak = (GameplayTuningArchive.GameplayTweak)row.Tag!;
            return !CellText(row).Equals(tweak.Value, StringComparison.Ordinal);
        });
        _status.Text = changed == 0 ? "No unsaved gameplay changes."
            : $"{changed} unsaved gameplay value{(changed == 1 ? string.Empty : "s")}.";
    }

    private IEnumerable<DataGridViewRow> GetRows() =>
        _grids.SelectMany(grid => grid.Rows.Cast<DataGridViewRow>());

    private static string CellText(DataGridViewRow row) => Convert.ToString(row.Cells[2].Value) ?? string.Empty;

    private void ResetValues()
    {
        _loading = true;
        foreach (DataGridViewRow row in GetRows())
        {
            GameplayTuningArchive.GameplayTweak tweak = (GameplayTuningArchive.GameplayTweak)row.Tag!;
            row.Cells[2].Value = tweak.Kind == GameplayTweakValueKind.Boolean
                ? (tweak.Value.Equals("True", StringComparison.OrdinalIgnoreCase) ? "True" : "False")
                : tweak.Value;
        }
        _loading = false;
        _status.Text = "Unsaved values reset to the archive values.";
    }

    private void Save_Click(object? sender, EventArgs e)
    {
        Validate();
        Dictionary<GameplayTuningArchive.GameplayTweak, string> edits = new();
        foreach (DataGridView grid in _grids)
        {
            grid.EndEdit();
            foreach (DataGridViewRow row in grid.Rows)
            {
                GameplayTuningArchive.GameplayTweak tweak = (GameplayTuningArchive.GameplayTweak)row.Tag!;
                if (!GameplayTweakValue.TryNormalize(tweak.Kind, CellText(row), out string normalized, out string error))
                {
                    _tabs.SelectedTab = (TabPage)grid.Parent!;
                    grid.CurrentCell = row.Cells[2];
                    MessageBox.Show(this, $"[{tweak.Section}] {tweak.Key}: {error}", "Invalid Gameplay Value",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                if (!normalized.Equals(tweak.Value, StringComparison.Ordinal)) edits[tweak] = normalized;
            }
        }

        if (edits.Count == 0)
        {
            MessageBox.Show(this, "No gameplay values were changed.", "Nothing to Save",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (MessageBox.Show(this,
                $"Write {edits.Count} gameplay value change{(edits.Count == 1 ? string.Empty : "s")} to DATA.MET?\n\n" +
                "A timestamped backup will be created first.",
                "Save Gameplay Tweaks", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;

        try
        {
            UseWaitCursor = true;
            Enabled = false;
            GameplayTuningSaveResult result = _archive.SaveWithBackup(edits);
            string rebuild = result.RebuiltArchive ? "\nThe archive was resized with sector alignment preserved." : string.Empty;
            MessageBox.Show(this,
                $"Saved {edits.Count} value change{(edits.Count == 1 ? string.Empty : "s")} across " +
                $"{result.ChangedFileCount} tuning file{(result.ChangedFileCount == 1 ? string.Empty : "s")}.\n\n" +
                $"Backup: {result.BackupPath}{rebuild}",
                "Gameplay Tweaks Saved", MessageBoxButtons.OK, MessageBoxIcon.Information);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception exception)
        {
            MessageBox.Show(this,
                $"The changes could not be saved. The archive was restored if a backup was created.\n\n{exception.Message}",
                "Unable to Save Gameplay Tweaks", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Enabled = true;
            UseWaitCursor = false;
        }
    }

    private static string Humanize(string value)
    {
        if (value.StartsWith("m_", StringComparison.Ordinal)) value = value[2..];
        System.Text.StringBuilder result = new(value.Length + 8);
        for (int index = 0; index < value.Length; index++)
        {
            char current = value[index];
            if (index > 0 && char.IsUpper(current) &&
                (char.IsLower(value[index - 1]) || char.IsDigit(value[index - 1]))) result.Append(' ');
            result.Append(current);
        }
        return result.ToString();
    }

    private static string KindName(GameplayTweakValueKind kind) => kind switch
    {
        GameplayTweakValueKind.Boolean => "True/False",
        GameplayTweakValueKind.Integer => "Integer",
        GameplayTweakValueKind.Decimal => "Number",
        _ => "Text"
    };
}
