using PS2_DATA_File_Extractor.FileOperations;
using System.Diagnostics;
using System.Globalization;
using System.Numerics;

namespace PS2_DATA_File_Extractor;

public sealed class StadiumEnvironmentEditorForm : Form
{
    private readonly StadiumEnvironmentArchive _archive;
    private readonly RenderWareSceneArchive _sceneArchive;
    private readonly RenderWareAnimationArchive _animationArchive;
    private readonly ComboBox _stadiums = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
    private readonly Label _source = new() { AutoEllipsis = true, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Label _summary = new() { AutoSize = true, Margin = new Padding(14, 8, 0, 0) };
    private readonly DataGridView _fieldGrid = CreateGrid();
    private readonly DataGridView _collisionGrid = CreateGrid();
    private readonly DataGridView _ambientGrid = CreateGrid();
    private readonly DataGridView _splineGrid = CreateSplineGrid();
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
    private readonly SplitContainer _workspaceSplit = new() { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel1 };
    private readonly RenderWareScenePreviewControl _preview = new()
    {
        Dock = DockStyle.Fill,
        HideSkyRoof = true,
        HideHelperGeometry = true
    };
    private readonly ComboBox _previewView = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 170 };
    private readonly CheckBox _showAmbientModels = new() { Text = "Ambient models", AutoSize = true, Checked = true };
    private readonly CheckBox _showDisabledAmbients = new() { Text = "Disabled translucent", AutoSize = true, Checked = true };
    private readonly CheckBox _showAmbientPaths = new() { Text = "All movement paths", AutoSize = true };
    private readonly Button _playAmbient = new() { Text = "Play", Width = 54, Height = 27 };
    private readonly Button _pauseAmbient = new() { Text = "Pause", Width = 58, Height = 27 };
    private readonly Button _stopAmbient = new() { Text = "Stop", Width = 54, Height = 27 };
    private readonly TrackBar _ambientScrubber = new()
    {
        Minimum = 0, Maximum = 1000, TickStyle = TickStyle.None, Width = 125, Height = 27, AutoSize = false
    };
    private readonly ComboBox _ambientPlaybackRate = new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList, Width = 58
    };
    private readonly CheckBox _loopAmbient = new() { Text = "Loop", AutoSize = true, Checked = true };
    private readonly CheckBox _faceAmbientPath = new() { Text = "Face path", AutoSize = true, Checked = true };
    private readonly ComboBox _ambientAnimation = new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList, Width = 210
    };
    private readonly CheckBox _syncAmbientAnimation = new()
    {
        Text = "Sync ANM to path", AutoSize = true, Checked = true
    };
    private readonly CheckBox _loopAmbientAnimation = new()
    {
        Text = "Loop ANM", AutoSize = true, Checked = true
    };
    private readonly Label _ambientAnimationStatus = new()
    {
        AutoSize = true, AutoEllipsis = true, MaximumSize = new Size(440, 32), Margin = new Padding(8, 8, 2, 0)
    };
    private readonly Label _ambientPlaybackTime = new() { AutoSize = true, Margin = new Padding(6, 8, 4, 0) };
    private readonly Label _previewSummary = new()
    {
        Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft
    };
    private readonly Label _previewStatus = new()
    {
        Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft
    };
    private readonly Label _ambientInfo = new()
    {
        Dock = DockStyle.Bottom, Height = 58, Padding = new Padding(5, 4, 5, 2),
        AutoEllipsis = true, BorderStyle = BorderStyle.FixedSingle
    };
    private readonly NumericUpDown[] _ambientPosition = CreatePlacementValues();
    private readonly NumericUpDown[] _ambientRotation = CreatePlacementValues();
    private readonly Label _placementStatus = new()
    {
        AutoSize = true, Margin = new Padding(8, 7, 0, 0), ForeColor = SystemColors.GrayText
    };
    private readonly GroupBox _splineEditor = new() { Text = "Movement Path Waypoints (.spl)", Dock = DockStyle.Fill };
    private readonly Label _splineStatus = new()
    {
        Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft,
        Padding = new Padding(4, 0, 4, 0)
    };
    private StadiumEnvironment? _current;
    private RenderWareScene? _scene;
    private RenderWareScene? _previewScene;
    private StadiumAmbientPreviewResult? _ambientPreview;
    private readonly Dictionary<string, RenderWareScene> _ambientModelCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, StadiumSplineDocument> _splineDocuments = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<AmbientPlaybackMesh> _playbackMeshes = [];
    private readonly List<RenderWareDetachedPreviewForm> _detachedPreviews = [];
    private readonly System.Windows.Forms.Timer _ambientPlaybackTimer = new() { Interval = 80 };
    private readonly Stopwatch _ambientPlaybackWatch = new();
    private Vector4 _previewLight = Vector4.One;
    private BackyardCameraPreset? _activePreviewCamera;
    private double _ambientPlaybackPosition, _ambientPlaybackStart, _ambientPlaybackDuration, _ambientPathDuration;
    private StadiumSplineDocument? _currentSpline;
    private RenderWareAnimationFile? _activeAmbientAnimation;
    private RenderWareAnimationBinding? _activeAmbientBinding;
    private RenderWareSkinnedModel? _activeAmbientModel;
    private int _selectedSplinePoint = -1;
    private bool _ambientPlaying, _updatingScrubber, _loadingSpline, _loadingAnimation, _loadingPlacement;
    private bool _loading;

    public StadiumEnvironmentEditorForm(StadiumEnvironmentArchive archive, string metPath)
    {
        _archive = archive;
        _sceneArchive = RenderWareSceneArchive.Load(metPath);
        _animationArchive = RenderWareAnimationArchive.LoadForPreview(metPath);
        _ambientPlaybackRate.Items.AddRange(new object[] { "0.25×", "0.5×", "1×", "2×", "4×" });
        _ambientPlaybackRate.SelectedIndex = 2;
        _ambientPlaybackTimer.Tick += AmbientPlaybackTimer_Tick;
        Text = "Stadium Editor and Live Preview - DATA.MET";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(1500, 860);
        MinimumSize = new Size(1120, 680);
        AutoScaleMode = AutoScaleMode.Dpi;

        Label instructions = new()
        {
            Dock = DockStyle.Top,
            Height = 50,
            Padding = new Padding(12, 8, 12, 4),
            Text = "Create, clone, place, and edit fielddata ambient objects and movement paths while viewing the textured stadium. Lighting, cameras, positions, paths, and compatible ANM motion update immediately; particles, movies, and collision behavior still require the game."
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

        Controls.Add(BuildWorkspace());
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
        _preview.GuideClicked += (_, e) => SelectAmbientGuide(e.Key, e.PointIndex);
        _showAmbientModels.CheckedChanged += (_, _) => RefreshAmbientComposition();
        _showDisabledAmbients.CheckedChanged += (_, _) => RefreshAmbientComposition();
        _showAmbientPaths.CheckedChanged += (_, _) => UpdatePreviewGuides();
        _tabs.SelectedIndexChanged += (_, _) => RefreshRawText();
        _playAmbient.Click += (_, _) => PlayAmbient();
        _pauseAmbient.Click += (_, _) => PauseAmbient();
        _stopAmbient.Click += (_, _) => StopAmbientPlayback(render: true);
        _ambientScrubber.Scroll += (_, _) => ScrubAmbient();
        _ambientPlaybackRate.SelectedIndexChanged += (_, _) => RebasePlaybackClock();
        _faceAmbientPath.CheckedChanged += (_, _) => ApplyPlaybackFrame();
        _ambientAnimation.SelectedIndexChanged += (_, _) => AmbientAnimationChanged();
        _syncAmbientAnimation.CheckedChanged += (_, _) => AmbientAnimationTimingChanged();
        _loopAmbientAnimation.CheckedChanged += (_, _) => ApplyPlaybackFrame();
        _splineGrid.CellValidating += SplineGrid_CellValidating;
        _splineGrid.CellValueChanged += SplineGrid_CellValueChanged;
        _splineGrid.SelectionChanged += (_, _) => SelectSplineGridPoint();
        foreach (NumericUpDown value in _ambientPosition.Concat(_ambientRotation))
            value.ValueChanged += (_, _) => PlacementValueChanged();
        HookGrid(_fieldGrid);
        HookGrid(_collisionGrid);
        HookGrid(_ambientGrid);
        Shown += (_, _) => ApplyDefaultWorkspaceLayout();
        if (_stadiums.Items.Count > 0) _stadiums.SelectedIndex = 0;
        _status.Text = $"Loaded {_archive.Stadiums.Count} stadium variants from {metPath}.";
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            CloseDetachedPreviews();
            _ambientPlaybackTimer.Dispose();
        }
        base.Dispose(disposing);
    }

    private Control BuildWorkspace()
    {
        _workspaceSplit.Panel1.Controls.Add(_tabs);
        _workspaceSplit.Panel2.Controls.Add(BuildPreviewPane());
        return _workspaceSplit;
    }

    private Control BuildPreviewPane()
    {
        TableLayoutPanel pane = new()
        {
            Dock = DockStyle.Fill,
            RowCount = 3,
            ColumnCount = 1,
            Padding = new Padding(6, 3, 8, 6)
        };
        pane.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        pane.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        pane.RowStyles.Add(new RowStyle(SizeType.Absolute, 202));

        TableLayoutPanel heading = new() { Dock = DockStyle.Fill, ColumnCount = 2 };
        heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        heading.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _previewSummary.Font = new Font(Font, FontStyle.Bold);
        Button openLarge = new() { Text = "Open Large Preview...", AutoSize = true, Margin = new Padding(8, 7, 2, 5) };
        openLarge.Click += (_, _) => OpenLargePreview();
        heading.Controls.Add(_previewSummary, 0, 0);
        heading.Controls.Add(openLarge, 1, 0);

        TableLayoutPanel toolbar = new() { Dock = DockStyle.Fill, RowCount = 5, ColumnCount = 1 };
        toolbar.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        toolbar.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        toolbar.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        toolbar.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        toolbar.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        FlowLayoutPanel actions = new()
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false, AutoScroll = true, Margin = Padding.Empty
        };
        _previewView.Items.AddRange(new object[]
        {
            "Fit / orbit view", "Fielddata camera", "Commentator camera", "Game batting POV"
        });
        _previewView.SelectedIndexChanged += (_, _) => ApplyPreviewCamera();
        _previewView.SelectedIndex = 0;
        CheckBox backdrop = PreviewCheck("Hide backdrop", true, value => _preview.HideSkyRoof = value);
        CheckBox helpers = PreviewCheck("Show helpers", false, value => _preview.HideHelperGeometry = !value);
        CheckBox wireframe = PreviewCheck("Wireframe", false, value => _preview.Wireframe = value);
        Button zoomOut = PreviewButton("Zoom −", (_, _) => _preview.ZoomOut());
        Button zoomIn = PreviewButton("Zoom +", (_, _) => _preview.ZoomIn());
        Button fit = PreviewButton("Fit View", (_, _) =>
        {
            _previewView.SelectedIndex = 0;
            _preview.ResetView();
        });
        actions.Controls.AddRange(new Control[]
        {
            PreviewLabel("View:"), _previewView, backdrop, helpers, wireframe, zoomOut, zoomIn, fit
        });
        FlowLayoutPanel ambientActions = new()
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false, AutoScroll = true, Margin = Padding.Empty
        };
        foreach (CheckBox check in new[] { _showAmbientModels, _showDisabledAmbients, _showAmbientPaths })
            check.Margin = new Padding(4, 7, 8, 0);
        ambientActions.Controls.AddRange(new Control[]
        {
            PreviewLabel("Fielddata:"), _showAmbientModels, _showDisabledAmbients, _showAmbientPaths,
            new Label { Text = "Click a marker to select its ambient block.", AutoSize = true, Margin = new Padding(10, 8, 0, 0) }
        });
        FlowLayoutPanel playback = new()
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false, AutoScroll = true, Margin = Padding.Empty
        };
        _ambientScrubber.Margin = new Padding(5, 5, 5, 0);
        foreach (CheckBox check in new[] { _loopAmbient, _faceAmbientPath })
            check.Margin = new Padding(6, 8, 6, 0);
        playback.Controls.AddRange(new Control[]
        {
            PreviewLabel("Path:"), _playAmbient, _pauseAmbient, _stopAmbient,
            _ambientScrubber, _ambientPlaybackTime, PreviewLabel("Rate:"), _ambientPlaybackRate,
            _loopAmbient, _faceAmbientPath
        });
        FlowLayoutPanel animation = new()
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false, AutoScroll = true, Margin = Padding.Empty
        };
        _ambientAnimation.Margin = new Padding(4, 4, 4, 0);
        foreach (CheckBox check in new[] { _syncAmbientAnimation, _loopAmbientAnimation })
            check.Margin = new Padding(6, 8, 6, 0);
        animation.Controls.AddRange(new Control[]
        {
            PreviewLabel("Animation:"), _ambientAnimation, _syncAmbientAnimation,
            _loopAmbientAnimation, _ambientAnimationStatus
        });
        toolbar.Controls.Add(actions, 0, 0);
        toolbar.Controls.Add(ambientActions, 0, 1);
        toolbar.Controls.Add(playback, 0, 2);
        toolbar.Controls.Add(animation, 0, 3);
        toolbar.Controls.Add(_previewStatus, 0, 4);
        UpdatePlaybackControls();

        pane.Controls.Add(heading, 0, 0);
        pane.Controls.Add(_preview, 0, 1);
        pane.Controls.Add(toolbar, 0, 2);
        return pane;
    }

    private void ApplyDefaultWorkspaceLayout()
    {
        int available = Math.Max(0, _workspaceSplit.ClientSize.Width - _workspaceSplit.SplitterWidth);
        int editorMinimum = Math.Min(520, available);
        int previewMinimum = Math.Min(480, Math.Max(0, available - editorMinimum));
        int maximumEditor = Math.Max(editorMinimum, available - previewMinimum);
        int desired = Math.Clamp((int)(available * 0.46F), editorMinimum, maximumEditor);
        _workspaceSplit.Panel1MinSize = 0;
        _workspaceSplit.Panel2MinSize = 0;
        _workspaceSplit.SplitterDistance = desired;
        _workspaceSplit.Panel1MinSize = editorMinimum;
        _workspaceSplit.Panel2MinSize = Math.Min(previewMinimum, Math.Max(0, available - desired));
    }

    private static Label PreviewLabel(string text) => new()
    {
        Text = text, AutoSize = true, Margin = new Padding(2, 8, 4, 0)
    };

    private static CheckBox PreviewCheck(string text, bool value, Action<bool> changed)
    {
        CheckBox check = new() { Text = text, Checked = value, AutoSize = true, Margin = new Padding(6, 7, 2, 0) };
        check.CheckedChanged += (_, _) => changed(check.Checked);
        return check;
    }

    private static Button PreviewButton(string text, EventHandler clicked)
    {
        Button button = new() { Text = text, AutoSize = true, Margin = new Padding(4, 3, 0, 1) };
        button.Click += clicked;
        return button;
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
        TableLayoutPanel ambientDetails = new() { Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1 };
        ambientDetails.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        ambientDetails.RowStyles.Add(new RowStyle(SizeType.Absolute, 86));
        ambientDetails.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        ambientDetails.RowStyles.Add(new RowStyle(SizeType.Absolute, 245));
        ambientDetails.Controls.Add(_ambientGrid, 0, 0);
        ambientDetails.Controls.Add(BuildPlacementEditor(), 0, 1);
        ambientDetails.Controls.Add(_ambientInfo, 0, 2);
        ambientDetails.Controls.Add(BuildSplineEditor(), 0, 3);
        split.Panel2.Controls.Add(ambientDetails);
        ambientPage.Controls.Add(split);
        ambientPage.Controls.Add(BuildAmbientObjectToolbar());

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
            HeaderText = "Setting", ReadOnly = true, Width = 170
        });
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Directive", ReadOnly = true, Width = 115
        });
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Value", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, MinimumWidth = 170
        });
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Format", ReadOnly = true, Width = 90
        });
        return grid;
    }

    private static DataGridView CreateSplineGrid()
    {
        DataGridView grid = new()
        {
            Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false, RowHeadersVisible = false, MultiSelect = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            EditMode = DataGridViewEditMode.EditOnEnter
        };
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "#", ReadOnly = true, Width = 45, FillWeight = 30 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "X", FillWeight = 100 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Y", FillWeight = 100 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Z", FillWeight = 100 });
        return grid;
    }

    private Control BuildAmbientObjectToolbar()
    {
        FlowLayoutPanel toolbar = new()
        {
            Dock = DockStyle.Top, Height = 42, FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false, AutoScroll = true, Padding = new Padding(2, 4, 2, 3)
        };
        toolbar.Controls.AddRange(new Control[]
        {
            SplineButton("New Object...", (_, _) => CreateAmbientObject()),
            SplineButton("Clone Selected", (_, _) => CloneSelectedAmbient()),
            SplineButton("Copy to Stadium...", (_, _) => CopyAmbientToStadium()),
            SplineButton("Delete Selected", (_, _) => DeleteSelectedAmbient()),
            new Label
            {
                Text = "New and cloned objects are enabled automatically through numAmbs.",
                AutoSize = true, Margin = new Padding(12, 8, 0, 0), ForeColor = SystemColors.GrayText
            }
        });
        return toolbar;
    }

    private Control BuildPlacementEditor()
    {
        GroupBox box = new() { Text = "Placement", Dock = DockStyle.Fill, Padding = new Padding(6, 4, 6, 4) };
        FlowLayoutPanel row = new()
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true, AutoScroll = true, Padding = new Padding(2, 2, 2, 0)
        };
        row.Controls.Add(PlacementLabel("Position:"));
        foreach (NumericUpDown value in _ambientPosition) row.Controls.Add(value);
        row.Controls.Add(PlacementLabel("H / P / R:"));
        foreach (NumericUpDown value in _ambientRotation) row.Controls.Add(value);
        row.Controls.Add(_placementStatus);
        box.Controls.Add(row);
        return box;
    }

    private static Label PlacementLabel(string text) => new()
    {
        Text = text, AutoSize = true, Margin = new Padding(5, 7, 3, 0)
    };

    private static NumericUpDown[] CreatePlacementValues() => Enumerable.Range(0, 3)
        .Select(_ => new NumericUpDown
        {
            Minimum = -1000000, Maximum = 1000000, DecimalPlaces = 3,
            Increment = 1, Width = 92, ThousandsSeparator = true, Margin = new Padding(2, 2, 2, 0)
        }).ToArray();

    private Control BuildSplineEditor()
    {
        TableLayoutPanel layout = new() { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Padding = new Padding(5, 2, 5, 4) };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        FlowLayoutPanel buttons = new()
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false, AutoScroll = true, Margin = Padding.Empty
        };
        Button add = SplineButton("Add After", (_, _) => AddSplinePoint());
        Button duplicate = SplineButton("Duplicate", (_, _) => DuplicateSplinePoint());
        Button delete = SplineButton("Delete", (_, _) => DeleteSplinePoint());
        Button up = SplineButton("Move Up", (_, _) => MoveSplinePoint(-1));
        Button down = SplineButton("Move Down", (_, _) => MoveSplinePoint(1));
        Button reset = SplineButton("Reset This Path", (_, _) => ResetSplinePath());
        buttons.Controls.AddRange(new Control[] { add, duplicate, delete, up, down, reset });
        layout.Controls.Add(_splineGrid, 0, 0);
        layout.Controls.Add(buttons, 0, 1);
        layout.Controls.Add(_splineStatus, 0, 2);
        _splineEditor.Controls.Add(layout);
        return _splineEditor;
    }

    private static Button SplineButton(string text, EventHandler clicked)
    {
        Button button = new() { Text = text, AutoSize = true, Margin = new Padding(2, 3, 2, 1) };
        button.Click += clicked;
        return button;
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
        StopAmbientPlayback(render: false);
        CloseDetachedPreviews();
        _current = _stadiums.SelectedItem as StadiumEnvironment;
        if (_current == null) return;
        _source.Text = _current.SourcePath;
        LoadSettings(_fieldGrid, _current.Document.FieldSettings);
        LoadSettings(_collisionGrid, _current.Document.CollisionSettings);
        LoadAmbientList();
        RefreshRawText();
        UpdateSummaryAndStatus();
        LoadStadiumScene();
    }

    private void LoadStadiumScene()
    {
        _scene = null;
        _previewScene = null;
        _ambientPreview = null;
        _ambientModelCache.Clear();
        _preview.Scene = null;
        _preview.Guides = [];
        _activePreviewCamera = null;
        if (_current == null) return;
        RenderWareAssetFile? asset = _sceneArchive.FindStadiumScene(_current.FolderName);
        if (asset == null)
        {
            _previewSummary.Text = $"{_current.DisplayName} — no matching RWS scene";
            _previewStatus.Text = "The fielddata remains editable, but this archive has no mapped stadium model.";
            return;
        }

        try
        {
            UseWaitCursor = true;
            _scene = _sceneArchive.LoadScene(asset);
            RebuildAmbientPreview(resetView: true);
            ConfigureAmbientPlayback();
            ApplyLivePreviewSettings();
        }
        catch (Exception exception)
        {
            _scene = null;
            _preview.Scene = null;
            _previewSummary.Text = asset.Path;
            _previewStatus.Text = "Preview unavailable: " + exception.Message;
        }
        finally
        {
            UseWaitCursor = false;
        }
    }

    private void RebuildAmbientPreview(bool resetView = false)
    {
        if (_loading || _current == null || _scene == null) return;
        int selected = (_ambientList.SelectedItem as AmbientListItem)?.Ambient.Index ?? -1;
        try
        {
            UseWaitCursor = true;
            _ambientPreview = StadiumAmbientPreviewBuilder.Build(_sceneArchive, _current, _scene,
                _ambientModelCache, selected, _showAmbientModels.Checked, _showDisabledAmbients.Checked,
                _splineDocuments);
            _previewScene = _ambientPreview.Scene;
            _preview.SetScene(_previewScene, resetView);
            UpdatePreviewGuides();
            UpdateAmbientListVisuals();
            UpdateAmbientInfo();
            _previewSummary.Text = $"{Path.GetFileName(_scene.SourcePath)}  |  {_scene.VertexCount:N0} field vertices  |  " +
                                   $"{_ambientPreview.VisibleModelCount:N0} ambient models  |  {_ambientPreview.PathCount:N0} paths";
            SyncDetachedPreviews();
        }
        catch (Exception exception)
        {
            _ambientPreview = null;
            _previewScene = _scene;
            _preview.SetScene(_scene, resetView);
            _preview.Guides = [];
            _previewStatus.Text = "Ambient preview unavailable: " + exception.Message;
        }
        finally
        {
            UseWaitCursor = false;
        }
    }

    private void UpdatePreviewGuides()
    {
        if (_ambientPreview == null)
        {
            _preview.Guides = [];
            return;
        }
        int selected = (_ambientList.SelectedItem as AmbientListItem)?.Ambient.Index ?? -1;
        _preview.Guides = _ambientPreview.Items
            .Where(item => item.Anchor.HasValue)
            .Select(item =>
            {
                Vector3 position = item.Anchor!.Value;
                if (item.AmbientIndex == selected && item.PathPoints.Count > 1 && _ambientPlaybackDuration > 0)
                {
                    float progress = (float)Math.Clamp(_ambientPlaybackPosition / _ambientPlaybackDuration, 0, 1);
                    position = StadiumAmbientPreviewBuilder.SamplePath(item.PathPoints, progress).Position;
                }
                return new RenderWarePreviewGuide(item.AmbientIndex, item.Name, position,
                    item.PathPoints, item.IsLoaded, item.AmbientIndex == selected,
                    item.AmbientIndex == selected ? _selectedSplinePoint : -1);
            })
            .ToList();
        _preview.ShowGuideMarkers = true;
        _preview.ShowGuidePaths = true;
        _preview.ShowAllGuidePaths = _showAmbientPaths.Checked;
    }

    private void UpdateAmbientListVisuals()
    {
        if (_current == null || _ambientPreview == null) return;
        int selected = _ambientList.SelectedIndex;
        _loading = true;
        _ambientList.BeginUpdate();
        _ambientList.Items.Clear();
        foreach (FieldDataAmbient ambient in _current.Document.Ambients)
        {
            StadiumAmbientVisual? visual = _ambientPreview.Items.FirstOrDefault(item => item.AmbientIndex == ambient.Index);
            _ambientList.Items.Add(new AmbientListItem(ambient,
                ambient.Index < _current.Document.DeclaredAmbientCount, visual));
        }
        _ambientList.EndUpdate();
        _ambientList.SelectedIndex = _ambientList.Items.Count == 0 ? -1 : Math.Clamp(selected, 0, _ambientList.Items.Count - 1);
        _loading = false;
    }

    private void UpdateAmbientInfo()
    {
        int selected = (_ambientList.SelectedItem as AmbientListItem)?.Ambient.Index ?? -1;
        StadiumAmbientVisual? visual = _ambientPreview?.Items.FirstOrDefault(item => item.AmbientIndex == selected);
        if (visual == null)
        {
            _ambientInfo.Text = "Select an ambient block to view its resolved model, placement, path, and animations.";
            return;
        }
        string position = visual.Anchor is Vector3 anchor
            ? $"Position {anchor.X:0.##}, {anchor.Y:0.##}, {anchor.Z:0.##}" : "No fixed position";
        string path = visual.PathPoints.Count > 1 ? $"Path: {visual.PathPoints.Count} points" : "No movement path";
        string animations = visual.Animations.Count == 0 ? "No ANM assignment" :
            "ANM: " + string.Join(", ", visual.Animations.Take(3)) + (visual.Animations.Count > 3 ? "…" : string.Empty);
        FieldDataAmbient? ambient = SelectedAmbient();
        string speed = ambient != null && visual.PathPoints.Count > 1
            ? $"Field speed {StadiumAmbientPreviewBuilder.GetPreviewSpeed(ambient):0.##}; preview cycle {_ambientPlaybackDuration:0.#}s"
            : "No spline playback";
        _ambientInfo.Text = $"{visual.AssetPath ?? visual.AssetKind}  |  {position}  |  {path}\r\n{animations}  |  {speed}  —  {visual.Note}";
    }

    private void SelectAmbientGuide(int ambientIndex, int pointIndex)
    {
        SelectAmbient(ambientIndex);
        if (pointIndex >= 0) SelectSplinePoint(pointIndex);
    }

    private void SelectAmbient(int ambientIndex)
    {
        for (int index = 0; index < _ambientList.Items.Count; index++)
        {
            if ((_ambientList.Items[index] as AmbientListItem)?.Ambient.Index != ambientIndex) continue;
            _tabs.SelectedIndex = 2;
            _ambientList.SelectedIndex = index;
            return;
        }
    }

    private void ApplyLivePreviewSettings()
    {
        if (_current == null || _scene == null) return;
        ApplyPreviewLight();
        ApplyPreviewCamera();
        UpdateOrbitLightStatus();
    }

    private void ApplyPreviewLight()
    {
        if (_current == null || _scene == null) return;
        _previewLight = ReadVector(_current.Document.FieldSettings, "ambLight", 4, Vector4.One);
        _preview.EnvironmentLight = _previewLight;
    }

    private void UpdateOrbitLightStatus()
    {
        if (_previewView.SelectedIndex == 0)
        {
            _previewStatus.Text = $"Live ambient light: {_previewLight.X:0.##}, {_previewLight.Y:0.##}, " +
                                  $"{_previewLight.Z:0.##}, {_previewLight.W:0.##}. Select a fielddata camera to preview camPos/camHpr edits.";
        }
    }

    private void ApplyPreviewCamera()
    {
        if (_scene == null || _current == null) return;
        BackyardCameraPreset? preset = _previewView.SelectedIndex switch
        {
            1 => ReadCamera("camPos", "camHpr", "Fielddata camera",
                "Live camPos/camHpr from fielddata.txt (team-photo/presentation camera)"),
            2 => ReadCamera("commPos", "commHpr", "Commentator camera",
                "Live commPos/commHpr from fielddata.txt"),
            3 => BackyardFieldCoordinates.CameraPresets[0],
            _ => null
        };
        _activePreviewCamera = preset;
        if (preset == null)
        {
            _preview.ResetView();
            if (_previewView.SelectedIndex == 0)
                _previewStatus.Text = "Orbit view. Drag to rotate, right-drag to pan, and use the mouse wheel to zoom.";
            return;
        }
        _preview.SetFieldCamera(preset);
        _previewStatus.Text = preset.Source + "  •  drag to look  •  WASD move  •  Q/E height";
        BeginInvoke(_preview.Focus);
    }

    private BackyardCameraPreset? ReadCamera(string positionKey, string hprKey, string name, string source)
    {
        if (_current == null) return null;
        float[]? position = ReadNumbers(_current.Document.FieldSettings, positionKey, 3);
        float[]? hpr = ReadNumbers(_current.Document.FieldSettings, hprKey, 2);
        if (position == null || hpr == null)
        {
            _previewStatus.Text = $"{name} needs valid {positionKey} and {hprKey} values.";
            return null;
        }
        return new BackyardCameraPreset(name, new Vector3(position[0], position[1], position[2]),
            hpr[0], hpr[1], source + "; roll is not shown by this preview");
    }

    private static Vector4 ReadVector(IReadOnlyList<FieldDataSetting> settings, string key, int count, Vector4 fallback)
    {
        float[]? values = ReadNumbers(settings, key, count);
        return values == null ? fallback : new Vector4(values[0], values[1], values[2], values[3]);
    }

    private static float[]? ReadNumbers(IReadOnlyList<FieldDataSetting> settings, string key, int minimumCount)
    {
        string? value = settings.FirstOrDefault(setting =>
            setting.Key.Equals(key, StringComparison.OrdinalIgnoreCase))?.Value;
        if (value == null) return null;
        string[] parts = value.Replace(';', ' ').Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < minimumCount) return null;
        float[] numbers = new float[parts.Length];
        for (int index = 0; index < parts.Length; index++)
        {
            if (!float.TryParse(parts[index], NumberStyles.Float, CultureInfo.InvariantCulture, out numbers[index]) ||
                !float.IsFinite(numbers[index])) return null;
        }
        return numbers;
    }

    private void OpenLargePreview()
    {
        if (_previewScene == null) return;
        RenderWareDetachedPreviewForm preview = new(_previewScene, true, _preview.HideSkyRoof,
            !_preview.HideHelperGeometry, _preview.CullBackfaces, _preview.Wireframe,
            _previewLight, _activePreviewCamera, _preview.Guides);
        _detachedPreviews.Add(preview);
        preview.GuideClicked += (_, e) => SelectAmbientGuide(e.Key, e.PointIndex);
        preview.FormClosed += (_, _) => _detachedPreviews.Remove(preview);
        preview.Show(this);
    }

    private void ConfigureAmbientPlayback()
    {
        FieldDataAmbient? ambient = SelectedAmbient();
        StadiumAmbientVisual? visual = SelectedAmbientVisual();
        _ambientPlaybackPosition = 0;
        _ambientPathDuration = ambient != null && visual?.PathPoints.Count > 1
            ? StadiumAmbientPreviewBuilder.EstimatePreviewDuration(ambient) : 0;
        LoadAmbientAnimations(ambient, visual);
        _ambientPlaybackDuration = _ambientPathDuration > 0
            ? _ambientPathDuration
            : _activeAmbientAnimation?.DurationSeconds ?? 0;
        CapturePlaybackMeshes(ambient);
        UpdatePlaybackControls();
        UpdateAmbientInfo();
    }

    private void LoadAmbientAnimations(FieldDataAmbient? ambient, StadiumAmbientVisual? visual)
    {
        _loadingAnimation = true;
        _ambientAnimation.Items.Clear();
        _activeAmbientAnimation = null;
        _activeAmbientBinding = null;
        _activeAmbientModel = null;
        if (ambient != null && visual != null)
        {
            string pathValue = ambient.Settings.FirstOrDefault(setting =>
                setting.Key.Equals("path", StringComparison.OrdinalIgnoreCase))?.Value
                ?? $"Fields/{_current?.FolderName}";
            foreach (StadiumAmbientAnimationAssignment assignment in visual.Animations)
            {
                RenderWareAnimationFile? file = _animationArchive.FindAmbientAnimation(
                    pathValue, assignment.AssetName, _current?.FolderName ?? string.Empty);
                _ambientAnimation.Items.Add(new AmbientAnimationItem(assignment, file));
            }
        }
        _ambientAnimation.SelectedIndex = _ambientAnimation.Items.Count > 0 ? 0 : -1;
        _loadingAnimation = false;
        ResolveAmbientAnimation();
    }

    private void AmbientAnimationChanged()
    {
        if (_loadingAnimation) return;
        StopAmbientPlayback(render: false);
        ResolveAmbientAnimation();
        _ambientPlaybackDuration = _ambientPathDuration > 0
            ? _ambientPathDuration
            : _activeAmbientAnimation?.DurationSeconds ?? 0;
        CapturePlaybackMeshes(SelectedAmbient());
        UpdatePlaybackControls();
        ApplyPlaybackFrame();
        UpdateAmbientInfo();
    }

    private void AmbientAnimationTimingChanged()
    {
        if (_loadingAnimation) return;
        ApplyPlaybackFrame();
        UpdatePlaybackControls();
    }

    private void ResolveAmbientAnimation()
    {
        _activeAmbientAnimation = null;
        _activeAmbientBinding = null;
        _activeAmbientModel = null;
        AmbientAnimationItem? item = _ambientAnimation.SelectedItem as AmbientAnimationItem;
        StadiumAmbientVisual? visual = SelectedAmbientVisual();
        if (item == null)
        {
            _ambientAnimationStatus.Text = "No ANM assigned";
            return;
        }
        _loopAmbientAnimation.Checked = !item.Assignment.PlaysOnce;
        if (item.File == null)
        {
            _ambientAnimationStatus.Text = "ANM file not found";
            return;
        }
        if (string.IsNullOrWhiteSpace(visual?.AssetPath))
        {
            _ambientAnimationStatus.Text = "No DFF model to animate";
            return;
        }
        RenderWareAnimationBinding? binding = _animationArchive.ResolveSkeleton(item.File, visual.AssetPath);
        if (binding == null)
        {
            _ambientAnimationStatus.Text = $"Incompatible: {item.File.TrackCount} ANM tracks do not match this DFF";
            return;
        }
        RenderWareSkinnedModel? model = _animationArchive.LoadModel(binding);
        if (model == null)
        {
            _ambientAnimationStatus.Text = "DFF has no supported RenderWare skin";
            return;
        }
        _activeAmbientAnimation = item.File;
        _activeAmbientBinding = binding;
        _activeAmbientModel = model;
        string mode = item.Assignment.PlaysOnce ? "once" : "loop";
        if (item.Assignment.IsHomeRun) mode = "home-run " + mode;
        _ambientAnimationStatus.Text = $"Compatible: {item.File.TrackCount} tracks, {item.File.DurationSeconds:0.###}s, {mode}";
    }

    private void LoadSplineEditor(FieldDataAmbient? ambient)
    {
        _currentSpline = null;
        _selectedSplinePoint = -1;
        string? value = ambient?.Settings.FirstOrDefault(setting =>
            setting.Key.Equals("spline", StringComparison.OrdinalIgnoreCase))?.Value;
        if (string.IsNullOrWhiteSpace(value))
        {
            LoadSplineRows();
            _splineStatus.Text = "This ambient has no spline movement path.";
            _splineEditor.Enabled = false;
            return;
        }
        string path = StadiumSplineDocument.NormalizePath(value);
        try
        {
            if (!_splineDocuments.TryGetValue(path, out _currentSpline))
            {
                byte[]? data = _sceneArchive.ReadRawPath(path);
                if (data == null) throw new InvalidDataException($"The archive does not contain '{path}'.");
                _currentSpline = StadiumSplineDocument.Parse(path, data);
                _splineDocuments[path] = _currentSpline;
            }
            _splineEditor.Enabled = true;
            LoadSplineRows(0);
            UpdateSplineStatus();
        }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentException)
        {
            _currentSpline = null;
            _splineEditor.Enabled = false;
            LoadSplineRows();
            _splineStatus.Text = "Path unavailable: " + exception.Message;
        }
    }

    private void LoadSplineRows(int selected = -1)
    {
        _loadingSpline = true;
        _splineGrid.Rows.Clear();
        if (_currentSpline != null)
        {
            for (int index = 0; index < _currentSpline.Points.Count; index++)
            {
                Vector3 point = _currentSpline.Points[index];
                _splineGrid.Rows.Add(index + 1, Coordinate(point.X), Coordinate(point.Y), Coordinate(point.Z));
            }
        }
        _loadingSpline = false;
        if (_splineGrid.Rows.Count > 0 && selected >= 0)
            SelectSplinePoint(Math.Clamp(selected, 0, _splineGrid.Rows.Count - 1));
        else
            _selectedSplinePoint = -1;
    }

    private void SelectSplineGridPoint()
    {
        if (_loadingSpline) return;
        _selectedSplinePoint = _splineGrid.SelectedRows.Count == 0
            ? -1 : _splineGrid.SelectedRows[0].Index;
        UpdatePreviewGuides();
        SyncDetachedPreviews();
    }

    private void SelectSplinePoint(int index)
    {
        if (index < 0 || index >= _splineGrid.Rows.Count) return;
        _splineGrid.ClearSelection();
        _splineGrid.Rows[index].Selected = true;
        _splineGrid.CurrentCell = _splineGrid.Rows[index].Cells[1];
        _selectedSplinePoint = index;
        UpdatePreviewGuides();
        SyncDetachedPreviews();
    }

    private void SplineGrid_CellValidating(object? sender, DataGridViewCellValidatingEventArgs e)
    {
        if (_loadingSpline || e.RowIndex < 0 || e.ColumnIndex is < 1 or > 3) return;
        if (!float.TryParse(Convert.ToString(e.FormattedValue), NumberStyles.Float,
                CultureInfo.InvariantCulture, out float value) || !float.IsFinite(value))
        {
            e.Cancel = true;
            _splineGrid.Rows[e.RowIndex].ErrorText = "Enter a finite numeric coordinate.";
        }
        else _splineGrid.Rows[e.RowIndex].ErrorText = string.Empty;
    }

    private void SplineGrid_CellValueChanged(object? sender, DataGridViewCellEventArgs e)
    {
        if (_loadingSpline || _currentSpline == null || e.RowIndex < 0 || e.ColumnIndex is < 1 or > 3) return;
        if (!TryReadSplineRow(e.RowIndex, out Vector3 point)) return;
        _currentSpline.SetPoint(e.RowIndex, point);
        SplineChanged(e.RowIndex);
    }

    private bool TryReadSplineRow(int row, out Vector3 point)
    {
        point = default;
        float[] values = new float[3];
        for (int column = 1; column <= 3; column++)
            if (!float.TryParse(Convert.ToString(_splineGrid.Rows[row].Cells[column].Value),
                    NumberStyles.Float, CultureInfo.InvariantCulture, out values[column - 1]) ||
                !float.IsFinite(values[column - 1])) return false;
        point = new Vector3(values[0], values[1], values[2]);
        return true;
    }

    private void AddSplinePoint()
    {
        if (_currentSpline == null) return;
        int selected = Math.Clamp(_selectedSplinePoint, 0, _currentSpline.Points.Count - 1);
        Vector3 value;
        if (selected < _currentSpline.Points.Count - 1)
            value = Vector3.Lerp(_currentSpline.Points[selected], _currentSpline.Points[selected + 1], 0.5F);
        else
        {
            Vector3 direction = _currentSpline.Points[^1] - _currentSpline.Points[^2];
            value = _currentSpline.Points[^1] + direction;
        }
        int target = _currentSpline.InsertAfter(selected, value);
        LoadSplineRows(target);
        SplineChanged(target);
    }

    private void DuplicateSplinePoint()
    {
        if (_currentSpline == null || _selectedSplinePoint < 0) return;
        int target = _currentSpline.Duplicate(_selectedSplinePoint);
        LoadSplineRows(target);
        SplineChanged(target);
    }

    private void DeleteSplinePoint()
    {
        if (_currentSpline == null || _selectedSplinePoint < 0) return;
        try
        {
            int target = _currentSpline.RemoveAt(_selectedSplinePoint);
            LoadSplineRows(target);
            SplineChanged(target);
        }
        catch (InvalidOperationException exception)
        {
            MessageBox.Show(this, exception.Message, "Cannot Delete Waypoint",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private void MoveSplinePoint(int offset)
    {
        if (_currentSpline == null || _selectedSplinePoint < 0) return;
        int target = _currentSpline.Move(_selectedSplinePoint, offset);
        LoadSplineRows(target);
        SplineChanged(target);
    }

    private void ResetSplinePath()
    {
        if (_currentSpline == null || !_currentSpline.IsChanged) return;
        _currentSpline.Reset();
        LoadSplineRows(0);
        SplineChanged(0);
    }

    private void SplineChanged(int selected)
    {
        StopAmbientPlayback(render: false);
        _selectedSplinePoint = selected;
        RebuildAmbientPreview();
        ConfigureAmbientPlayback();
        SelectSplinePoint(selected);
        UpdateSplineStatus();
        UpdateSummaryAndStatus();
    }

    private void UpdateSplineStatus()
    {
        if (_currentSpline == null) return;
        string changed = _currentSpline.IsChanged ? " — modified" : string.Empty;
        _splineStatus.Text = $"{_currentSpline.SourcePath}  |  {_currentSpline.Points.Count} points  |  " +
                             $"type {_currentSpline.SplineType}{changed}";
    }

    private static string Coordinate(float value) => value.ToString("0.######", CultureInfo.InvariantCulture);

    private void RefreshAmbientComposition()
    {
        StopAmbientPlayback(render: false);
        RebuildAmbientPreview();
        ConfigureAmbientPlayback();
    }

    private void PlayAmbient()
    {
        if (_ambientPlaybackDuration <= 0) return;
        if (_ambientPlaybackPosition >= _ambientPlaybackDuration - 0.0001)
            SetAmbientPlaybackPosition(0, render: true);
        _ambientPlaybackStart = _ambientPlaybackPosition;
        _ambientPlaybackWatch.Restart();
        _ambientPlaying = true;
        _ambientPlaybackTimer.Start();
        UpdatePlaybackControls();
    }

    private void PauseAmbient()
    {
        if (!_ambientPlaying) return;
        AmbientPlaybackTimer_Tick(this, EventArgs.Empty);
        _ambientPlaying = false;
        _ambientPlaybackTimer.Stop();
        _ambientPlaybackWatch.Stop();
        UpdatePlaybackControls();
    }

    private void StopAmbientPlayback(bool render)
    {
        _ambientPlaying = false;
        _ambientPlaybackTimer.Stop();
        _ambientPlaybackWatch.Reset();
        _ambientPlaybackStart = 0;
        SetAmbientPlaybackPosition(0, render);
    }

    private void AmbientPlaybackTimer_Tick(object? sender, EventArgs e)
    {
        if (!_ambientPlaying || _ambientPlaybackDuration <= 0) return;
        double position = _ambientPlaybackStart + _ambientPlaybackWatch.Elapsed.TotalSeconds * PlaybackRate();
        if (position >= _ambientPlaybackDuration)
        {
            if (ShouldLoopPlayback())
            {
                position %= _ambientPlaybackDuration;
                _ambientPlaybackStart = position;
                _ambientPlaybackWatch.Restart();
            }
            else
            {
                position = _ambientPlaybackDuration;
                _ambientPlaying = false;
                _ambientPlaybackTimer.Stop();
                _ambientPlaybackWatch.Stop();
            }
        }
        SetAmbientPlaybackPosition(position, render: true);
    }

    private void ScrubAmbient()
    {
        if (_updatingScrubber || _ambientPlaybackDuration <= 0) return;
        double position = _ambientScrubber.Value / (double)_ambientScrubber.Maximum * _ambientPlaybackDuration;
        SetAmbientPlaybackPosition(position, render: true);
        if (_ambientPlaying)
        {
            _ambientPlaybackStart = _ambientPlaybackPosition;
            _ambientPlaybackWatch.Restart();
        }
    }

    private void RebasePlaybackClock()
    {
        if (!_ambientPlaying) return;
        AmbientPlaybackTimer_Tick(this, EventArgs.Empty);
        _ambientPlaybackStart = _ambientPlaybackPosition;
        _ambientPlaybackWatch.Restart();
    }

    private void SetAmbientPlaybackPosition(double position, bool render)
    {
        _ambientPlaybackPosition = Math.Clamp(position, 0, Math.Max(0, _ambientPlaybackDuration));
        UpdatePlaybackControls();
        if (render && _ambientPlaybackDuration > 0)
            ApplyPlaybackFrame();
    }

    private void UpdatePlaybackControls()
    {
        bool available = _ambientPlaybackDuration > 0;
        _playAmbient.Enabled = available && !_ambientPlaying;
        _pauseAmbient.Enabled = available && _ambientPlaying;
        _stopAmbient.Enabled = available;
        _ambientScrubber.Enabled = available;
        _ambientPlaybackRate.Enabled = available;
        _loopAmbient.Enabled = _ambientPathDuration > 0;
        _faceAmbientPath.Enabled = _ambientPathDuration > 0;
        _ambientAnimation.Enabled = _ambientAnimation.Items.Count > 0;
        bool animationAvailable = _activeAmbientAnimation != null;
        _syncAmbientAnimation.Enabled = animationAvailable && _ambientPathDuration > 0;
        _loopAmbientAnimation.Enabled = animationAvailable;
        _updatingScrubber = true;
        _ambientScrubber.Value = !available ? 0 : Math.Clamp(
            (int)Math.Round(_ambientPlaybackPosition / _ambientPlaybackDuration * _ambientScrubber.Maximum),
            _ambientScrubber.Minimum, _ambientScrubber.Maximum);
        _updatingScrubber = false;
        _ambientPlaybackTime.Text = available
            ? $"{_ambientPlaybackPosition:0.0} / {_ambientPlaybackDuration:0.0}s"
            : "No path or compatible ANM";
    }

    private bool ShouldLoopPlayback() => _ambientPathDuration > 0
        ? _loopAmbient.Checked
        : _loopAmbientAnimation.Checked;

    private double PlaybackRate() => _ambientPlaybackRate.SelectedIndex switch
    {
        0 => 0.25,
        1 => 0.5,
        3 => 2,
        4 => 4,
        _ => 1
    };

    private void CapturePlaybackMeshes(FieldDataAmbient? ambient)
    {
        List<AmbientPlaybackMesh> previous = _playbackMeshes.ToList();
        _playbackMeshes.Clear();
        if (ambient == null || _previewScene == null) return;
        string prefix = $"Ambient {ambient.Index + 1:00}:";
        List<RenderWareSkinnedMesh> unusedSkinned = _activeAmbientModel?.Meshes.ToList() ?? [];
        foreach (RenderWareSceneMesh mesh in _previewScene.Meshes.Where(item =>
                     item.Name.StartsWith(prefix, StringComparison.Ordinal)))
        {
            if (mesh.Vertices is IList<RenderWareSceneVertex> vertices && !vertices.IsReadOnly)
            {
                int skinIndex = unusedSkinned.FindIndex(candidate =>
                    candidate.Vertices.Count == vertices.Count &&
                    candidate.Triangles.Count == mesh.Triangles.Count);
                RenderWareSkinnedMesh? skinned = skinIndex >= 0 ? unusedSkinned[skinIndex] : null;
                if (skinIndex >= 0) unusedSkinned.RemoveAt(skinIndex);
                RenderWareSceneVertex[] baseline = previous.FirstOrDefault(item =>
                    ReferenceEquals(item.Vertices, vertices))?.Baseline ?? vertices.ToArray();
                _playbackMeshes.Add(new AmbientPlaybackMesh(vertices, baseline, skinned));
            }
        }
    }

    private void ApplyPlaybackFrame()
    {
        FieldDataAmbient? ambient = SelectedAmbient();
        StadiumAmbientVisual? visual = SelectedAmbientVisual();
        if (ambient == null || visual?.Anchor == null || _ambientPlaybackDuration <= 0) return;
        bool hasPath = visual.PathPoints.Count > 1 && _ambientPathDuration > 0;
        Matrix4x4 delta = Matrix4x4.Identity;
        if (hasPath)
        {
            float progress = (float)Math.Clamp(_ambientPlaybackPosition / _ambientPathDuration, 0, 1);
            StadiumAmbientPathSample sample = StadiumAmbientPreviewBuilder.SamplePath(visual.PathPoints, progress);
            delta = StadiumAmbientPreviewBuilder.CreatePlaybackDelta(
                ambient, visual.Anchor.Value, sample, _faceAmbientPath.Checked);
        }
        IReadOnlyList<RenderWareDeformedMesh>? deformed = null;
        if (_activeAmbientAnimation != null && _activeAmbientBinding != null && _activeAmbientModel != null)
        {
            float animationTime = StadiumAmbientPreviewBuilder.GetAnimationPlaybackTime(
                _ambientPlaybackPosition, _ambientPathDuration, _activeAmbientAnimation.DurationSeconds,
                _syncAmbientAnimation.Checked, _loopAmbientAnimation.Checked);
            deformed = _activeAmbientModel.Deform(
                _activeAmbientBinding, _activeAmbientAnimation, animationTime);
        }
        Matrix4x4 modelTransform = visual.ModelTransform ?? Matrix4x4.Identity;
        List<RenderWareSkinnedMesh> skinOrder = _activeAmbientModel?.Meshes.ToList() ?? [];
        foreach (AmbientPlaybackMesh playbackMesh in _playbackMeshes)
        {
            IList<RenderWareSceneVertex> vertices = playbackMesh.Vertices;
            RenderWareSceneVertex[] baseline = playbackMesh.Baseline;
            RenderWareDeformedMesh? animatedMesh = null;
            if (playbackMesh.Skinned != null && deformed != null)
            {
                int index = skinOrder.IndexOf(playbackMesh.Skinned);
                if (index >= 0 && index < deformed.Count) animatedMesh = deformed[index];
            }
            Matrix4x4 localToCurrent = modelTransform * delta;
            for (int index = 0; index < baseline.Length; index++)
            {
                RenderWareSceneVertex vertex = baseline[index];
                Vector3 normal = animatedMesh != null
                    ? Vector3.TransformNormal(animatedMesh.Normals[index], localToCurrent)
                    : Vector3.TransformNormal(vertex.Normal, delta);
                if (normal.LengthSquared() > 0.000001F) normal = Vector3.Normalize(normal);
                Vector3 position = animatedMesh != null
                    ? Vector3.Transform(animatedMesh.Positions[index], localToCurrent)
                    : Vector3.Transform(vertex.Position, delta);
                vertices[index] = vertex with
                {
                    Position = position,
                    Normal = normal
                };
            }
        }
        UpdatePreviewGuides();
        _preview.Invalidate();
        SyncDetachedPreviews();
    }

    private FieldDataAmbient? SelectedAmbient() =>
        (_ambientList.SelectedItem as AmbientListItem)?.Ambient;

    private StadiumAmbientVisual? SelectedAmbientVisual()
    {
        int selected = SelectedAmbient()?.Index ?? -1;
        return _ambientPreview?.Items.FirstOrDefault(item => item.AmbientIndex == selected);
    }

    private void SyncDetachedPreviews()
    {
        if (_previewScene == null) return;
        foreach (RenderWareDetachedPreviewForm preview in _detachedPreviews.Where(form => !form.IsDisposed).ToList())
            preview.UpdateScene(_previewScene, _preview.Guides);
        _detachedPreviews.RemoveAll(form => form.IsDisposed);
    }

    private void CloseDetachedPreviews()
    {
        foreach (RenderWareDetachedPreviewForm preview in _detachedPreviews.ToList())
            if (!preview.IsDisposed) preview.Close();
        _detachedPreviews.Clear();
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
        StopAmbientPlayback(render: false);
        FieldDataAmbient? ambient = (_ambientList.SelectedItem as AmbientListItem)?.Ambient;
        LoadSettings(_ambientGrid, ambient?.Settings ?? Array.Empty<FieldDataSetting>());
        LoadSplineEditor(ambient);
        LoadPlacementEditor(ambient);
        RebuildAmbientPreview();
        ConfigureAmbientPlayback();
    }

    private void CreateAmbientObject()
    {
        if (_current == null) return;
        using AmbientObjectCreatorForm dialog = new(_sceneArchive, _animationArchive, _current.FolderName);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            int index = _current.AddAmbient(dialog.ObjectName, dialog.Settings);
            ReloadCurrentDocument(index);
            _status.Text = $"Created ambient object {index + 1:00}. Save Stadiums to write it to DATA.MET.";
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidDataException)
        {
            MessageBox.Show(this, exception.Message, "Unable to Create Ambient Object",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void CloneSelectedAmbient()
    {
        if (_current == null || SelectedAmbient() is not FieldDataAmbient source) return;
        int index = _current.CloneAmbient(source.Index);
        ReloadCurrentDocument(index);
        _status.Text = $"Cloned ambient object {source.Index + 1:00} as {index + 1:00}.";
    }

    private void DeleteSelectedAmbient()
    {
        if (_current == null || SelectedAmbient() is not FieldDataAmbient ambient) return;
        if (MessageBox.Show(this,
                $"Delete ambient object {ambient.Index + 1:00}: {ambient.DisplayName}?\n\n" +
                "This removes its complete amb { } block and updates numAmbs. The change is not written until you save.",
                "Delete Ambient Object", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        int next = Math.Min(ambient.Index, Math.Max(0, _current.Document.Ambients.Count - 2));
        _current.RemoveAmbient(ambient.Index);
        ReloadCurrentDocument(_current.Document.Ambients.Count == 0 ? -1 : next);
        _status.Text = "Ambient object deleted. Save Stadiums to write the change to DATA.MET.";
    }

    private void CopyAmbientToStadium()
    {
        if (_current == null || SelectedAmbient() is not FieldDataAmbient ambient) return;
        StadiumEnvironment? target = SelectTargetStadium(_current);
        if (target == null) return;
        int index = target.CloneAmbientFrom(_current, ambient.Index,
            $"Copy of {ambient.DisplayName} from {_current.DisplayName}");
        _stadiums.SelectedItem = target;
        if (ReferenceEquals(_current, target)) ReloadCurrentDocument(index);
        else SelectAmbient(index);
        _status.Text = $"Copied the object to {target.DisplayName} as ambient {index + 1:00}.";
    }

    private StadiumEnvironment? SelectTargetStadium(StadiumEnvironment source)
    {
        using Form dialog = new()
        {
            Text = "Copy Ambient Object to Stadium", StartPosition = FormStartPosition.CenterParent,
            ClientSize = new Size(470, 145), MinimizeBox = false, MaximizeBox = false,
            FormBorderStyle = FormBorderStyle.FixedDialog, AutoScaleMode = AutoScaleMode.Dpi
        };
        ComboBox choices = new()
        {
            Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(12), Height = 28
        };
        choices.Items.AddRange(_archive.Stadiums.Where(item => !ReferenceEquals(item, source)).Cast<object>().ToArray());
        if (choices.Items.Count > 0) choices.SelectedIndex = 0;
        Label label = new()
        {
            Text = "Destination stadium:", Dock = DockStyle.Top, Height = 35, Padding = new Padding(12, 10, 12, 2)
        };
        FlowLayoutPanel buttons = new()
        {
            Dock = DockStyle.Bottom, Height = 52, FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(8)
        };
        Button cancel = new() { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        Button copy = new() { Text = "Copy Object", AutoSize = true, DialogResult = DialogResult.OK };
        buttons.Controls.AddRange(new Control[] { cancel, copy });
        dialog.Controls.Add(choices);
        dialog.Controls.Add(label);
        dialog.Controls.Add(buttons);
        dialog.AcceptButton = copy;
        dialog.CancelButton = cancel;
        return dialog.ShowDialog(this) == DialogResult.OK ? choices.SelectedItem as StadiumEnvironment : null;
    }

    private void ReloadCurrentDocument(int ambientIndex)
    {
        if (_current == null) return;
        StopAmbientPlayback(render: false);
        LoadSettings(_fieldGrid, _current.Document.FieldSettings);
        LoadSettings(_collisionGrid, _current.Document.CollisionSettings);
        LoadAmbientList();
        _loading = true;
        _ambientList.SelectedIndex = ambientIndex < 0 || _ambientList.Items.Count == 0
            ? -1 : Math.Clamp(ambientIndex, 0, _ambientList.Items.Count - 1);
        _loading = false;
        LoadSelectedAmbient();
        RefreshRawText();
        UpdateSummaryAndStatus();
        _tabs.SelectedIndex = 2;
    }

    private void LoadPlacementEditor(FieldDataAmbient? ambient)
    {
        _loadingPlacement = true;
        bool available = ambient != null;
        foreach (NumericUpDown value in _ambientPosition.Concat(_ambientRotation)) value.Enabled = available;
        float[]? position = ambient == null ? null : ReadNumbers(ambient.Settings, "pos", 3);
        float[]? relative = ambient == null ? null : ReadNumbers(ambient.Settings, "relPosHpr", 6);
        float[]? rotation = ambient == null ? null : ReadNumbers(ambient.Settings, "hpr", 3);
        Vector3? splineStart = _currentSpline?.Points.Count > 0 ? _currentSpline.Points[0] : null;
        for (int index = 0; index < 3; index++)
        {
            float fallback = index switch
            {
                0 => splineStart?.X ?? 0,
                1 => splineStart?.Y ?? 0,
                _ => splineStart?.Z ?? 0
            };
            SetPlacementValue(_ambientPosition[index], position?[index] ?? relative?[index] ?? fallback);
            SetPlacementValue(_ambientRotation[index], rotation?[index] ?? relative?[index + 3] ?? 0);
        }
        bool hasSpline = ambient?.Settings.Any(setting =>
            setting.Key.Equals("spline", StringComparison.OrdinalIgnoreCase)) == true;
        _placementStatus.Text = ambient == null ? "Select an object"
            : hasSpline && position == null
                ? "Showing the spline start; changing a value adds a fixed pos override"
                : "Changes update the 3D preview immediately";
        _loadingPlacement = false;
    }

    private void PlacementValueChanged()
    {
        if (_loadingPlacement || _current == null || SelectedAmbient() is not FieldDataAmbient ambient) return;
        string position = string.Join(" ", _ambientPosition.Select(value =>
            value.Value.ToString("0.###", CultureInfo.InvariantCulture)));
        string rotation = string.Join(" ", _ambientRotation.Select(value =>
            value.Value.ToString("0.###", CultureInfo.InvariantCulture)));
        int index = ambient.Index;
        _current.SetAmbientSetting(index, "pos", position);
        _current.SetAmbientSetting(index, "hpr", rotation);
        ReloadCurrentDocument(index);
    }

    private static void SetPlacementValue(NumericUpDown control, float value)
    {
        decimal converted = float.IsFinite(value) ? (decimal)value : 0;
        control.Value = Math.Clamp(converted, control.Minimum, control.Maximum);
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
        if (ReferenceEquals(grid, _fieldGrid) && setting.Key.Equals("ambLight", StringComparison.OrdinalIgnoreCase))
        {
            ApplyPreviewLight();
            UpdateOrbitLightStatus();
        }
        else if (ReferenceEquals(grid, _fieldGrid) &&
                 (_previewView.SelectedIndex == 1 &&
                  (setting.Key.Equals("camPos", StringComparison.OrdinalIgnoreCase) ||
                   setting.Key.Equals("camHpr", StringComparison.OrdinalIgnoreCase)) ||
                  _previewView.SelectedIndex == 2 &&
                  (setting.Key.Equals("commPos", StringComparison.OrdinalIgnoreCase) ||
                   setting.Key.Equals("commHpr", StringComparison.OrdinalIgnoreCase))))
        {
            ApplyPreviewCamera();
        }
        if (ReferenceEquals(grid, _ambientGrid))
        {
            StopAmbientPlayback(render: false);
            LoadSplineEditor(SelectedAmbient());
            RebuildAmbientPreview();
            ConfigureAmbientPlayback();
        }
        else if (ReferenceEquals(grid, _fieldGrid) &&
                 setting.Key.Equals("numAmbs", StringComparison.OrdinalIgnoreCase))
        {
            RebuildAmbientPreview();
        }
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
        RebuildAmbientPreview();
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
        int splines = _splineDocuments.Values.Count(document => document.IsChanged);
        _status.Text = changed == 0 && splines == 0 ? "No unsaved stadium changes."
            : $"{changed} fielddata file{(changed == 1 ? string.Empty : "s")} and " +
              $"{splines} spline path{(splines == 1 ? string.Empty : "s")} changed.";
    }

    private void ResetAll()
    {
        int selected = _stadiums.SelectedIndex;
        _loading = true;
        _archive.ResetAll();
        _splineDocuments.Clear();
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
        _splineGrid.EndEdit();
        int changed = _archive.ChangedStadiumCount;
        Dictionary<string, byte[]> splineChanges = _splineDocuments.Values
            .Where(document => document.IsChanged)
            .ToDictionary(document => document.SourcePath, document => document.Serialize(),
                StringComparer.OrdinalIgnoreCase);
        if (changed == 0 && splineChanges.Count == 0)
        {
            MessageBox.Show(this, "No stadium files were changed.", "Nothing to Save",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (MessageBox.Show(this,
                $"Write {changed} fielddata file{(changed == 1 ? string.Empty : "s")} and " +
                $"{splineChanges.Count} spline path{(splineChanges.Count == 1 ? string.Empty : "s")} to DATA.MET?\n\n" +
                "A timestamped DATA.MET backup will be created first.",
                "Save Stadium Environments", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;

        try
        {
            UseWaitCursor = true;
            Enabled = false;
            StadiumEnvironmentSaveResult result = _archive.SaveWithBackup(splineChanges);
            string rebuild = result.RebuiltArchive ? "\nThe archive was resized with sector alignment preserved." : string.Empty;
            MessageBox.Show(this,
                $"Saved {result.ChangedStadiumCount} fielddata file{(result.ChangedStadiumCount == 1 ? string.Empty : "s")} and " +
                $"{result.ChangedSplineCount} spline path{(result.ChangedSplineCount == 1 ? string.Empty : "s")}.\n\n" +
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

    private sealed record AmbientListItem(FieldDataAmbient Ambient, bool IsLoaded, StadiumAmbientVisual? Visual = null)
    {
        public override string ToString()
        {
            string loaded = IsLoaded ? string.Empty : "  [not loaded]";
            string model = Visual?.AssetPath != null ? (Visual.ModelVisible ? "  [model]" : "  [DFF]") : string.Empty;
            string path = Visual?.PathPoints.Count > 1 ? "  [path]" : string.Empty;
            string animation = Visual?.Animations.Count > 0 ? "  [ANM]" : string.Empty;
            return $"{Ambient.Index + 1:00}. {Ambient.DisplayName}{loaded}{model}{path}{animation}";
        }
    }

    private sealed record AmbientAnimationItem(
        StadiumAmbientAnimationAssignment Assignment,
        RenderWareAnimationFile? File)
    {
        public override string ToString() => File == null
            ? $"{Assignment.AssetName} ({Assignment.Directive}, missing)"
            : $"{Assignment.AssetName} ({Assignment.Directive})";
    }

    private sealed record AmbientPlaybackMesh(
        IList<RenderWareSceneVertex> Vertices,
        RenderWareSceneVertex[] Baseline,
        RenderWareSkinnedMesh? Skinned);
}
