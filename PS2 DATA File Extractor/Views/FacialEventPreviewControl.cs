using System.Drawing.Drawing2D;
using PS2_DATA_File_Extractor.FileOperations;

namespace PS2_DATA_File_Extractor;

public sealed class FacialEventPreviewControl : Control
{
    private FacialEventFile? _file;
    private double _positionSeconds;
    private double _timelineDuration;

    public FacialEventPreviewControl()
    {
        DoubleBuffered = true;
        BackColor = SystemColors.Window;
        ForeColor = SystemColors.ControlText;
        SetStyle(ControlStyles.ResizeRedraw, true);
    }

    public FacialEventFile? EventFile
    {
        get => _file;
        set
        {
            _file = value;
            _positionSeconds = 0;
            _timelineDuration = value?.DurationSeconds ?? 0;
            Invalidate();
        }
    }

    public double PositionSeconds
    {
        get => _positionSeconds;
        set
        {
            _positionSeconds = Math.Max(0, value);
            Invalidate();
        }
    }

    public double TimelineDuration
    {
        get => _timelineDuration;
        set
        {
            _timelineDuration = Math.Max(0, value);
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Graphics graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using Pen border = new(SystemColors.ControlDark);
        graphics.DrawRectangle(border, 0, 0, Math.Max(0, Width - 1), Math.Max(0, Height - 1));

        if (_file == null)
        {
            TextRenderer.DrawText(graphics, "Select an EVT file to preview.", Font,
                ClientRectangle, SystemColors.GrayText,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            return;
        }

        int faceWidth = Math.Min(190, Math.Max(130, Width / 5));
        Rectangle faceArea = new(18, 16, faceWidth, Math.Max(120, Height - 34));
        DrawFace(graphics, faceArea);
        Rectangle timeline = new(faceArea.Right + 22, 34,
            Math.Max(50, Width - faceArea.Right - 42), Math.Max(80, Height - 64));
        DrawTimeline(graphics, timeline);
    }

    private void DrawFace(Graphics graphics, Rectangle area)
    {
        int diameter = Math.Min(area.Width - 20, area.Height - 40);
        Rectangle face = new(area.Left + (area.Width - diameter) / 2, area.Top + 4, diameter, diameter);
        using Brush skin = new SolidBrush(Color.FromArgb(255, 224, 186));
        using Pen outline = new(Color.FromArgb(80, 80, 80), 2);
        graphics.FillEllipse(skin, face);
        graphics.DrawEllipse(outline, face);

        FacialEvent? eyes = _file!.GetActiveEvent("CLASS_EYES", _positionSeconds);
        int eyeY = face.Top + face.Height * 38 / 100;
        int eyeSpacing = face.Width / 5;
        int centerX = face.Left + face.Width / 2;
        float openness = NumericOpenness(eyes?.EventType);
        DrawEye(graphics, centerX - eyeSpacing, eyeY, openness);
        DrawEye(graphics, centerX + eyeSpacing, eyeY, openness);

        FacialEvent? mouth = _file.GetActiveEvent("CLASS_TALKIES", _positionSeconds)
            ?? _file.GetActiveEvent("CLASS_MOUTH", _positionSeconds);
        string mouthType = mouth?.EventType ?? "STATIC";
        DrawMouth(graphics, new Rectangle(centerX - face.Width / 5,
            face.Top + face.Height * 62 / 100, face.Width * 2 / 5, face.Height / 5), mouthType);

        string state = mouth == null ? "Mouth: resting" : $"Mouth: {mouth.EventType}";
        if (eyes != null) state += $"    Eyes: {eyes.EventType}";
        TextRenderer.DrawText(graphics, state, Font,
            new Rectangle(area.Left, face.Bottom + 5, area.Width, 24), ForeColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.EndEllipsis);
    }

    private static void DrawEye(Graphics graphics, int x, int y, float openness)
    {
        Rectangle eye = new(x - 15, y - 7, 30, 14);
        using Pen pen = new(Color.FromArgb(50, 50, 50), 2);
        if (openness < 0.18F)
        {
            graphics.DrawLine(pen, eye.Left, y, eye.Right, y);
            return;
        }
        RectangleF adjusted = new(eye.X, y - eye.Height * openness / 2F,
            eye.Width, eye.Height * openness);
        graphics.DrawEllipse(pen, adjusted);
        graphics.FillEllipse(Brushes.Black, x - 2, y - 2, 4, 4);
    }

    private static void DrawMouth(Graphics graphics, Rectangle area, string type)
    {
        using Pen outline = new(Color.FromArgb(90, 35, 45), 3);
        using Brush inside = new SolidBrush(Color.FromArgb(105, 35, 45));
        string normalized = type.ToUpperInvariant();
        if (normalized is "STATIC" or "MM" or "INVALID")
        {
            graphics.DrawLine(outline, area.Left + 4, area.Top + area.Height / 2,
                area.Right - 4, area.Top + area.Height / 2);
            return;
        }

        Rectangle mouth = normalized switch
        {
            "AI" => new Rectangle(area.Left, area.Top + 1, area.Width, area.Height - 2),
            "EE" => new Rectangle(area.Left, area.Top + area.Height / 3, area.Width, Math.Max(6, area.Height / 3)),
            "OH" => new Rectangle(area.Left + area.Width / 4, area.Top,
                area.Width / 2, area.Height),
            "OO" => new Rectangle(area.Left + area.Width / 3, area.Top + area.Height / 5,
                area.Width / 3, area.Height * 3 / 5),
            "FV" => new Rectangle(area.Left, area.Top + area.Height / 3,
                area.Width, Math.Max(7, area.Height / 3)),
            "CDG" => new Rectangle(area.Left + 2, area.Top + area.Height / 4,
                area.Width - 4, area.Height / 2),
            _ => NumericMouth(area, normalized)
        };
        graphics.FillEllipse(inside, mouth);
        graphics.DrawEllipse(outline, mouth);
        if (normalized is "FV" or "CDG")
        {
            using Pen teeth = new(Color.WhiteSmoke, 2);
            graphics.DrawLine(teeth, mouth.Left + 4, mouth.Top + mouth.Height / 3,
                mouth.Right - 4, mouth.Top + mouth.Height / 3);
        }
    }

    private void DrawTimeline(Graphics graphics, Rectangle area)
    {
        double duration = Math.Max(0.01, Math.Max(_timelineDuration, _file!.DurationSeconds));
        int axisY = area.Bottom - 30;
        using Pen axis = new(SystemColors.ControlDark, 1);
        graphics.DrawLine(axis, area.Left, axisY, area.Right, axisY);
        TextRenderer.DrawText(graphics, "0.00", Font, new Point(area.Left, axisY + 4), SystemColors.GrayText);
        string end = duration.ToString("0.00") + " s";
        Size endSize = TextRenderer.MeasureText(end, Font);
        TextRenderer.DrawText(graphics, end, Font,
            new Point(area.Right - endSize.Width, axisY + 4), SystemColors.GrayText);

        foreach (FacialEvent item in _file.Events)
        {
            int x = area.Left + (int)Math.Round(Math.Clamp(item.Timestamp / duration, 0, 1) * area.Width);
            Color color = item.EventClass.Equals("CLASS_EYES", StringComparison.OrdinalIgnoreCase)
                ? Color.SteelBlue
                : item.EventClass.Equals("CLASS_MOUTH", StringComparison.OrdinalIgnoreCase)
                    ? Color.DarkOrange
                    : Color.Firebrick;
            using Pen tick = new(color, 1);
            graphics.DrawLine(tick, x, area.Top + 28, x, axisY);
        }

        int playX = area.Left + (int)Math.Round(Math.Clamp(_positionSeconds / duration, 0, 1) * area.Width);
        using Pen playhead = new(Color.Black, 2);
        graphics.DrawLine(playhead, playX, area.Top + 12, playX, axisY + 2);
        string active = ActiveStateText();
        TextRenderer.DrawText(graphics,
            $"{_positionSeconds:0.000} s    {active}", Font,
            new Rectangle(area.Left, area.Top - 2, area.Width, 24), ForeColor,
            TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        TextRenderer.DrawText(graphics,
            "Red: talkie/mouth    Blue: eyes", Font,
            new Rectangle(area.Left, axisY - 24, area.Width, 20), SystemColors.GrayText,
            TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
    }

    private string ActiveStateText()
    {
        List<string> states = new();
        foreach (string eventClass in _file!.EventClasses)
        {
            FacialEvent? active = _file.GetActiveEvent(eventClass, _positionSeconds);
            if (active != null) states.Add($"{ShortClass(eventClass)}: {active.EventType}");
        }
        return states.Count == 0 ? "No event yet" : string.Join("    ", states);
    }

    private static string ShortClass(string eventClass) => eventClass.Replace("CLASS_", string.Empty);

    private static float NumericOpenness(string? type)
    {
        if (!int.TryParse(type, out int value)) return 1F;
        return (value % 6) / 5F;
    }

    private static Rectangle NumericMouth(Rectangle area, string type)
    {
        if (!int.TryParse(type, out int value)) value = 1;
        int width = Math.Max(12, area.Width * (2 + value % 4) / 5);
        int height = Math.Max(5, area.Height * (1 + value % 3) / 3);
        return new Rectangle(area.Left + (area.Width - width) / 2,
            area.Top + (area.Height - height) / 2, width, height);
    }
}
