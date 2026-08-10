using PS2_DATA_File_Extractor.FileOperations;

namespace PS2_DATA_File_Extractor;

public sealed class StadiumEnvironmentEditorForm : Form
{
    private readonly StadiumEnvironmentArchive _archive;
    private readonly ComboBox _stadiums = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
    private readonly Label _source = new() { AutoEllipsis = true, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Label _summary = new() { AutoSize = true, Margin = new Padding(14, 8, 0, 0) };
    private readonly DataGridView _fieldGrid = CreateGrid();
    private readonly DataGridView _collisionGrid = CreateGrid();
    private readonly DataGridView _ambientGrid = CreateGrid();
    private readonly ListBox _ambientList = new() { Dock = DockStyle.Fill, IntegralHeight = false };
    private readonly TextBox _rawText = new()
    {
        Dock = DockStyle.Fill,
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Both,
        WordWrap = false,
        Font = new Font("Consolas", 9F)
    };
    private readonly TabControl _tabs = new() { Dock = DockStyle.Fill };
    private readonly Label _status = new() { Dock = DockStyle.Bottom, Height = 30, Padding = new Padding(12, 5, 12, 2) };
    private StadiumEnvironment? _current;
    private bool _loading;

    public StadiumEnvironmentEditorForm(StadiumEnvironmentArchive archive, string metPath)
    {
        _archive = archive;
        Text = "Stadium Environment Editor - DATA.MET";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(1060, 720);
        MinimumSize = new Size(820, 580);
        AutoScaleMode = AutoScaleMode.Dpi;

        Label instructions = new()
        {
            Dock = DockStyle.Top,
            Height = 56,
            Padding = new Padding(12, 8, 12, 4),
            Text = "Edit stadium lighting, camera positions, collision tags, ambient models, particles, positions, " +
                   "animations, and movement speeds stored in fielddata.txt. Unknown lines and comments are preserved."
        };
        TableLayoutPanel selector = new()
        {
            Dock = DockStyle.Top,
            Height = 48,
            ColumnCount = 4,
            Padding = new Padding(12, 6, 12, 4)
        };
        selector.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        selector.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        selector.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        selector.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        selector.Controls.Add(new Label
        {
            Text = "Stadium:", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 8, 8, 0)
        }, 0, 0);
        selector.Controls.Add(_stadiums, 1, 0);
        selector.Controls.Add(_summary, 2, 0);
        Button useAllAmbients = new()
        {
            Text = "Set Count to All Ambient Blocks", AutoSize = true, Anchor = AnchorStyles.Right
        };
        useAllAmbients.Click += (_, _) => UseAllAmbientBlocks();
        selector.Controls.Add(useAllAmbients, 3, 0);

        Panel pathPanel = new() { Dock = DockStyle.Top, Height = 28, Padding = new Padding(12, 0, 12, 3) };
        pathPanel.Controls.Add(_source);
        BuildTabs();

        FlowLayoutPanel buttons = new()
        {
            Dock = DockStyle.Bottom,
            Height = 56,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(8),
            WrapContents = false
        };
        Button cancel = new() { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        Button save = new() { Text = "Save Stadiums to DATA.MET", AutoSize = true };
        Button reset = new() { Text = "Reset All Unsaved Changes", AutoSize = true };
        save.Click += Save_Click;
        reset.Click += (_, _) => ResetAll();
        buttons.Controls.AddRange(new Control[] { cancel, save, reset });

        Controls.Add(_tabs);
        Controls.Add(_status);
        Controls.Add(buttons);
        Controls.Add(pathPanel);
        Controls.Add(selector);
        Controls.Add(instructions);
        AcceptButton = save;
        CancelButton = cancel;

        _stadiums.Items.AddRange(_archive.Stadiums.Cast<object>().ToArray());
        _stadiums.SelectedIndexChanged += (_, _) => LoadSelectedStadium();
        _ambientList.SelectedIndexChanged += (_, _) => LoadSelectedAmbient();
        _tabs.SelectedIndexChanged += (_, _) => RefreshRawText();
        HookGrid(_fieldGrid);
        HookGrid(_collisionGrid);
        HookGrid(_ambientGrid);
        if (_stadiums.Items.Count > 0) _stadiums.SelectedIndex = 0;
        _status.Text = $"Loaded {_archive.Stadiums.Count} stadium variants from {metPath}.";
    }

    private void BuildTabs()
    {
        TabPage fieldPage = new("Field & Cameras") { Padding = new Padding(6) };
        fieldPage.Controls.Add(_fieldGrid);
        TabPage collisionPage = new("Collision Tags") { Padding = new Padding(6) };
        collisionPage.Controls.Add(_collisionGrid);

        TabPage ambientPage = new("Ambient Objects") { Padding = new Padding(6) };
        SplitContainer split = new() { Dock = DockStyle.Fill, SplitterDistance = 310, FixedPanel = FixedPanel.Panel1 };
        split.Panel1.Controls.Add(_ambientList);
        split.Panel2.Controls.Add(_ambientGrid);
        ambientPage.Controls.Add(split);

        TabPage rawPage = new("Raw Preview") { Padding = new Padding(6) };
        rawPage.Controls.Add(_rawText);
        rawPage.Controls.Add(new Label
        {
            Dock = DockStyle.Top,
            Height = 34,
            Padding = new Padding(4, 4, 4, 4),
            Text = "Read-only preview of the preserved file. Use the Advanced DATA.MET Browser for unrestricted raw editing."
        });
        _tabs.TabPages.AddRange(new[] { fieldPage, collisionPage, ambientPage, rawPage });
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
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Setting", ReadOnly = true, Width = 220
        });
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Directive", ReadOnly = true, Width = 150
        });
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Value", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, MinimumWidth = 260
        });
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Format", ReadOnly = true, Width = 105
        });
        return grid;
    }

    private void HookGrid(DataGridView grid)
    {
        grid.CellValidating += Grid_CellValidating;
        grid.CellValueChanged += Grid_CellValueChanged;
        grid.DataError += (_, _) => { };
    }

    private void LoadSelectedStadium()
    {
        if (_loading) return;
        _current = _stadiums.SelectedItem as StadiumEnvironment;
        if (_current == null) return;
        _source.Text = _current.SourcePath;
        LoadSettings(_fieldGrid, _current.Document.FieldSettings);
        LoadSettings(_collisionGrid, _current.Document.CollisionSettings);
        LoadAmbientList();
        RefreshRawText();
        UpdateSummaryAndStatus();
    }

    private void LoadAmbientList()
    {
        if (_current == null) return;
        int selectedIndex = _ambientList.SelectedIndex;
        _loading = true;
        _ambientList.BeginUpdate();
        _ambientList.Items.Clear();
        int declared = _current.Document.DeclaredAmbientCount;
        foreach (FieldDataAmbient ambient in _current.Document.Ambients)
        {
            _ambientList.Items.Add(new AmbientListItem(ambient, ambient.Index < declared));
        }
        _ambientList.EndUpdate();
        _ambientList.SelectedIndex = _ambientList.Items.Count == 0 ? -1 : Math.Clamp(selectedIndex, 0, _ambientList.Items.Count - 1);
        if (_ambientList.SelectedIndex < 0 && _ambientList.Items.Count > 0) _ambientList.SelectedIndex = 0;
        _loading = false;
        LoadSelectedAmbient();
    }

    private void LoadSelectedAmbient()
    {
        if (_loading) return;
        FieldDataAmbient? ambient = (_ambientList.SelectedItem as AmbientListItem)?.Ambient;
        LoadSettings(_ambientGrid, ambient?.Settings ?? Array.Empty<FieldDataSetting>());
    }

    private void LoadSettings(DataGridView grid, IReadOnlyList<FieldDataSetting> settings)
    {
        _loading = true;
        grid.Rows.Clear();
        foreach (FieldDataSetting setting in settings)
        {
            int index = grid.Rows.Add(setting.FriendlyName, setting.Key, setting.Value, KindName(setting.Kind));
            grid.Rows[index].Tag = setting;
            if (setting.Key.Equals("numAmbs", StringComparison.OrdinalIgnoreCase))
            {
                grid.Rows[index].Cells[2].ToolTipText = "The executable reads exactly this many consecutive amb { } blocks.";
            }
        }
        _loading = false;
    }

    private void Grid_CellValidating(object? sender, DataGridViewCellValidatingEventArgs e)
    {
        if (_loading || e.RowIndex < 0 || e.ColumnIndex != 2) return;
        DataGridView grid = (DataGridView)sender!;
        FieldDataSetting setting = (FieldDataSetting)grid.Rows[e.RowIndex].Tag!;
        if (!FieldDataValue.TryNormalize(setting.Kind, Convert.ToString(e.FormattedValue) ?? string.Empty,
                out _, out string error))
        {
            e.Cancel = true;
            grid.Rows[e.RowIndex].ErrorText = error;
        }
        else
        {
            grid.Rows[e.RowIndex].ErrorText = string.Empty;
        }
    }

    private void Grid_CellValueChanged(object? sender, DataGridViewCellEventArgs e)
    {
        if (_loading || _current == null || e.RowIndex < 0 || e.ColumnIndex != 2) return;
        DataGridView grid = (DataGridView)sender!;
        FieldDataSetting setting = (FieldDataSetting)grid.Rows[e.RowIndex].Tag!;
        string input = Convert.ToString(grid.Rows[e.RowIndex].Cells[2].Value) ?? string.Empty;
        if (!FieldDataValue.TryNormalize(setting.Kind, input, out string normalized, out _)) return;
        setting.Value = normalized;
        _loading = true;
        grid.Rows[e.RowIndex].Cells[2].Value = normalized;
        _loading = false;
        if (setting.Key.Equals("numAmbs", StringComparison.OrdinalIgnoreCase)) LoadAmbientList();
        RefreshRawText();
        UpdateSummaryAndStatus();
    }

    private void UseAllAmbientBlocks()
    {
        if (_current == null) return;
        if (!_current.Document.TrySetDeclaredAmbientCount(_current.Document.Ambients.Count))
        {
            MessageBox.Show(this, "This stadium has no editable numAmbs directive.", "Ambient Count",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        LoadSettings(_fieldGrid, _current.Document.FieldSettings);
        LoadAmbientList();
        RefreshRawText();
        UpdateSummaryAndStatus();
    }

    private void RefreshRawText()
    {
        if (_current == null) return;
        _rawText.Text = _current.Document.ToString();
    }

    private void UpdateSummaryAndStatus()
    {
        if (_current == null) return;
        int declared = _current.Document.DeclaredAmbientCount;
        int actual = _current.Document.Ambients.Count;
        _summary.Text = declared == actual
            ? $"{actual} ambient blocks"
            : $"Game loads {declared} of {actual} ambient blocks";
        int changed = _archive.ChangedStadiumCount;
        _status.Text = changed == 0 ? "No unsaved stadium changes."
            : $"{changed} stadium file{(changed == 1 ? string.Empty : "s")} changed.";
    }

    private void ResetAll()
    {
        int selected = _stadiums.SelectedIndex;
        _loading = true;
        _archive.ResetAll();
        _stadiums.Items.Clear();
        _stadiums.Items.AddRange(_archive.Stadiums.Cast<object>().ToArray());
        _stadiums.SelectedIndex = _stadiums.Items.Count == 0 ? -1 : Math.Clamp(selected, 0, _stadiums.Items.Count - 1);
        _loading = false;
        LoadSelectedStadium();
        _status.Text = "All unsaved stadium changes were reset.";
    }

    private void Save_Click(object? sender, EventArgs e)
    {
        _fieldGrid.EndEdit();
        _collisionGrid.EndEdit();
        _ambientGrid.EndEdit();
        int changed = _archive.ChangedStadiumCount;
        if (changed == 0)
        {
            MessageBox.Show(this, "No stadium files were changed.", "Nothing to Save",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (MessageBox.Show(this,
                $"Write changes to {changed} stadium fielddata.txt file{(changed == 1 ? string.Empty : "s")}?\n\n" +
                "A timestamped DATA.MET backup will be created first.",
                "Save Stadium Environments", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;

        try
        {
            UseWaitCursor = true;
            Enabled = false;
            StadiumEnvironmentSaveResult result = _archive.SaveWithBackup();
            string rebuild = result.RebuiltArchive ? "\nThe archive was resized with sector alignment preserved." : string.Empty;
            MessageBox.Show(this,
                $"Saved {result.ChangedStadiumCount} stadium file{(result.ChangedStadiumCount == 1 ? string.Empty : "s")}.\n\n" +
                $"Backup: {result.BackupPath}{rebuild}",
                "Stadium Environments Saved", MessageBoxButtons.OK, MessageBoxIcon.Information);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception exception)
        {
            MessageBox.Show(this,
                $"The stadium changes could not be saved. The archive was restored if a backup was created.\n\n{exception.Message}",
                "Unable to Save Stadiums", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Enabled = true;
            UseWaitCursor = false;
        }
    }

    private static string KindName(FieldDataValueKind kind) => kind switch
    {
        FieldDataValueKind.Integer => "Integer",
        FieldDataValueKind.Number => "Number",
        FieldDataValueKind.NumericList => "Number list",
        FieldDataValueKind.Flag => "Flag",
        _ => "Text / asset"
    };

    private sealed record AmbientListItem(FieldDataAmbient Ambient, bool IsLoaded)
    {
        public override string ToString() => $"{Ambient.Index + 1:00}. {Ambient.DisplayName}" +
            (IsLoaded ? string.Empty : "  [not loaded]");
    }
}
