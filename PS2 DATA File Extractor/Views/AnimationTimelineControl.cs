using PS2_DATA_File_Extractor.FileOperations;

namespace PS2_DATA_File_Extractor;

public sealed class AnimationTimelineControl : Control
{
    private const int LeftGutter = 66;
    private RenderWareAnimationFile? _animation;
    private double _position;

    public AnimationTimelineControl()
    {
        DoubleBuffered = true;
        BackColor = SystemColors.Window;
        ForeColor = SystemColors.ControlText;
        MinimumSize = new Size(300, 150);
        SetStyle(ControlStyles.ResizeRedraw, true);
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

    public double PositionSeconds
    {
        get => _position;
        set
        {
            double duration = _animation?.DurationSeconds ?? 0;
            _position = Math.Clamp(value, 0, duration);
            Invalidate();
        }
    }

    public event EventHandler<double>? SeekRequested;

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (_animation == null || e.X < LeftGutter || Width <= LeftGutter + 8) return;
        double amount = Math.Clamp((double)(e.X - LeftGutter) / (Width - LeftGutter - 8), 0, 1);
        SeekRequested?.Invoke(this, amount * _animation.DurationSeconds);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Graphics graphics = e.Graphics;
        graphics.Clear(BackColor);
        if (_animation == null)
        {
            TextRenderer.DrawText(graphics, "Select an animation to view its timeline.", Font,
                ClientRectangle, SystemColors.GrayText,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            return;
        }

        int right = Math.Max(LeftGutter + 1, Width - 8);
        int evtTop = 7;
        int evtHeight = _animation.PairedEvent == null ? 0 : 24;
        int tracksTop = evtTop + evtHeight + 20;
        int available = Math.Max(1, Height - tracksTop - 20);
        int trackCount = Math.Max(1, _animation.TrackCount);
        float rowHeight = Math.Max(2.5F, (float)available / trackCount);

        using Pen gridPen = new(SystemColors.ControlLight);
        using Pen axisPen = new(SystemColors.ControlDark);
        using Pen playheadPen = new(Color.FromArgb(210, 36, 44), 2F);
        using Brush keyBrush = new SolidBrush(Color.FromArgb(42, 105, 180));
        using Brush pairedBrush = new SolidBrush(Color.FromArgb(230, 144, 34));
        using Brush pairedAlternateBrush = new SolidBrush(Color.FromArgb(113, 85, 155));

        DrawTimeAxis(graphics, right, tracksTop, axisPen, gridPen);
        if (_animation.PairedEvent != null)
        {
            TextRenderer.DrawText(graphics, "EVT", Font, new Point(8, evtTop + 2), ForeColor);
            bool alternate = false;
            foreach (FacialEvent item in _animation.PairedEvent.Events)
            {
                int x = TimeToX(item.Timestamp, right);
                graphics.FillRectangle(alternate ? pairedAlternateBrush : pairedBrush,
                    x - 1, evtTop + 2, 3, 16);
                alternate = !alternate;
            }
        }

        for (int track = 0; track < trackCount; track++)
        {
            float y = tracksTop + track * rowHeight;
            if (rowHeight >= 12)
                TextRenderer.DrawText(graphics, $"Track {track}", Font,
                    new Rectangle(4, (int)y, LeftGutter - 8, (int)Math.Ceiling(rowHeight)),
                    ForeColor, TextFormatFlags.VerticalCenter | TextFormatFlags.Right);
            graphics.DrawLine(gridPen, LeftGutter, y + rowHeight / 2, right, y + rowHeight / 2);
            foreach (int frameIndex in _animation.Tracks[track].FrameIndices)
            {
                RenderWareAnimationKeyFrame frame = _animation.Frames[frameIndex];
                int x = TimeToX(frame.TimeSeconds, right);
                float radius = rowHeight >= 9 ? 3F : 1.6F;
                graphics.FillEllipse(keyBrush, x - radius, y + rowHeight / 2 - radius,
                    radius * 2, radius * 2);
            }
        }

        int playX = TimeToX(_position, right);
        graphics.DrawLine(playheadPen, playX, evtTop, playX, Height - 5);
        TextRenderer.DrawText(graphics, $"{_position:0.000}s", Font,
            new Rectangle(Math.Max(LeftGutter, playX - 45), Height - 19, 90, 18),
            playheadPen.Color, TextFormatFlags.HorizontalCenter);
    }

    private void DrawTimeAxis(Graphics graphics, int right, int top, Pen axisPen, Pen gridPen)
    {
        graphics.DrawLine(axisPen, LeftGutter, top - 4, right, top - 4);
        for (int tick = 0; tick <= 4; tick++)
        {
            double time = _animation!.DurationSeconds * tick / 4D;
            int x = TimeToX(time, right);
            graphics.DrawLine(gridPen, x, top - 4, x, Height - 20);
            TextRenderer.DrawText(graphics, $"{time:0.##}", Font,
                new Rectangle(x - 30, top - 19, 60, 16), SystemColors.GrayText,
                TextFormatFlags.HorizontalCenter);
        }
    }

    private int TimeToX(double time, int right)
    {
        double duration = Math.Max(0.000001, _animation?.DurationSeconds ?? 0);
        return LeftGutter + (int)Math.Round(Math.Clamp(time / duration, 0, 1) * (right - LeftGutter));
    }
}
