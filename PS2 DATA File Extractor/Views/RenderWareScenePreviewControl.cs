using System.Drawing.Imaging;
using System.Numerics;
using System.Runtime.InteropServices;
using PS2_DATA_File_Extractor.FileOperations;

namespace PS2_DATA_File_Extractor;

public sealed class RenderWareScenePreviewControl : Control
{
    private RenderWareScene? _scene;
    private float _yaw = -0.55F, _pitch = -0.28F, _zoom = 1F, _panX, _panY;
    private float _fieldHeading, _fieldPitch;
    private Vector3 _fieldPosition;
    private Vector4 _environmentLight = Vector4.One;
    private Point _lastMouse;
    private Point _mouseDownPoint;
    private bool _rotating, _panning, _wireframe, _perspective = true, _cullBackfaces, _hideSkyRoof = true,
        _hideHelperGeometry = true, _fieldCamera;
    private readonly HashSet<Keys> _movementKeys = [];
    private readonly System.Windows.Forms.Timer _movementTimer = new() { Interval = 16 };
    private IReadOnlyList<RenderWarePreviewGuide> _guides = [];

    public RenderWareScenePreviewControl()
    {
        DoubleBuffered = true;
        BackColor = Color.FromArgb(31, 35, 40);
        ForeColor = Color.White;
        MinimumSize = new Size(360, 220);
        SetStyle(ControlStyles.ResizeRedraw, true);
        Cursor = Cursors.SizeAll;
        TabStop = true;
        _movementTimer.Tick += (_, _) => MoveFieldCamera();
        _movementTimer.Start();
    }

    public RenderWareScene? Scene
    {
        get => _scene;
        set => SetScene(value, resetView: true);
    }

    public void SetScene(RenderWareScene? scene, bool resetView)
    {
        _scene = scene;
        if (resetView) ResetView();
        else Invalidate();
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

    public bool IsFieldCamera => _fieldCamera;
    public float MovementSpeed { get; set; } = 900F;
    public Vector3 FieldCameraPosition => _fieldPosition;
    public IReadOnlyList<RenderWarePreviewGuide> Guides
    {
        get => _guides;
        set { _guides = value ?? []; Invalidate(); }
    }
    public event EventHandler<RenderWarePreviewGuideClickedEventArgs>? GuideClicked;
    public Vector4 EnvironmentLight
    {
        get => _environmentLight;
        set
        {
            _environmentLight = new Vector4(
                Math.Clamp(value.X, 0F, 4F), Math.Clamp(value.Y, 0F, 4F),
                Math.Clamp(value.Z, 0F, 4F), Math.Clamp(value.W, 0F, 4F));
            Invalidate();
        }
    }

    public void SetFieldCamera(BackyardCameraPreset preset)
    {
        ArgumentNullException.ThrowIfNull(preset);
        _fieldCamera = true;
        _perspective = true;
        _fieldPosition = preset.Position;
        _fieldHeading = preset.HeadingDegrees;
        _fieldPitch = preset.PitchDegrees;
        _zoom = 1F;
        _panX = _panY = 0F;
        Focus();
        Invalidate();
    }

    public void ResetView()
    {
        _fieldCamera = false;
        _movementKeys.Clear();
        _yaw = -0.55F;
        _pitch = _scene?.Kind == RenderWareAssetKind.RwsScene ? 0.36F : -0.28F;
        _zoom = 1F;
        _panX = 0F;
        _panY = 0F;
        Invalidate();
    }

    public void ZoomIn() => ChangeZoom(1.25F, null);
    public void ZoomOut() => ChangeZoom(0.8F, null);

    public void ShowFrontView()
    {
        _fieldCamera = false;
        _yaw = 0F;
        _pitch = 0F;
        _panX = _panY = 0F;
        Invalidate();
    }

    public void ShowTopView()
    {
        _fieldCamera = false;
        _yaw = 0F;
        _pitch = 1.45F;
        _panX = _panY = 0F;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        bool pan = !_fieldCamera && (e.Button is MouseButtons.Right or MouseButtons.Middle ||
                   (e.Button == MouseButtons.Left && ModifierKeys.HasFlag(Keys.Shift)));
        if (e.Button != MouseButtons.Left && !pan) return;
        _panning = pan;
        _rotating = !pan;
        _mouseDownPoint = e.Location;
        _lastMouse = e.Location;
        Capture = true;
        Focus();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_rotating && !_panning) return;
        if (_fieldCamera)
        {
            _fieldHeading += (e.X - _lastMouse.X) * 0.22F;
            _fieldPitch = Math.Clamp(_fieldPitch + (e.Y - _lastMouse.Y) * 0.18F, -89F, 89F);
        }
        else if (_panning)
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
        base.OnMouseUp(e);
        bool click = e.Button == MouseButtons.Left &&
                     Math.Abs(e.X - _mouseDownPoint.X) <= 4 && Math.Abs(e.Y - _mouseDownPoint.Y) <= 4;
        _rotating = false; _panning = false; Capture = false;
        if (click) HitTestGuide(e.Location);
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
        if (_fieldCamera && IsMovementKey(e.KeyCode))
        {
            _movementKeys.Add(e.KeyCode);
            e.Handled = true;
            return;
        }
        if (e.KeyCode is Keys.Add or Keys.Oemplus) { ZoomIn(); e.Handled = true; }
        else if (e.KeyCode is Keys.Subtract or Keys.OemMinus) { ZoomOut(); e.Handled = true; }
        else if (e.KeyCode == Keys.Home) { ResetView(); e.Handled = true; }
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        _movementKeys.Remove(e.KeyCode);
    }

    protected override bool IsInputKey(Keys keyData) =>
        IsMovementKey(keyData & Keys.KeyCode) || base.IsInputKey(keyData);

    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        _movementKeys.Clear();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _movementTimer.Dispose();
        base.Dispose(disposing);
    }

    private void ChangeZoom(float factor, Point? cursor)
    {
        float oldZoom = _zoom;
        _zoom = Math.Clamp(_zoom * factor, 0.08F, 30F);
        float applied = _zoom / oldZoom;
        if (!_fieldCamera && cursor is Point point && Width > 0 && Height > 0)
        {
            float x = point.X / (float)Width - 0.5F;
            float y = point.Y / (float)Height - 0.5F;
            _panX = Math.Clamp(x - (x - _panX) * applied, -4F, 4F);
            _panY = Math.Clamp(y - (y - _panY) * applied, -4F, 4F);
        }
        Invalidate();
    }

    private void MoveFieldCamera()
    {
        if (!_fieldCamera || _movementKeys.Count == 0 || !Focused) return;
        Vector3 forward = FieldForward();
        Vector3 right = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, forward));
        Vector3 movement = Vector3.Zero;
        if (_movementKeys.Contains(Keys.W)) movement += forward;
        if (_movementKeys.Contains(Keys.S)) movement -= forward;
        if (_movementKeys.Contains(Keys.D)) movement += right;
        if (_movementKeys.Contains(Keys.A)) movement -= right;
        if (_movementKeys.Contains(Keys.E)) movement += Vector3.UnitY;
        if (_movementKeys.Contains(Keys.Q)) movement -= Vector3.UnitY;
        if (movement.LengthSquared() < 0.0001F) return;
        float multiplier = ModifierKeys.HasFlag(Keys.Shift) ? 4F : 1F;
        _fieldPosition += Vector3.Normalize(movement) * MovementSpeed * multiplier * (_movementTimer.Interval / 1000F);
        Invalidate();
    }

    private static bool IsMovementKey(Keys key) => key is Keys.W or Keys.A or Keys.S or Keys.D or Keys.Q or Keys.E;

    private Vector3 FieldForward()
    {
        float heading = _fieldHeading * MathF.PI / 180F;
        float pitch = _fieldPitch * MathF.PI / 180F;
        float cp = MathF.Cos(pitch);
        return Vector3.Normalize(new Vector3(MathF.Sin(heading) * cp, -MathF.Sin(pitch), MathF.Cos(heading) * cp));
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
            DrawGuides(e.Graphics);
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
                    vertex.TextureCoordinate.Y * p.InverseW, p.InverseW, vertex.Color.ToArgb(), normal, p.Visible);
            }).ToArray();
            foreach (RenderWareTriangle triangle in mesh.Triangles)
            {
                if (!Valid(triangle, screen.Length)) continue;
                RenderWareMaterial material = triangle.MaterialIndex >= 0 && triangle.MaterialIndex < mesh.Materials.Count
                    ? mesh.Materials[triangle.MaterialIndex] : new RenderWareMaterial(null, Color.LightGray);
                if (ShouldHide(material))
                    continue;
                if (_fieldCamera)
                {
                    RasterizeFieldTriangle(mesh.Vertices[triangle.First], mesh.Vertices[triangle.Second],
                        mesh.Vertices[triangle.Third], material, _scene.ResolveTexture(material), projection,
                        pixels, depth, width, height);
                    continue;
                }
                if (!screen[triangle.First].Visible || !screen[triangle.Second].Visible || !screen[triangle.Third].Visible)
                    continue;
                Rasterize(screen[triangle.First], screen[triangle.Second], screen[triangle.Third], material,
                    _scene.ResolveTexture(material), pixels, depth, width, height, _perspective, _cullBackfaces,
                    _environmentLight);
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

    private void RasterizeFieldTriangle(RenderWareSceneVertex a, RenderWareSceneVertex b,
        RenderWareSceneVertex c, RenderWareMaterial material, RenderWareTexture? texture,
        Projection projection, int[] pixels, float[] depth, int width, int height)
    {
        Span<FieldVertex> input = stackalloc FieldVertex[3];
        input[0] = ToFieldVertex(a); input[1] = ToFieldVertex(b); input[2] = ToFieldVertex(c);
        Span<FieldVertex> polygon = stackalloc FieldVertex[4];
        int count = ClipNearPlane(input, polygon, 5F);
        if (count < 3) return;
        ScreenVertex first = ProjectFieldVertex(polygon[0], projection);
        for (int index = 1; index < count - 1; index++)
        {
            Rasterize(first, ProjectFieldVertex(polygon[index], projection),
                ProjectFieldVertex(polygon[index + 1], projection), material, texture,
                pixels, depth, width, height, true, _cullBackfaces, _environmentLight);
        }
    }

    private FieldVertex ToFieldVertex(RenderWareSceneVertex vertex) => new(
        RotateVector(vertex.Position - _fieldPosition), vertex.TextureCoordinate,
        vertex.Color.ToArgb(), RotateVector(vertex.Normal));

    private static int ClipNearPlane(ReadOnlySpan<FieldVertex> input, Span<FieldVertex> output, float near)
    {
        int count = 0;
        FieldVertex previous = input[^1];
        bool previousInside = previous.View.Z >= near;
        foreach (FieldVertex current in input)
        {
            bool currentInside = current.View.Z >= near;
            if (currentInside != previousInside)
            {
                float t = (near - previous.View.Z) / (current.View.Z - previous.View.Z);
                output[count++] = Lerp(previous, current, t);
            }
            if (currentInside) output[count++] = current;
            previous = current;
            previousInside = currentInside;
        }
        return count;
    }

    private static FieldVertex Lerp(FieldVertex a, FieldVertex b, float amount) => new(
        Vector3.Lerp(a.View, b.View, amount), Vector2.Lerp(a.Uv, b.Uv, amount),
        LerpArgb(a.Argb, b.Argb, amount),
        Vector3.Lerp(a.Normal, b.Normal, amount));

    private static int LerpArgb(int a, int b, float amount)
    {
        int alpha = LerpChannel(a, b, 24, amount);
        int red = LerpChannel(a, b, 16, amount);
        int green = LerpChannel(a, b, 8, amount);
        int blue = LerpChannel(a, b, 0, amount);
        return (alpha << 24) | (red << 16) | (green << 8) | blue;
    }

    private static int LerpChannel(int a, int b, int shift, float amount) =>
        Math.Clamp((int)MathF.Round(((a >>> shift) & 0xFF) +
            (((b >>> shift) & 0xFF) - ((a >>> shift) & 0xFF)) * amount), 0, 255);

    private static ScreenVertex ProjectFieldVertex(FieldVertex vertex, Projection projection)
    {
        float inverseW = 1F / vertex.View.Z;
        return new ScreenVertex(projection.CenterX + vertex.View.X * projection.FocalLength * inverseW,
            projection.CenterY - vertex.View.Y * projection.FocalLength * inverseW, inverseW,
            vertex.Uv.X * inverseW, vertex.Uv.Y * inverseW, inverseW,
            vertex.Argb, vertex.Normal, true);
    }

    private void DrawWireframe(Graphics graphics)
    {
        Projection projection = BuildProjection(Width, Height);
        using Pen pen = new(Color.FromArgb(145, 112, 205, 255), 1F);
        int total = Math.Max(1, _scene!.TriangleCount), stride = Math.Max(1, total / 18_000), current = 0;
        foreach (RenderWareSceneMesh mesh in _scene.Meshes)
        {
            ProjectedVertex[] points = mesh.Vertices.Select(vertex => Project(vertex.Position, projection)).ToArray();
            foreach (RenderWareTriangle triangle in mesh.Triangles)
            {
                RenderWareMaterial material = triangle.MaterialIndex >= 0 && triangle.MaterialIndex < mesh.Materials.Count
                    ? mesh.Materials[triangle.MaterialIndex] : new RenderWareMaterial(null, Color.LightGray);
                if (ShouldHide(material)) continue;
                if (current++ % stride != 0 || !Valid(triangle, points.Length)) continue;
                ProjectedVertex a = points[triangle.First], b = points[triangle.Second], c = points[triangle.Third];
                if (!a.Visible || !b.Visible || !c.Visible) continue;
                graphics.DrawPolygon(pen, new[] { new PointF(a.X, a.Y), new PointF(b.X, b.Y), new PointF(c.X, c.Y) });
            }
        }
    }

    private void DrawGuides(Graphics graphics)
    {
        if (_guides.Count == 0 || _scene == null) return;
        Projection projection = BuildProjection(Width, Height);
        foreach (RenderWarePreviewGuide guide in _guides.Where(item => item.PathPoints.Count > 1))
        {
            Color color = guide.Selected ? Color.Gold : guide.Enabled
                ? Color.FromArgb(220, 70, 210, 255) : Color.FromArgb(155, 175, 175, 175);
            using Pen pen = new(color, guide.Selected ? 2.5F : 1.5F)
            {
                DashStyle = guide.Enabled ? System.Drawing.Drawing2D.DashStyle.Solid :
                    System.Drawing.Drawing2D.DashStyle.Dash
            };
            for (int index = 1; index < guide.PathPoints.Count; index++)
            {
                ProjectedVertex a = Project(guide.PathPoints[index - 1], projection);
                ProjectedVertex b = Project(guide.PathPoints[index], projection);
                if (a.Visible && b.Visible) graphics.DrawLine(pen, a.X, a.Y, b.X, b.Y);
            }
        }
        foreach (RenderWarePreviewGuide guide in _guides)
        {
            ProjectedVertex marker = Project(guide.Position, projection);
            if (!marker.Visible) continue;
            float radius = guide.Selected ? 7F : 4.5F;
            Color color = guide.Selected ? Color.Gold : guide.Enabled
                ? Color.FromArgb(235, 70, 210, 255) : Color.FromArgb(190, 175, 175, 175);
            using SolidBrush fill = new(Color.FromArgb(190, color));
            using Pen outline = new(Color.Black, 1.5F);
            graphics.FillEllipse(fill, marker.X - radius, marker.Y - radius, radius * 2F, radius * 2F);
            graphics.DrawEllipse(outline, marker.X - radius, marker.Y - radius, radius * 2F, radius * 2F);
            if (guide.Selected)
            {
                TextRenderer.DrawText(graphics, guide.Label, Font,
                    new Point((int)marker.X + 9, (int)marker.Y - 9), Color.Gold,
                    TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
            }
        }
    }

    private void HitTestGuide(Point location)
    {
        if (_guides.Count == 0 || _scene == null) return;
        Projection projection = BuildProjection(Width, Height);
        RenderWarePreviewGuide? closest = null;
        float closestDistance = 14F * 14F;
        foreach (RenderWarePreviewGuide guide in _guides)
        {
            ProjectedVertex marker = Project(guide.Position, projection);
            if (!marker.Visible) continue;
            float dx = marker.X - location.X, dy = marker.Y - location.Y;
            float distance = dx * dx + dy * dy;
            if (distance > closestDistance) continue;
            closest = guide;
            closestDistance = distance;
        }
        if (closest != null)
            GuideClicked?.Invoke(this, new RenderWarePreviewGuideClickedEventArgs(closest.Key));
    }

    private Projection BuildProjection(int width, int height)
    {
        float fieldAvailableHeight = Math.Max(1, height - 64);
        float fieldHalfFov = 25F * MathF.PI / 180F;
        float fieldFocalLength = fieldAvailableHeight * 0.5F / MathF.Tan(fieldHalfFov) * _zoom;
        if (_fieldCamera)
            return new Projection(width * 0.5F, height * 0.5F + 3F, Vector3.Zero, 1F,
                fieldFocalLength, 0F, 0F, 0F);

        IReadOnlyList<Vector3> all = GetProjectionPoints();
        Vector3 minimum = new(float.MaxValue), maximum = new(float.MinValue);
        foreach (Vector3 point in all) { minimum = Vector3.Min(minimum, point); maximum = Vector3.Max(maximum, point); }
        Vector3 modelCenter = (minimum + maximum) * 0.5F;
        float radius = Math.Max(0.001F, all.Max(point => Vector3.Distance(point, modelCenter)));
        float availableWidth = Math.Max(1, width - 28), availableHeight = Math.Max(1, height - 64);
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
        float centerX = width * (0.5F + _panX);
        float centerY = height * (0.5F + _panY) + 3F;
        if (_perspective)
        {
            float projectedMinX = float.MaxValue, projectedMaxX = float.MinValue;
            float projectedMinY = float.MaxValue, projectedMaxY = float.MinValue;
            foreach (Vector3 point in all)
            {
                Vector3 rotated = RotateVector(point - modelCenter);
                float distance = Math.Max(0.001F, cameraDistance - rotated.Z);
                float projectedX = rotated.X * focalLength / distance;
                float projectedY = rotated.Y * focalLength / distance;
                projectedMinX = Math.Min(projectedMinX, projectedX);
                projectedMaxX = Math.Max(projectedMaxX, projectedX);
                projectedMinY = Math.Min(projectedMinY, projectedY);
                projectedMaxY = Math.Max(projectedMaxY, projectedY);
            }
            centerX -= (projectedMinX + projectedMaxX) * 0.5F;
            centerY += (projectedMinY + projectedMaxY) * 0.5F;
        }
        return new Projection(centerX, centerY, modelCenter, orthographicScale,
            focalLength, cameraDistance, (minX + maxX) / 2F, (minY + maxY) / 2F);
    }

    private IReadOnlyList<Vector3> GetProjectionPoints()
    {
        List<Vector3> visible = new();
        foreach (RenderWareSceneMesh mesh in _scene!.Meshes)
        {
            foreach (RenderWareTriangle triangle in mesh.Triangles)
            {
                if (!Valid(triangle, mesh.Vertices.Count)) continue;
                RenderWareMaterial material = triangle.MaterialIndex >= 0 && triangle.MaterialIndex < mesh.Materials.Count
                    ? mesh.Materials[triangle.MaterialIndex] : new RenderWareMaterial(null, Color.LightGray);
                if (ShouldHide(material)) continue;
                visible.Add(mesh.Vertices[triangle.First].Position);
                visible.Add(mesh.Vertices[triangle.Second].Position);
                visible.Add(mesh.Vertices[triangle.Third].Position);
            }
        }
        if (visible.Count > 0) return visible;
        List<Vector3> fallback = _scene.Meshes.SelectMany(mesh => mesh.Vertices)
            .Select(vertex => vertex.Position).ToList();
        if (fallback.Count == 0) fallback.Add(Vector3.Zero);
        return fallback;
    }

    private ProjectedVertex Project(Vector3 point, Projection projection)
    {
        if (_fieldCamera)
        {
            Vector3 view = RotateVector(point - _fieldPosition);
            if (view.Z <= 5F) return new ProjectedVertex(0F, 0F, 0F, 0F, false);
            float fieldInverseW = 1F / view.Z;
            return new ProjectedVertex(projection.CenterX + view.X * projection.FocalLength * fieldInverseW,
                projection.CenterY - view.Y * projection.FocalLength * fieldInverseW,
                fieldInverseW, fieldInverseW, true);
        }
        Vector3 rotated = RotateVector(point - projection.ModelCenter);
        if (!_perspective)
            return new ProjectedVertex(projection.CenterX + (rotated.X - projection.OrthographicCenterX) * projection.Scale,
                projection.CenterY - (rotated.Y - projection.OrthographicCenterY) * projection.Scale,
                rotated.Z, 1F, true);
        float viewDistance = Math.Max(0.001F, projection.CameraDistance - rotated.Z);
        float inverseW = 1F / viewDistance;
        return new ProjectedVertex(projection.CenterX + rotated.X * projection.FocalLength * inverseW,
            projection.CenterY - rotated.Y * projection.FocalLength * inverseW, inverseW, inverseW, true);
    }

    private Vector3 RotateVector(Vector3 point)
    {
        if (_fieldCamera)
        {
            Vector3 forward = FieldForward();
            Vector3 right = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, forward));
            Vector3 up = Vector3.Normalize(Vector3.Cross(forward, right));
            return new Vector3(Vector3.Dot(point, right), Vector3.Dot(point, up), Vector3.Dot(point, forward));
        }
        float cy = MathF.Cos(_yaw), sy = MathF.Sin(_yaw);
        float x = cy * point.X + sy * point.Z, z = -sy * point.X + cy * point.Z;
        float cp = MathF.Cos(_pitch), sp = MathF.Sin(_pitch);
        return new Vector3(x, cp * point.Y - sp * z, sp * point.Y + cp * z);
    }

    private static void Rasterize(ScreenVertex a, ScreenVertex b, ScreenVertex c,
        RenderWareMaterial material, RenderWareTexture? texture, int[] pixels, float[] depth, int width, int height,
        bool perspective, bool cullBackfaces, Vector4 environmentLight)
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
            int red = (int)(((color >>> 16) & 0xFF) * material.Color.R / 255F * vertexRed * shade * environmentLight.X);
            int green = (int)(((color >>> 8) & 0xFF) * material.Color.G / 255F * vertexGreen * shade * environmentLight.Y);
            int blue = (int)((color & 0xFF) * material.Color.B / 255F * vertexBlue * shade * environmentLight.Z);
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
        string help = _fieldCamera
            ? "Field POV  •  Drag to look  •  WASD move  •  Q/E height  •  Shift faster  •  Wheel zoom"
            : "Left-drag rotate  •  Right-drag pan  •  Wheel zoom  •  Double-click fit";
        TextRenderer.DrawText(graphics, help,
            Font, new Rectangle(8, Height - 23, Width - 16, 18), Color.FromArgb(175, 205, 215, 225), TextFormatFlags.Right);
    }

    private void DrawHeader(Graphics graphics)
    {
        using Brush brush = new SolidBrush(Color.FromArgb(185, 16, 19, 23));
        graphics.FillRectangle(brush, 0, 0, Width, 29);
        string camera = _fieldCamera
            ? $"  |  POV ({_fieldPosition.X:0.0}, {_fieldPosition.Y:0.0}, {_fieldPosition.Z:0.0})"
            : string.Empty;
        TextRenderer.DrawText(graphics,
            $"{Path.GetFileName(_scene!.SourcePath)}  |  {_scene.VertexCount:N0} vertices  |  {_scene.TriangleCount:N0} triangles{camera}",
            Font, new Rectangle(9, 5, Width - 18, 19), Color.White, TextFormatFlags.EndEllipsis);
    }

    private void DrawMessage(Graphics graphics, string message) => TextRenderer.DrawText(graphics, message, Font,
        new Rectangle(35, 35, Math.Max(1, Width - 70), Math.Max(1, Height - 70)), Color.FromArgb(220, 230, 235, 240),
        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);

    private readonly record struct ProjectedVertex(float X, float Y, float Depth, float InverseW, bool Visible);
    private readonly record struct ScreenVertex(float X, float Y, float Depth, float UOverW, float VOverW,
        float InverseW, int Color, Vector3 Normal, bool Visible);
    private readonly record struct FieldVertex(Vector3 View, Vector2 Uv, int Argb, Vector3 Normal);
    private readonly record struct Projection(float CenterX, float CenterY, Vector3 ModelCenter, float Scale,
        float FocalLength, float CameraDistance, float OrthographicCenterX, float OrthographicCenterY);
}

public sealed record RenderWarePreviewGuide(int Key, string Label, Vector3 Position,
    IReadOnlyList<Vector3> PathPoints, bool Enabled, bool Selected);

public sealed class RenderWarePreviewGuideClickedEventArgs(int key) : EventArgs
{
    public int Key { get; } = key;
}
