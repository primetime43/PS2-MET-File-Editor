using System.Numerics;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using PS2_DATA_File_Extractor.FileOperations;

namespace PS2_DATA_File_Extractor;

public sealed class AnimationPosePreviewControl : Control
{
    private RenderWareAnimationFile? _animation;
    private RenderWareAnimationBinding? _binding;
    private RenderWareSkinnedModel? _model;
    private FacialEventFile? _facialEvent;
    private double _position;
    private float _yaw = -0.38F;
    private float _pitch = -0.10F;
    private float _zoom = 1F;
    private Point _lastMouse;
    private bool _rotating;

    public AnimationPosePreviewControl()
    {
        DoubleBuffered = true;
        BackColor = Color.FromArgb(31, 35, 40);
        ForeColor = Color.White;
        MinimumSize = new Size(320, 180);
        SetStyle(ControlStyles.ResizeRedraw, true);
        Cursor = Cursors.SizeAll;
    }

    public RenderWareAnimationFile? Animation
    {
        get => _animation;
        set
        {
            _animation = value;
            _position = 0;
            Invalidate();
        }
    }

    public RenderWareAnimationBinding? Binding
    {
        get => _binding;
        set
        {
            _binding = value;
            Invalidate();
        }
    }

    public RenderWareSkinnedModel? Model
    {
        get => _model;
        set
        {
            _model = value;
            Invalidate();
        }
    }

    public FacialEventFile? FacialEvent
    {
        get => _facialEvent;
        set
        {
            _facialEvent = value;
            Invalidate();
        }
    }

    public int SelectedTrack { get; set; }

    public double PositionSeconds
    {
        get => _position;
        set
        {
            _position = Math.Clamp(value, 0, _animation?.DurationSeconds ?? 0);
            Invalidate();
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        _rotating = true;
        _lastMouse = e.Location;
        Capture = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_rotating) return;
        _yaw += (e.X - _lastMouse.X) * 0.012F;
        _pitch = Math.Clamp(_pitch + (e.Y - _lastMouse.Y) * 0.009F, -1.25F, 1.25F);
        _lastMouse = e.Location;
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _rotating = false;
        Capture = false;
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        _zoom = Math.Clamp(_zoom * (e.Delta > 0 ? 1.12F : 0.89F), 0.35F, 4F);
        Invalidate();
    }

    protected override void OnDoubleClick(EventArgs e)
    {
        base.OnDoubleClick(e);
        _yaw = -0.38F;
        _pitch = -0.10F;
        _zoom = 1F;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Graphics graphics = e.Graphics;
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        graphics.Clear(BackColor);
        DrawBackdrop(graphics);
        if (_animation == null)
        {
            DrawCenteredMessage(graphics, "Select an animation to preview its pose.");
            return;
        }
        if (_binding == null)
        {
            DrawCenteredMessage(graphics,
                $"No compatible DFF/HAnim skeleton was found for this {_animation.TrackCount}-track animation.\n" +
                "Its timing and raw tracks are still available below.");
            DrawHeader(graphics, "No model binding");
            return;
        }

        try
        {
            if (_model != null)
            {
                DrawTexturedModel(graphics);
                DrawHeader(graphics,
                    $"Textured player model — {Path.GetFileName(_binding.ModelPath)}  |  " +
                    $"{_model.VertexCount:N0} vertices  |  {_model.TriangleCount:N0} triangles  |  {_position:0.000}s");
                return;
            }
            IReadOnlyList<Vector3> pose = _binding.SamplePose(_animation, (float)_position);
            DrawPose(graphics, pose, _binding.Skeleton.Bones);
            DrawHeader(graphics,
                $"Animated skeleton — {Path.GetFileName(_binding.ModelPath)}  |  " +
                $"{pose.Count} bones  |  {_position:0.000}s");
        }
        catch (Exception exception)
        {
            DrawCenteredMessage(graphics, $"The pose could not be rendered.\n{exception.Message}");
        }
    }

    private void DrawTexturedModel(Graphics graphics)
    {
        IReadOnlyList<RenderWareDeformedMesh> meshes = _model!.Deform(
            _binding!, _animation!, (float)_position);
        int renderWidth = Math.Clamp(Width, 1, 720);
        int renderHeight = Math.Clamp(Height, 1, 400);
        int pixelCount = checked(renderWidth * renderHeight);
        int[] pixels = new int[pixelCount];
        float[] depth = Enumerable.Repeat(float.MinValue, pixelCount).ToArray();

        List<ProjectedMesh> projectedMeshes = new(meshes.Count);
        float minX = float.MaxValue;
        float maxX = float.MinValue;
        float minY = float.MaxValue;
        float maxY = float.MinValue;
        foreach (RenderWareDeformedMesh mesh in meshes)
        {
            ProjectedPoint[] points = mesh.Positions.Select(RotateToCamera).ToArray();
            projectedMeshes.Add(new ProjectedMesh(mesh, points));
            foreach (ProjectedPoint point in points)
            {
                minX = Math.Min(minX, point.X);
                maxX = Math.Max(maxX, point.X);
                minY = Math.Min(minY, point.Y);
                maxY = Math.Max(maxY, point.Y);
            }
        }
        float rangeX = Math.Max(1, maxX - minX);
        float rangeY = Math.Max(1, maxY - minY);
        float scale = Math.Min((renderWidth - 20F) / rangeX,
            (renderHeight - 32F) / rangeY) * 0.92F * _zoom;
        float centerX = renderWidth / 2F;
        float centerY = renderHeight / 2F + 8;
        float poseCenterX = (minX + maxX) / 2F;
        float poseCenterY = (minY + maxY) / 2F;

        foreach (ProjectedMesh projected in projectedMeshes)
        {
            RenderWareSkinnedMesh mesh = projected.Mesh.Source;
            ScreenVertex[] screen = new ScreenVertex[projected.Points.Length];
            for (int vertex = 0; vertex < screen.Length; vertex++)
            {
                ProjectedPoint point = projected.Points[vertex];
                Vector2 uv = mesh.Vertices[vertex].TextureCoordinate;
                screen[vertex] = new ScreenVertex(
                    centerX + (point.X - poseCenterX) * scale,
                    centerY - (point.Y - poseCenterY) * scale,
                    point.Depth, uv.X, uv.Y);
            }
            foreach (RenderWareTriangle triangle in mesh.Triangles)
            {
                if (triangle.MaterialIndex < 0 || triangle.MaterialIndex >= mesh.Materials.Count) continue;
                RenderWareMaterial material = mesh.Materials[triangle.MaterialIndex];
                RenderWareTexture? texture = _model.ResolveTexture(material, _facialEvent, _position);
                RasterizeTriangle(screen[triangle.First], screen[triangle.Second], screen[triangle.Third],
                    material, texture, pixels, depth, renderWidth, renderHeight);
            }
        }

        using Bitmap bitmap = new(renderWidth, renderHeight, PixelFormat.Format32bppArgb);
        BitmapData locked = bitmap.LockBits(new Rectangle(0, 0, renderWidth, renderHeight),
            ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            Marshal.Copy(pixels, 0, locked.Scan0, pixels.Length);
        }
        finally
        {
            bitmap.UnlockBits(locked);
        }
        graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear;
        graphics.DrawImage(bitmap, new Rectangle(0, 0, Width, Height),
            0, 0, renderWidth, renderHeight, GraphicsUnit.Pixel);
    }

    private static void RasterizeTriangle(
        ScreenVertex a,
        ScreenVertex b,
        ScreenVertex c,
        RenderWareMaterial material,
        RenderWareTexture? texture,
        int[] pixels,
        float[] depth,
        int width,
        int height)
    {
        float area = Edge(a.X, a.Y, b.X, b.Y, c.X, c.Y);
        if (Math.Abs(area) < 0.0001F) return;
        int minX = Math.Clamp((int)MathF.Floor(MathF.Min(a.X, MathF.Min(b.X, c.X))), 0, width - 1);
        int maxX = Math.Clamp((int)MathF.Ceiling(MathF.Max(a.X, MathF.Max(b.X, c.X))), 0, width - 1);
        int minY = Math.Clamp((int)MathF.Floor(MathF.Min(a.Y, MathF.Min(b.Y, c.Y))), 0, height - 1);
        int maxY = Math.Clamp((int)MathF.Ceiling(MathF.Max(a.Y, MathF.Max(b.Y, c.Y))), 0, height - 1);
        float inverseArea = 1F / area;
        Vector3 edge1 = new(b.X - a.X, b.Y - a.Y, b.Depth - a.Depth);
        Vector3 edge2 = new(c.X - a.X, c.Y - a.Y, c.Depth - a.Depth);
        Vector3 normal = Vector3.Cross(edge1, edge2);
        float shade = normal.LengthSquared() < 0.000001F
            ? 1F
            : 0.42F + 0.58F * Math.Abs(Vector3.Dot(Vector3.Normalize(normal),
                Vector3.Normalize(new Vector3(-0.25F, -0.45F, 1F))));
        for (int y = minY; y <= maxY; y++)
        {
            float py = y + 0.5F;
            for (int x = minX; x <= maxX; x++)
            {
                float px = x + 0.5F;
                float w0 = Edge(b.X, b.Y, c.X, c.Y, px, py) * inverseArea;
                float w1 = Edge(c.X, c.Y, a.X, a.Y, px, py) * inverseArea;
                float w2 = 1F - w0 - w1;
                if (w0 < -0.0001F || w1 < -0.0001F || w2 < -0.0001F) continue;
                float z = a.Depth * w0 + b.Depth * w1 + c.Depth * w2;
                int pixel = y * width + x;
                if (z <= depth[pixel]) continue;
                float u = a.U * w0 + b.U * w1 + c.U * w2;
                float v = a.V * w0 + b.V * w1 + c.V * w2;
                int color = texture == null
                    ? material.Color.ToArgb()
                    : SampleTexture(texture, u, v);
                int alpha = (color >>> 24) & 0xFF;
                if (alpha < 24) continue;
                int red = (int)(((color >>> 16) & 0xFF) * material.Color.R / 255F * shade);
                int green = (int)(((color >>> 8) & 0xFF) * material.Color.G / 255F * shade);
                int blue = (int)((color & 0xFF) * material.Color.B / 255F * shade);
                pixels[pixel] = (alpha << 24) | (Math.Clamp(red, 0, 255) << 16) |
                                (Math.Clamp(green, 0, 255) << 8) | Math.Clamp(blue, 0, 255);
                depth[pixel] = z;
            }
        }
    }

    private static int SampleTexture(RenderWareTexture texture, float u, float v)
    {
        u -= MathF.Floor(u);
        v -= MathF.Floor(v);
        int x = Math.Clamp((int)(u * texture.Width), 0, texture.Width - 1);
        int y = Math.Clamp((int)(v * texture.Height), 0, texture.Height - 1);
        return texture.Pixels[y * texture.Width + x];
    }

    private static float Edge(float ax, float ay, float bx, float by, float px, float py) =>
        (px - ax) * (by - ay) - (py - ay) * (bx - ax);

    private void DrawPose(
        Graphics graphics,
        IReadOnlyList<Vector3> pose,
        IReadOnlyList<RenderWareSkeletonBone> bones)
    {
        ProjectedPoint[] projected = pose.Select(RotateToCamera).ToArray();
        float minX = projected.Min(point => point.X);
        float maxX = projected.Max(point => point.X);
        float minY = projected.Min(point => point.Y);
        float maxY = projected.Max(point => point.Y);
        float rangeX = Math.Max(1, maxX - minX);
        float rangeY = Math.Max(1, maxY - minY);
        Rectangle viewport = new(34, 32, Math.Max(1, Width - 68), Math.Max(1, Height - 65));
        float scale = Math.Min(viewport.Width / rangeX, viewport.Height / rangeY) * 0.88F * _zoom;
        float centerX = viewport.Left + viewport.Width / 2F;
        float centerY = viewport.Top + viewport.Height / 2F;
        float poseCenterX = (minX + maxX) / 2F;
        float poseCenterY = (minY + maxY) / 2F;
        PointF ToScreen(ProjectedPoint point) => new(
            centerX + (point.X - poseCenterX) * scale,
            centerY - (point.Y - poseCenterY) * scale);

        using Pen shadowPen = new(Color.FromArgb(125, 0, 0, 0), 8F)
        {
            StartCap = System.Drawing.Drawing2D.LineCap.Round,
            EndCap = System.Drawing.Drawing2D.LineCap.Round
        };
        using Pen bonePen = new(Color.FromArgb(104, 190, 255), 4F)
        {
            StartCap = System.Drawing.Drawing2D.LineCap.Round,
            EndCap = System.Drawing.Drawing2D.LineCap.Round
        };
        using Pen selectedPen = new(Color.FromArgb(255, 178, 48), 6F)
        {
            StartCap = System.Drawing.Drawing2D.LineCap.Round,
            EndCap = System.Drawing.Drawing2D.LineCap.Round
        };
        using Brush jointBrush = new SolidBrush(Color.FromArgb(222, 239, 250));
        using Brush selectedBrush = new SolidBrush(Color.FromArgb(255, 190, 62));
        using Pen jointOutline = new(Color.FromArgb(15, 19, 24), 2F);

        var segments = bones
            .Where(bone => bone.ParentTrackIndex >= 0)
            .Select(bone => new
            {
                Bone = bone,
                Depth = (projected[bone.TrackIndex].Depth +
                         projected[bone.ParentTrackIndex].Depth) / 2F
            })
            .OrderBy(item => item.Depth)
            .ToList();
        foreach (var item in segments)
        {
            PointF child = ToScreen(projected[item.Bone.TrackIndex]);
            PointF parent = ToScreen(projected[item.Bone.ParentTrackIndex]);
            graphics.DrawLine(shadowPen, parent, child);
            bool selected = item.Bone.TrackIndex == SelectedTrack ||
                            item.Bone.ParentTrackIndex == SelectedTrack;
            graphics.DrawLine(selected ? selectedPen : bonePen, parent, child);
        }

        foreach (RenderWareSkeletonBone bone in bones.OrderBy(item => projected[item.TrackIndex].Depth))
        {
            PointF point = ToScreen(projected[bone.TrackIndex]);
            bool selected = bone.TrackIndex == SelectedTrack;
            float radius = selected ? 6F : 4F;
            graphics.FillEllipse(selected ? selectedBrush : jointBrush,
                point.X - radius, point.Y - radius, radius * 2, radius * 2);
            graphics.DrawEllipse(jointOutline,
                point.X - radius, point.Y - radius, radius * 2, radius * 2);
        }

        if (SelectedTrack >= 0 && SelectedTrack < projected.Length)
        {
            PointF selected = ToScreen(projected[SelectedTrack]);
            TextRenderer.DrawText(graphics, $"Track {SelectedTrack}", Font,
                new Point((int)selected.X + 8, (int)selected.Y - 18),
                Color.FromArgb(255, 206, 95));
        }
    }

    private ProjectedPoint RotateToCamera(Vector3 point)
    {
        float cosYaw = MathF.Cos(_yaw);
        float sinYaw = MathF.Sin(_yaw);
        float x = cosYaw * point.X + sinYaw * point.Z;
        float z = -sinYaw * point.X + cosYaw * point.Z;
        float cosPitch = MathF.Cos(_pitch);
        float sinPitch = MathF.Sin(_pitch);
        float y = cosPitch * point.Y - sinPitch * z;
        float depth = sinPitch * point.Y + cosPitch * z;
        return new ProjectedPoint(x, y, depth);
    }

    private void DrawBackdrop(Graphics graphics)
    {
        using Pen gridPen = new(Color.FromArgb(42, 255, 255, 255));
        for (int x = 0; x < Width; x += 40) graphics.DrawLine(gridPen, x, 0, x, Height);
        for (int y = 0; y < Height; y += 40) graphics.DrawLine(gridPen, 0, y, Width, y);
        TextRenderer.DrawText(graphics,
            "Drag to rotate  •  Mouse wheel to zoom  •  Double-click to reset view",
            Font, new Rectangle(8, Height - 24, Width - 16, 18),
            Color.FromArgb(175, 205, 215, 225), TextFormatFlags.Right);
    }

    private void DrawHeader(Graphics graphics, string text)
    {
        using Brush background = new SolidBrush(Color.FromArgb(175, 16, 19, 23));
        graphics.FillRectangle(background, 0, 0, Width, 28);
        TextRenderer.DrawText(graphics, text, Font, new Rectangle(9, 5, Width - 18, 19),
            Color.White, TextFormatFlags.EndEllipsis);
    }

    private void DrawCenteredMessage(Graphics graphics, string text)
    {
        TextRenderer.DrawText(graphics, text, Font,
            new Rectangle(35, 35, Math.Max(1, Width - 70), Math.Max(1, Height - 70)),
            Color.FromArgb(220, 230, 235, 240),
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
            TextFormatFlags.WordBreak);
    }

    private readonly record struct ProjectedPoint(float X, float Y, float Depth);
    private readonly record struct ScreenVertex(float X, float Y, float Depth, float U, float V);
    private sealed record ProjectedMesh(RenderWareDeformedMesh Mesh, ProjectedPoint[] Points);
}
