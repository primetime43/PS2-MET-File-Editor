using PS2_DATA_File_Extractor.FileOperations;
using System.Globalization;

namespace PS2_DATA_File_Extractor;

public sealed class AmbientObjectCreatorForm : Form
{
    private readonly RenderWareAnimationArchive _animations;
    private readonly TextBox _name = new() { Dock = DockStyle.Fill, Text = "New ambient object" };
    private readonly ComboBox _model = AssetCombo();
    private readonly ComboBox _animation = AssetCombo();
    private readonly ComboBox _animationKind = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 145 };
    private readonly ComboBox _spline = AssetCombo();
    private readonly NumericUpDown[] _position = Coordinates();
    private readonly NumericUpDown[] _rotation = Coordinates();
    private readonly Label _compatibility = new() { Dock = DockStyle.Fill, AutoEllipsis = true, ForeColor = SystemColors.GrayText };
    private bool _loading;

    public AmbientObjectCreatorForm(
        RenderWareSceneArchive scenes,
        RenderWareAnimationArchive animations,
        string stadiumFolder)
    {
        ArgumentNullException.ThrowIfNull(scenes);
        _animations = animations ?? throw new ArgumentNullException(nameof(animations));
        Text = "Create Ambient Object";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(880, 440);
        MinimumSize = new Size(720, 420);
        AutoScaleMode = AutoScaleMode.Dpi;
        MaximizeBox = false;
        MinimizeBox = false;

        _animationKind.Items.AddRange(new object[]
        {
            new DirectiveItem("Loop", "anim"),
            new DirectiveItem("Play once", "animOnce"),
            new DirectiveItem("Home run", "hrAnim"),
            new DirectiveItem("Home run once", "hrAnimOnce"),
            new DirectiveItem("Home run only", "hrAnimOnly")
        });
        _animationKind.SelectedIndex = 0;

        _loading = true;
        List<RenderWareAssetFile> models = scenes.Assets
            .Where(asset => asset.Kind == RenderWareAssetKind.DffModel)
            .OrderByDescending(asset => Normalize(asset.Path)
                .StartsWith($"data/fields/{stadiumFolder}/", StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(asset => Normalize(asset.Path)
                .StartsWith("data/fields/", StringComparison.OrdinalIgnoreCase))
            .ThenBy(asset => asset.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();
        foreach (RenderWareAssetFile model in models) _model.Items.Add(new ModelItem(model));
        _spline.Items.Add(new OptionalAssetItem("(no movement path)", null));
        foreach (string path in scenes.SplinePaths.Where(path =>
                     Normalize(path).StartsWith("data/fields/", StringComparison.OrdinalIgnoreCase)))
            _spline.Items.Add(new OptionalAssetItem(path, path));
        _spline.SelectedIndex = 0;
        _model.SelectedIndex = _model.Items.Count > 0 ? 0 : -1;
        _loading = false;

        TableLayoutPanel layout = new()
        {
            Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 2, RowCount = 8
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 145));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int row = 0; row < 6; row++) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 43));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        AddRow(layout, 0, "Object name:", _name);
        AddRow(layout, 1, "DFF model:", _model);
        AddRow(layout, 2, "ANM animation:", BuildAnimationRow());
        AddRow(layout, 3, "Movement path:", _spline);
        AddRow(layout, 4, "Position X / Y / Z:", CoordinateRow(_position));
        AddRow(layout, 5, "Heading / pitch / roll:", CoordinateRow(_rotation));
        layout.Controls.Add(_compatibility, 1, 6);

        FlowLayoutPanel buttons = new()
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false,
            Padding = new Padding(0, 9, 0, 0)
        };
        Button cancel = new() { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        Button create = new() { Text = "Create Object", AutoSize = true };
        create.Click += (_, _) => Accept();
        buttons.Controls.AddRange(new Control[] { cancel, create });
        layout.Controls.Add(buttons, 0, 7);
        layout.SetColumnSpan(buttons, 2);
        Controls.Add(layout);
        AcceptButton = create;
        CancelButton = cancel;

        _model.SelectedIndexChanged += (_, _) => LoadCompatibleAnimations();
        _animation.SelectedIndexChanged += (_, _) => UpdateCompatibilityText();
        _spline.SelectedIndexChanged += (_, _) => UpdateCompatibilityText();
        LoadCompatibleAnimations();
    }

    public string ObjectName => _name.Text.Trim();

    public IReadOnlyList<KeyValuePair<string, string>> Settings
    {
        get
        {
            ModelItem model = (ModelItem)_model.SelectedItem!;
            string directory = Normalize(Path.GetDirectoryName(model.Asset.Path) ?? string.Empty).Trim('/');
            if (directory.StartsWith("data/", StringComparison.OrdinalIgnoreCase)) directory = directory[5..];
            List<KeyValuePair<string, string>> settings =
            [
                new("path", directory),
                new("model", Path.GetFileName(model.Asset.Path))
            ];
            if (_animation.SelectedItem is AnimationItem animation)
                settings.Add(new KeyValuePair<string, string>(
                    ((DirectiveItem)_animationKind.SelectedItem!).Directive,
                    Path.GetFileName(animation.File.SourcePath)));
            if (_spline.SelectedItem is OptionalAssetItem { Path: string splinePath })
            {
                string spline = Normalize(splinePath);
                if (spline.StartsWith("data/", StringComparison.OrdinalIgnoreCase)) spline = spline[5..];
                settings.Add(new KeyValuePair<string, string>("spline", spline));
                settings.Add(new KeyValuePair<string, string>("speed", "1.0"));
            }
            else
                settings.Add(new KeyValuePair<string, string>("pos", Values(_position)));
            settings.Add(new KeyValuePair<string, string>("hpr", Values(_rotation)));
            return settings;
        }
    }

    private Control BuildAnimationRow()
    {
        TableLayoutPanel row = new() { Dock = DockStyle.Fill, ColumnCount = 2 };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.Controls.Add(_animation, 0, 0);
        row.Controls.Add(_animationKind, 1, 0);
        return row;
    }

    private void LoadCompatibleAnimations()
    {
        if (_loading) return;
        _animation.BeginUpdate();
        _animation.Items.Clear();
        _animation.Items.Add(new OptionalAssetItem("(no animation)", null));
        if (_model.SelectedItem is ModelItem model)
        {
            string modelDirectory = Normalize(Path.GetDirectoryName(model.Asset.Path) ?? string.Empty);
            IEnumerable<RenderWareAnimationFile> compatible = _animations.Files.Where(file =>
                Normalize(Path.GetDirectoryName(file.SourcePath) ?? string.Empty)
                    .Equals(modelDirectory, StringComparison.OrdinalIgnoreCase) &&
                _animations.ResolveSkeleton(file, model.Asset.Path) != null);
            foreach (RenderWareAnimationFile file in compatible.OrderBy(file => file.SourcePath,
                         StringComparer.OrdinalIgnoreCase))
                _animation.Items.Add(new AnimationItem(file));
        }
        _animation.SelectedIndex = 0;
        _animation.EndUpdate();
        UpdateCompatibilityText();
    }

    private void UpdateCompatibilityText()
    {
        if (_model.SelectedItem is not ModelItem model)
        {
            _compatibility.Text = "This archive has no field ambient DFF models.";
            return;
        }
        int count = _animation.Items.OfType<AnimationItem>().Count();
        string path = _spline.SelectedItem is OptionalAssetItem { Path: not null }
            ? "The object starts at the selected spline's first waypoint; position values are used only without a path."
            : "The object uses the entered fixed position.";
        _compatibility.Text = $"{model.Asset.Path}  •  {count:N0} compatible ANM file(s) in this model folder. {path}";
    }

    private void Accept()
    {
        if (_model.SelectedItem is not ModelItem)
        {
            MessageBox.Show(this, "Select a DFF model first.", "Ambient Object",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (ObjectName.Length == 0)
        {
            MessageBox.Show(this, "Enter a short name for the object.", "Ambient Object",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _name.Focus();
            return;
        }
        DialogResult = DialogResult.OK;
        Close();
    }

    private static void AddRow(TableLayoutPanel layout, int row, string label, Control control)
    {
        layout.Controls.Add(new Label
        {
            Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 10, 8, 0)
        }, 0, row);
        control.Margin = new Padding(0, 5, 0, 4);
        layout.Controls.Add(control, 1, row);
    }

    private static Control CoordinateRow(IEnumerable<NumericUpDown> values)
    {
        FlowLayoutPanel row = new() { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
        foreach (NumericUpDown value in values) row.Controls.Add(value);
        return row;
    }

    private static ComboBox AssetCombo() => new()
    {
        Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDown,
        AutoCompleteMode = AutoCompleteMode.SuggestAppend, AutoCompleteSource = AutoCompleteSource.ListItems
    };

    private static NumericUpDown[] Coordinates() => Enumerable.Range(0, 3).Select(_ => new NumericUpDown
    {
        Minimum = -1000000, Maximum = 1000000, DecimalPlaces = 3, Increment = 1,
        Width = 145, ThousandsSeparator = true, Margin = new Padding(0, 1, 8, 0)
    }).ToArray();

    private static string Values(IEnumerable<NumericUpDown> values) => string.Join(" ",
        values.Select(value => value.Value.ToString("0.###", CultureInfo.InvariantCulture)));
    private static string Normalize(string value) => value.Replace('\\', '/');

    private sealed record ModelItem(RenderWareAssetFile Asset)
    {
        public override string ToString() => Asset.Path;
    }

    private sealed record AnimationItem(RenderWareAnimationFile File)
    {
        public override string ToString() => $"{File.SourcePath}  [{File.DurationSeconds:0.###}s, {File.TrackCount} tracks]";
    }

    private sealed record OptionalAssetItem(string Label, string? Path)
    {
        public override string ToString() => Label;
    }

    private sealed record DirectiveItem(string Label, string Directive)
    {
        public override string ToString() => Label;
    }
}
