using PS2_DATA_File_Extractor.FileOperations;

namespace PS2_DATA_File_Extractor;

public sealed class TeamLeagueSetupEditorForm : Form
{
    private readonly TeamLeagueArchive _archive;
    private readonly ComboBox _filter = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 190 };
    private readonly DataGridView _teams = new();
    private readonly DataGridView _summary = new();
    private readonly SplitContainer _mainSplit = new();
    private readonly Label _counts = new() { AutoSize = true, Margin = new Padding(16, 7, 0, 0) };
    private readonly Label _status = new() { Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Button _moveUp = new() { Text = "Move Up", AutoSize = true };
    private readonly Button _moveDown = new() { Text = "Move Down", AutoSize = true };
    private readonly Button _activate = new() { Text = "Set Active", AutoSize = true };
    private readonly Button _deactivate = new() { Text = "Set Inactive", AutoSize = true };
    private bool _loading;

    public TeamLeagueSetupEditorForm(TeamLeagueArchive archive, string metPath)
    {
        _archive = archive ?? throw new ArgumentNullException(nameof(archive));
        ArgumentException.ThrowIfNullOrWhiteSpace(metPath);

        Text = "Team and League Setup Editor - DATA.MET";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(1120, 760);
        MinimumSize = new Size(900, 620);
        AutoScaleMode = AutoScaleMode.Dpi;

        ConfigureTeamsGrid();
        ConfigureSummaryGrid();
        Controls.Add(BuildLayout(metPath));

        _filter.Items.AddRange(new object[]
        {
            new FilterItem("All teams", null, null),
            new FilterItem("Active teams", null, true),
            new FilterItem("Inactive teams", null, false),
            new FilterItem("AL West", BaseballDivision.ALWest, null),
            new FilterItem("AL Central", BaseballDivision.ALCentral, null),
            new FilterItem("AL East", BaseballDivision.ALEast, null),
            new FilterItem("NL West", BaseballDivision.NLWest, null),
            new FilterItem("NL Central", BaseballDivision.NLCentral, null),
            new FilterItem("NL East", BaseballDivision.NLEast, null)
        });
        _filter.SelectedIndexChanged += (_, _) => ReloadTeams();
        _teams.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_teams.IsCurrentCellDirty) _teams.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        _teams.CellValueChanged += TeamCellValueChanged;
        _teams.DataError += (_, args) => args.ThrowException = false;
        _teams.SelectionChanged += (_, _) => UpdateActionState();
        _moveUp.Click += (_, _) => MoveSelected(-1);
        _moveDown.Click += (_, _) => MoveSelected(1);
        _activate.Click += (_, _) => SetSelectedActive(true);
        _deactivate.Click += (_, _) => SetSelectedActive(false);

        _filter.SelectedIndex = 0;
        ReloadAll();
        Shown += (_, _) => ApplyDefaultSplitterLayout();
    }

    private Control BuildLayout(string metPath)
    {
        TableLayoutPanel shell = new()
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12, 10, 12, 10),
            ColumnCount = 1,
            RowCount = 7
        };
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        shell.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        shell.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        shell.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        shell.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        shell.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        shell.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        shell.Controls.Add(new Label
        {
            Text = "Set each club's division, active/inactive state, and order for newly created seasons. " +
                   "These are stable game team IDs; schedule templates use a separate set of generated season slots.",
            Dock = DockStyle.Fill,
            AutoSize = true,
            MaximumSize = new Size(1500, 0),
            Margin = new Padding(0, 0, 0, 8)
        }, 0, 0);
        shell.Controls.Add(new Label
        {
            Text = metPath,
            Dock = DockStyle.Fill,
            AutoEllipsis = true,
            Height = 25,
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(0, 0, 0, 8)
        }, 0, 1);

        FlowLayoutPanel filters = new()
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            WrapContents = false,
            Margin = new Padding(0, 0, 0, 8)
        };
        filters.Controls.Add(new Label { Text = "Show:", AutoSize = true, Margin = new Padding(0, 7, 5, 0) });
        filters.Controls.Add(_filter);
        filters.Controls.Add(_counts);
        shell.Controls.Add(filters, 0, 2);

        SplitContainer split = _mainSplit;
        split.Dock = DockStyle.Fill;
        split.Orientation = Orientation.Horizontal;
        split.FixedPanel = FixedPanel.Panel2;
        GroupBox teamsGroup = new() { Text = "League Teams", Dock = DockStyle.Fill, Padding = new Padding(8) };
        teamsGroup.Controls.Add(_teams);
        GroupBox summaryGroup = new() { Text = "Division Summary", Dock = DockStyle.Fill, Padding = new Padding(8) };
        summaryGroup.Controls.Add(_summary);
        split.Panel1.Controls.Add(teamsGroup);
        split.Panel2.Controls.Add(summaryGroup);
        shell.Controls.Add(split, 0, 3);

        FlowLayoutPanel actions = new()
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            WrapContents = false,
            Margin = new Padding(0, 7, 0, 7)
        };
        actions.Controls.AddRange(new Control[] { _moveUp, _moveDown, _activate, _deactivate });
        actions.Controls.Add(CreateActionButton("Reset Unsaved Changes", (_, _) =>
        {
            _archive.Setup.RestoreOriginal();
            ReloadAll();
        }));
        actions.Controls.Add(CreateActionButton("Restore Retail Alignment", (_, _) => RestoreRetailAlignment()));
        actions.Controls.Add(new Label
        {
            Text = "Move Up/Down changes the selected club's order inside its current active or inactive division list.",
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(14, 7, 0, 0)
        });
        shell.Controls.Add(actions, 0, 4);
        shell.Controls.Add(_status, 0, 5);
        shell.Controls.Add(BuildBottomButtons(), 0, 6);
        return shell;
    }

    private void ApplyDefaultSplitterLayout()
    {
        int available = Math.Max(0, _mainSplit.ClientSize.Height - _mainSplit.SplitterWidth);
        if (available == 0) return;
        int topMinimum = Math.Min(260, available);
        int bottomMinimum = Math.Min(125, Math.Max(0, available - topMinimum));
        int preferredBottom = Math.Min(155, Math.Max(bottomMinimum, available / 3));
        int distance = Math.Clamp(available - preferredBottom, topMinimum, Math.Max(topMinimum, available - bottomMinimum));
        _mainSplit.Panel1MinSize = 0;
        _mainSplit.Panel2MinSize = 0;
        _mainSplit.SplitterDistance = distance;
        _mainSplit.Panel1MinSize = topMinimum;
        _mainSplit.Panel2MinSize = Math.Min(bottomMinimum, Math.Max(0, available - distance));
    }

    private void ConfigureTeamsGrid()
    {
        _teams.Dock = DockStyle.Fill;
        _teams.AllowUserToAddRows = false;
        _teams.AllowUserToDeleteRows = false;
        _teams.AllowUserToResizeRows = false;
        _teams.AutoGenerateColumns = false;
        _teams.MultiSelect = false;
        _teams.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _teams.RowHeadersVisible = false;
        _teams.BackgroundColor = SystemColors.Window;
        _teams.EditMode = DataGridViewEditMode.EditOnEnter;

        _teams.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Order",
            HeaderText = "Order",
            Width = 65,
            ReadOnly = true,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        _teams.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Id",
            HeaderText = "Team ID",
            Width = 70,
            ReadOnly = true,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        _teams.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Team",
            HeaderText = "Team",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            FillWeight = 42,
            ReadOnly = true,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        _teams.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "League",
            HeaderText = "League",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            FillWeight = 25,
            ReadOnly = true,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });

        DataGridViewComboBoxColumn division = new()
        {
            Name = "Division",
            HeaderText = "Division",
            Width = 145,
            DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton,
            FlatStyle = FlatStyle.Standard,
            ValueType = typeof(BaseballDivision),
            DataSource = BaseballDivisionInfo.All.Select(value => new DivisionChoice(value, value.DisplayName())).ToList(),
            ValueMember = nameof(DivisionChoice.Value),
            DisplayMember = nameof(DivisionChoice.Name)
        };
        _teams.Columns.Add(division);
        _teams.Columns.Add(new DataGridViewCheckBoxColumn
        {
            Name = "Active",
            HeaderText = "Active",
            Width = 72,
            ThreeState = false,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
    }

    private void ConfigureSummaryGrid()
    {
        _summary.Dock = DockStyle.Fill;
        _summary.AllowUserToAddRows = false;
        _summary.AllowUserToDeleteRows = false;
        _summary.AllowUserToResizeRows = false;
        _summary.ReadOnly = true;
        _summary.RowHeadersVisible = false;
        _summary.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _summary.BackgroundColor = SystemColors.Window;
        _summary.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _summary.Columns.Add("Division", "Division");
        _summary.Columns.Add("Active", "Active");
        _summary.Columns.Add("Inactive", "Inactive");
        _summary.Columns.Add("Teams", "Active order");
        _summary.Columns[0].FillWeight = 18;
        _summary.Columns[1].FillWeight = 10;
        _summary.Columns[2].FillWeight = 10;
        _summary.Columns[3].FillWeight = 62;
    }

    private Control BuildBottomButtons()
    {
        TableLayoutPanel row = new() { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2 };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        FlowLayoutPanel buttons = new()
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Anchor = AnchorStyles.Right
        };
        Button save = new() { Text = "Save League Setup to DATA.MET", AutoSize = true };
        save.Click += (_, _) => SaveLeagueSetup();
        Button cancel = new() { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        buttons.Controls.Add(save);
        buttons.Controls.Add(cancel);
        row.Controls.Add(new Panel(), 0, 0);
        row.Controls.Add(buttons, 1, 0);
        AcceptButton = save;
        CancelButton = cancel;
        return row;
    }

    private static Button CreateActionButton(string text, EventHandler click)
    {
        Button button = new() { Text = text, AutoSize = true };
        button.Click += click;
        return button;
    }

    private void ReloadAll(int? selectedTeamId = null)
    {
        ReloadTeams(selectedTeamId);
        ReloadSummary();
        UpdateStatus();
    }

    private void ReloadTeams(int? selectedTeamId = null)
    {
        if (_filter.SelectedItem is not FilterItem filter) return;
        selectedTeamId ??= SelectedTeamId();
        _loading = true;
        try
        {
            _teams.Rows.Clear();
            foreach (TeamLeaguePlacement placement in _archive.Setup.GetPlacements()
                         .Where(filter.Matches))
            {
                int rowIndex = _teams.Rows.Add(
                    placement.Position + 1,
                    placement.TeamId,
                    placement.Team.Name,
                    placement.Division.LeagueName(),
                    placement.Division,
                    placement.IsActive);
                DataGridViewRow row = _teams.Rows[rowIndex];
                row.Tag = placement.TeamId;
                if (placement.Team.Name.StartsWith("Unknown / custom", StringComparison.Ordinal))
                    row.Cells["Team"].Style.ForeColor = Color.DarkOrange;
            }

            DataGridViewRow? selected = _teams.Rows.Cast<DataGridViewRow>()
                .FirstOrDefault(row => row.Tag is int id && id == selectedTeamId);
            if (selected != null)
            {
                selected.Selected = true;
                _teams.CurrentCell = selected.Cells["Team"];
            }
            else if (_teams.Rows.Count > 0)
            {
                _teams.Rows[0].Selected = true;
                _teams.CurrentCell = _teams.Rows[0].Cells["Team"];
            }
        }
        finally
        {
            _loading = false;
        }

        _counts.Text = $"{_archive.Setup.ActiveTeamCount} active  •  {_archive.Setup.InactiveTeamCount} inactive  •  {_archive.Setup.TeamCount} total";
        UpdateActionState();
    }

    private void ReloadSummary()
    {
        _summary.Rows.Clear();
        foreach (BaseballDivision division in BaseballDivisionInfo.All)
        {
            IReadOnlyList<int> active = _archive.Setup.GetTeamIds(division, true);
            IReadOnlyList<int> inactive = _archive.Setup.GetTeamIds(division, false);
            _summary.Rows.Add(
                division.DisplayName(),
                active.Count,
                inactive.Count,
                string.Join(", ", active.Select(id => BaseballTeamDefinition.ForId(id).Name)));
        }
        _summary.ClearSelection();
    }

    private void TeamCellValueChanged(object? sender, DataGridViewCellEventArgs e)
    {
        if (_loading || e.RowIndex < 0 || e.RowIndex >= _teams.Rows.Count) return;
        DataGridViewRow row = _teams.Rows[e.RowIndex];
        if (row.Tag is not int teamId) return;
        if (row.Cells["Division"].Value is not BaseballDivision division) return;
        bool active = row.Cells["Active"].Value is true;
        TeamLeaguePlacement current = _archive.Setup.GetPlacement(teamId);
        if (current.Division == division && current.IsActive == active) return;
        _archive.Setup.MoveTeam(teamId, division, active);
        ReloadAll(teamId);
    }

    private void MoveSelected(int delta)
    {
        int? teamId = SelectedTeamId();
        if (teamId is null) return;
        if (_archive.Setup.MoveWithinGroup(teamId.Value, delta)) ReloadAll(teamId);
    }

    private void SetSelectedActive(bool active)
    {
        int? teamId = SelectedTeamId();
        if (teamId is null) return;
        TeamLeaguePlacement placement = _archive.Setup.GetPlacement(teamId.Value);
        if (placement.IsActive == active) return;
        _archive.Setup.MoveTeam(teamId.Value, placement.Division, active);
        ReloadAll(teamId);
    }

    private int? SelectedTeamId() => _teams.SelectedRows.Count > 0 && _teams.SelectedRows[0].Tag is int id
        ? id
        : null;

    private void UpdateActionState()
    {
        int? teamId = SelectedTeamId();
        if (teamId is null)
        {
            _moveUp.Enabled = _moveDown.Enabled = _activate.Enabled = _deactivate.Enabled = false;
            return;
        }
        TeamLeaguePlacement placement = _archive.Setup.GetPlacement(teamId.Value);
        int count = _archive.Setup.GetTeamIds(placement.Division, placement.IsActive).Count;
        _moveUp.Enabled = placement.Position > 0;
        _moveDown.Enabled = placement.Position + 1 < count;
        _activate.Enabled = !placement.IsActive;
        _deactivate.Enabled = placement.IsActive;
    }

    private void RestoreRetailAlignment()
    {
        if (MessageBox.Show(this,
                "Replace the current setup with the retail 30-team alignment and make every retail club active?\n\n" +
                "Unknown or custom team IDs currently in the division lists will be removed.",
                "Restore Retail Alignment", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;
        _archive.Setup.RestoreRetailAlignment();
        ReloadAll();
    }

    private void UpdateStatus()
    {
        IReadOnlyList<string> errors = _archive.Setup.Validate();
        if (errors.Count > 0)
        {
            _status.ForeColor = Color.DarkRed;
            _status.Text = errors[0];
        }
        else
        {
            _status.ForeColor = SystemColors.ControlText;
            _status.Text = !_archive.HasChanges
                ? "No unsaved league changes. Changes apply when a new season is created."
                : "League setup changed. Saving creates one timestamped DATA.MET backup; existing memory-card seasons are unchanged.";
        }
    }

    private void SaveLeagueSetup()
    {
        IReadOnlyList<string> errors = _archive.Setup.Validate();
        if (errors.Count > 0)
        {
            MessageBox.Show(this, string.Join(Environment.NewLine, errors.Select(error => $"• {error}")),
                "League Setup Is Not Valid", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (!_archive.HasChanges)
        {
            MessageBox.Show(this, "No team or league values were changed.", "Nothing to Save",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (MessageBox.Show(this,
                "Write this team and division setup to DATA.MET?\n\n" +
                "A timestamped backup will be created first. The setup is used by newly created seasons.",
                "Save Team and League Setup", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;

        try
        {
            Enabled = false;
            UseWaitCursor = true;
            TeamLeagueSaveResult result = _archive.SaveWithBackup();
            MessageBox.Show(this,
                $"Saved the team and league setup.\n\nBackup: {result.BackupPath}",
                "Team and League Setup Saved", MessageBoxButtons.OK, MessageBoxIcon.Information);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception exception)
        {
            MessageBox.Show(this,
                "The league setup could not be saved. DATA.MET was restored if a backup was created.\n\n" + exception.Message,
                "Unable to Save Team and League Setup", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Enabled = true;
            UseWaitCursor = false;
        }
    }

    private sealed record FilterItem(string Name, BaseballDivision? Division, bool? Active)
    {
        public bool Matches(TeamLeaguePlacement placement) =>
            (!Division.HasValue || placement.Division == Division.Value) &&
            (!Active.HasValue || placement.IsActive == Active.Value);
        public override string ToString() => Name;
    }

    private sealed record DivisionChoice(BaseballDivision Value, string Name);
}
