using System.Drawing.Imaging;
using System.Numerics;
using System.Runtime.InteropServices;
using PS2_DATA_File_Extractor.FileOperations;

namespace PS2_DATA_File_Extractor;

public sealed class RenderWareScenePreviewControl : Control
{
    private RenderWareScene? _scene;
    private float _yaw = -0.55F, _pitch = -0.28F, _zoom = 1F, _panX, _panY;
    private Point _lastMouse;
    private bool _rotating, _panning, _wireframe, _perspective = true, _cullBackfaces, _hideSkyRoof = true,
        _hideHelperGeometry = true;

    public RenderWareScenePreviewControl()
    {
        DoubleBuffered = true;
        BackColor = Color.FromArgb(31, 35, 40);
        ForeColor = Color.White;
        MinimumSize = new Size(360, 220);
        SetStyle(ControlStyles.ResizeRedraw, true);
        Cursor = Cursors.SizeAll;
    }

    public RenderWareScene? Scene
    {
        get => _scene;
        set { _scene = value; ResetView(); }
    }

    public bool Wireframe
    {
        get => _wireframe;
        set { _wireframe = value; Invalidate(); }
    }

    public bool Perspective
    {
        get => _perspective;
        set { _perspective = value; Invalidate(); }
    }

    public bool CullBackfaces
    {
        get => _cullBackfaces;
        set { _cullBackfaces = value; Invalidate(); }
    }

    public bool HideSkyRoof
    {
        get => _hideSkyRoof;
        set { _hideSkyRoof = value; Invalidate(); }
    }

    public bool HideHelperGeometry
    {
        get => _hideHelperGeometry;
        set { _hideHelperGeometry = value; Invalidate(); }
    }

    public void ResetView()
    {
        _yaw = -0.55F;
        _pitch = _scene?.Kind == RenderWareAssetKind.RwsScene ? 0.36F : -0.28F;
        float aspect = ClientSize.Height > 0 ? ClientSize.Width / (float)ClientSize.Height : 2F;
        _zoom = _scene?.Kind == RenderWareAssetKind.RwsScene
            ? Math.Clamp(2.15F + (aspect - 2F) * 0.75F, 1.9F, 3.4F)
            : 1F;
        _panX = 0F;
        _panY = 0F;
        Invalidate();
    }

    public void ZoomIn() => ChangeZoom(1.25F, null);
    public void ZoomOut() => ChangeZoom(0.8F, null);

    public void ShowFrontView()
    {
        _yaw = 0F;
        _pitch = 0F;
        _panX = _panY = 0F;
        Invalidate();
    }

    public void ShowTopView()
    {
        _yaw = 0F;
        _pitch = 1.45F;
        _panX = _panY = 0F;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        bool pan = e.Button is MouseButtons.Right or MouseButtons.Middle ||
                   (e.Button == MouseButtons.Left && ModifierKeys.HasFlag(Keys.Shift));
        if (e.Button != MouseButtons.Left && !pan) return;
        _panning = pan;
        _rotating = !pan;
        _lastMouse = e.Location;
        Capture = true;
        Focus();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_rotating && !_panning) return;
        if (_panning)
        {
            _panX = Math.Clamp(_panX + (e.X - _lastMouse.X) / (float)Math.Max(1, Width), -4F, 4F);
            _panY = Math.Clamp(_panY + (e.Y - _lastMouse.Y) / (float)Math.Max(1, Height), -4F, 4F);
        }
        else
        {
            _yaw += (e.X - _lastMouse.X) * 0.012F;
            _pitch = Math.Clamp(_pitch + (e.Y - _lastMouse.Y) * 0.009F, -1.45F, 1.45F);
        }
        _lastMouse = e.Location; Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e); _rotating = false; _panning = false; Capture = false;
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        float steps = e.Delta / 120F;
        ChangeZoom(MathF.Pow(1.18F, steps), e.Location);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode is Keys.Add or Keys.Oemplus) { ZoomIn(); e.Handled = true; }
        else if (e.KeyCode is Keys.Subtract or Keys.OemMinus) { ZoomOut(); e.Handled = true; }
        else if (e.KeyCode == Keys.Home) { ResetView(); e.Handled = true; }
    }

    private void ChangeZoom(float factor, Point? cursor)
    {
        float oldZoom = _zoom;
        _zoom = Math.Clamp(_zoom * factor, 0.08F, 30F);
        float applied = _zoom / oldZoom;
        if (cursor is Point point && Width > 0 && Height > 0)
        {
            float x = point.X / (float)Width - 0.5F;
            float y = point.Y / (float)Height - 0.5F;
            _panX = Math.Clamp(x - (x - _panX) * applied, -4F, 4F);
            _panY = Math.Clamp(y - (y - _panY) * applied, -4F, 4F);
        }
        Invalidate();
    }

    protected override void OnDoubleClick(EventArgs e) { base.OnDoubleClick(e); ResetView(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.Clear(BackColor);
        DrawBackdrop(e.Graphics);
        if (_scene == null) { DrawMessage(e.Graphics, "Select a DFF model or RWS scene."); return; }
        if (_scene.Meshes.Count == 0)
        {
            DrawMessage(e.Graphics, "This file has no triangle mesh to display.\nIts chunks and metadata are still available.");
            DrawHeader(e.Graphics); return;
        }
        try
        {
            if (_wireframe) DrawWireframe(e.Graphics); else DrawSolid(e.Graphics);
            DrawHeader(e.Graphics);
        }
        catch (Exception exception) { DrawMessage(e.Graphics, "Preview failed.\n" + exception.Message); }
    }

    private void DrawSolid(Graphics graphics)
    {
        float renderScale = Math.Min(1F, Math.Min(900F / Math.Max(1, Width), 500F / Math.Max(1, Height)));
        int width = Math.Max(1, (int)MathF.Round(Width * renderScale));
        int height = Math.Max(1, (int)MathF.Round(Height * renderScale));
        int[] pixels = new int[width * height];
        float[] depth = Enumerable.Repeat(float.MinValue, pixels.Length).ToArray();
        Projection projection = BuildProjection(width, height);
        foreach (RenderWareSceneMesh mesh in _scene!.Meshes)
        {
            ScreenVertex[] screen = mesh.Vertices.Select(vertex =>
            {
                ProjectedVertex p = Project(vertex.Position, projection);
                Vector3 normal = RotateVector(vertex.Normal);
                return new ScreenVertex(p.X, p.Y, p.Depth, vertex.TextureCoordinate.X * p.InverseW,
                    vertex.TextureCoordinate.Y * p.InverseW, p.InverseW, vertex.Color.ToArgb(), normal);
            }).ToArray();
            foreach (RenderWareTriangle triangle in mesh.Triangles)
            {
                if (!Valid(triangle, screen.Length)) continue;
                RenderWareMaterial material = triangle.MaterialIndex >= 0 && triangle.MaterialIndex < mesh.Materials.Count
                    ? mesh.Materials[triangle.MaterialIndex] : new RenderWareMaterial(null, Color.LightGray);
                if (ShouldHide(material))
                    continue;
                Rasterize(screen[triangle.First], screen[triangle.Second], screen[triangle.Third], material,
                    _scene.ResolveTexture(material), pixels, depth, width, height, _perspective, _cullBackfaces);
            }
        }
        using Bitmap bitmap = new(width, height, PixelFormat.Format32bppArgb);
        BitmapData locked = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly,
            PixelFormat.Format32bppArgb);
        try { Marshal.Copy(pixels, 0, locked.Scan0, pixels.Length); }
        finally { bitmap.UnlockBits(locked); }
        graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear;
        graphics.DrawImage(bitmap, new Rectangle(0, 0, Width, Height), 0, 0, width, height, GraphicsUnit.Pixel);
    }

    private void DrawWireframe(Graphics graphics)
    {
        Projection projection = BuildProjection(Width, Height);
        using Pen pen = new(Color.FromArgb(145, 112, 205, 255), 1F);
        int total = Math.Max(1, _scene!.TriangleCount), stride = Math.Max(1, total / 18_000), current = 0;
        foreach (RenderWareSceneMesh mesh in _scene.Meshes)
        {
            PointF[] points = mesh.Vertices.Select(vertex =>
            {
                ProjectedVertex p = Project(vertex.Position, projection);
                return new PointF(p.X, p.Y);
            }).ToArray();
            foreach (RenderWareTriangle triangle in mesh.Triangles)
            {
                RenderWareMaterial material = triangle.MaterialIndex >= 0 && triangle.MaterialIndex < mesh.Materials.Count
                    ? mesh.Materials[triangle.MaterialIndex] : new RenderWareMaterial(null, Color.LightGray);
                if (ShouldHide(material)) continue;
                if (current++ % stride != 0 || !Valid(triangle, points.Length)) continue;
                graphics.DrawPolygon(pen, new[] { points[triangle.First], points[triangle.Second], points[triangle.Third] });
            }
        }
    }

    private Projection BuildProjection(int width, int height)
    {
        IReadOnlyList<Vector3> all = _scene!.Meshes.SelectMany(mesh => mesh.Vertices)
            .Select(vertex => vertex.Position).ToList();
        Vector3 minimum = new(float.MaxValue), maximum = new(float.MinValue);
        foreach (Vector3 point in all) { minimum = Vector3.Min(minimum, point); maximum = Vector3.Max(maximum, point); }
        Vector3 modelCenter = (minimum + maximum) * 0.5F;
        float radius = Math.Max(0.001F, all.Max(point => Vector3.Distance(point, modelCenter)));
        float availableWidth = Math.Max(1, width - 28), availableHeight = Math.Max(1, height - 52);
        float halfFov = 25F * MathF.PI / 180F;
        float baseFocalLength = availableHeight * 0.5F / MathF.Tan(halfFov);
        float focalLength = baseFocalLength * _zoom;
        float cameraDistance = 0F;

        float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
        foreach (Vector3 point in all)
        {
            Vector3 rotated = RotateVector(point - modelCenter);
            minX = Math.Min(minX, rotated.X); maxX = Math.Max(maxX, rotated.X);
            minY = Math.Min(minY, rotated.Y); maxY = Math.Max(maxY, rotated.Y);
            cameraDistance = Math.Max(cameraDistance,
                rotated.Z + Math.Abs(rotated.X) * baseFocalLength / (availableWidth * 0.46F));
            cameraDistance = Math.Max(cameraDistance,
                rotated.Z + Math.Abs(rotated.Y) * baseFocalLength / (availableHeight * 0.46F));
        }
        cameraDistance += radius * 0.04F;
        float rangeX = Math.Max(0.001F, maxX - minX), rangeY = Math.Max(0.001F, maxY - minY);
        float orthographicScale = Math.Min(availableWidth / rangeX, availableHeight / rangeY) * 0.92F * _zoom;
        return new Projection(width * (0.5F + _panX), height * (0.5F + _panY) + 9, modelCenter, orthographicScale,
            focalLength, cameraDistance, (minX + maxX) / 2F, (minY + maxY) / 2F);
    }

    private ProjectedVertex Project(Vector3 point, Projection projection)
    {
        Vector3 rotated = RotateVector(point - projection.ModelCenter);
        if (!_perspective)
            return new ProjectedVertex(projection.CenterX + (rotated.X - projection.OrthographicCenterX) * projection.Scale,
                projection.CenterY - (rotated.Y - projection.OrthographicCenterY) * projection.Scale,
                rotated.Z, 1F);
        float viewDistance = Math.Max(0.001F, projection.CameraDistance - rotated.Z);
        float inverseW = 1F / viewDistance;
        return new ProjectedVertex(projection.CenterX + rotated.X * projection.FocalLength * inverseW,
            projection.CenterY - rotated.Y * projection.FocalLength * inverseW, inverseW, inverseW);
    }

    private Vector3 RotateVector(Vector3 point)
    {
        float cy = MathF.Cos(_yaw), sy = MathF.Sin(_yaw);
        float x = cy * point.X + sy * point.Z, z = -sy * point.X + cy * point.Z;
        float cp = MathF.Cos(_pitch), sp = MathF.Sin(_pitch);
        return new Vector3(x, cp * point.Y - sp * z, sp * point.Y + cp * z);
    }

    private static void Rasterize(ScreenVertex a, ScreenVertex b, ScreenVertex c,
        RenderWareMaterial material, RenderWareTexture? texture, int[] pixels, float[] depth, int width, int height,
        bool perspective, bool cullBackfaces)
    {
        float area = Edge(a.X, a.Y, b.X, b.Y, c.X, c.Y);
        if (Math.Abs(area) < 0.0001F) return;
        if (cullBackfaces && area > 0F) return;
        int minX = Math.Clamp((int)MathF.Floor(MathF.Min(a.X, MathF.Min(b.X, c.X))), 0, width - 1);
        int maxX = Math.Clamp((int)MathF.Ceiling(MathF.Max(a.X, MathF.Max(b.X, c.X))), 0, width - 1);
        int minY = Math.Clamp((int)MathF.Floor(MathF.Min(a.Y, MathF.Min(b.Y, c.Y))), 0, height - 1);
        int maxY = Math.Clamp((int)MathF.Ceiling(MathF.Max(a.Y, MathF.Max(b.Y, c.Y))), 0, height - 1);
        float inverse = 1F / area;
        Vector3 light = Vector3.Normalize(new Vector3(-0.35F, 0.65F, 0.55F));
        for (int y = minY; y <= maxY; y++) for (int x = minX; x <= maxX; x++)
        {
            float px = x + 0.5F, py = y + 0.5F;
            float w0 = Edge(b.X, b.Y, c.X, c.Y, px, py) * inverse;
            float w1 = Edge(c.X, c.Y, a.X, a.Y, px, py) * inverse;
            float w2 = 1F - w0 - w1;
            if (w0 < -0.0001F || w1 < -0.0001F || w2 < -0.0001F) continue;
            float inverseW = a.InverseW * w0 + b.InverseW * w1 + c.InverseW * w2;
            if (inverseW <= 0) continue;
            float p0 = perspective ? a.InverseW * w0 / inverseW : w0;
            float p1 = perspective ? b.InverseW * w1 / inverseW : w1;
            float p2 = perspective ? c.InverseW * w2 / inverseW : w2;
            float z = perspective ? inverseW : a.Depth * w0 + b.Depth * w1 + c.Depth * w2;
            int pixel = y * width + x;
            if (z <= depth[pixel]) continue;
            float u = (a.UOverW * w0 + b.UOverW * w1 + c.UOverW * w2) / inverseW;
            float v = (a.VOverW * w0 + b.VOverW * w1 + c.VOverW * w2) / inverseW;
            int color = texture == null ? unchecked((int)0xFFFFFFFF) : Sample(texture,
                u, v, material.AddressU, material.AddressV);
            float vertexRed = InterpolateChannel(a.Color, b.Color, c.Color, 16, p0, p1, p2) / 255F;
            float vertexGreen = InterpolateChannel(a.Color, b.Color, c.Color, 8, p0, p1, p2) / 255F;
            float vertexBlue = InterpolateChannel(a.Color, b.Color, c.Color, 0, p0, p1, p2) / 255F;
            float vertexAlpha = InterpolateChannel(a.Color, b.Color, c.Color, 24, p0, p1, p2) / 255F;
            Vector3 normal = a.Normal * p0 + b.Normal * p1 + c.Normal * p2;
            float shade = normal.LengthSquared() < 0.000001F ? 1F :
                0.38F + 0.62F * Math.Abs(Vector3.Dot(Vector3.Normalize(normal), light));
            int alpha = (int)(((color >>> 24) & 0xFF) * material.Color.A / 255F * vertexAlpha);
            if (alpha < 24) continue;
            int red = (int)(((color >>> 16) & 0xFF) * material.Color.R / 255F * vertexRed * shade);
            int green = (int)(((color >>> 8) & 0xFF) * material.Color.G / 255F * vertexGreen * shade);
            int blue = (int)((color & 0xFF) * material.Color.B / 255F * vertexBlue * shade);
            pixels[pixel] = (alpha << 24) | (Math.Clamp(red, 0, 255) << 16) |
                            (Math.Clamp(green, 0, 255) << 8) | Math.Clamp(blue, 0, 255);
            depth[pixel] = z;
        }
    }

    private static int Sample(RenderWareTexture texture, float u, float v, byte addressU, byte addressV)
    {
        u = Address(u, addressU); v = Address(v, addressV);
        int x = Math.Clamp((int)(u * texture.Width), 0, texture.Width - 1);
        int y = Math.Clamp((int)(v * texture.Height), 0, texture.Height - 1);
        return texture.Pixels[y * texture.Width + x];
    }

    private static float Address(float value, byte mode) => mode switch
    {
        2 => 1F - MathF.Abs(value % 2F - 1F),
        3 or 4 => Math.Clamp(value, 0F, 0.999999F),
        _ => value - MathF.Floor(value)
    };

    private static bool IsSkyShellMaterial(string? textureName) =>
        textureName?.Contains("sky", StringComparison.OrdinalIgnoreCase) == true ||
        textureName?.Contains("backdrop", StringComparison.OrdinalIgnoreCase) == true ||
        textureName?.Contains("horizon", StringComparison.OrdinalIgnoreCase) == true;

    private bool ShouldHide(RenderWareMaterial material) =>
        (_hideSkyRoof && IsSkyShellMaterial(material.TextureName)) ||
        (_hideHelperGeometry && IsHelperMaterial(material.TextureName));

    private static bool IsHelperMaterial(string? textureName) => textureName is not null &&
        (textureName.Equals("C", StringComparison.OrdinalIgnoreCase) ||
         textureName.Equals("WT", StringComparison.OrdinalIgnoreCase) ||
         textureName.Equals("HR", StringComparison.OrdinalIgnoreCase));

    private static float InterpolateChannel(int a, int b, int c, int shift,
        float w0, float w1, float w2) =>
        ((a >>> shift) & 0xFF) * w0 + ((b >>> shift) & 0xFF) * w1 + ((c >>> shift) & 0xFF) * w2;

    private static bool Valid(RenderWareTriangle t, int count) =>
        t.First >= 0 && t.Second >= 0 && t.Third >= 0 && t.First < count && t.Second < count && t.Third < count;
    private static float Edge(float ax, float ay, float bx, float by, float px, float py) =>
        (px - ax) * (by - ay) - (py - ay) * (bx - ax);

    private void DrawBackdrop(Graphics graphics)
    {
        using Pen pen = new(Color.FromArgb(42, 255, 255, 255));
        for (int x = 0; x < Width; x += 40) graphics.DrawLine(pen, x, 0, x, Height);
        for (int y = 0; y < Height; y += 40) graphics.DrawLine(pen, 0, y, Width, y);
        TextRenderer.DrawText(graphics, "Left-drag rotate  •  Right-drag pan  •  Wheel zoom  •  Double-click fit",
            Font, new Rectangle(8, Height - 23, Width - 16, 18), Color.FromArgb(175, 205, 215, 225), TextFormatFlags.Right);
    }

    private void DrawHeader(Graphics graphics)
    {
        using Brush brush = new SolidBrush(Color.FromArgb(185, 16, 19, 23));
        graphics.FillRectangle(brush, 0, 0, Width, 29);
        TextRenderer.DrawText(graphics,
            $"{Path.GetFileName(_scene!.SourcePath)}  |  {_scene.VertexCount:N0} vertices  |  {_scene.TriangleCount:N0} triangles",
            Font, new Rectangle(9, 5, Width - 18, 19), Color.White, TextFormatFlags.EndEllipsis);
    }

    private void DrawMessage(Graphics graphics, string message) => TextRenderer.DrawText(graphics, message, Font,
        new Rectangle(35, 35, Math.Max(1, Width - 70), Math.Max(1, Height - 70)), Color.FromArgb(220, 230, 235, 240),
        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);

    private readonly record struct ProjectedVertex(float X, float Y, float Depth, float InverseW);
    private readonly record struct ScreenVertex(float X, float Y, float Depth, float UOverW, float VOverW,
        float InverseW, int Color, Vector3 Normal);
    private readonly record struct Projection(float CenterX, float CenterY, Vector3 ModelCenter, float Scale,
        float FocalLength, float CameraDistance, float OrthographicCenterX, float OrthographicCenterY);
}
