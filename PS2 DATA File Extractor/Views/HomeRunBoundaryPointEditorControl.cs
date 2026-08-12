using System.Numerics;
using PS2_DATA_File_Extractor.FileOperations;

namespace PS2_DATA_File_Extractor;

public sealed class HomeRunBoundaryPointEditorControl : Control
{
    private IReadOnlyList<StadiumHomeRunBoundaryVertex> _vertices = [];
    private IReadOnlyList<StadiumHomeRunBoundaryTriangle> _triangles = [];
    private readonly HashSet<int> _selected = [];
    private Dictionary<int, Vector3> _dragStartPositions = [];
    private Vector2 _dragStartWorld;
    private bool _dragging;
    private float _minimumX, _maximumX, _minimumZ, _maximumZ;

    public HomeRunBoundaryPointEditorControl()
    {
        DoubleBuffered = true;
        BackColor = SystemColors.Window;
        ForeColor = SystemColors.ControlText;
        MinimumSize = new Size(260, 180);
        SetStyle(ControlStyles.ResizeRedraw, true);
        TabStop = true;
    }

    public int PrimarySelectedIndex { get; private set; } = -1;
    public IReadOnlyCollection<int> SelectedIndices => _selected;
    public event EventHandler? SelectionChanged;
    public event EventHandler<HomeRunBoundaryPointsMovedEventArgs>? PointsMoved;

    public void SetBoundary(
        IReadOnlyList<StadiumHomeRunBoundaryVertex>? vertices,
        IReadOnlyList<StadiumHomeRunBoundaryTriangle>? triangles,
        bool resetView)
    {
        _vertices = vertices ?? [];
        _triangles = triangles ?? [];
        _selected.RemoveWhere(index => index < 0 || index >= _vertices.Count);
        if (PrimarySelectedIndex >= _vertices.Count) PrimarySelectedIndex = -1;
        if (resetView || !HasUsableView()) FitView();
        Invalidate();
    }

    public void SelectPoint(int index, bool preserveOthers = false)
    {
        if (index < 0 || index >= _vertices.Count) index = -1;
        if (!preserveOthers) _selected.Clear();
        if (index >= 0) _selected.Add(index);
        PrimarySelectedIndex = index;
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    public void FitView()
    {
        if (_vertices.Count == 0)
        {
            _minimumX = _minimumZ = -1;
            _maximumX = _maximumZ = 1;
            Invalidate();
            return;
        }
        _minimumX = _vertices.Min(vertex => vertex.Position.X);
        _maximumX = _vertices.Max(vertex => vertex.Position.X);
        _minimumZ = _vertices.Min(vertex => vertex.Position.Z);
        _maximumZ = _vertices.Max(vertex => vertex.Position.Z);
        float spanX = Math.Max(1F, _maximumX - _minimumX);
        float spanZ = Math.Max(1F, _maximumZ - _minimumZ);
        _minimumX -= spanX * 0.1F;
        _maximumX += spanX * 0.1F;
        _minimumZ -= spanZ * 0.1F;
        _maximumZ += spanZ * 0.1F;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.Clear(BackColor);
        Rectangle plot = PlotRectangle();
        using Pen border = new(SystemColors.ControlDark);
        e.Graphics.DrawRectangle(border, plot);
        TextRenderer.DrawText(e.Graphics, "Top view (X / Z) — drag points; Ctrl-click selects multiple", Font,
            new Rectangle(8, 5, Math.Max(1, Width - 16), 20), ForeColor,
            TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
        if (_vertices.Count == 0)
        {
            TextRenderer.DrawText(e.Graphics, "No editable home-run boundary points.", Font, plot,
                SystemColors.GrayText, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            return;
        }

        using SolidBrush face = new(Color.FromArgb(36, Color.Goldenrod));
        using Pen edge = new(Color.FromArgb(135, 170, 120, 15), 1F);
        foreach (StadiumHomeRunBoundaryTriangle triangle in _triangles)
        {
            if (!Valid(triangle.First) || !Valid(triangle.Second) || !Valid(triangle.Third)) continue;
            PointF[] polygon =
            [
                ToScreen(_vertices[triangle.First].Position, plot),
                ToScreen(_vertices[triangle.Second].Position, plot),
                ToScreen(_vertices[triangle.Third].Position, plot)
            ];
            e.Graphics.FillPolygon(face, polygon);
            e.Graphics.DrawPolygon(edge, polygon);
        }

        for (int index = 0; index < _vertices.Count; index++)
        {
            if (_selected.Contains(index)) continue;
            DrawPoint(e.Graphics, plot, index, selected: false, primary: false);
        }
        foreach (int index in _selected.Where(Valid))
            DrawPoint(e.Graphics, plot, index, selected: true, primary: index == PrimarySelectedIndex);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left || _vertices.Count == 0) return;
        int hit = HitTest(e.Location);
        bool control = ModifierKeys.HasFlag(Keys.Control);
        if (hit < 0)
        {
            if (!control) SelectPoint(-1);
            return;
        }
        if (control)
        {
            if (!_selected.Add(hit)) _selected.Remove(hit);
            PrimarySelectedIndex = _selected.Contains(hit) ? hit : _selected.LastOrDefault(-1);
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            Invalidate();
        }
        else if (!_selected.Contains(hit)) SelectPoint(hit);
        else
        {
            PrimarySelectedIndex = hit;
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            Invalidate();
        }
        if (_selected.Count == 0) return;
        _dragStartPositions = _selected.ToDictionary(index => index, index => _vertices[index].Position);
        _dragStartWorld = ToWorld(e.Location, PlotRectangle());
        _dragging = true;
        Capture = true;
        Focus();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_dragging) return;
        Vector2 current = ToWorld(e.Location, PlotRectangle());
        Vector2 delta = current - _dragStartWorld;
        Dictionary<int, Vector3> positions = _dragStartPositions.ToDictionary(
            pair => pair.Key,
            pair => new Vector3(pair.Value.X + delta.X, pair.Value.Y, pair.Value.Z + delta.Y));
        StadiumHomeRunBoundaryVertex[] changed = _vertices.ToArray();
        foreach ((int index, Vector3 position) in positions)
            changed[index] = changed[index] with { Position = position, IsModified = true };
        _vertices = changed;
        PointsMoved?.Invoke(this, new HomeRunBoundaryPointsMovedEventArgs(positions));
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left) return;
        _dragging = false;
        Capture = false;
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        if (e.Button == MouseButtons.Left && HitTest(e.Location) < 0) FitView();
    }

    private void DrawPoint(Graphics graphics, Rectangle plot, int index, bool selected, bool primary)
    {
        PointF point = ToScreen(_vertices[index].Position, plot);
        float radius = primary ? 6F : selected ? 5F : 3.5F;
        Color color = primary ? Color.DeepPink : selected ? Color.OrangeRed :
            _vertices[index].IsModified ? Color.MediumVioletRed : Color.DodgerBlue;
        using SolidBrush fill = new(color);
        using Pen outline = new(primary ? Color.White : Color.FromArgb(45, 45, 45), primary ? 2F : 1F);
        graphics.FillEllipse(fill, point.X - radius, point.Y - radius, radius * 2, radius * 2);
        graphics.DrawEllipse(outline, point.X - radius, point.Y - radius, radius * 2, radius * 2);
    }

    private int HitTest(Point location)
    {
        Rectangle plot = PlotRectangle();
        int closest = -1;
        float closestDistance = 12F * 12F;
        for (int index = 0; index < _vertices.Count; index++)
        {
            PointF point = ToScreen(_vertices[index].Position, plot);
            float x = point.X - location.X, y = point.Y - location.Y;
            float distance = x * x + y * y;
            if (distance > closestDistance) continue;
            closest = index;
            closestDistance = distance;
        }
        return closest;
    }

    private Rectangle PlotRectangle() => new(24, 28, Math.Max(1, Width - 48), Math.Max(1, Height - 50));

    private PointF ToScreen(Vector3 position, Rectangle plot) => new(
        plot.Left + (position.X - _minimumX) / Math.Max(0.0001F, _maximumX - _minimumX) * plot.Width,
        plot.Bottom - (position.Z - _minimumZ) / Math.Max(0.0001F, _maximumZ - _minimumZ) * plot.Height);

    private Vector2 ToWorld(Point position, Rectangle plot) => new(
        _minimumX + (position.X - plot.Left) / (float)Math.Max(1, plot.Width) * (_maximumX - _minimumX),
        _minimumZ + (plot.Bottom - position.Y) / (float)Math.Max(1, plot.Height) * (_maximumZ - _minimumZ));

    private bool Valid(int index) => index >= 0 && index < _vertices.Count;
    private bool HasUsableView() => float.IsFinite(_minimumX) && float.IsFinite(_maximumX) &&
                                    float.IsFinite(_minimumZ) && float.IsFinite(_maximumZ) &&
                                    _maximumX > _minimumX && _maximumZ > _minimumZ;
}

public sealed class HomeRunBoundaryPointsMovedEventArgs(IReadOnlyDictionary<int, Vector3> positions) : EventArgs
{
    public IReadOnlyDictionary<int, Vector3> Positions { get; } = positions;
}
