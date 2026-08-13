using PS2_DATA_File_Extractor.FileOperations;

namespace PS2_DATA_File_Extractor;

public sealed class DeveloperToolsForm : Form
{
    private readonly string _metPath;
    private DeveloperOptionsArchive _archive;
    private readonly DataGridView _options = new();
    private readonly Label _status = new() { Dock = DockStyle.Bottom, Height = 30, Padding = new Padding(12, 5, 12, 2) };
    private readonly TextBox _executablePath = new() { ReadOnly = true, Dock = DockStyle.Fill };
    private readonly Label _executableState = new() { AutoSize = true, ForeColor = SystemColors.GrayText };
    private readonly CheckBox _oneInning = new() { Text = "One-inning games", AutoSize = true };
    private readonly CheckBox _cpuSeason = new() { Text = "CPU controls season games", AutoSize = true };
    private readonly ComboBox _resultCheat = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 210 };
    private readonly CheckBox _exactHit = new() { Text = "Override every batted ball with these exact values", AutoSize = true };
    private readonly NumericUpDown[] _hitValues = Enumerable.Range(0, 6).Select(_ => CreateHitNumber()).ToArray();
    private readonly Button _applyExecutable = new() { Text = "Apply to SLUS_208.65", AutoSize = true };
    private readonly Button _restoreExecutable = new() { Text = "Restore Retail Developer Modes", AutoSize = true };
    private string? _selectedExecutablePath;
    private bool _loading;

    public DeveloperToolsForm(DeveloperOptionsArchive archive, string metPath, string? executablePath)
    {
        _archive = archive;
        _metPath = metPath;
        Text = "Developer Tools - Backyard Baseball PS2";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(1100, 760);
        MinimumSize = new Size(860, 620);
        AutoScaleMode = AutoScaleMode.Dpi;

        Label instructions = new()
        {
            Dock = DockStyle.Top,
            Height = 62,
            Padding = new Padding(12, 9, 12, 4),
            Text = "Edit developer switches still honored by the retail game and enable dormant executable-only modes. " +
                   "DATA.MET and SLUS changes are saved separately, and each save creates its own timestamped backup."
        };
        TabControl tabs = new() { Dock = DockStyle.Fill, Padding = new Point(14, 5) };
        tabs.TabPages.Add(BuildRuntimePage());
        tabs.TabPages.Add(BuildExecutablePage());
        Button close = new() { Text = "Close", AutoSize = true, DialogResult = DialogResult.Cancel };
        FlowLayoutPanel footer = new()
        {
            Dock = DockStyle.Bottom,
            Height = 50,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(8)
        };
        footer.Controls.Add(close);
        Controls.Add(tabs);
        Controls.Add(_status);
        Controls.Add(footer);
        Controls.Add(instructions);
        CancelButton = close;

        PopulateRuntimeOptions();
        _resultCheat.Items.AddRange(new object[]
        {
            new CheatModeItem("Normal season results", DeveloperUserCheatMode.Normal),
            new CheatModeItem("Force the user's team to win", DeveloperUserCheatMode.ForceWins),
            new CheatModeItem("Force the user's team to lose", DeveloperUserCheatMode.ForceLosses)
        });
        _exactHit.CheckedChanged += (_, _) => UpdateHitControls();
        LoadExecutable(executablePath);
        _status.Text = $"Loaded {_archive.Options.Count(option => option.RetailSupported)} working runtime developer options.";
    }

    public bool ArchiveModified { get; private set; }
    public bool ExecutableModified { get; private set; }

    private TabPage BuildRuntimePage()
    {
        TabPage page = new("Runtime Options") { Padding = new Padding(8) };
        _options.Dock = DockStyle.Fill;
        _options.AllowUserToAddRows = false;
        _options.AllowUserToDeleteRows = false;
        _options.AllowUserToResizeRows = false;
        _options.RowHeadersVisible = false;
        _options.BackgroundColor = SystemColors.Window;
        _options.EditMode = DataGridViewEditMode.EditOnEnter;
        _options.SelectionMode = DataGridViewSelectionMode.CellSelect;
        _options.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(246, 248, 250);
        _options.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Group", ReadOnly = true, Width = 145 });
        _options.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Setting", ReadOnly = true, Width = 215 });
        _options.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Value", Width = 150 });
        _options.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "What it does", ReadOnly = true, AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            MinimumWidth = 320
        });
        _options.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_options.IsCurrentCellDirty) _options.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };

        Label note = new()
        {
            Dock = DockStyle.Top,
            Height = 46,
            Padding = new Padding(4, 5, 4, 5),
            Text = "Status logging is normally visible in the PCSX2 console or log. " +
                   "The assertion key is shown for reference but is not read by the retail executable."
        };
        FlowLayoutPanel buttons = new()
        {
            Dock = DockStyle.Bottom, Height = 48, FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false, Padding = new Padding(4, 7, 4, 4)
        };
        Button save = new() { Text = "Save Runtime Options to DATA.MET", AutoSize = true };
        Button reset = new() { Text = "Reset Unsaved Values", AutoSize = true };
        save.Click += (_, _) => SaveRuntimeOptions();
        reset.Click += (_, _) => PopulateRuntimeOptions();
        buttons.Controls.Add(save);
        buttons.Controls.Add(reset);
        page.Controls.Add(_options);
        page.Controls.Add(buttons);
        page.Controls.Add(note);
        return page;
    }

    private TabPage BuildExecutablePage()
    {
        TabPage page = new("Executable Modes") { Padding = new Padding(8) };
        TableLayoutPanel shell = new()
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5,
            Padding = new Padding(4)
        };
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        shell.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        shell.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        shell.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        shell.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        GroupBox fileGroup = new() { Text = "Game Executable", Dock = DockStyle.Top, Height = 95, Padding = new Padding(10) };
        TableLayoutPanel fileLayout = new() { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2 };
        fileLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        fileLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        Button browse = new() { Text = "Choose SLUS_208.65...", AutoSize = true };
        browse.Click += (_, _) => BrowseExecutable();
        fileLayout.Controls.Add(_executablePath, 0, 0);
        fileLayout.Controls.Add(browse, 1, 0);
        fileLayout.Controls.Add(_executableState, 0, 1);
        fileLayout.SetColumnSpan(_executableState, 2);
        fileGroup.Controls.Add(fileLayout);

        GroupBox modes = new() { Text = "Dormant Game Modes", Dock = DockStyle.Top, Height = 145, Padding = new Padding(12) };
        TableLayoutPanel modeLayout = new() { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3 };
        modeLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        modeLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        modeLayout.Controls.Add(_oneInning, 0, 0);
        modeLayout.SetColumnSpan(_oneInning, 2);
        modeLayout.Controls.Add(_cpuSeason, 0, 1);
        modeLayout.SetColumnSpan(_cpuSeason, 2);
        modeLayout.Controls.Add(new Label { Text = "Season result:", AutoSize = true, Margin = new Padding(0, 7, 8, 0) }, 0, 2);
        modeLayout.Controls.Add(_resultCheat, 1, 2);
        modes.Controls.Add(modeLayout);

        GroupBox hit = new() { Text = "Exact Hit Override (Experimental)", Dock = DockStyle.Top, Height = 230, Padding = new Padding(12) };
        TableLayoutPanel hitLayout = new() { Dock = DockStyle.Fill, ColumnCount = 7, RowCount = 4 };
        hitLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        for (int index = 0; index < 6; index++) hitLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 16.66F));
        hitLayout.Controls.Add(_exactHit, 0, 0);
        hitLayout.SetColumnSpan(_exactHit, 7);
        string[] labels = { "Origin X", "Origin Y", "Origin Z", "Velocity X", "Velocity Y", "Velocity Z" };
        for (int index = 0; index < labels.Length; index++)
        {
            hitLayout.Controls.Add(new Label { Text = labels[index], AutoSize = true, Margin = new Padding(3, 6, 3, 1) }, index + 1, 1);
            _hitValues[index].Dock = DockStyle.Fill;
            hitLayout.Controls.Add(_hitValues[index], index + 1, 2);
        }
        Label axisNote = new()
        {
            Text = "Y is up; center field is negative Z. Origin is the ball position and velocity controls direction and power.",
            AutoSize = true, Margin = new Padding(0, 8, 12, 0)
        };
        hitLayout.Controls.Add(axisNote, 0, 3);
        hitLayout.SetColumnSpan(axisNote, 4);
        FlowLayoutPanel presets = new() { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        Button centerDrive = new() { Text = "Center-field Drive", AutoSize = true };
        Button highFly = new() { Text = "High Fly Ball", AutoSize = true };
        Button rightDrive = new() { Text = "Right-field Drive", AutoSize = true };
        centerDrive.Click += (_, _) => SetHitValues(0, 70, 0, 0, 550, -1100);
        highFly.Click += (_, _) => SetHitValues(0, 70, 0, 0, 900, -900);
        rightDrive.Click += (_, _) => SetHitValues(0, 70, 0, 450, 550, -1050);
        presets.Controls.AddRange(new Control[] { centerDrive, highFly, rightDrive });
        hitLayout.Controls.Add(presets, 4, 3);
        hitLayout.SetColumnSpan(presets, 3);
        hit.Controls.Add(hitLayout);

        FlowLayoutPanel actions = new()
        {
            Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false, Padding = new Padding(0, 8, 0, 0)
        };
        _applyExecutable.Click += (_, _) => ApplyExecutable();
        _restoreExecutable.Click += (_, _) => RestoreExecutable();
        actions.Controls.Add(_applyExecutable);
        actions.Controls.Add(_restoreExecutable);

        shell.Controls.Add(fileGroup, 0, 0);
        shell.Controls.Add(modes, 0, 1);
        shell.Controls.Add(hit, 0, 2);
        shell.Controls.Add(new Label
        {
            Dock = DockStyle.Top,
            Height = 54,
            Padding = new Padding(5, 10, 5, 5),
            ForeColor = SystemColors.GrayText,
            Text = "These patches target verified USA SLUS_208.65 instructions and can coexist with the editor's unlock patch. " +
                   "Restore Retail Developer Modes removes only the patches on this page."
        }, 0, 3);
        shell.Controls.Add(actions, 0, 4);
        page.Controls.Add(shell);
        return page;
    }

    private void PopulateRuntimeOptions()
    {
        _loading = true;
        _options.Rows.Clear();
        foreach (DeveloperOption option in _archive.Options)
        {
            int index = _options.Rows.Add(option.Category, option.Label, option.Value, option.Description);
            DataGridViewRow row = _options.Rows[index];
            row.Tag = option;
            row.Cells[1].ToolTipText = $"[{option.Section}] {option.Key}";
            if (option.Choices.Count > 0)
            {
                DataGridViewComboBoxCell cell = new()
                {
                    FlatStyle = FlatStyle.Flat,
                    DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton
                };
                foreach (DeveloperOptionChoice choice in option.Choices) cell.Items.Add(choice);
                DeveloperOptionChoice? selected = option.Choices.FirstOrDefault(choice =>
                    choice.Value.Equals(option.Value, StringComparison.Ordinal));
                if (selected == null)
                {
                    selected = new DeveloperOptionChoice(option.Value,
                        $"{option.Value} — Unknown/custom value (preserved)");
                    cell.Items.Add(selected);
                }
                cell.Value = selected;
                row.Cells[2] = cell;
            }
            else if (option.Kind == GameplayTweakValueKind.Boolean)
            {
                DataGridViewComboBoxCell cell = new() { FlatStyle = FlatStyle.Flat };
                cell.Items.AddRange("False", "True");
                cell.Value = option.Value.Equals("True", StringComparison.OrdinalIgnoreCase) ? "True" : "False";
                row.Cells[2] = cell;
            }
            if (!option.RetailSupported)
            {
                row.Cells[2].ReadOnly = true;
                row.DefaultCellStyle.ForeColor = SystemColors.GrayText;
                row.Cells[3].Value = option.Description + " It is preserved but cannot be changed here.";
            }
        }
        _loading = false;
        _status.Text = "Unsaved runtime values reset to the opened archive.";
    }

    private void SaveRuntimeOptions()
    {
        _options.EndEdit();
        Dictionary<DeveloperOption, string> edits = new();
        foreach (DataGridViewRow row in _options.Rows)
        {
            DeveloperOption option = (DeveloperOption)row.Tag!;
            object? cellValue = row.Cells[2].Value;
            string value = cellValue is DeveloperOptionChoice choice
                ? choice.Value
                : Convert.ToString(cellValue) ?? string.Empty;
            if (!option.RetailSupported || value.Equals(option.Value, StringComparison.OrdinalIgnoreCase)) continue;
            edits[option] = value;
        }
        if (edits.Count == 0)
        {
            MessageBox.Show(this, "No runtime developer options were changed.", "Nothing to Save",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (MessageBox.Show(this,
                $"Write {edits.Count} developer option change{(edits.Count == 1 ? string.Empty : "s")} to DATA.MET?\n\n" +
                "A timestamped backup will be created first.", "Save Runtime Developer Options",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        try
        {
            DeveloperOptionsSaveResult result = _archive.SaveWithBackup(edits);
            ArchiveModified = true;
            _archive = DeveloperOptionsArchive.Load(_metPath);
            PopulateRuntimeOptions();
            _status.Text = $"Saved {result.ChangedOptionCount} runtime option(s). Backup: {result.BackupPath}";
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Unable to Save Developer Options",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void BrowseExecutable()
    {
        using OpenFileDialog dialog = new()
        {
            Title = "Open Backyard Baseball USA executable",
            FileName = "SLUS_208.65",
            Filter = "Backyard Baseball executable (SLUS_208.65)|SLUS_208.65|All files (*.*)|*.*"
        };
        if (dialog.ShowDialog(this) == DialogResult.OK) LoadExecutable(dialog.FileName);
    }

    private void LoadExecutable(string? path)
    {
        _selectedExecutablePath = null;
        _executablePath.Text = path ?? "No sibling SLUS_208.65 was found. Choose the executable manually.";
        _applyExecutable.Enabled = false;
        _restoreExecutable.Enabled = false;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            _executableState.Text = "No executable loaded.";
            return;
        }
        try
        {
            GameExecutableDeveloperState state = GameExecutableDeveloperPatcher.Inspect(path);
            _selectedExecutablePath = path;
            _executablePath.Text = path;
            LoadExecutableState(state);
            _applyExecutable.Enabled = true;
            _restoreExecutable.Enabled = state.IsPatched;
            _executableState.Text = state.IsPatched
                ? "Supported USA executable — developer patches are active."
                : "Supported USA executable — retail developer-mode instructions are unchanged.";
        }
        catch (Exception exception)
        {
            _executableState.Text = exception.Message;
        }
    }

    private void LoadExecutableState(GameExecutableDeveloperState state)
    {
        _loading = true;
        _oneInning.Checked = state.OneInningGames;
        _cpuSeason.Checked = state.CpuSeasonPlay;
        _resultCheat.SelectedItem = _resultCheat.Items.Cast<CheatModeItem>()
            .First(item => item.Value == state.UserCheatMode);
        _exactHit.Checked = state.HitOverride != null;
        DeveloperHitOverride values = state.HitOverride ?? new DeveloperHitOverride(0, 70, 0, 0, 650, -1100);
        SetHitValues(values.OriginX, values.OriginY, values.OriginZ,
            values.VelocityX, values.VelocityY, values.VelocityZ);
        _loading = false;
        UpdateHitControls();
    }

    private void ApplyExecutable()
    {
        if (_selectedExecutablePath == null) return;
        GameExecutableDeveloperState desired;
        try { desired = ReadDesiredExecutableState(); }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Invalid Developer Patch", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (MessageBox.Show(this,
                "Apply the selected developer modes to SLUS_208.65?\n\nA timestamped executable backup will be created first.",
                "Apply Developer Modes", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        try
        {
            string backup = GameExecutableDeveloperPatcher.ApplyWithBackup(_selectedExecutablePath, desired);
            ExecutableModified = true;
            LoadExecutable(_selectedExecutablePath);
            _status.Text = $"Developer modes applied. Executable backup: {backup}";
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Unable to Patch Game Executable",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void RestoreExecutable()
    {
        if (_selectedExecutablePath == null) return;
        if (MessageBox.Show(this,
                "Restore all developer-mode instructions controlled by this page?\n\nOther patches, including content unlocks, are preserved.",
                "Restore Retail Developer Modes", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        try
        {
            string backup = GameExecutableDeveloperPatcher.ApplyWithBackup(_selectedExecutablePath,
                new GameExecutableDeveloperState(false, false, DeveloperUserCheatMode.Normal, null));
            ExecutableModified = true;
            LoadExecutable(_selectedExecutablePath);
            _status.Text = $"Retail developer modes restored. Backup: {backup}";
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Unable to Restore Game Executable",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private GameExecutableDeveloperState ReadDesiredExecutableState()
    {
        DeveloperUserCheatMode cheat = (_resultCheat.SelectedItem as CheatModeItem)?.Value
            ?? DeveloperUserCheatMode.Normal;
        DeveloperHitOverride? hit = _exactHit.Checked
            ? new DeveloperHitOverride((float)_hitValues[0].Value, (float)_hitValues[1].Value,
                (float)_hitValues[2].Value, (float)_hitValues[3].Value,
                (float)_hitValues[4].Value, (float)_hitValues[5].Value)
            : null;
        return new GameExecutableDeveloperState(_oneInning.Checked, _cpuSeason.Checked, cheat, hit);
    }

    private void UpdateHitControls()
    {
        foreach (NumericUpDown number in _hitValues) number.Enabled = _exactHit.Checked;
    }

    private void SetHitValues(float originX, float originY, float originZ,
        float velocityX, float velocityY, float velocityZ)
    {
        float[] values = { originX, originY, originZ, velocityX, velocityY, velocityZ };
        for (int index = 0; index < values.Length; index++)
            _hitValues[index].Value = Math.Clamp((decimal)values[index], _hitValues[index].Minimum, _hitValues[index].Maximum);
        if (!_loading) _exactHit.Checked = true;
    }

    private static NumericUpDown CreateHitNumber() => new()
    {
        DecimalPlaces = 2,
        Minimum = -100000,
        Maximum = 100000,
        Increment = 10,
        ThousandsSeparator = true
    };

    private sealed record CheatModeItem(string Label, DeveloperUserCheatMode Value)
    {
        public override string ToString() => Label;
    }
}
