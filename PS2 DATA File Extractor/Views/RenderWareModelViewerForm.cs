using System.Text;
using PS2_DATA_File_Extractor.FileOperations;

namespace PS2_DATA_File_Extractor;

public sealed class RenderWareModelViewerForm : Form
{
    private readonly RenderWareSceneArchive _archive;
    private readonly string _metPath;
    private readonly SplitContainer _mainSplit = new();
    private readonly TextBox _search = new();
    private readonly ComboBox _filter = new();
    private readonly ListBox _assets = new();
    private readonly Label _summary = new();
    private readonly Label _status = new();
    private readonly RenderWareScenePreviewControl _preview = new();
    private readonly DataGridView _meshes = Grid();
    private readonly DataGridView _materials = Grid();
    private readonly DataGridView _chunks = Grid();
    private readonly CheckBox _wireframe = new() { Text = "Wireframe", AutoSize = true };
    private readonly Button _rawButton = Button("Export Raw...");
    private readonly Button _objButton = Button("Export OBJ...");
    private readonly Button _textureButton = Button("Export Textures...");
    private readonly Button _mapButton = Button("Export Texture Map...");
    private IReadOnlyList<RenderWareAssetFile> _visible = Array.Empty<RenderWareAssetFile>();
    private RenderWareAssetFile? _selectedAsset;
    private RenderWareScene? _scene;

    public RenderWareModelViewerForm(RenderWareSceneArchive archive, string metPath, string? preferredPath = null)
    {
        _archive = archive;
        _metPath = metPath;
        Text = "3D Model and Stadium Viewer - DATA.MET";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(1180, 720);
        ClientSize = new Size(1500, 860);
        Font = new Font("Segoe UI", 9F);

        Label intro = new()
        {
            Dock = DockStyle.Top, Height = 38, Padding = new Padding(12, 9, 8, 4),
            Text = "View RenderWare DFF models and RWS stadium scenes. Drag the preview to rotate; export original files, Wavefront OBJ geometry, and texture mappings."
        };
        Label path = new()
        {
            Dock = DockStyle.Top, Height = 34, Padding = new Padding(12, 7, 8, 3),
            Text = metPath, AutoEllipsis = true
        };
        Controls.Add(BuildMain());
        Controls.Add(BuildFooter());
        Controls.Add(path);
        Controls.Add(intro);

        _search.PlaceholderText = "Search models and stadiums...";
        _search.TextChanged += (_, _) => ApplyFilter();
        _filter.DropDownStyle = ComboBoxStyle.DropDownList;
        _filter.Items.AddRange(new object[] { "All assets", "Stadium scenes (.rws)", "DFF models", "Fields", "Players", "Props and effects" });
        _filter.SelectedIndex = 0;
        _filter.SelectedIndexChanged += (_, _) => ApplyFilter();
        _assets.SelectedIndexChanged += (_, _) => LoadSelected();
        _wireframe.CheckedChanged += (_, _) => _preview.Wireframe = _wireframe.Checked;
        _rawButton.Click += (_, _) => ExportRaw();
        _objButton.Click += (_, _) => ExportObj();
        _textureButton.Click += (_, _) => ExportTextures();
        _mapButton.Click += (_, _) => ExportTextureMap();
        Shown += (_, _) =>
        {
            ApplyDefaultLayout();
            ApplyFilter();
            if (!string.IsNullOrWhiteSpace(preferredPath))
            {
                int index = _visible.ToList().FindIndex(asset => asset.Path.Equals(preferredPath, StringComparison.OrdinalIgnoreCase));
                if (index >= 0) _assets.SelectedIndex = index;
            }
            if (_assets.SelectedIndex < 0 && _assets.Items.Count > 0) _assets.SelectedIndex = 0;
        };
    }

    private Control BuildMain()
    {
        _mainSplit.Dock = DockStyle.Fill;
        _mainSplit.FixedPanel = FixedPanel.Panel1;
        TableLayoutPanel left = new() { Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1, Padding = new Padding(8, 4, 4, 4) };
        left.RowStyles.Add(new RowStyle(SizeType.Absolute, 31));
        left.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        left.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        left.RowStyles.Add(new RowStyle(SizeType.Absolute, 29));
        _search.Dock = DockStyle.Fill; _filter.Dock = DockStyle.Fill; _assets.Dock = DockStyle.Fill;
        left.Controls.Add(_search, 0, 0); left.Controls.Add(_filter, 0, 1); left.Controls.Add(_assets, 0, 2);
        left.Controls.Add(new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft,
            Text = $"{_archive.DffCount:N0} DFF models  •  {_archive.RwsCount:N0} RWS scenes" }, 0, 3);
        _mainSplit.Panel1.Controls.Add(left);

        TableLayoutPanel right = new() { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Padding = new Padding(4) };
        right.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 56));
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 44));
        _summary.Dock = DockStyle.Fill; _summary.TextAlign = ContentAlignment.MiddleLeft;
        _summary.Font = new Font(Font, FontStyle.Bold); _summary.AutoEllipsis = true;
        _preview.Dock = DockStyle.Fill;
        right.Controls.Add(_summary, 0, 0); right.Controls.Add(_preview, 0, 1); right.Controls.Add(BuildDetails(), 0, 2);
        _mainSplit.Panel2.Controls.Add(right);
        return _mainSplit;
    }

    private void ApplyDefaultLayout()
    {
        int available = Math.Max(0, _mainSplit.ClientSize.Width - _mainSplit.SplitterWidth);
        int leftMinimum = Math.Min(280, available);
        int rightMinimum = Math.Min(700, Math.Max(0, available - leftMinimum));
        int maximumLeft = Math.Max(leftMinimum, available - rightMinimum);
        int desired = Math.Clamp(330, leftMinimum, maximumLeft);

        // SplitContainer validates each property immediately. Move the splitter while both
        // minimums are still zero, then establish minimums that the final width can satisfy.
        _mainSplit.Panel1MinSize = 0;
        _mainSplit.Panel2MinSize = 0;
        _mainSplit.SplitterDistance = desired;
        _mainSplit.Panel1MinSize = leftMinimum;
        _mainSplit.Panel2MinSize = Math.Min(rightMinimum, available - desired);
    }

    private Control BuildDetails()
    {
        TabControl tabs = new() { Dock = DockStyle.Fill };
        TabPage meshes = new("Meshes / sectors"); meshes.Controls.Add(_meshes);
        TabPage materials = new("Materials and textures"); materials.Controls.Add(_materials);
        TabPage chunks = new("RWS chunks"); chunks.Controls.Add(_chunks);
        tabs.TabPages.AddRange(new[] { meshes, materials, chunks });
        return tabs;
    }

    private Control BuildFooter()
    {
        TableLayoutPanel footer = new() { Dock = DockStyle.Bottom, Height = 50, ColumnCount = 2, Padding = new Padding(10, 6, 10, 6) };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _status.Dock = DockStyle.Fill; _status.TextAlign = ContentAlignment.MiddleLeft; _status.AutoEllipsis = true;
        FlowLayoutPanel actions = new() { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        Button reset = Button("Reset View"); reset.Click += (_, _) => _preview.ResetView();
        Button close = Button("Close"); close.Click += (_, _) => Close();
        actions.Controls.AddRange(new Control[] { _wireframe, reset, _rawButton, _objButton, _textureButton, _mapButton, close });
        footer.Controls.Add(_status, 0, 0); footer.Controls.Add(actions, 1, 0);
        return footer;
    }

    private void ApplyFilter()
    {
        string search = _search.Text.Trim();
        IEnumerable<RenderWareAssetFile> query = _archive.Assets;
        query = _filter.SelectedIndex switch
        {
            1 => query.Where(asset => asset.Kind == RenderWareAssetKind.RwsScene),
            2 => query.Where(asset => asset.Kind == RenderWareAssetKind.DffModel),
            3 => query.Where(asset => asset.Category.Equals("fields", StringComparison.OrdinalIgnoreCase)),
            4 => query.Where(asset => asset.Category is "batting" or "fielding" or "playercard" or "kids" or "commentators"),
            5 => query.Where(asset => asset.Category.Equals("models", StringComparison.OrdinalIgnoreCase)),
            _ => query
        };
        if (search.Length > 0) query = query.Where(asset => asset.Path.Contains(search, StringComparison.OrdinalIgnoreCase));
        _visible = query.ToList();
        _assets.BeginUpdate(); _assets.Items.Clear();
        foreach (RenderWareAssetFile asset in _visible) _assets.Items.Add(asset.DisplayName);
        _assets.EndUpdate();
        _status.Text = $"Showing {_visible.Count:N0} of {_archive.Assets.Count:N0} RenderWare assets.";
        if (_assets.Items.Count > 0) _assets.SelectedIndex = 0;
    }

    private void LoadSelected()
    {
        if (_assets.SelectedIndex < 0 || _assets.SelectedIndex >= _visible.Count) return;
        _selectedAsset = _visible[_assets.SelectedIndex];
        UseWaitCursor = true;
        try
        {
            _scene = _archive.LoadScene(_selectedAsset);
            _preview.Scene = _scene;
            _summary.Text = $"{_scene.SourcePath}  |  {_scene.VertexCount:N0} vertices  |  {_scene.TriangleCount:N0} triangles  |  " +
                            $"{_scene.UniqueMaterialCount:N0} materials  |  {_scene.Textures.Count:N0} textures" +
                            (_scene.Kind == RenderWareAssetKind.RwsScene
                                ? $"  |  {_scene.WorldSectorCount:N0} world sectors  |  {_scene.EmbeddedClumpCount:N0} clumps" : string.Empty);
            PopulateDetails();
            _rawButton.Enabled = true; _objButton.Enabled = _scene.Meshes.Count > 0;
            _textureButton.Enabled = _scene.Textures.Count > 0; _mapButton.Enabled = _scene.MaterialCount > 0;
            _status.Text = _scene.Warnings.Count == 0
                ? $"Loaded {_selectedAsset.Kind}: {_selectedAsset.Size:N0} bytes."
                : $"Loaded with {_scene.Warnings.Count} note(s): {_scene.Warnings[0]}";
        }
        catch (Exception exception)
        {
            _scene = null; _preview.Scene = null; ClearDetails();
            _summary.Text = _selectedAsset.Path;
            _status.Text = exception.Message;
            _rawButton.Enabled = true; _objButton.Enabled = false; _textureButton.Enabled = false; _mapButton.Enabled = false;
        }
        finally { UseWaitCursor = false; }
    }

    private void PopulateDetails()
    {
        ClearDetails();
        _meshes.Columns.Add("name", "Mesh / sector"); _meshes.Columns.Add("type", "Type");
        _meshes.Columns.Add("vertices", "Vertices"); _meshes.Columns.Add("triangles", "Triangles");
        _meshes.Columns.Add("materials", "Materials");
        foreach (RenderWareSceneMesh mesh in _scene!.Meshes)
            _meshes.Rows.Add(mesh.Name, mesh.SourceType, mesh.Vertices.Count.ToString("N0"),
                mesh.Triangles.Count.ToString("N0"), mesh.Materials.Count.ToString("N0"));
        _materials.Columns.Add("uses", "Used by"); _materials.Columns.Add("texture", "Texture name");
        _materials.Columns.Add("size", "Image size"); _materials.Columns.Add("color", "RGBA");
        _materials.Columns.Add("sampling", "Sampling"); _materials.Columns.Add("source", "Resolved source");
        var materialGroups = _scene.Meshes.SelectMany(mesh => mesh.Materials.Select(material => (mesh, material)))
            .GroupBy(item => (Name: item.material.TextureName?.ToUpperInvariant(),
                Color: item.material.Color.ToArgb(), item.material.FilterMode, item.material.AddressU, item.material.AddressV));
        foreach (var group in materialGroups)
        {
            RenderWareMaterial material = group.First().material;
            RenderWareTexture? texture = _scene.ResolveTexture(material);
            int meshCount = group.Select(item => item.mesh.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            _materials.Rows.Add($"{meshCount:N0} mesh{(meshCount == 1 ? string.Empty : "es")}",
                material.TextureName ?? "(none)", texture == null ? "—" : $"{texture.Width} × {texture.Height}",
                $"{material.Color.R}, {material.Color.G}, {material.Color.B}, {material.Color.A}",
                $"filter {material.FilterMode}, U {AddressName(material.AddressU)}, V {AddressName(material.AddressV)}",
                texture?.SourcePath ?? "Not found");
        }
        _chunks.Columns.Add("offset", "Offset"); _chunks.Columns.Add("id", "ID");
        _chunks.Columns.Add("name", "Chunk"); _chunks.Columns.Add("length", "Payload bytes");
        _chunks.Columns.Add("version", "Version");
        foreach (RenderWareChunkInfo chunk in _scene.Chunks)
            _chunks.Rows.Add($"0x{chunk.Offset:X}", $"0x{chunk.Id:X}", chunk.Name,
                chunk.Length.ToString("N0"), $"0x{chunk.Version:X8}");
        foreach (DataGridView grid in new[] { _meshes, _materials, _chunks }) grid.AutoResizeColumns(DataGridViewAutoSizeColumnsMode.DisplayedCells);
    }

    private void ClearDetails()
    {
        foreach (DataGridView grid in new[] { _meshes, _materials, _chunks }) { grid.Rows.Clear(); grid.Columns.Clear(); }
    }

    private void ExportRaw()
    {
        if (_selectedAsset == null) return;
        using SaveFileDialog dialog = new() { FileName = Path.GetFileName(_selectedAsset.Path), Filter = "RenderWare file|*" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        File.WriteAllBytes(dialog.FileName, _archive.ReadRaw(_selectedAsset));
        _status.Text = $"Exported original file to {dialog.FileName}.";
    }

    private void ExportObj()
    {
        if (_scene == null) return;
        using SaveFileDialog dialog = new() { FileName = Path.GetFileNameWithoutExtension(_scene.SourcePath) + ".obj", Filter = "Wavefront OBJ|*.obj" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        RenderWareSceneArchive.ExportObj(_scene, dialog.FileName);
        _status.Text = $"Exported OBJ and MTL to {Path.GetDirectoryName(dialog.FileName)}.";
    }

    private void ExportTextures()
    {
        if (_scene == null || _scene.Textures.Count == 0) return;
        using FolderBrowserDialog dialog = new() { Description = "Choose a folder for decoded PNG textures", UseDescriptionForTitle = true };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        RenderWareSceneArchive.ExportTextures(_scene, dialog.SelectedPath);
        _status.Text = $"Exported {_scene.Textures.Count:N0} decoded textures to {dialog.SelectedPath}.";
    }

    private void ExportTextureMap()
    {
        if (_scene == null) return;
        using SaveFileDialog dialog = new() { FileName = Path.GetFileNameWithoutExtension(_scene.SourcePath) + "_textures.csv", Filter = "CSV file|*.csv" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        using StreamWriter writer = new(dialog.FileName, false, new UTF8Encoding(true));
        writer.WriteLine("mesh,material_index,texture_name,color_rgba,resolved_source");
        foreach (RenderWareSceneMesh mesh in _scene.Meshes)
            for (int index = 0; index < mesh.Materials.Count; index++)
            {
                RenderWareMaterial material = mesh.Materials[index];
                RenderWareTexture? texture = _scene.ResolveTexture(material);
                writer.WriteLine($"{Csv(mesh.Name)},{index},{Csv(material.TextureName ?? string.Empty)}," +
                                 $"{Csv($"{material.Color.R} {material.Color.G} {material.Color.B} {material.Color.A}")}," +
                                 $"{Csv(texture?.SourcePath ?? "embedded/unresolved")}");
            }
        _status.Text = $"Exported texture/material mapping to {dialog.FileName}.";
    }

    private static string Csv(string value) => '"' + value.Replace("\"", "\"\"") + '"';
    private static string AddressName(byte value) => value switch { 1 => "wrap", 2 => "mirror", 3 => "clamp", 4 => "border", _ => "default" };
    private static Button Button(string text) => new() { Text = text, AutoSize = true, Height = 29, Margin = new Padding(4, 1, 0, 1) };
    private static DataGridView Grid() => new()
    {
        Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
        AllowUserToResizeRows = false, RowHeadersVisible = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, BackgroundColor = SystemColors.Window
    };
}
