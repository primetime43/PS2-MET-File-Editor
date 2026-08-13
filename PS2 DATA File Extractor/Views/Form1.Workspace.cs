namespace PS2_DATA_File_Extractor;

public partial class Form1
{
    private TabControl _workspaceTabs = null!;
    private TabPage _gameToolsTab = null!;
    private TabPage _archiveBrowserTab = null!;
    private Label _workspaceArchiveStatus = null!;
    private Label _workspaceArchivePath = null!;
    private Button _workspacePlayerButton = null!;
    private Button _workspaceAppearanceButton = null!;
    private Button _workspaceStadiumButton = null!;
    private Button _workspaceGameplayButton = null!;
    private Button _workspaceDeveloperButton = null!;
    private Button _workspaceTeamLeagueButton = null!;
    private Button _workspaceScheduleButton = null!;
    private Button _workspaceAnimationButton = null!;
    private Button _workspaceFacialEventButton = null!;
    private Button _workspaceRenderWareButton = null!;
    private Button _workspaceOpenBrowserButton = null!;
    private Button _workspaceSaveFileButton = null!;
    private Button _workspaceImportFileButton = null!;
    private Button _workspaceExportFileButton = null!;
    private Button _workspaceExportAllButton = null!;

    private void BuildTabbedWorkspace()
    {
        SuspendLayout();
        Controls.Remove(statusStrip1);
        Controls.Remove(splitContainer1);
        Controls.Remove(menuStrip1);

        openmetFileToolStripMenuItem.Text = "Open Backyard Baseball DATA.MET...";
        exportFileToPCToolStripMenuItem.Text = "Export from DATA.MET";
        editToolStripMenuItem.Text = "Edit";
        viewToolStripMenuItem.Text = "View";

        _workspaceTabs = new TabControl
        {
            Name = "workspaceTabs",
            Dock = DockStyle.Fill,
            Padding = new Point(14, 5)
        };
        _gameToolsTab = new TabPage("Game Tools")
        {
            Name = "gameToolsTab",
            BackColor = SystemColors.Control,
            Padding = new Padding(12)
        };
        _archiveBrowserTab = new TabPage("DATA.MET Browser")
        {
            Name = "archiveBrowserTab",
            BackColor = SystemColors.Control,
            Padding = new Padding(6)
        };

        _gameToolsTab.Controls.Add(BuildGameToolsPage());
        BuildArchiveBrowserPage();
        _workspaceTabs.TabPages.AddRange(new[] { _gameToolsTab, _archiveBrowserTab });
        _workspaceTabs.SelectedTab = _gameToolsTab;

        TableLayoutPanel shell = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        shell.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        shell.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        shell.Controls.Add(menuStrip1, 0, 0);
        shell.Controls.Add(_workspaceTabs, 0, 1);
        shell.Controls.Add(statusStrip1, 0, 2);
        Controls.Add(shell);
        MainMenuStrip = menuStrip1;
        MinimumSize = new Size(900, 650);
        ClientSize = new Size(1120, 740);
        ResumeLayout(performLayout: true);
        UpdateWorkspaceState();
    }

    private Control BuildGameToolsPage()
    {
        Panel scrollArea = new()
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = SystemColors.Control
        };
        TableLayoutPanel layout = new()
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(10, 6, 10, 12)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

        Panel heading = new() { Dock = DockStyle.Fill, Height = 58, Margin = new Padding(3, 3, 3, 9) };
        heading.Controls.Add(new Label
        {
            Text = "Backyard Baseball PS2 Editor",
            Dock = DockStyle.Top,
            Height = 30,
            Font = new Font("Segoe UI", 15F, FontStyle.Bold),
            ForeColor = SystemColors.ControlText
        });
        heading.Controls.Add(new Label
        {
            Text = "Open the extracted game archive, then choose the part of the game you want to modify.",
            Dock = DockStyle.Bottom,
            Height = 25,
            ForeColor = SystemColors.GrayText
        });
        layout.Controls.Add(heading, 0, 0);
        layout.Controls.Add(BuildArchiveGroup(), 0, 1);
        layout.Controls.Add(BuildStructuredEditorsGroup(), 0, 2);
        layout.Controls.Add(BuildOtherToolsGroup(), 0, 3);
        scrollArea.Controls.Add(layout);
        return scrollArea;
    }

    private GroupBox BuildArchiveGroup()
    {
        GroupBox group = new()
        {
            Text = "Game Archive",
            Dock = DockStyle.Fill,
            Height = 112,
            Margin = new Padding(3, 3, 3, 10),
            Padding = new Padding(10, 8, 10, 9)
        };
        TableLayoutPanel table = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 2
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        table.RowStyles.Add(new RowStyle(SizeType.Percent, 52F));
        table.RowStyles.Add(new RowStyle(SizeType.Percent, 48F));

        _workspaceArchiveStatus = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI", 10F, FontStyle.Bold),
            AutoEllipsis = true
        };
        _workspaceArchivePath = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = SystemColors.GrayText,
            AutoEllipsis = true
        };
        Button open = CreateWorkspaceButton("Open DATA.MET...", 150);
        open.Click += (_, e) => openmetFileToolStripMenuItem_Click(this, e);
        _workspaceOpenBrowserButton = CreateWorkspaceButton("Open Archive Browser", 170);
        _workspaceOpenBrowserButton.Click += (_, _) => _workspaceTabs.SelectedTab = _archiveBrowserTab;

        table.Controls.Add(_workspaceArchiveStatus, 0, 0);
        table.SetColumnSpan(_workspaceArchiveStatus, 1);
        table.Controls.Add(open, 1, 0);
        table.Controls.Add(_workspaceOpenBrowserButton, 2, 0);
        table.Controls.Add(_workspaceArchivePath, 0, 1);
        table.SetColumnSpan(_workspaceArchivePath, 3);
        group.Controls.Add(table);
        return group;
    }

    private GroupBox BuildStructuredEditorsGroup()
    {
        GroupBox group = new()
        {
            Text = "Game Editors",
            Dock = DockStyle.Fill,
            Height = 508,
            Margin = new Padding(3, 3, 3, 10),
            Padding = new Padding(10, 8, 10, 9)
        };
        TableLayoutPanel table = CreateToolTable(9);
        _workspacePlayerButton = AddToolRow(table, 0, "Player Editor...",
            "Edit player names, batting, running, fielding, pitching, identity, and clone appearance values.");
        _workspacePlayerButton.Click += (_, _) => playerEditorMenuItem_Click(this, EventArgs.Empty);
        _workspaceAppearanceButton = AddToolRow(table, 1, "3D Player Appearance Editor...",
            "Preview animated player models and export, replace, or reset their clothing, face, hair, and equipment textures.");
        _workspaceAppearanceButton.Click += playerAppearanceButton_Click;
        _workspaceStadiumButton = AddToolRow(table, 2, "Stadium Editor...",
            "Edit field lighting, cameras, collision tags, ambient objects, particles, animations, and movement.");
        _workspaceStadiumButton.Click += (_, _) => stadiumEnvironmentMenuItem_Click(this, EventArgs.Empty);
        _workspaceGameplayButton = AddToolRow(table, 3, "Gameplay Tweaks...",
            "Edit ball, bat, power-up, field physics, simulation, practice, cheat, and game-default values.");
        _workspaceGameplayButton.Click += (_, _) => gameplayTweaksMenuItem_Click(this, EventArgs.Empty);
        _workspaceTeamLeagueButton = AddToolRow(table, 4, "Team and League Setup...",
            "Move clubs between the six divisions, set active or inactive teams, and control division order.");
        _workspaceTeamLeagueButton.Click += teamLeagueButton_Click;
        _workspaceScheduleButton = AddToolRow(table, 5, "Season Schedule Editor...",
            "Edit every matchup in the 18-game and 32-game season templates while preserving valid rounds.");
        _workspaceScheduleButton.Click += seasonScheduleButton_Click;
        _workspaceAnimationButton = AddToolRow(table, 6, "Animation Viewer / Editor...",
            "View all ANM tracks and keyframes, synchronize paired EVT expressions, and edit speed or timing.");
        _workspaceAnimationButton.Click += animationEditorButton_Click;
        _workspaceFacialEventButton = AddToolRow(table, 7, "Facial Event Editor...",
            "Edit and preview talkie lip sync, eye events, and mouth events; play paired VAG dialogue.");
        _workspaceFacialEventButton.Click += (_, _) => facialEventEditorMenuItem_Click(this, EventArgs.Empty);
        _workspaceRenderWareButton = AddToolRow(table, 8, "3D Model and Stadium Viewer...",
            "Browse all 1,170 DFF models and 26 RWS scenes, inspect stadium sectors and materials, and export OBJ geometry or textures.");
        _workspaceRenderWareButton.Click += renderWareViewerButton_Click;
        group.Controls.Add(table);
        return group;
    }

    private GroupBox BuildOtherToolsGroup()
    {
        GroupBox group = new()
        {
            Text = "Game and Save Tools",
            Dock = DockStyle.Fill,
            Height = 246,
            Margin = new Padding(3, 3, 3, 3),
            Padding = new Padding(10, 8, 10, 9)
        };
        TableLayoutPanel table = CreateToolTable(4);
        _workspaceDeveloperButton = AddToolRow(table, 0, "Developer Tools...",
            "Enable retail debug switches, status logging, dormant game modes, forced season results, and exact hit trajectories.");
        _workspaceDeveloperButton.Click += developerToolsButton_Click;
        Button unlocks = AddToolRow(table, 1, "Unlock Game Content...",
            "Patch the USA game executable to unlock players, fields, Darts, and Aquadome for every save.");
        unlocks.Click += (_, _) => patchGameMenuItem_Click(this, EventArgs.Empty);
        Button iso = AddToolRow(table, 2, "Build Game ISO...",
            "Rebuild a playable ISO from the extracted game folder after applying your changes.");
        iso.Click += (_, _) => rebuildIsoMenuItem_Click(this, EventArgs.Empty);
        Button save = AddToolRow(table, 3, "Edit Exported Save...",
            "Edit an exported Backyard Baseball Settings save, including its content unlock flags.");
        save.Click += (_, _) => editSaveMenuItem_Click(this, EventArgs.Empty);
        group.Controls.Add(table);
        return group;
    }

    private void BuildArchiveBrowserPage()
    {
        FlowLayoutPanel toolbar = new()
        {
            Dock = DockStyle.Top,
            Height = 44,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(3, 4, 3, 4)
        };
        _workspaceSaveFileButton = CreateWorkspaceButton("Save File Changes", 145);
        _workspaceSaveFileButton.Click += (_, e) => saveFileChangesToolStripMenuItem_Click(this, e);
        _workspaceImportFileButton = CreateWorkspaceButton("Import File...", 120);
        _workspaceImportFileButton.Click += (_, e) => importFileToolStripMenuItem_Click(this, e);
        _workspaceExportFileButton = CreateWorkspaceButton("Export Selected...", 140);
        _workspaceExportFileButton.Click += (_, e) => exportSelectFileToolStripMenuItem_Click(this, e);
        _workspaceExportAllButton = CreateWorkspaceButton("Export All...", 115);
        _workspaceExportAllButton.Click += (_, e) => exportAllFilesToolStripMenuItem_Click(this, e);
        treeView1.NodeMouseDoubleClick += (_, args) =>
        {
            if (args.Node.Tag != _selectedEntry) return;
            string extension = Path.GetExtension(_selectedEntry.Path);
            if (extension.Equals(".evt", StringComparison.OrdinalIgnoreCase))
                facialEventEditorMenuItem_Click(this, EventArgs.Empty);
            else if (extension.Equals(".anm", StringComparison.OrdinalIgnoreCase))
                animationEditorButton_Click(this, EventArgs.Empty);
            else if (extension.Equals(".dff", StringComparison.OrdinalIgnoreCase) ||
                     extension.Equals(".rws", StringComparison.OrdinalIgnoreCase))
                renderWareViewerButton_Click(this, EventArgs.Empty);
        };
        toolbar.Controls.AddRange(new Control[]
        {
            _workspaceSaveFileButton, _workspaceImportFileButton, _workspaceExportFileButton, _workspaceExportAllButton
        });

        Label note = new()
        {
            Dock = DockStyle.Top,
            Height = 34,
            Padding = new Padding(7, 7, 5, 3),
            Text = "Advanced archive browser for individual DATA.MET files. Game-specific editors are on the Game Tools tab."
        };
        splitContainer1.Dock = DockStyle.Fill;
        splitContainer1.SplitterDistance = 340;
        _archiveBrowserTab.Controls.Add(splitContainer1);
        _archiveBrowserTab.Controls.Add(note);
        _archiveBrowserTab.Controls.Add(toolbar);
    }

    private static TableLayoutPanel CreateToolTable(int rows)
    {
        TableLayoutPanel table = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = rows
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220F));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        for (int row = 0; row < rows; row++)
            table.RowStyles.Add(new RowStyle(SizeType.Percent, 100F / rows));
        return table;
    }

    private static Button AddToolRow(TableLayoutPanel table, int row, string buttonText, string description)
    {
        Button button = CreateWorkspaceButton(buttonText, 195);
        Label label = new()
        {
            Text = description,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            Margin = new Padding(6, 3, 3, 3)
        };
        table.Controls.Add(button, 0, row);
        table.Controls.Add(label, 1, row);
        return button;
    }

    private static Button CreateWorkspaceButton(string text, int width)
    {
        return new Button
        {
            Text = text,
            Width = width,
            Height = 34,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(3, 3, 8, 3),
            UseVisualStyleBackColor = true
        };
    }

    private void UpdateWorkspaceState()
    {
        if (_workspaceArchiveStatus == null) return;
        bool hasArchive = !string.IsNullOrWhiteSpace(_dataMetPath) && _metFileStructure != null;
        bool selectedFile = hasArchive && _selectedEntry != null;
        bool structuredEditorsAvailable = hasArchive && !_hasUnsavedChanges;

        _workspacePlayerButton.Enabled = structuredEditorsAvailable;
        _workspaceAppearanceButton.Enabled = structuredEditorsAvailable;
        _workspaceStadiumButton.Enabled = structuredEditorsAvailable;
        _workspaceGameplayButton.Enabled = structuredEditorsAvailable;
        _workspaceDeveloperButton.Enabled = structuredEditorsAvailable;
        _workspaceTeamLeagueButton.Enabled = structuredEditorsAvailable;
        _workspaceScheduleButton.Enabled = structuredEditorsAvailable;
        _workspaceAnimationButton.Enabled = structuredEditorsAvailable;
        _workspaceFacialEventButton.Enabled = structuredEditorsAvailable;
        _workspaceRenderWareButton.Enabled = structuredEditorsAvailable;
        _workspaceOpenBrowserButton.Enabled = hasArchive;
        _workspaceSaveFileButton.Enabled = selectedFile && _hasUnsavedChanges;
        _workspaceImportFileButton.Enabled = selectedFile;
        _workspaceExportFileButton.Enabled = selectedFile;
        _workspaceExportAllButton.Enabled = hasArchive;

        if (!hasArchive)
        {
            _workspaceArchiveStatus.Text = "No DATA.MET loaded";
            _workspaceArchivePath.Text = "Open DATA.MET from an extracted Backyard Baseball PS2 game folder to use the game editors.";
            return;
        }

        string editState = _hasUnsavedChanges ? " — unsaved raw file changes" : string.Empty;
        _workspaceArchiveStatus.Text = $"DATA.MET loaded — {_metFileStructure!.FileCount:N0} files{editState}";
        _workspaceArchivePath.Text = _dataMetPath;
    }
}
