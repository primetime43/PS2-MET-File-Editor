using System.Diagnostics;
using System.Globalization;
using PS2_DATA_File_Extractor.FileOperations;

namespace PS2_DATA_File_Extractor;

public sealed class AnimationEditorForm : Form
{
    private readonly RenderWareAnimationArchive _archive;
    private readonly TextBox _search = new() { Dock = DockStyle.Fill, PlaceholderText = "Search ANM files..." };
    private readonly ComboBox _filter = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ListBox _files = new() { Dock = DockStyle.Fill, IntegralHeight = false };
    private readonly Label _fileInfo = new()
    {
        Dock = DockStyle.Top, Height = 50, Padding = new Padding(8, 5, 8, 3), AutoEllipsis = true
    };
    private readonly AnimationPosePreviewControl _posePreview = new() { Dock = DockStyle.Fill };
    private readonly AnimationTimelineControl _timeline = new() { Dock = DockStyle.Fill };
    private readonly DataGridView _tracks = CreateTrackGrid();
    private readonly DataGridView _frames = CreateFrameGrid();
    private readonly NumericUpDown _duration = new()
    {
        DecimalPlaces = 6, Minimum = 0.000001M, Maximum = 3600, Increment = 0.033333M,
        Width = 105, TextAlign = HorizontalAlignment.Right
    };
    private readonly TrackBar _scrubber = new()
    {
        Minimum = 0, Maximum = 10000, TickStyle = TickStyle.None, Width = 300, AutoSize = false, Height = 28
    };
    private readonly Label _position = new() { Width = 145, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Label _activeEvent = new()
    {
        AutoSize = false, Width = 470, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true
    };
    private readonly Label _sample = new()
    {
        Dock = DockStyle.Bottom, Height = 27, Padding = new Padding(8, 4, 8, 2), AutoEllipsis = true
    };
    private readonly Label _status = new()
    {
        Dock = DockStyle.Bottom, Height = 29, Padding = new Padding(12, 5, 12, 2), AutoEllipsis = true
    };
    private readonly Button _play = new() { Text = "Play", AutoSize = true };
    private readonly Button _stop = new() { Text = "Stop", AutoSize = true, Enabled = false };
    private readonly CheckBox _loop = new() { Text = "Loop", AutoSize = true, Margin = new Padding(8, 7, 8, 3) };
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 16 };
    private readonly Stopwatch _clock = new();
    private RenderWareAnimationFile? _current;
    private bool _loading;
    private bool _saved;
    private double _playStart;
    private int _selectedTrack;

    public AnimationEditorForm(
        RenderWareAnimationArchive archive,
        string metPath,
        string? preferredPath = null)
    {
        _archive = archive;
        Text = "Animation Viewer and Timing Editor - DATA.MET";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(1320, 850);
        MinimumSize = new Size(980, 690);
        AutoScaleMode = AutoScaleMode.Dpi;

        Label instructions = new()
        {
            Dock = DockStyle.Top,
            Height = 50,
            Padding = new Padding(12, 7, 12, 3),
            Text = "Preview the animated player model with its original textures and view ANM tracks with matching EVT expressions. " +
                   "Duration, speed, and individual keyframe times can be edited without changing the archive entry size."
        };
        Label path = new()
        {
            Dock = DockStyle.Top, Height = 27, Padding = new Padding(12, 2, 12, 2),
            AutoEllipsis = true, Text = metPath
        };

        SplitContainer split = new()
        {
            Dock = DockStyle.Fill,
            Size = new Size(1280, 690),
            SplitterDistance = 330,
            FixedPanel = FixedPanel.Panel1,
            Panel1MinSize = 240,
            Panel2MinSize = 600
        };
        split.Panel1.Padding = new Padding(8, 4, 6, 6);
        split.Panel2.Padding = new Padding(6, 4, 8, 6);
        split.Panel1.Controls.Add(_files);
        split.Panel1.Controls.Add(BuildFileFilter());
        split.Panel2.Controls.Add(BuildEditorPanel());

        FlowLayoutPanel bottom = new()
        {
            Dock = DockStyle.Bottom, Height = 55, FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(8), WrapContents = false
        };
        Button cancel = new() { Text = "Close", AutoSize = true, DialogResult = DialogResult.Cancel };
        Button save = new() { Text = "Save Animations to DATA.MET", AutoSize = true };
        Button resetAll = new() { Text = "Reset All Unsaved Changes", AutoSize = true };
        save.Click += Save_Click;
        resetAll.Click += (_, _) => ResetAll();
        bottom.Controls.AddRange(new Control[] { cancel, save, resetAll });

        Controls.Add(split);
        Controls.Add(_status);
        Controls.Add(bottom);
        Controls.Add(path);
        Controls.Add(instructions);
        CancelButton = cancel;

        _filter.Items.AddRange(new object[]
        {
            "All animations", "Standard (scheme 1)", "Compressed (scheme 2)", "Paired with EVT"
        });
        _filter.SelectedIndex = 0;
        _search.TextChanged += (_, _) => RefreshFileList();
        _filter.SelectedIndexChanged += (_, _) => RefreshFileList();
        _files.SelectedIndexChanged += (_, _) => SelectedFileChanged();
        _tracks.SelectionChanged += (_, _) => TrackSelectionChanged();
        _frames.CellEndEdit += FrameCellEndEdit;
        _frames.DataError += (_, _) => { };
        _timeline.SeekRequested += (_, seconds) => SetPosition(seconds);
        _scrubber.Scroll += (_, _) => SetPosition(ScrubberToTime());
        _play.Click += (_, _) => StartPlayback();
        _stop.Click += (_, _) => StopPlayback();
        _timer.Tick += (_, _) => PlaybackTick();
        FormClosing += Editor_FormClosing;

        RefreshFileList();
        SelectPreferredFile(preferredPath);
        _status.Text = $"Loaded {_archive.Files.Count:N0} ANM files; {_archive.PairedEventCount:N0} have matching EVT timelines.";
    }

    private Control BuildFileFilter()
    {
        TableLayoutPanel panel = new()
        {
            Dock = DockStyle.Top, Height = 82, ColumnCount = 1, RowCount = 2,
            Padding = new Padding(0, 0, 0, 6)
        };
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        panel.Controls.Add(_search, 0, 0);
        panel.Controls.Add(_filter, 0, 1);
        return panel;
    }

    private Control BuildEditorPanel()
    {
        Panel panel = new() { Dock = DockStyle.Fill };
        TabControl tabs = new() { Dock = DockStyle.Fill };
        TabPage trackPage = new("Track Summary") { Padding = new Padding(4) };
        TabPage framePage = new("Keyframes (edit Time)") { Padding = new Padding(4) };
        trackPage.Controls.Add(_tracks);
        framePage.Controls.Add(_frames);
        tabs.TabPages.AddRange(new[] { trackPage, framePage });

        FlowLayoutPanel timing = new()
        {
            Dock = DockStyle.Top, Height = 40, FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(2, 4, 2, 2), WrapContents = false
        };
        Button applyDuration = new() { Text = "Apply Duration", AutoSize = true };
        Button halfSpeed = new() { Text = "Half Speed (2x time)", AutoSize = true };
        Button doubleSpeed = new() { Text = "Double Speed (half time)", AutoSize = true };
        Button replaceAnimation = new() { Text = "Replace from Another ANM...", AutoSize = true };
        Button reset = new() { Text = "Reset This ANM", AutoSize = true };
        applyDuration.Click += (_, _) => ApplyDuration((float)_duration.Value);
        halfSpeed.Click += (_, _) => ScaleDuration(2F);
        doubleSpeed.Click += (_, _) => ScaleDuration(0.5F);
        replaceAnimation.Click += (_, _) => ReplaceCurrentAnimation();
        reset.Click += (_, _) => ResetCurrent();
        timing.Controls.AddRange(new Control[]
        {
            new Label { Text = "Duration (seconds):", AutoSize = true, Margin = new Padding(4, 8, 3, 3) },
            _duration, applyDuration, halfSpeed, doubleSpeed, replaceAnimation, reset
        });

        FlowLayoutPanel transport = new()
        {
            Dock = DockStyle.Top, Height = 39, FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(2, 3, 2, 2), WrapContents = false
        };
        transport.Controls.AddRange(new Control[]
        {
            _play, _stop, _loop, _scrubber, _position, _activeEvent
        });

        panel.Controls.Add(tabs);
        panel.Controls.Add(_sample);
        panel.Controls.Add(timing);
        panel.Controls.Add(transport);
        panel.Controls.Add(BuildPreviewPanel());
        panel.Controls.Add(_fileInfo);
        return panel;
    }

    private Control BuildPreviewPanel()
    {
        SplitContainer previews = new()
        {
            Dock = DockStyle.Top,
            Size = new Size(950, 285),
            Height = 285,
            SplitterDistance = 500,
            Panel1MinSize = 280,
            Panel2MinSize = 280
        };
        previews.Panel1.Padding = new Padding(0, 0, 3, 0);
        previews.Panel2.Padding = new Padding(3, 0, 0, 0);
        previews.Panel1.Controls.Add(_posePreview);
        previews.Panel2.Controls.Add(_timeline);
        return previews;
    }

    private static DataGridView CreateTrackGrid()
    {
        DataGridView grid = CreateBaseGrid();
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Track", Width = 80 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Keyframes", Width = 90 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Start", Width = 100 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "End", Width = 100 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Span", Width = 100 });
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Frame indices", ReadOnly = true,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, MinimumWidth = 180
        });
        foreach (DataGridViewColumn column in grid.Columns) column.ReadOnly = true;
        return grid;
    }

    private static DataGridView CreateFrameGrid()
    {
        DataGridView grid = CreateBaseGrid();
        string[] headers =
        {
            "Index", "Track", "Time", "Previous", "Quat X", "Quat Y", "Quat Z", "Quat W",
            "Translate X", "Translate Y", "Translate Z"
        };
        int[] widths = { 60, 60, 90, 75, 85, 85, 85, 85, 90, 90, 90 };
        for (int index = 0; index < headers.Length; index++)
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = headers[index], Width = widths[index],
                ReadOnly = index != 2
            });
        grid.Columns[^1].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        grid.Columns[^1].MinimumWidth = 90;
        return grid;
    }

    private static DataGridView CreateBaseGrid() => new()
    {
        Dock = DockStyle.Fill,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        AllowUserToResizeRows = false,
        RowHeadersVisible = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        MultiSelect = false,
        BackgroundColor = SystemColors.Window,
        EditMode = DataGridViewEditMode.EditOnEnter,
        AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Color.FromArgb(246, 248, 250)
        }
    };

    private void RefreshFileList()
    {
        if (_loading) return;
        string? selected = _current?.SourcePath;
        string search = _search.Text.Trim();
        IEnumerable<RenderWareAnimationFile> files = _archive.Files;
        files = _filter.SelectedIndex switch
        {
            1 => files.Where(file => file.SchemeId == RenderWareAnimationFile.StandardScheme),
            2 => files.Where(file => file.SchemeId == RenderWareAnimationFile.CompressedScheme),
            3 => files.Where(file => file.PairedEvent != null),
            _ => files
        };
        if (search.Length > 0)
            files = files.Where(file => file.SourcePath.Contains(search, StringComparison.OrdinalIgnoreCase));

        _loading = true;
        _files.BeginUpdate();
        _files.Items.Clear();
        foreach (RenderWareAnimationFile file in files)
            _files.Items.Add(new AnimationListItem(file));
        _files.EndUpdate();
        int index = selected == null ? -1 : FindListIndex(selected);
        _files.SelectedIndex = index >= 0 ? index : (_files.Items.Count > 0 ? 0 : -1);
        _loading = false;
        SelectedFileChanged();
    }

    private void SelectPreferredFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        int index = FindListIndex(path);
        if (index >= 0) _files.SelectedIndex = index;
    }

    private int FindListIndex(string path)
    {
        for (int index = 0; index < _files.Items.Count; index++)
            if (_files.Items[index] is AnimationListItem item &&
                item.File.SourcePath.Equals(path, StringComparison.OrdinalIgnoreCase)) return index;
        return -1;
    }

    private void SelectedFileChanged()
    {
        if (_loading) return;
        StopPlayback();
        _current = (_files.SelectedItem as AnimationListItem)?.File;
        try
        {
            LoadCurrent();
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Unable to Parse Animation",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void LoadCurrent(int selectedFrame = -1)
    {
        _loading = true;
        _tracks.Rows.Clear();
        _frames.Rows.Clear();
        _timeline.Animation = _current;
        _posePreview.Animation = _current;
        _posePreview.Binding = null;
        _posePreview.Model = null;
        _posePreview.FacialEvent = null;
        _selectedTrack = 0;
        _posePreview.SelectedTrack = 0;
        if (_current == null)
        {
            _fileInfo.Text = "No animation selected.";
            _duration.Value = _duration.Minimum;
        }
        else
        {
            RenderWareAnimationBinding? binding = _archive.ResolveSkeleton(_current);
            _posePreview.Binding = binding;
            RenderWareSkinnedModel? model = binding == null ? null : _archive.LoadModel(binding);
            _posePreview.Model = model;
            _posePreview.FacialEvent = _current.PairedEvent;
            _fileInfo.Text = $"{_current.SourcePath}   |   {_current.SchemeName} scheme   |   " +
                             $"{_current.DurationSeconds:0.######} sec   |   {_current.FrameCount:N0} keyframes   |   " +
                             $"{_current.TrackCount:N0} tracks" +
                             (_current.PairedEvent == null ? "   |   no matching EVT" :
                                 $"   |   EVT: {_current.PairedEvent.SourcePath}") +
                              (binding == null ? "   |   no compatible DFF model" :
                                  $"   |   Model: {binding.ModelPath}") +
                              (binding != null && model == null ? " (skeleton preview only)" :
                               model == null ? string.Empty :
                                  $" ({model.VertexCount:N0} vertices, {model.Textures.Count:N0} textures)");
            _duration.Value = Math.Clamp((decimal)_current.DurationSeconds, _duration.Minimum, _duration.Maximum);
            foreach (RenderWareAnimationTrack track in _current.Tracks)
                _tracks.Rows.Add(track.Index, track.FrameIndices.Count,
                    Format(track.StartTime), Format(track.EndTime), Format(track.EndTime - track.StartTime),
                    string.Join(", ", track.FrameIndices));
            foreach (RenderWareAnimationKeyFrame frame in _current.Frames)
            {
                RenderWareAnimationTransform t = frame.Transform;
                _frames.Rows.Add(frame.Index, frame.TrackIndex, Format(frame.TimeSeconds),
                    frame.PreviousFrameIndex?.ToString(CultureInfo.InvariantCulture) ?? "root",
                    Format(t.QuaternionX), Format(t.QuaternionY), Format(t.QuaternionZ), Format(t.QuaternionW),
                    Format(t.TranslationX), Format(t.TranslationY), Format(t.TranslationZ));
            }
        }
        _loading = false;
        if (_tracks.Rows.Count > 0)
        {
            _tracks.ClearSelection();
            _tracks.Rows[0].Selected = true;
        }
        if (selectedFrame >= 0 && selectedFrame < _frames.Rows.Count)
        {
            _frames.ClearSelection();
            _frames.Rows[selectedFrame].Selected = true;
            _frames.CurrentCell = _frames.Rows[selectedFrame].Cells[2];
        }
        SetPosition(0);
        UpdateStatus();
    }

    private void TrackSelectionChanged()
    {
        if (_loading || _tracks.CurrentRow == null) return;
        _selectedTrack = _tracks.CurrentRow.Index;
        _posePreview.SelectedTrack = _selectedTrack;
        _posePreview.Invalidate();
        UpdatePositionDetails();
    }

    private void FrameCellEndEdit(object? sender, DataGridViewCellEventArgs e)
    {
        if (_loading || _current == null || e.RowIndex < 0 || e.ColumnIndex != 2) return;
        string text = Convert.ToString(_frames.Rows[e.RowIndex].Cells[2].Value) ?? string.Empty;
        if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
        {
            ShowTimingError("Keyframe time must be a number.", e.RowIndex);
            return;
        }
        try
        {
            _current.SetKeyFrameTime(e.RowIndex, value);
            LoadCurrent(e.RowIndex);
        }
        catch (Exception exception)
        {
            ShowTimingError(exception.Message, e.RowIndex);
        }
    }

    private void ShowTimingError(string message, int frameIndex)
    {
        MessageBox.Show(this, message, "Invalid Keyframe Time",
            MessageBoxButtons.OK, MessageBoxIcon.Warning);
        LoadCurrent(frameIndex);
    }

    private void ScaleDuration(float factor)
    {
        if (_current == null) return;
        ApplyDuration(_current.DurationSeconds * factor);
    }

    private void ApplyDuration(float value)
    {
        if (_current == null) return;
        try
        {
            _current.ScaleToDuration(value);
            LoadCurrent();
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Invalid Animation Duration",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void SetPosition(double seconds)
    {
        if (_current == null) return;
        seconds = Math.Clamp(seconds, 0, _current.DurationSeconds);
        _timeline.PositionSeconds = seconds;
        _posePreview.PositionSeconds = seconds;
        _scrubber.Value = TimeToScrubber(seconds);
        UpdatePositionDetails();
    }

    private void UpdatePositionDetails()
    {
        if (_current == null)
        {
            _position.Text = string.Empty;
            _activeEvent.Text = string.Empty;
            _sample.Text = string.Empty;
            return;
        }
        double seconds = _timeline.PositionSeconds;
        _position.Text = $"{seconds:0.000} / {_current.DurationSeconds:0.000} sec";
        FacialEventFile? evt = _current.PairedEvent;
        if (evt == null)
            _activeEvent.Text = "EVT: no matching expression timeline";
        else
        {
            string expression = string.Join("  |  ", evt.EventClasses.Select(eventClass =>
            {
                FacialEvent? item = evt.GetActiveEvent(eventClass, seconds);
                return item == null ? $"{ShortClass(eventClass)}: —" :
                    $"{ShortClass(eventClass)}: {item.EventType} ({item.Value:0.###})";
            }));
            _activeEvent.Text = $"EVT: {expression}";
        }
        if (_current.TrackCount == 0) return;
        int track = Math.Clamp(_selectedTrack, 0, _current.TrackCount - 1);
        RenderWareAnimationTransform transform = _current.SampleTrack(track, (float)seconds);
        _sample.Text = $"Track {track} sampled transform — Translation " +
                       $"({transform.TranslationX:0.###}, {transform.TranslationY:0.###}, {transform.TranslationZ:0.###})   " +
                       $"Quaternion ({transform.QuaternionX:0.###}, {transform.QuaternionY:0.###}, " +
                       $"{transform.QuaternionZ:0.###}, {transform.QuaternionW:0.###})";
    }

    private void StartPlayback()
    {
        if (_current == null) return;
        _playStart = _timeline.PositionSeconds >= _current.DurationSeconds - 0.0001
            ? 0 : _timeline.PositionSeconds;
        _clock.Restart();
        _timer.Start();
        _play.Enabled = false;
        _stop.Enabled = true;
    }

    private void PlaybackTick()
    {
        if (_current == null) return;
        double position = _playStart + _clock.Elapsed.TotalSeconds;
        if (position >= _current.DurationSeconds)
        {
            if (_loop.Checked)
            {
                position %= _current.DurationSeconds;
                _playStart = position;
                _clock.Restart();
            }
            else
            {
                SetPosition(_current.DurationSeconds);
                StopPlayback();
                return;
            }
        }
        SetPosition(position);
    }

    private void StopPlayback()
    {
        _timer.Stop();
        _clock.Reset();
        _play.Enabled = _current != null;
        _stop.Enabled = false;
    }

    private void ReplaceCurrentAnimation()
    {
        if (_current == null) return;
        StopPlayback();
        using AnimationReplacementForm dialog = new(_archive, _current);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        string eventText = dialog.CopyPairedEvent
            ? " The source EVT eye/mouth timeline will also replace the target EVT."
            : " The target EVT timeline will be kept.";
        string timingText = dialog.KeepTargetDuration
            ? $"The source motion will be fitted to the target's {_current.DurationSeconds:0.###}-second duration."
            : $"The target will use the source's {dialog.Source.DurationSeconds:0.###}-second duration.";
        if (MessageBox.Show(this,
                $"Replace the motion in:\n{_current.SourcePath}\n\nWith:\n{dialog.Source.SourcePath}\n\n" +
                timingText + eventText +
                "\n\nThis stages an unsaved DATA.MET change; the original is preserved until you save.",
                "Confirm Animation Replacement", MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) != DialogResult.Yes) return;
        try
        {
            AnimationReplacementResult result = _archive.ReplaceAnimation(
                _current, dialog.Source, dialog.KeepTargetDuration, dialog.CopyPairedEvent);
            RefreshFileList();
            UpdateStatus();
            MessageBox.Show(this,
                $"Staged {Path.GetFileName(result.SourcePath)} in {Path.GetFileName(result.TargetPath)}.\n" +
                $"The target now has {result.FrameCount:N0} keyframes over {result.DurationSeconds:0.###} seconds." +
                (result.EventCopied ? " Its paired EVT was copied too." : string.Empty),
                "Animation Replacement Staged", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Unable to Replace Animation",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ResetCurrent()
    {
        if (_current == null) return;
        bool eventChanged = _current.PairedEvent?.IsChanged == true;
        if ((_current.IsChanged || eventChanged) && MessageBox.Show(this,
                "Discard unsaved timing or replacement changes to this ANM and its paired EVT?", "Reset Animation",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        StopPlayback();
        _current.Reset();
        if (eventChanged) _current.PairedEvent!.Reset();
        LoadCurrent();
        RefreshFileList();
    }

    private void ResetAll()
    {
        if (_archive.ChangedFileCount > 0 && MessageBox.Show(this,
                "Discard every unsaved animation, replacement, and EVT change?", "Reset Animations",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        StopPlayback();
        _archive.ResetAll();
        LoadCurrent();
    }

    private void Save_Click(object? sender, EventArgs e)
    {
        if (_archive.ChangedFileCount == 0)
        {
            MessageBox.Show(this, "There are no animation changes to save.", "Animation Editor",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (MessageBox.Show(this,
                $"Write {_archive.ChangedAnimationCount:N0} changed ANM file(s) and " +
                $"{_archive.ChangedEventCount:N0} changed EVT file(s) to DATA.MET? " +
                "A timestamped backup will be created first.",
                "Save Animation Changes", MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) != DialogResult.Yes) return;
        try
        {
            StopPlayback();
            AnimationSaveResult result = _archive.SaveWithBackup();
            _saved = true;
            string rebuild = result.RebuiltArchive ? " DATA.MET was rebuilt because an entry changed size." : string.Empty;
            MessageBox.Show(this,
                $"Saved {result.ChangedFileCount:N0} archive file(s).{rebuild}\n\nBackup: {result.BackupPath}",
                "Animations Saved", MessageBoxButtons.OK, MessageBoxIcon.Information);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Unable to Save Animations",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void Editor_FormClosing(object? sender, FormClosingEventArgs e)
    {
        StopPlayback();
        if (_saved || _archive.ChangedFileCount == 0) return;
        if (MessageBox.Show(this, "Close and discard all unsaved animation, replacement, and EVT changes?",
                "Unsaved Animation Changes", MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning) == DialogResult.Yes) return;
        e.Cancel = true;
    }

    private void UpdateStatus()
    {
        int changed = _archive.ChangedFileCount;
        _status.Text = changed == 0
            ? $"Loaded {_archive.Files.Count:N0} animations ({_archive.PairedEventCount:N0} paired EVT timelines)."
            : $"{_archive.ChangedAnimationCount:N0} ANM and {_archive.ChangedEventCount:N0} EVT file(s) have unsaved changes.";
    }

    private double ScrubberToTime() => _current == null
        ? 0 : (double)_scrubber.Value / _scrubber.Maximum * _current.DurationSeconds;

    private int TimeToScrubber(double seconds) => _current == null || _current.DurationSeconds <= 0
        ? 0 : Math.Clamp((int)Math.Round(seconds / _current.DurationSeconds * _scrubber.Maximum),
            _scrubber.Minimum, _scrubber.Maximum);

    private static string Format(float value) => value.ToString("0.######", CultureInfo.InvariantCulture);

    private static string ShortClass(string eventClass) =>
        eventClass.StartsWith("CLASS_", StringComparison.OrdinalIgnoreCase) ? eventClass[6..] : eventClass;

    private sealed record AnimationListItem(RenderWareAnimationFile File)
    {
        public override string ToString() =>
            $"{Path.GetFileName(File.SourcePath)}  [{File.DurationSeconds:0.###}s, {File.TrackCount} tracks]" +
            (File.IsChanged ? " *" : string.Empty);
    }
}
