using PS2_DATA_File_Extractor.FileOperations;

namespace PS2_DATA_File_Extractor;

internal sealed class RenderWareDetachedPreviewForm : Form
{
    private readonly RenderWareScenePreviewControl _preview = new() { Dock = DockStyle.Fill };

    public RenderWareDetachedPreviewForm(RenderWareScene scene, bool perspective, bool hideBackdrop,
        bool showHelpers, bool cullBackfaces, bool wireframe)
    {
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

        Controls.Add(_preview);
        Controls.Add(BuildToolbar(perspective, hideBackdrop, showHelpers, cullBackfaces, wireframe));
    }

    private Control BuildToolbar(bool perspective, bool hideBackdrop, bool showHelpers,
        bool cullBackfaces, bool wireframe)
    {
        FlowLayoutPanel toolbar = new()
        {
            Dock = DockStyle.Bottom,
            Height = 45,
            Padding = new Padding(8, 7, 8, 5),
            BackColor = SystemColors.Control,
            WrapContents = false
        };
        CheckBox perspectiveBox = Check("Perspective", perspective,
            value => _preview.Perspective = value);
        CheckBox backdropBox = Check("Hide backdrop", hideBackdrop,
            value => _preview.HideSkyRoof = value);
        CheckBox helpersBox = Check("Show helpers", showHelpers,
            value => _preview.HideHelperGeometry = !value);
        CheckBox cullBox = Check("Cull backfaces", cullBackfaces,
            value => _preview.CullBackfaces = value);
        CheckBox wireframeBox = Check("Wireframe", wireframe,
            value => _preview.Wireframe = value);
        Button reset = new() { Text = "Reset View", AutoSize = true };
        reset.Click += (_, _) => _preview.ResetView();
        Button maximize = new() { Text = "Maximize", AutoSize = true };
        maximize.Click += (_, _) => WindowState = WindowState == FormWindowState.Maximized
            ? FormWindowState.Normal : FormWindowState.Maximized;
        Button close = new() { Text = "Close", AutoSize = true };
        close.Click += (_, _) => Close();
        toolbar.Controls.AddRange(new Control[]
        {
            perspectiveBox, backdropBox, helpersBox, cullBox, wireframeBox, reset, maximize, close
        });
        return toolbar;
    }

    private static CheckBox Check(string text, bool value, Action<bool> changed)
    {
        CheckBox check = new() { Text = text, Checked = value, AutoSize = true, Margin = new Padding(4, 6, 8, 3) };
        check.CheckedChanged += (_, _) => changed(check.Checked);
        return check;
    }
}
