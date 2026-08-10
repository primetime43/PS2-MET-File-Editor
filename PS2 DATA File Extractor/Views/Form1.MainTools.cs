namespace PS2_DATA_File_Extractor;

public partial class Form1
{
    private Label _gameFileStatusLabel = null!;
    private Button _playerEditorMainButton = null!;
    private Button _gameplayMainButton = null!;
    private Button _stadiumMainButton = null!;

    private void BuildMainWindowTools()
    {
        SuspendLayout();
        Controls.Remove(statusStrip1);
        Controls.Remove(splitContainer1);
        Controls.Remove(menuStrip1);

        openmetFileToolStripMenuItem.Text = "Open Backyard Baseball DATA.MET...";
        editToolStripMenuItem.Text = "Edit";
        viewToolStripMenuItem.Text = "View";

        Panel toolsPanel = BuildMainToolsPanel();
        splitContainer1.Dock = DockStyle.Fill;

        Controls.Add(statusStrip1);
        Controls.Add(splitContainer1);
        Controls.Add(toolsPanel);
        Controls.Add(menuStrip1);
        MainMenuStrip = menuStrip1;
        MinimumSize = new Size(900, 650);
        ClientSize = new Size(1200, 760);
        ResumeLayout(performLayout: true);
        UpdateDashboardState();
    }

    private Panel BuildMainToolsPanel()
    {
        Panel panel = new()
        {
            Name = "mainToolsPanel",
            Dock = DockStyle.Top,
            Height = 112,
            BackColor = SystemColors.Control,
            BorderStyle = BorderStyle.FixedSingle,
            Padding = new Padding(9, 7, 9, 7)
        };

        Label title = new()
        {
            Text = "Backyard Baseball PS2 Editor",
            Dock = DockStyle.Top,
            Height = 26,
            Font = new Font("Segoe UI", 12F, FontStyle.Bold),
            ForeColor = SystemColors.ControlText
        };
        _gameFileStatusLabel = new Label
        {
            Dock = DockStyle.Top,
            Height = 23,
            AutoEllipsis = true,
            Font = new Font("Segoe UI", 9F),
            ForeColor = SystemColors.GrayText
        };
        FlowLayoutPanel actions = new()
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(0, 5, 0, 0)
        };

        Button openMet = CreateMainToolButton("Open DATA.MET...");
        openMet.Name = "openDataMetMainButton";
        openMet.Click += (_, e) => openmetFileToolStripMenuItem_Click(this, e);

        _playerEditorMainButton = CreateMainToolButton("Player Editor...");
        _playerEditorMainButton.Name = "playerEditorMainButton";
        _playerEditorMainButton.Click += (_, _) => playerEditorMenuItem_Click(this, EventArgs.Empty);

        _stadiumMainButton = CreateMainToolButton("Stadium Editor...");
        _stadiumMainButton.Name = "stadiumEditorMainButton";
        _stadiumMainButton.Click += (_, _) => stadiumEnvironmentMenuItem_Click(this, EventArgs.Empty);

        _gameplayMainButton = CreateMainToolButton("Gameplay Tweaks...");
        _gameplayMainButton.Name = "gameplayTweaksMainButton";
        _gameplayMainButton.Click += (_, _) => gameplayTweaksMenuItem_Click(this, EventArgs.Empty);

        Button unlocks = CreateMainToolButton("Unlock Game Content...");
        unlocks.Name = "unlockContentMainButton";
        unlocks.Click += (_, _) => patchGameMenuItem_Click(this, EventArgs.Empty);

        Button iso = CreateMainToolButton("Build Game ISO...");
        iso.Name = "buildIsoMainButton";
        iso.Click += (_, _) => rebuildIsoMenuItem_Click(this, EventArgs.Empty);

        Button saveUnlocks = CreateMainToolButton("Edit Exported Save...");
        saveUnlocks.Name = "editSaveMainButton";
        saveUnlocks.Click += (_, _) => editSaveMenuItem_Click(this, EventArgs.Empty);

        actions.Controls.AddRange(new Control[]
        {
            openMet, _playerEditorMainButton, _stadiumMainButton, _gameplayMainButton, unlocks, iso, saveUnlocks
        });
        panel.Controls.Add(actions);
        panel.Controls.Add(_gameFileStatusLabel);
        panel.Controls.Add(title);
        return panel;
    }

    private static Button CreateMainToolButton(string text)
    {
        return new Button
        {
            Text = text,
            AutoSize = true,
            Height = 34,
            Margin = new Padding(0, 0, 7, 0),
            Padding = new Padding(9, 0, 9, 0),
            UseVisualStyleBackColor = true
        };
    }

    private void UpdateDashboardState()
    {
        if (_gameFileStatusLabel == null) return;
        bool hasArchive = !string.IsNullOrWhiteSpace(_dataMetPath) && _metFileStructure != null;
        _playerEditorMainButton.Enabled = hasArchive && !_hasUnsavedChanges;
        _stadiumMainButton.Enabled = hasArchive && !_hasUnsavedChanges;
        _gameplayMainButton.Enabled = hasArchive && !_hasUnsavedChanges;

        if (!hasArchive)
        {
            _gameFileStatusLabel.Text = "No DATA.MET loaded. Open the extracted game archive to enable player, stadium, and gameplay editing.";
            return;
        }

        string changeStatus = _hasUnsavedChanges ? " — unsaved archive changes" : string.Empty;
        _gameFileStatusLabel.Text = $"Loaded: {_dataMetPath}  ({_metFileStructure!.FileCount:N0} files){changeStatus}";
    }
}