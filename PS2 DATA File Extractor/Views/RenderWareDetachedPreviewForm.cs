using PS2_DATA_File_Extractor.FileOperations;
using System.Numerics;

namespace PS2_DATA_File_Extractor;

internal sealed class RenderWareDetachedPreviewForm : Form
{
    private readonly RenderWareScenePreviewControl _preview = new() { Dock = DockStyle.Fill };
    private readonly BackyardCameraPreset? _initialCamera;
    private CheckBox? _helpersBox;
    public event EventHandler<RenderWarePreviewGuideClickedEventArgs>? GuideClicked;

    public RenderWareDetachedPreviewForm(RenderWareScene scene, bool perspective, bool hideBackdrop,
        bool showHelpers, bool cullBackfaces, bool wireframe,
        Vector4? environmentLight = null, BackyardCameraPreset? initialCamera = null,
        IReadOnlyList<RenderWarePreviewGuide>? guides = null)
    {
        _initialCamera = initialCamera;
        Text = $"3D Preview - {Path.GetFileName(scene.SourcePath)}";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(720, 480);
        ClientSize = new Size(1280, 800);
        MaximizeBox = true;
        MinimizeBox = true;
        ShowIcon = false;

        _preview.Scene = scene;
        _preview.Perspective = perspective;
        _preview.HideSkyRoof = hideBackdrop;
        _preview.HideHelperGeometry = !showHelpers;
        _preview.CullBackfaces = cullBackfaces;
        _preview.Wireframe = wireframe;
        _preview.EnvironmentLight = environmentLight ?? Vector4.One;
        _preview.Guides = guides ?? [];
        _preview.GuideClicked += (_, e) => GuideClicked?.Invoke(this, e);
        _preview.ShowGuideMarkers = true;
        _preview.ShowGuidePaths = true;
        _preview.ShowAllGuidePaths = false;

        Controls.Add(_preview);
        Controls.Add(BuildToolbar(perspective, hideBackdrop, showHelpers, cullBackfaces, wireframe,
            _preview.Guides.Count > 0));
        Shown += (_, _) =>
        {
            if (_initialCamera == null) _preview.ResetView();
            else _preview.SetFieldCamera(_initialCamera);
        };
    }

    public void UpdateScene(RenderWareScene scene, IReadOnlyList<RenderWarePreviewGuide> guides)
    {
        if (IsDisposed) return;
        _preview.SetScene(scene, resetView: false);
        _preview.Guides = guides;
    }

    public void SetShowHelpers(bool value)
    {
        if (_helpersBox != null) _helpersBox.Checked = value;
        else _preview.HideHelperGeometry = !value;
    }

    private Control BuildToolbar(bool perspective, bool hideBackdrop, bool showHelpers,
        bool cullBackfaces, bool wireframe, bool hasGuides)
    {
        TableLayoutPanel toolbar = new()
        {
            Dock = DockStyle.Bottom,
            Height = 82,
            Padding = new Padding(8, 3, 8, 3),
            BackColor = SystemColors.Control,
            RowCount = 2,
            ColumnCount = 1
        };
        toolbar.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        toolbar.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        FlowLayoutPanel navigation = Row();
        FlowLayoutPanel display = Row();

        ComboBox views = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 205 };
        views.Items.Add("Fit / orbit view");
        if (_initialCamera != null) views.Items.Add(_initialCamera);
        foreach (BackyardCameraPreset preset in BackyardFieldCoordinates.CameraPresets)
            if (!Equals(preset, _initialCamera)) views.Items.Add(preset);
        Label cameraInfo = new()
        {
            AutoSize = true,
            Text = "Field POV: drag to look • WASD move • Q/E height • Shift faster",
            Margin = new Padding(12, 7, 4, 0)
        };
        views.SelectedIndexChanged += (_, _) =>
        {
            if (views.SelectedItem is BackyardCameraPreset preset)
            {
                _preview.SetFieldCamera(preset);
                cameraInfo.Text = preset.Source + "  •  WASD/QE move, drag to look";
                if (IsHandleCreated) BeginInvoke(_preview.Focus);
            }
            else
            {
                _preview.ResetView();
                cameraInfo.Text = "Orbit: drag rotate • right-drag pan • wheel zoom";
            }
        };
        views.SelectedIndex = _initialCamera == null ? 0 : 1;
        ComboBox speed = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 85 };
        speed.Items.AddRange(new object[] { "Slow", "Normal", "Fast" });
        speed.SelectedIndex = 1;
        speed.SelectedIndexChanged += (_, _) => _preview.MovementSpeed = speed.SelectedIndex switch
        {
            0 => 350F,
            2 => 2500F,
            _ => 900F
        };
        navigation.Controls.AddRange(new Control[]
        {
            ToolbarLabel("View:"), views, ToolbarLabel("Move:"), speed, cameraInfo
        });

        CheckBox perspectiveBox = Check("Perspective", perspective,
            value => _preview.Perspective = value);
        CheckBox backdropBox = Check("Hide backdrop", hideBackdrop,
            value => _preview.HideSkyRoof = value);
        CheckBox helpersBox = Check("Show helpers", showHelpers,
            value => _preview.HideHelperGeometry = !value);
        _helpersBox = helpersBox;
        CheckBox cullBox = Check("Cull backfaces", cullBackfaces,
            value => _preview.CullBackfaces = value);
        CheckBox wireframeBox = Check("Wireframe", wireframe,
            value => _preview.Wireframe = value);
        CheckBox? markersBox = null;
        ComboBox? paths = null;
        if (hasGuides)
        {
            markersBox = Check("Ambient markers", true, value => _preview.ShowGuideMarkers = value);
            paths = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 112 };
            paths.Items.AddRange(new object[] { "Paths: Off", "Selected path", "All paths" });
            paths.SelectedIndexChanged += (_, _) =>
            {
                _preview.ShowGuidePaths = paths.SelectedIndex != 0;
                _preview.ShowAllGuidePaths = paths.SelectedIndex == 2;
            };
            paths.SelectedIndex = 1;
        }
        Button zoomOut = new() { Text = "Zoom −", AutoSize = true };
        zoomOut.Click += (_, _) => _preview.ZoomOut();
        Button zoomIn = new() { Text = "Zoom +", AutoSize = true };
        zoomIn.Click += (_, _) => _preview.ZoomIn();
        Button reset = new() { Text = "Fit View", AutoSize = true };
        reset.Click += (_, _) => _preview.ResetView();
        Button front = new() { Text = "Front", AutoSize = true };
        front.Click += (_, _) => _preview.ShowFrontView();
        Button top = new() { Text = "Top", AutoSize = true };
        top.Click += (_, _) => _preview.ShowTopView();
        Button maximize = new() { Text = "Maximize", AutoSize = true };
        maximize.Click += (_, _) => WindowState = WindowState == FormWindowState.Maximized
            ? FormWindowState.Normal : FormWindowState.Maximized;
        Button close = new() { Text = "Close", AutoSize = true };
        close.Click += (_, _) => Close();
        display.Controls.AddRange(new Control[]
        {
            perspectiveBox, backdropBox, helpersBox, cullBox, wireframeBox
        });
        if (markersBox != null && paths != null)
            display.Controls.AddRange(new Control[] { markersBox, paths });
        display.Controls.AddRange(new Control[] { zoomOut, zoomIn, reset, front, top, maximize, close });
        toolbar.Controls.Add(navigation, 0, 0);
        toolbar.Controls.Add(display, 0, 1);
        return toolbar;
    }

    private static FlowLayoutPanel Row() => new()
    {
        Dock = DockStyle.Fill,
        FlowDirection = FlowDirection.LeftToRight,
        WrapContents = false,
        AutoScroll = true,
        Margin = Padding.Empty
    };

    private static Label ToolbarLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Margin = new Padding(4, 7, 3, 0)
    };

    private static CheckBox Check(string text, bool value, Action<bool> changed)
    {
        CheckBox check = new() { Text = text, Checked = value, AutoSize = true, Margin = new Padding(4, 6, 8, 3) };
        check.CheckedChanged += (_, _) => changed(check.Checked);
        return check;
    }
}
