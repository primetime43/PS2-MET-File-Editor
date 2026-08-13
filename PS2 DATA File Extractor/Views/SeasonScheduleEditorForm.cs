using PS2_DATA_File_Extractor.FileOperations;

namespace PS2_DATA_File_Extractor;

public sealed class SeasonScheduleEditorForm : Form
{
    private readonly SeasonScheduleArchive _archive;
    private readonly ComboBox _seasonLength = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150 };
    private readonly ComboBox _template = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 280 };
    private readonly ListBox _rounds = new() { Dock = DockStyle.Fill, IntegralHeight = false };
    private readonly DataGridView _games = new();
    private readonly DataGridView _balance = new();
    private readonly SplitContainer _mainSplit = new();
    private readonly SplitContainer _detailsSplit = new();
    private readonly Label _templatePath = new() { AutoEllipsis = true, Dock = DockStyle.Fill, ForeColor = SystemColors.GrayText };
    private readonly Label _roundSummary = new() { AutoEllipsis = true, Dock = DockStyle.Fill };
    private readonly Label _status = new() { Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Button _swap = new() { Text = "Swap Team A / B", AutoSize = true };
    private readonly Button _resetRound = new() { Text = "Reset This Round", AutoSize = true };
    private readonly Button _resetTemplate = new() { Text = "Reset This Template", AutoSize = true };
    private bool _loading;

    public SeasonScheduleEditorForm(SeasonScheduleArchive archive, string metPath)
    {
        _archive = archive ?? throw new ArgumentNullException(nameof(archive));
        ArgumentException.ThrowIfNullOrWhiteSpace(metPath);

        Text = "Season Schedule Editor - DATA.MET";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(1180, 760);
        MinimumSize = new Size(920, 620);
        AutoScaleMode = AutoScaleMode.Dpi;

        ConfigureGamesGrid();
        ConfigureBalanceGrid();
        Controls.Add(BuildLayout(metPath));

        _seasonLength.Items.Add(new SeasonLengthItem(18));
        _seasonLength.Items.Add(new SeasonLengthItem(32));
        _seasonLength.SelectedIndexChanged += (_, _) => LoadTemplates();
        _template.SelectedIndexChanged += (_, _) => LoadTemplate();
        _rounds.SelectedIndexChanged += (_, _) => LoadRound();
        _games.CellValueChanged += GamesCellValueChanged;
        _games.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_games.IsCurrentCellDirty) _games.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        _games.DataError += (_, args) => args.ThrowException = false;
        _swap.Click += (_, _) => SwapSelectedGame();
        _resetRound.Click += (_, _) => ResetRound();
        _resetTemplate.Click += (_, _) => ResetTemplate();

        _seasonLength.SelectedIndex = 0;
        UpdateStatus();
        Shown += (_, _) =>
        {
            ApplyDefaultSplitterLayout();
        };
    }

    private Control BuildLayout(string metPath)
    {
        TableLayoutPanel shell = new()
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12, 10, 12, 10),
            ColumnCount = 1,
            RowCount = 6
        };
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        shell.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        shell.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        shell.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        shell.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        shell.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        shell.Controls.Add(new Label
        {
            Text = "Edit the 18-game and 32-game season templates used when a new season is created. " +
                   "Every round contains all 24 team slots exactly once; selecting a used slot automatically swaps it into place.",
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

        FlowLayoutPanel selector = new()
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            WrapContents = false,
            Margin = new Padding(0, 0, 0, 8)
        };
        selector.Controls.Add(new Label { Text = "Season length:", AutoSize = true, Margin = new Padding(0, 7, 5, 0) });
        selector.Controls.Add(_seasonLength);
        selector.Controls.Add(new Label { Text = "Template:", AutoSize = true, Margin = new Padding(18, 7, 5, 0) });
        selector.Controls.Add(_template);
        selector.Controls.Add(_templatePath);
        shell.Controls.Add(selector, 0, 2);

        SplitContainer main = _mainSplit;
        main.Dock = DockStyle.Fill;
        main.FixedPanel = FixedPanel.Panel1;
        GroupBox roundGroup = new() { Text = "Rounds", Dock = DockStyle.Fill, Padding = new Padding(8) };
        roundGroup.Controls.Add(_rounds);
        main.Panel1.Controls.Add(roundGroup);

        SplitContainer details = _detailsSplit;
        details.Dock = DockStyle.Fill;
        details.Orientation = Orientation.Horizontal;
        details.Panel1.Controls.Add(BuildGamesPanel());
        details.Panel2.Controls.Add(BuildBalancePanel());
        main.Panel2.Controls.Add(details);
        shell.Controls.Add(main, 0, 3);

        shell.Controls.Add(_status, 0, 4);
        shell.Controls.Add(BuildBottomButtons(), 0, 5);
        return shell;
    }

    private Control BuildGamesPanel()
    {
        GroupBox group = new() { Text = "Round Matchups", Dock = DockStyle.Fill, Padding = new Padding(8) };
        TableLayoutPanel layout = new() { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(_roundSummary, 0, 0);
        layout.Controls.Add(_games, 0, 1);

        FlowLayoutPanel actions = new() { Dock = DockStyle.Fill, AutoSize = true, Margin = new Padding(0, 6, 0, 0) };
        actions.Controls.Add(_swap);
        actions.Controls.Add(_resetRound);
        actions.Controls.Add(_resetTemplate);
        actions.Controls.Add(new Label
        {
            Text = "Team A and Team B are the two ordered participant slots stored by the game; results are saved separately.",
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(14, 6, 0, 0)
        });
        layout.Controls.Add(actions, 0, 2);
        group.Controls.Add(layout);
        return group;
    }

    private void ApplyDefaultSplitterLayout()
    {
        int horizontal = Math.Max(0, _mainSplit.ClientSize.Width - _mainSplit.SplitterWidth);
        int left = Math.Min(145, horizontal);
        int right = Math.Min(600, Math.Max(0, horizontal - left));
        int leftDistance = Math.Clamp(175, left, Math.Max(left, horizontal - right));
        _mainSplit.Panel1MinSize = 0;
        _mainSplit.Panel2MinSize = 0;
        _mainSplit.SplitterDistance = leftDistance;
        _mainSplit.Panel1MinSize = left;
        _mainSplit.Panel2MinSize = Math.Min(right, Math.Max(0, horizontal - leftDistance));

        int vertical = Math.Max(0, _detailsSplit.ClientSize.Height - _detailsSplit.SplitterWidth);
        int upper = Math.Min(260, vertical);
        int lower = Math.Min(150, Math.Max(0, vertical - upper));
        int upperDistance = Math.Clamp((int)(vertical * 0.68), upper, Math.Max(upper, vertical - lower));
        _detailsSplit.Panel1MinSize = 0;
        _detailsSplit.Panel2MinSize = 0;
        _detailsSplit.SplitterDistance = upperDistance;
        _detailsSplit.Panel1MinSize = upper;
        _detailsSplit.Panel2MinSize = Math.Min(lower, Math.Max(0, vertical - upperDistance));
    }

    private Control BuildBalancePanel()
    {
        GroupBox group = new()
        {
            Text = "Template Overview",
            Dock = DockStyle.Fill,
            Padding = new Padding(8)
        };
        group.Controls.Add(_balance);
        return group;
    }

    private Control BuildBottomButtons()
    {
        TableLayoutPanel layout = new() { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        Button resetAll = new() { Text = "Reset All Unsaved Changes", AutoSize = true };
        resetAll.Click += (_, _) => ResetAll();
        layout.Controls.Add(resetAll, 0, 0);

        FlowLayoutPanel right = new() { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        Button save = new() { Text = "Save Schedules to DATA.MET", AutoSize = true };
        Button close = new() { Text = "Close", AutoSize = true, DialogResult = DialogResult.Cancel };
        save.Click += (_, _) => SaveSchedules();
        right.Controls.Add(save);
        right.Controls.Add(close);
        layout.Controls.Add(right, 1, 0);
        CancelButton = close;
        return layout;
    }

    private void ConfigureGamesGrid()
    {
        _games.Dock = DockStyle.Fill;
        _games.AllowUserToAddRows = false;
        _games.AllowUserToDeleteRows = false;
        _games.AllowUserToResizeRows = false;
        _games.AutoGenerateColumns = false;
        _games.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _games.RowHeadersVisible = false;
        _games.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _games.MultiSelect = false;

        _games.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Game",
            HeaderText = "Game",
            ReadOnly = true,
            FillWeight = 25,
            MinimumWidth = 70
        });
        _games.Columns.Add(CreateTeamColumn("TeamA", "Team A"));
        _games.Columns.Add(CreateTeamColumn("TeamB", "Team B"));
    }

    private static DataGridViewComboBoxColumn CreateTeamColumn(string name, string header)
    {
        DataGridViewComboBoxColumn column = new()
        {
            Name = name,
            HeaderText = header,
            DisplayMember = nameof(TeamSlotItem.DisplayName),
            ValueMember = nameof(TeamSlotItem.Value),
            FlatStyle = FlatStyle.Flat,
            FillWeight = 70,
            MinimumWidth = 180
        };
        column.DataSource = Enumerable.Range(0, SeasonScheduleArchive.TeamCount)
            .Select(index => new TeamSlotItem(index)).ToArray();
        return column;
    }

    private void ConfigureBalanceGrid()
    {
        _balance.Dock = DockStyle.Fill;
        _balance.ReadOnly = true;
        _balance.AllowUserToAddRows = false;
        _balance.AllowUserToDeleteRows = false;
        _balance.AllowUserToResizeRows = false;
        _balance.RowHeadersVisible = false;
        _balance.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _balance.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _balance.Columns.Add("Team", "Team slot");
        _balance.Columns.Add("Games", "Games");
        _balance.Columns.Add("SideA", "Team A side");
        _balance.Columns.Add("SideB", "Team B side");
        _balance.Columns.Add("Opponents", "Unique opponents");
        _balance.Columns.Add("Repeats", "Repeat matchups");
    }

    private SeasonScheduleTemplate? CurrentTemplate => (_template.SelectedItem as TemplateItem)?.Template;
    private int CurrentRound => _rounds.SelectedIndex;

    private void LoadTemplates()
    {
        if (_seasonLength.SelectedItem is not SeasonLengthItem length) return;
        _loading = true;
        _template.Items.Clear();
        foreach (SeasonScheduleTemplate template in _archive.Templates.Where(item => item.RoundCount == length.Rounds))
            _template.Items.Add(new TemplateItem(template));
        _template.SelectedIndex = _template.Items.Count > 0 ? 0 : -1;
        _loading = false;
        LoadTemplate();
    }

    private void LoadTemplate()
    {
        if (_loading) return;
        SeasonScheduleTemplate? template = CurrentTemplate;
        _loading = true;
        _rounds.Items.Clear();
        if (template != null)
        {
            for (int round = 0; round < template.RoundCount; round++)
                _rounds.Items.Add($"Round {round + 1:00}");
            _rounds.SelectedIndex = 0;
            _templatePath.Text = template.SourcePath;
        }
        else
        {
            _templatePath.Text = string.Empty;
        }
        _loading = false;
        LoadRound();
        UpdateBalance();
        UpdateStatus();
    }

    private void LoadRound()
    {
        if (_loading) return;
        SeasonScheduleTemplate? template = CurrentTemplate;
        int round = CurrentRound;
        _loading = true;
        _games.Rows.Clear();
        if (template != null && round >= 0)
        {
            for (int game = 0; game < SeasonScheduleArchive.GamesPerRound; game++)
            {
                SeasonScheduleGame matchup = template.GetGame(round, game);
                _games.Rows.Add($"Game {game + 1:00}", matchup.TeamA, matchup.TeamB);
            }
            _roundSummary.Text = $"Round {round + 1} of {template.RoundCount} — 12 games, all 24 team slots present";
        }
        else
        {
            _roundSummary.Text = string.Empty;
        }
        _loading = false;
    }

    private void GamesCellValueChanged(object? sender, DataGridViewCellEventArgs args)
    {
        if (_loading || args.RowIndex < 0 || args.ColumnIndex is not (1 or 2)) return;
        SeasonScheduleTemplate? template = CurrentTemplate;
        if (template == null || CurrentRound < 0) return;
        if (_games.Rows[args.RowIndex].Cells[args.ColumnIndex].Value is not int selected) return;

        int position = args.RowIndex * 2 + args.ColumnIndex - 1;
        template.AssignTeam(CurrentRound, position, selected);
        LoadRound();
        _games.ClearSelection();
        _games.Rows[args.RowIndex].Selected = true;
        UpdateBalance();
        UpdateStatus();
    }

    private void SwapSelectedGame()
    {
        if (CurrentTemplate == null || CurrentRound < 0 || _games.CurrentRow == null) return;
        int row = _games.CurrentRow.Index;
        CurrentTemplate.SwapGameSides(CurrentRound, row);
        LoadRound();
        _games.Rows[row].Selected = true;
        UpdateBalance();
        UpdateStatus();
    }

    private void ResetRound()
    {
        if (CurrentTemplate == null || CurrentRound < 0) return;
        CurrentTemplate.ResetRound(CurrentRound);
        LoadRound();
        UpdateBalance();
        UpdateStatus();
    }

    private void ResetTemplate()
    {
        if (CurrentTemplate == null) return;
        CurrentTemplate.Reset();
        LoadRound();
        UpdateBalance();
        UpdateStatus();
    }

    private void ResetAll()
    {
        foreach (SeasonScheduleTemplate template in _archive.Templates) template.Reset();
        LoadRound();
        UpdateBalance();
        UpdateStatus();
    }

    private void UpdateBalance()
    {
        _balance.Rows.Clear();
        SeasonScheduleTemplate? template = CurrentTemplate;
        if (template == null) return;

        for (int team = 0; team < SeasonScheduleArchive.TeamCount; team++)
        {
            int sideA = 0, sideB = 0;
            HashSet<int> opponents = new();
            int repeats = 0;
            for (int round = 0; round < template.RoundCount; round++)
            {
                for (int game = 0; game < SeasonScheduleArchive.GamesPerRound; game++)
                {
                    SeasonScheduleGame matchup = template.GetGame(round, game);
                    int opponent;
                    if (matchup.TeamA == team)
                    {
                        sideA++;
                        opponent = matchup.TeamB;
                    }
                    else if (matchup.TeamB == team)
                    {
                        sideB++;
                        opponent = matchup.TeamA;
                    }
                    else continue;

                    if (!opponents.Add(opponent)) repeats++;
                }
            }

            _balance.Rows.Add(TeamSlotItem.NameFor(team), sideA + sideB, sideA, sideB, opponents.Count, repeats);
        }
    }

    private void UpdateStatus()
    {
        int changed = _archive.Templates.Count(template => template.IsChanged);
        _status.Text = changed == 0
            ? $"Loaded {_archive.Templates.Count} schedule templates. No unsaved changes."
            : $"{changed} schedule template{(changed == 1 ? string.Empty : "s")} changed. Saving creates one timestamped DATA.MET backup.";
    }

    private void SaveSchedules()
    {
        int changed = _archive.Templates.Count(template => template.IsChanged);
        if (changed == 0)
        {
            MessageBox.Show(this, "No schedule templates were changed.", "Nothing to Save",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (MessageBox.Show(this,
                $"Write {changed} changed schedule template{(changed == 1 ? string.Empty : "s")} to DATA.MET?\n\n" +
                "A timestamped backup will be created first. These templates are used when starting a new season.",
                "Save Season Schedules", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;

        try
        {
            Enabled = false;
            UseWaitCursor = true;
            SeasonScheduleSaveResult result = _archive.SaveWithBackup();
            MessageBox.Show(this,
                $"Saved {result.ChangedTemplateCount} schedule template" +
                $"{(result.ChangedTemplateCount == 1 ? string.Empty : "s")}.\n\nBackup: {result.BackupPath}",
                "Season Schedules Saved", MessageBoxButtons.OK, MessageBoxIcon.Information);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception exception)
        {
            MessageBox.Show(this,
                "The schedules could not be saved. DATA.MET was restored if a backup was created.\n\n" + exception.Message,
                "Unable to Save Season Schedules", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Enabled = true;
            UseWaitCursor = false;
        }
    }

    private sealed record SeasonLengthItem(int Rounds)
    {
        public override string ToString() => $"{Rounds}-game season";
    }

    private sealed record TemplateItem(SeasonScheduleTemplate Template)
    {
        public override string ToString() => Template.DisplayName;
    }

    private sealed record TeamSlotItem(int Value)
    {
        public string DisplayName => NameFor(Value);
        public static string NameFor(int value) => $"Team slot {value + 1:00} (ID {value})";
    }
}
