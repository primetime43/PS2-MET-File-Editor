using System.Drawing.Imaging;
using System.Numerics;
using System.Runtime.InteropServices;
using PS2_DATA_File_Extractor.FileOperations;

namespace PS2_DATA_File_Extractor;

public sealed class RenderWareScenePreviewControl : Control
{
    private RenderWareScene? _scene;
    private float _yaw = -0.55F, _pitch = -0.28F, _zoom = 1F;
    private Point _lastMouse;
    private bool _rotating, _wireframe;

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

    public void ResetView()
    {
        _yaw = -0.55F; _pitch = -0.28F; _zoom = 1F; Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        _rotating = true; _lastMouse = e.Location; Capture = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_rotating) return;
        _yaw += (e.X - _lastMouse.X) * 0.012F;
        _pitch = Math.Clamp(_pitch + (e.Y - _lastMouse.Y) * 0.009F, -1.45F, 1.45F);
        _lastMouse = e.Location; Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e); _rotating = false; Capture = false;
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        _zoom = Math.Clamp(_zoom * (e.Delta > 0 ? 1.12F : 0.89F), 0.25F, 8F);
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
        int width = Math.Clamp(Width, 1, 640), height = Math.Clamp(Height, 1, 360);
        int[] pixels = new int[width * height];
        float[] depth = Enumerable.Repeat(float.MinValue, pixels.Length).ToArray();
        Projection projection = BuildProjection(width, height);
        foreach (RenderWareSceneMesh mesh in _scene!.Meshes)
        {
            ScreenVertex[] screen = mesh.Vertices.Select(vertex =>
            {
                ProjectedPoint p = Rotate(vertex.Position);
                return new ScreenVertex(projection.CenterX + (p.X - projection.ModelCenterX) * projection.Scale,
                    projection.CenterY - (p.Y - projection.ModelCenterY) * projection.Scale,
                    p.Depth, vertex.TextureCoordinate.X, vertex.TextureCoordinate.Y, vertex.Color.ToArgb());
            }).ToArray();
            foreach (RenderWareTriangle triangle in mesh.Triangles)
            {
                if (!Valid(triangle, screen.Length)) continue;
                RenderWareMaterial material = triangle.MaterialIndex >= 0 && triangle.MaterialIndex < mesh.Materials.Count
                    ? mesh.Materials[triangle.MaterialIndex] : new RenderWareMaterial(null, Color.LightGray);
                Rasterize(screen[triangle.First], screen[triangle.Second], screen[triangle.Third], material,
                    _scene.ResolveTexture(material), pixels, depth, width, height);
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
                ProjectedPoint p = Rotate(vertex.Position);
                return new PointF(projection.CenterX + (p.X - projection.ModelCenterX) * projection.Scale,
                    projection.CenterY - (p.Y - projection.ModelCenterY) * projection.Scale);
            }).ToArray();
            foreach (RenderWareTriangle triangle in mesh.Triangles)
            {
                if (current++ % stride != 0 || !Valid(triangle, points.Length)) continue;
                graphics.DrawPolygon(pen, new[] { points[triangle.First], points[triangle.Second], points[triangle.Third] });
            }
        }
    }

    private Projection BuildProjection(int width, int height)
    {
        IEnumerable<ProjectedPoint> all = _scene!.Meshes.SelectMany(mesh => mesh.Vertices).Select(vertex => Rotate(vertex.Position));
        float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
        foreach (ProjectedPoint p in all) { minX = Math.Min(minX, p.X); maxX = Math.Max(maxX, p.X); minY = Math.Min(minY, p.Y); maxY = Math.Max(maxY, p.Y); }
        float rangeX = Math.Max(0.001F, maxX - minX), rangeY = Math.Max(0.001F, maxY - minY);
        float scale = Math.Min(Math.Max(1, width - 28) / rangeX, Math.Max(1, height - 52) / rangeY) * 0.92F * _zoom;
        return new Projection(width / 2F, height / 2F + 9, (minX + maxX) / 2F, (minY + maxY) / 2F, scale);
    }

    private ProjectedPoint Rotate(Vector3 point)
    {
        float cy = MathF.Cos(_yaw), sy = MathF.Sin(_yaw);
        float x = cy * point.X + sy * point.Z, z = -sy * point.X + cy * point.Z;
        float cp = MathF.Cos(_pitch), sp = MathF.Sin(_pitch);
        return new ProjectedPoint(x, cp * point.Y - sp * z, sp * point.Y + cp * z);
    }

    private static void Rasterize(ScreenVertex a, ScreenVertex b, ScreenVertex c,
        RenderWareMaterial material, RenderWareTexture? texture, int[] pixels, float[] depth, int width, int height)
    {
        float area = Edge(a.X, a.Y, b.X, b.Y, c.X, c.Y);
        if (Math.Abs(area) < 0.0001F) return;
        int minX = Math.Clamp((int)MathF.Floor(MathF.Min(a.X, MathF.Min(b.X, c.X))), 0, width - 1);
        int maxX = Math.Clamp((int)MathF.Ceiling(MathF.Max(a.X, MathF.Max(b.X, c.X))), 0, width - 1);
        int minY = Math.Clamp((int)MathF.Floor(MathF.Min(a.Y, MathF.Min(b.Y, c.Y))), 0, height - 1);
        int maxY = Math.Clamp((int)MathF.Ceiling(MathF.Max(a.Y, MathF.Max(b.Y, c.Y))), 0, height - 1);
        float inverse = 1F / area;
        Vector3 normal = Vector3.Cross(new Vector3(b.X - a.X, b.Y - a.Y, b.Depth - a.Depth),
            new Vector3(c.X - a.X, c.Y - a.Y, c.Depth - a.Depth));
        float shade = normal.LengthSquared() < 0.000001F ? 1F :
            0.4F + 0.6F * Math.Abs(Vector3.Dot(Vector3.Normalize(normal), Vector3.Normalize(new Vector3(-0.25F, -0.45F, 1F))));
        for (int y = minY; y <= maxY; y++) for (int x = minX; x <= maxX; x++)
        {
            float px = x + 0.5F, py = y + 0.5F;
            float w0 = Edge(b.X, b.Y, c.X, c.Y, px, py) * inverse;
            float w1 = Edge(c.X, c.Y, a.X, a.Y, px, py) * inverse;
            float w2 = 1F - w0 - w1;
            if (w0 < -0.0001F || w1 < -0.0001F || w2 < -0.0001F) continue;
            float z = a.Depth * w0 + b.Depth * w1 + c.Depth * w2;
            int pixel = y * width + x;
            if (z <= depth[pixel]) continue;
            int color = texture == null ? unchecked((int)0xFFFFFFFF) : Sample(texture,
                a.U * w0 + b.U * w1 + c.U * w2, a.V * w0 + b.V * w1 + c.V * w2,
                material.AddressU, material.AddressV);
            float vertexRed = InterpolateChannel(a.Color, b.Color, c.Color, 16, w0, w1, w2) / 255F;
            float vertexGreen = InterpolateChannel(a.Color, b.Color, c.Color, 8, w0, w1, w2) / 255F;
            float vertexBlue = InterpolateChannel(a.Color, b.Color, c.Color, 0, w0, w1, w2) / 255F;
            float vertexAlpha = InterpolateChannel(a.Color, b.Color, c.Color, 24, w0, w1, w2) / 255F;
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
        TextRenderer.DrawText(graphics, "Drag to rotate  •  Mouse wheel to zoom  •  Double-click to reset",
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

    private readonly record struct ProjectedPoint(float X, float Y, float Depth);
    private readonly record struct ScreenVertex(float X, float Y, float Depth, float U, float V, int Color);
    private readonly record struct Projection(float CenterX, float CenterY, float ModelCenterX, float ModelCenterY, float Scale);
}
