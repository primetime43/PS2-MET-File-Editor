using PS2_DATA_File_Extractor.FileOperations;
using System.Globalization;
using System.Numerics;

namespace PS2_DATA_File_Extractor;

public sealed class StadiumEnvironmentEditorForm : Form
{
    private readonly StadiumEnvironmentArchive _archive;
    private readonly RenderWareSceneArchive _sceneArchive;
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
    private StadiumEnvironment? _current;
    private RenderWareScene? _scene;
    private RenderWareScene? _previewScene;
    private StadiumAmbientPreviewResult? _ambientPreview;
    private readonly Dictionary<string, RenderWareScene> _ambientModelCache = new(StringComparer.OrdinalIgnoreCase);
    private Vector4 _previewLight = Vector4.One;
    private BackyardCameraPreset? _activePreviewCamera;
    private bool _loading;

    public StadiumEnvironmentEditorForm(StadiumEnvironmentArchive archive, string metPath)
    {
        _archive = archive;
        _sceneArchive = RenderWareSceneArchive.Load(metPath);
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
            Text = "Edit fielddata.txt while viewing the textured stadium and placed ambient models. Lighting, cameras, model positions, and movement paths update immediately; animation, particles, movies, and collision behavior still require the game."
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
        _preview.GuideClicked += (_, e) => SelectAmbient(e.Key);
        _showAmbientModels.CheckedChanged += (_, _) => RebuildAmbientPreview();
        _showDisabledAmbients.CheckedChanged += (_, _) => RebuildAmbientPreview();
        _showAmbientPaths.CheckedChanged += (_, _) => UpdatePreviewGuides();
        _tabs.SelectedIndexChanged += (_, _) => RefreshRawText();
        HookGrid(_fieldGrid);
        HookGrid(_collisionGrid);
        HookGrid(_ambientGrid);
        Shown += (_, _) => ApplyDefaultWorkspaceLayout();
        if (_stadiums.Items.Count > 0) _stadiums.SelectedIndex = 0;
        _status.Text = $"Loaded {_archive.Stadiums.Count} stadium variants from {metPath}.";
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
        pane.RowStyles.Add(new RowStyle(SizeType.Absolute, 128));

        TableLayoutPanel heading = new() { Dock = DockStyle.Fill, ColumnCount = 2 };
        heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        heading.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _previewSummary.Font = new Font(Font, FontStyle.Bold);
        Button openLarge = new() { Text = "Open Large Preview...", AutoSize = true, Margin = new Padding(8, 7, 2, 5) };
        openLarge.Click += (_, _) => OpenLargePreview();
        heading.Controls.Add(_previewSummary, 0, 0);
        heading.Controls.Add(openLarge, 1, 0);

        TableLayoutPanel toolbar = new() { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1 };
        toolbar.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        toolbar.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
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
        toolbar.Controls.Add(actions, 0, 0);
        toolbar.Controls.Add(ambientActions, 0, 1);
        toolbar.Controls.Add(_previewStatus, 0, 2);

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
        split.Panel2.Controls.Add(_ambientGrid);
        split.Panel2.Controls.Add(_ambientInfo);
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
                _ambientModelCache, selected, _showAmbientModels.Checked, _showDisabledAmbients.Checked);
            _previewScene = _ambientPreview.Scene;
            _preview.SetScene(_previewScene, resetView);
            UpdatePreviewGuides();
            UpdateAmbientListVisuals();
            UpdateAmbientInfo();
            _previewSummary.Text = $"{Path.GetFileName(_scene.SourcePath)}  |  {_scene.VertexCount:N0} field vertices  |  " +
                                   $"{_ambientPreview.VisibleModelCount:N0} ambient models  |  {_ambientPreview.PathCount:N0} paths";
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
            .Select(item => new RenderWarePreviewGuide(item.AmbientIndex, item.Name, item.Anchor!.Value,
                item.PathPoints, item.IsLoaded, item.AmbientIndex == selected))
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
        _ambientInfo.Text = $"{visual.AssetPath ?? visual.AssetKind}  |  {position}  |  {path}\r\n{animations}  —  {visual.Note}";
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
        preview.Show(this);
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
        RebuildAmbientPreview();
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
        if (ReferenceEquals(grid, _ambientGrid) ||
            ReferenceEquals(grid, _fieldGrid) && setting.Key.Equals("numAmbs", StringComparison.OrdinalIgnoreCase))
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

    private sealed record AmbientListItem(FieldDataAmbient Ambient, bool IsLoaded, StadiumAmbientVisual? Visual = null)
    {
        public override string ToString()
        {
            string loaded = IsLoaded ? string.Empty : "  [not loaded]";
            string model = Visual?.AssetPath != null ? (Visual.ModelVisible ? "  [model]" : "  [DFF]") : string.Empty;
            string path = Visual?.PathPoints.Count > 1 ? "  [path]" : string.Empty;
            return $"{Ambient.Index + 1:00}. {Ambient.DisplayName}{loaded}{model}{path}";
        }
    }
}
