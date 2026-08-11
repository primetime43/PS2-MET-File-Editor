using System.Diagnostics;
using PS2_DATA_File_Extractor.FileOperations;

namespace PS2_DATA_File_Extractor;

public sealed class AnimationReplacementForm : Form
{
    private readonly RenderWareAnimationArchive _archive;
    private readonly RenderWareAnimationFile _target;
    private readonly List<RenderWareAnimationFile> _compatible;
    private readonly TextBox _search = new() { Dock = DockStyle.Top, PlaceholderText = "Search compatible animations..." };
    private readonly ListBox _sources = new() { Dock = DockStyle.Fill, IntegralHeight = false };
    private readonly Label _count = new() { Dock = DockStyle.Bottom, Height = 26, Padding = new Padding(5, 4, 5, 2) };
    private readonly Label _sourceInfo = new() { Dock = DockStyle.Top, Height = 55, Padding = new Padding(8, 5, 8, 3), AutoEllipsis = true };
    private readonly Label _position = new() { AutoSize = false, Width = 235, TextAlign = ContentAlignment.MiddleLeft };
    private readonly AnimationPosePreviewControl _targetPreview = new() { Dock = DockStyle.Fill };
    private readonly AnimationPosePreviewControl _sourcePreview = new() { Dock = DockStyle.Fill };
    private readonly TrackBar _scrubber = new() { Minimum = 0, Maximum = 10000, TickStyle = TickStyle.None, Width = 360, Height = 28, AutoSize = false };
    private readonly Button _play = new() { Text = "Play", AutoSize = true };
    private readonly Button _stop = new() { Text = "Stop", AutoSize = true, Enabled = false };
    private readonly RadioButton _keepDuration = new() { Text = "Fit source animation to the target duration", AutoSize = true, Checked = true };
    private readonly RadioButton _sourceDuration = new() { Text = "Use the source animation's duration", AutoSize = true };
    private readonly CheckBox _copyEvent = new() { Text = "Copy and synchronize the source EVT eye/mouth timeline", AutoSize = true };
    private readonly Label _eventNote = new() { AutoSize = true, ForeColor = SystemColors.GrayText };
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 16 };
    private readonly Stopwatch _clock = new();
    private RenderWareAnimationFile? _source;
    private double _playStart;

    public AnimationReplacementForm(RenderWareAnimationArchive archive, RenderWareAnimationFile target)
    {
        _archive = archive;
        _target = target;
        _compatible = archive.Files
            .Where(file => file.TrackCount == target.TrackCount &&
                           archive.GetReplacementCompatibility(target, file).IsCompatible)
            .ToList();

        Text = "Replace Animation Slot";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(1240, 700);
        MinimumSize = new Size(980, 620);
        AutoScaleMode = AutoScaleMode.Dpi;

        SplitContainer split = new()
        {
            Dock = DockStyle.Fill,
            Size = new Size(1240, 600),
            SplitterDistance = 330,
            FixedPanel = FixedPanel.Panel1,
            Panel1MinSize = 260,
            Panel2MinSize = 620
        };
        split.Panel1.Padding = new Padding(8);
        split.Panel1.Controls.Add(_sources);
        split.Panel1.Controls.Add(_search);
        split.Panel1.Controls.Add(_count);
        split.Panel2.Padding = new Padding(6, 8, 8, 5);
        split.Panel2.Controls.Add(BuildRightPanel());

        FlowLayoutPanel buttons = new()
        {
            Dock = DockStyle.Bottom, Height = 54, FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(8), WrapContents = false
        };
        Button cancel = new() { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        Button replace = new() { Text = "Use This Animation", AutoSize = true };
        replace.Click += Replace_Click;
        buttons.Controls.AddRange(new Control[] { cancel, replace });

        Label heading = new()
        {
            Dock = DockStyle.Top, Height = 45, Padding = new Padding(12, 7, 12, 3),
            Text = $"Replace the motion stored in {target.SourcePath}. Only animations with the same verified HAnim bone layout are listed."
        };
        Controls.Add(split);
        Controls.Add(buttons);
        Controls.Add(heading);
        CancelButton = cancel;
        AcceptButton = replace;

        ConfigurePreview(_targetPreview, target);
        _search.TextChanged += (_, _) => RefreshSources();
        _sources.SelectedIndexChanged += (_, _) => SourceChanged();
        _scrubber.Scroll += (_, _) => SetNormalized((double)_scrubber.Value / _scrubber.Maximum);
        _play.Click += (_, _) => StartPlayback();
        _stop.Click += (_, _) => StopPlayback();
        _timer.Tick += (_, _) => PlaybackTick();
        RefreshSources();
    }

    public RenderWareAnimationFile Source => _source
        ?? throw new InvalidOperationException("No source animation was selected.");
    public bool KeepTargetDuration => _keepDuration.Checked;
    public bool CopyPairedEvent => _copyEvent.Checked;

    private Control BuildRightPanel()
    {
        Panel panel = new() { Dock = DockStyle.Fill };
        TableLayoutPanel previews = new()
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Padding = new Padding(0, 3, 0, 3)
        };
        previews.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        previews.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        previews.Controls.Add(WrapPreview("Target slot", _targetPreview), 0, 0);
        previews.Controls.Add(WrapPreview("Source animation", _sourcePreview), 1, 0);

        FlowLayoutPanel transport = new()
        {
            Dock = DockStyle.Bottom, Height = 42, Padding = new Padding(3, 5, 3, 2), WrapContents = false
        };
        transport.Controls.AddRange(new Control[] { _play, _stop, _scrubber, _position });

        FlowLayoutPanel options = new()
        {
            Dock = DockStyle.Bottom, Height = 92, FlowDirection = FlowDirection.TopDown,
            Padding = new Padding(7, 4, 7, 2), WrapContents = false
        };
        options.Controls.AddRange(new Control[] { _keepDuration, _sourceDuration, _copyEvent, _eventNote });

        panel.Controls.Add(previews);
        panel.Controls.Add(transport);
        panel.Controls.Add(options);
        panel.Controls.Add(_sourceInfo);
        return panel;
    }

    private static Control WrapPreview(string title, Control preview)
    {
        GroupBox box = new() { Text = title, Dock = DockStyle.Fill, Padding = new Padding(6) };
        box.Controls.Add(preview);
        return box;
    }

    private void RefreshSources()
    {
        string selectedPath = _source?.SourcePath ?? string.Empty;
        string search = _search.Text.Trim();
        IEnumerable<RenderWareAnimationFile> files = _compatible;
        if (search.Length > 0)
            files = files.Where(file => file.SourcePath.Contains(search, StringComparison.OrdinalIgnoreCase));
        List<RenderWareAnimationFile> visible = files.ToList();
        _sources.BeginUpdate();
        _sources.Items.Clear();
        foreach (RenderWareAnimationFile file in visible) _sources.Items.Add(new SourceItem(file));
        _sources.EndUpdate();
        _count.Text = $"{visible.Count:N0} compatible animation(s)";
        int selected = visible.FindIndex(file =>
            file.SourcePath.Equals(selectedPath, StringComparison.OrdinalIgnoreCase));
        _sources.SelectedIndex = selected >= 0 ? selected : (visible.Count > 0 ? 0 : -1);
    }

    private void SourceChanged()
    {
        StopPlayback();
        _source = (_sources.SelectedItem as SourceItem)?.File;
        _sourcePreview.Animation = _source;
        _sourcePreview.Binding = null;
        _sourcePreview.Model = null;
        _sourcePreview.FacialEvent = null;
        if (_source == null)
        {
            _sourceInfo.Text = "No compatible source selected.";
            _copyEvent.Enabled = false;
            SetNormalized(0);
            return;
        }
        ConfigurePreview(_sourcePreview, _source);
        AnimationReplacementCompatibility compatibility =
            _archive.GetReplacementCompatibility(_target, _source);
        _sourceInfo.Text = $"{_source.SourcePath}   |   {_source.DurationSeconds:0.######} sec   |   " +
                           $"{_source.FrameCount:N0} keyframes   |   {compatibility.Message}";
        _copyEvent.Enabled = compatibility.CanCopyPairedEvent;
        if (!_copyEvent.Enabled) _copyEvent.Checked = false;
        _eventNote.Text = compatibility.CanCopyPairedEvent
            ? "Both slots have compatible EVT definitions."
            : "EVT copying is unavailable for this pair; the target EVT will remain unchanged.";
        SetNormalized(0);
    }

    private void ConfigurePreview(AnimationPosePreviewControl preview, RenderWareAnimationFile file)
    {
        RenderWareAnimationBinding? binding = _archive.ResolveSkeleton(file);
        preview.Animation = file;
        preview.Binding = binding;
        preview.Model = binding == null ? null : _archive.LoadModel(binding);
        preview.FacialEvent = file.PairedEvent;
        preview.SelectedTrack = 0;
    }

    private void SetNormalized(double normalized)
    {
        normalized = Math.Clamp(normalized, 0, 1);
        _scrubber.Value = Math.Clamp((int)Math.Round(normalized * _scrubber.Maximum),
            _scrubber.Minimum, _scrubber.Maximum);
        _targetPreview.PositionSeconds = normalized * _target.DurationSeconds;
        _sourcePreview.PositionSeconds = normalized * (_source?.DurationSeconds ?? 0);
        _position.Text = _source == null
            ? $"Target {_targetPreview.PositionSeconds:0.000}s"
            : $"Target {_targetPreview.PositionSeconds:0.000}s  |  Source {_sourcePreview.PositionSeconds:0.000}s";
    }

    private void StartPlayback()
    {
        if (_source == null) return;
        double normalized = (double)_scrubber.Value / _scrubber.Maximum;
        _playStart = normalized * PlaybackDuration();
        _clock.Restart();
        _timer.Start();
        _play.Enabled = false;
        _stop.Enabled = true;
    }

    private void PlaybackTick()
    {
        double duration = PlaybackDuration();
        double elapsed = _playStart + _clock.Elapsed.TotalSeconds;
        if (elapsed >= duration)
        {
            SetNormalized(1);
            StopPlayback();
            return;
        }
        SetNormalized(elapsed / duration);
    }

    private double PlaybackDuration() => Math.Max(_target.DurationSeconds, _source?.DurationSeconds ?? 0.001);

    private void StopPlayback()
    {
        _timer.Stop();
        _clock.Reset();
        _play.Enabled = _source != null;
        _stop.Enabled = false;
    }

    private void Replace_Click(object? sender, EventArgs e)
    {
        if (_source == null)
        {
            MessageBox.Show(this, "Select a compatible source animation first.", "Replace Animation",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        DialogResult = DialogResult.OK;
        Close();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        StopPlayback();
        _timer.Dispose();
        base.OnFormClosed(e);
    }

    private sealed record SourceItem(RenderWareAnimationFile File)
    {
        public override string ToString() =>
            $"{File.SourcePath}  [{File.DurationSeconds:0.###}s, {File.FrameCount:N0} frames]";
    }
}
