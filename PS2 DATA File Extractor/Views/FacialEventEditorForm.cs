using System.Diagnostics;
using System.Globalization;
using System.Media;
using PS2_DATA_File_Extractor.FileOperations;

namespace PS2_DATA_File_Extractor;

public sealed class FacialEventEditorForm : Form
{
    private readonly FacialEventArchive _archive;
    private readonly RenderWareAnimationArchive? _animationArchive;
    private readonly PlayerStatsArchive? _playerStats;
    private readonly string _metPath;
    private readonly TextBox _search = new() { Dock = DockStyle.Fill, PlaceholderText = "Search EVT files..." };
    private readonly ComboBox _filter = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ListBox _files = new() { Dock = DockStyle.Fill, IntegralHeight = false };
    private readonly Label _fileInfo = new()
    {
        Dock = DockStyle.Top,
        Height = 48,
        Padding = new Padding(8, 5, 8, 3),
        AutoEllipsis = true
    };
    private readonly FacialEventPreviewControl _preview = new() { Dock = DockStyle.Fill };
    private readonly AnimationPosePreviewControl _modelPreview = new()
    {
        Dock = DockStyle.Fill,
        UpperBodyFraming = true
    };
    private readonly TableLayoutPanel _previewLayout = new()
    {
        Dock = DockStyle.Top,
        Height = 250,
        ColumnCount = 2,
        RowCount = 1,
        Margin = Padding.Empty,
        Padding = Padding.Empty
    };
    private readonly DataGridView _grid = CreateGrid();
    private readonly Label _position = new()
    {
        AutoSize = false,
        Width = 190,
        TextAlign = ContentAlignment.MiddleLeft,
        Margin = new Padding(8, 6, 8, 3)
    };
    private readonly Label _status = new()
    {
        Dock = DockStyle.Bottom,
        Height = 29,
        Padding = new Padding(12, 5, 12, 2)
    };
    private readonly Button _play = new() { Text = "Play Preview", AutoSize = true };
    private readonly Button _stop = new() { Text = "Stop", AutoSize = true, Enabled = false };
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 30 };
    private readonly Stopwatch _clock = new();
    private readonly Dictionary<string, Ps2AudioInfo?> _audioInfo = new(StringComparer.OrdinalIgnoreCase);
    private SoundPlayer? _soundPlayer;
    private MemoryStream? _soundStream;
    private FacialEventFile? _current;
    private bool _loading;
    private bool _gridDirty;
    private bool _saved;
    private double _playDuration;
    private RenderWareAnimationFile? _previewAnimation;

    public FacialEventEditorForm(
        FacialEventArchive archive,
        string metPath,
        string? preferredPath = null,
        RenderWareAnimationArchive? animationArchive = null)
    {
        _archive = archive;
        _animationArchive = animationArchive;
        _playerStats = LoadPlayerStatsForPreview(animationArchive, metPath);
        _metPath = metPath;
        Text = "Facial Event and Lip-Sync Editor - DATA.MET";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(1220, 790);
        MinimumSize = new Size(920, 650);
        AutoScaleMode = AutoScaleMode.Dpi;

        Label instructions = new()
        {
            Dock = DockStyle.Top,
            Height = 55,
            Padding = new Padding(12, 8, 12, 4),
            Text = "Edit EVT facial-animation timelines. The preview uses the character's textured 3D model when " +
                   "a matching DFF/ANM pair exists, with its real eye and mouth textures driven by the event " +
                   "timestamps. Talkie files also play their matching VAG voice clip."
        };
        Label path = new()
        {
            Dock = DockStyle.Top,
            Height = 28,
            Padding = new Padding(12, 2, 12, 3),
            AutoEllipsis = true,
            Text = metPath
        };

        SplitContainer split = new()
        {
            Dock = DockStyle.Fill,
            Size = new Size(1180, 620),
            SplitterDistance = 315,
            FixedPanel = FixedPanel.Panel1,
            Panel1MinSize = 235,
            Panel2MinSize = 560
        };
        split.Panel1.Padding = new Padding(8, 4, 6, 6);
        split.Panel2.Padding = new Padding(6, 4, 8, 6);
        split.Panel1.Controls.Add(_files);
        split.Panel1.Controls.Add(BuildFileFilter());
        split.Panel2.Controls.Add(BuildEditorPanel());

        FlowLayoutPanel bottomButtons = new()
        {
            Dock = DockStyle.Bottom,
            Height = 55,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(8),
            WrapContents = false
        };
        Button cancel = new() { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        Button save = new() { Text = "Save EVT Files to DATA.MET", AutoSize = true };
        Button resetAll = new() { Text = "Reset All Unsaved Changes", AutoSize = true };
        save.Click += Save_Click;
        resetAll.Click += (_, _) => ResetAll();
        bottomButtons.Controls.AddRange(new Control[] { cancel, save, resetAll });

        Controls.Add(split);
        Controls.Add(_status);
        Controls.Add(bottomButtons);
        Controls.Add(path);
        Controls.Add(instructions);
        AcceptButton = save;
        CancelButton = cancel;

        _filter.Items.AddRange(new object[]
        {
            "All EVT files", "Talkie lip sync", "Batting/animation face events"
        });
        _filter.SelectedIndex = 0;
        _search.TextChanged += (_, _) => RefreshFileList();
        _filter.SelectedIndexChanged += (_, _) => RefreshFileList();
        _files.SelectedIndexChanged += (_, _) => SelectedFileChanged();
        _grid.CellValueChanged += Grid_CellValueChanged;
        _grid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_grid.IsCurrentCellDirty)
                _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        _grid.SelectionChanged += (_, _) => ShowSelectedEvent();
        _grid.DataError += (_, _) => { };
        _timer.Tick += (_, _) => PlaybackTick();
        _play.Click += (_, _) => StartPlayback();
        _stop.Click += (_, _) => StopPlayback(resetPosition: true);
        FormClosing += Editor_FormClosing;

        RefreshFileList();
        SelectPreferredFile(preferredPath);
        _status.Text = $"Loaded {_archive.Files.Count:N0} EVT files.";
    }

    private Control BuildFileFilter()
    {
        TableLayoutPanel panel = new()
        {
            Dock = DockStyle.Top,
            Height = 82,
            ColumnCount = 1,
            RowCount = 2,
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
        _previewLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 44));
        _previewLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 56));
        _previewLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _previewLayout.Controls.Add(_modelPreview, 0, 0);
        _previewLayout.Controls.Add(_preview, 1, 0);
        FlowLayoutPanel eventButtons = new()
        {
            Dock = DockStyle.Bottom,
            Height = 42,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(0, 5, 0, 3),
            WrapContents = false
        };
        Button add = new() { Text = "Add Event", AutoSize = true };
        Button delete = new() { Text = "Delete Event", AutoSize = true };
        Button reset = new() { Text = "Reset This EVT", AutoSize = true };
        add.Click += (_, _) => AddEvent();
        delete.Click += (_, _) => DeleteEvent();
        reset.Click += (_, _) => ResetCurrent();
        eventButtons.Controls.AddRange(new Control[] { add, delete, reset });

        FlowLayoutPanel transport = new()
        {
            Dock = DockStyle.Top,
            Height = 42,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(0, 5, 0, 3),
            WrapContents = false
        };
        transport.Controls.AddRange(new Control[] { _play, _stop, _position });

        panel.Controls.Add(_grid);
        panel.Controls.Add(eventButtons);
        panel.Controls.Add(transport);
        panel.Controls.Add(_previewLayout);
        panel.Controls.Add(_fileInfo);
        return panel;
    }

    private static DataGridView CreateGrid()
    {
        DataGridView grid = new()
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            BackgroundColor = SystemColors.Window,
            EditMode = DataGridViewEditMode.EditOnEnter,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = Color.FromArgb(246, 248, 250)
            }
        };
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Time (seconds)", Width = 105 });
        grid.Columns.Add(new DataGridViewComboBoxColumn
        {
            HeaderText = "Class", Width = 145, FlatStyle = FlatStyle.Flat,
            DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton
        });
        grid.Columns.Add(new DataGridViewComboBoxColumn
        {
            HeaderText = "Mouth / pose", Width = 125, FlatStyle = FlatStyle.Flat,
            DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton
        });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Value", Width = 90 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Element ID", Width = 85 });
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Preview meaning", ReadOnly = true,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, MinimumWidth = 150
        });
        return grid;
    }

    private void RefreshFileList()
    {
        if (_loading) return;
        if (!TryCommitGrid()) return;
        string? selectedPath = _current?.SourcePath;
        string search = _search.Text.Trim();
        IEnumerable<FacialEventFile> files = _archive.Files;
        files = _filter.SelectedIndex switch
        {
            1 => files.Where(file => file.IsTalkie),
            2 => files.Where(file => !file.IsTalkie),
            _ => files
        };
        if (search.Length > 0)
            files = files.Where(file => file.SourcePath.Contains(search, StringComparison.OrdinalIgnoreCase));

        _loading = true;
        _files.BeginUpdate();
        _files.Items.Clear();
        foreach (FacialEventFile file in files)
            _files.Items.Add(new FacialEventListItem(file));
        _files.EndUpdate();
        int index = selectedPath == null ? -1 : FindListIndex(selectedPath);
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
        {
            if (_files.Items[index] is FacialEventListItem item &&
                item.File.SourcePath.Equals(path, StringComparison.OrdinalIgnoreCase)) return index;
        }
        return -1;
    }

    private void SelectedFileChanged()
    {
        if (_loading) return;
        if (!TryCommitGrid())
        {
            _loading = true;
            _files.SelectedIndex = _current == null ? -1 : FindListIndex(_current.SourcePath);
            _loading = false;
            return;
        }

        StopPlayback(resetPosition: true);
        _current = (_files.SelectedItem as FacialEventListItem)?.File;
        LoadGrid();
    }

    private void LoadGrid(int selectedIndex = 0)
    {
        _loading = true;
        _grid.Rows.Clear();
        DataGridViewComboBoxColumn classColumn = (DataGridViewComboBoxColumn)_grid.Columns[1];
        DataGridViewComboBoxColumn typeColumn = (DataGridViewComboBoxColumn)_grid.Columns[2];
        classColumn.Items.Clear();
        typeColumn.Items.Clear();
        if (_current != null)
        {
            classColumn.Items.AddRange(_current.EventClasses.Cast<object>().ToArray());
            string[] types = _current.ClassDefinitions.Values.SelectMany(value => value)
                .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => NumericSort(value)).ToArray();
            typeColumn.Items.AddRange(types.Cast<object>().ToArray());
            foreach (FacialEvent item in _current.Events)
            {
                _grid.Rows.Add(
                    item.Timestamp.ToString(
                        _current.IsTalkie ? "0.00#" : "0.#######",
                        CultureInfo.InvariantCulture),
                    item.EventClass,
                    item.EventType,
                    item.Value.ToString("0.0###", CultureInfo.InvariantCulture),
                    item.ElementId.ToString(CultureInfo.InvariantCulture),
                    EventMeaning(item));
            }
        }
        _gridDirty = false;
        _loading = false;
        if (_grid.Rows.Count > 0)
        {
            int index = Math.Clamp(selectedIndex, 0, _grid.Rows.Count - 1);
            _grid.ClearSelection();
            _grid.Rows[index].Selected = true;
            _grid.CurrentCell = _grid.Rows[index].Cells[0];
        }
        UpdateFileInfo();
        ShowSelectedEvent();
        UpdateStatus();
    }

    private void Grid_CellValueChanged(object? sender, DataGridViewCellEventArgs e)
    {
        if (_loading || e.RowIndex < 0) return;
        if (e.ColumnIndex == 1 && _current != null)
        {
            string eventClass = Convert.ToString(_grid.Rows[e.RowIndex].Cells[1].Value) ?? string.Empty;
            IReadOnlyList<string> valid = _current.GetEventTypes(eventClass);
            string type = Convert.ToString(_grid.Rows[e.RowIndex].Cells[2].Value) ?? string.Empty;
            if (valid.Count > 0 && !valid.Contains(type, StringComparer.OrdinalIgnoreCase))
                _grid.Rows[e.RowIndex].Cells[2].Value = PreferredType(valid);
        }
        _gridDirty = true;
        UpdateStatus();
        ShowSelectedEvent();
    }

    private bool TryCommitGrid()
    {
        if (!_gridDirty || _current == null) return true;
        _grid.EndEdit();
        List<FacialEvent> events = new(_grid.Rows.Count);
        for (int index = 0; index < _grid.Rows.Count; index++)
        {
            DataGridViewRow row = _grid.Rows[index];
            if (!TryDouble(row.Cells[0].Value, out double timestamp) || timestamp < 0)
                return GridError(index, 0, "Timestamp must be a non-negative number.");
            string eventClass = Convert.ToString(row.Cells[1].Value) ?? string.Empty;
            string eventType = Convert.ToString(row.Cells[2].Value) ?? string.Empty;
            if (!TryDouble(row.Cells[3].Value, out double value))
                return GridError(index, 3, "Value must be a number.");
            if (!int.TryParse(Convert.ToString(row.Cells[4].Value), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out int elementId) || elementId < 0)
                return GridError(index, 4, "Element ID must be zero or a positive integer.");
            events.Add(new FacialEvent(timestamp, eventClass, eventType, value, elementId));
        }

        try
        {
            _current.ReplaceEvents(events);
            _gridDirty = false;
            return true;
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Invalid EVT Timeline",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
    }

    private bool GridError(int row, int column, string message)
    {
        _grid.CurrentCell = _grid.Rows[row].Cells[column];
        MessageBox.Show(this, $"Event {row + 1}: {message}", "Invalid EVT Value",
            MessageBoxButtons.OK, MessageBoxIcon.Warning);
        return false;
    }

    private void AddEvent()
    {
        if (_current == null || !TryCommitGrid()) return;
        List<FacialEvent> events = _current.Events.ToList();
        int selected = _grid.CurrentRow?.Index ?? events.Count - 1;
        int insert = Math.Clamp(selected + 1, 0, events.Count);
        double timestamp;
        if (events.Count == 0) timestamp = 0;
        else
        {
            FacialEvent selectedEvent = events[Math.Clamp(selected, 0, events.Count - 1)];
            FacialEvent? nextSameClass = events.Skip(insert).FirstOrDefault(item =>
                item.EventClass.Equals(selectedEvent.EventClass, StringComparison.OrdinalIgnoreCase));
            int decimals = _current.IsTalkie ? 3 : 7;
            timestamp = nextSameClass != null && nextSameClass.Timestamp > selectedEvent.Timestamp
                ? Math.Round((selectedEvent.Timestamp + nextSameClass.Timestamp) / 2, decimals)
                : Math.Round(selectedEvent.Timestamp + 0.03, decimals);
        }
        string eventClass = events.Count > 0
            ? events[Math.Clamp(selected, 0, events.Count - 1)].EventClass
            : _current.EventClasses[0];
        IReadOnlyList<string> types = _current.GetEventTypes(eventClass);
        events.Insert(insert, new FacialEvent(timestamp, eventClass, PreferredType(types), 1, 0));
        _current.ReplaceEvents(events);
        LoadGrid(insert);
    }

    private void DeleteEvent()
    {
        if (_current == null || _grid.CurrentRow == null || !TryCommitGrid()) return;
        int index = _grid.CurrentRow.Index;
        List<FacialEvent> events = _current.Events.ToList();
        if (index < 0 || index >= events.Count) return;
        events.RemoveAt(index);
        _current.ReplaceEvents(events);
        LoadGrid(Math.Max(0, index - 1));
    }

    private void ResetCurrent()
    {
        if (_current == null) return;
        if ((_current.IsChanged || _gridDirty) && MessageBox.Show(this,
                "Discard all unsaved changes to this EVT file?", "Reset EVT File",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        StopPlayback(resetPosition: true);
        _current.Reset();
        LoadGrid();
    }

    private void ResetAll()
    {
        if (_archive.ChangedFileCount > 0 || _gridDirty)
        {
            if (MessageBox.Show(this, "Discard every unsaved EVT change?", "Reset EVT Files",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        }
        StopPlayback(resetPosition: true);
        _archive.ResetAll();
        LoadGrid();
    }

    private void StartPlayback()
    {
        if (_current == null || !TryCommitGrid()) return;
        StopPlayback(resetPosition: true);
        try
        {
            Ps2AudioInfo? info = GetAudioInfo(_current);
            _playDuration = Math.Max(_current.DurationSeconds, info?.DurationSeconds ?? 0);
            if (_playDuration <= 0) return;
            if (info != null)
            {
                byte[] wave = _archive.DecodePairedAudio(_current);
                _soundStream = new MemoryStream(wave, writable: false);
                _soundPlayer = new SoundPlayer(_soundStream);
                _soundPlayer.Load();
            }
            _preview.TimelineDuration = _playDuration;
            SetPreviewPosition(0);
            _clock.Restart();
            _timer.Start();
            _soundPlayer?.Play();
            _play.Enabled = false;
            _stop.Enabled = true;
        }
        catch (Exception exception)
        {
            StopPlayback(resetPosition: true);
            MessageBox.Show(this, $"The preview could not be played.\n\n{exception.Message}",
                "Unable to Play EVT Preview", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void PlaybackTick()
    {
        double elapsed = _clock.Elapsed.TotalSeconds;
        SetPreviewPosition(Math.Min(elapsed, _playDuration));
        UpdatePositionLabel();
        SelectEventAtPosition(elapsed);
        if (elapsed >= _playDuration) StopPlayback(resetPosition: false);
    }

    private void StopPlayback(bool resetPosition)
    {
        _timer.Stop();
        _clock.Reset();
        _soundPlayer?.Stop();
        _soundPlayer?.Dispose();
        _soundPlayer = null;
        _soundStream?.Dispose();
        _soundStream = null;
        _play.Enabled = _current != null && _current.Events.Count > 0;
        _stop.Enabled = false;
        if (resetPosition)
        {
            SetPreviewPosition(0);
            UpdatePositionLabel();
        }
    }

    private void SelectEventAtPosition(double position)
    {
        if (_current == null || _grid.Rows.Count == 0) return;
        int index = Enumerable.Range(0, _current.Events.Count)
            .Where(candidate => _current.Events[candidate].Timestamp <= position)
            .OrderByDescending(candidate => _current.Events[candidate].Timestamp)
            .FirstOrDefault();
        if (!_grid.Rows[index].Selected)
        {
            _grid.ClearSelection();
            _grid.Rows[index].Selected = true;
        }
    }

    private void ShowSelectedEvent()
    {
        if (_loading || _current == null || _grid.CurrentRow == null || _timer.Enabled) return;
        if (_gridDirty)
        {
            if (!TryCommitGrid()) return;
            UpdateFileInfo();
            UpdateStatus();
        }
        int index = _grid.CurrentRow.Index;
        if (index >= 0 && index < _current.Events.Count)
            SetPreviewPosition(_current.Events[index].Timestamp);
        UpdatePositionLabel();
    }

    private void UpdateFileInfo()
    {
        _preview.EventFile = _current;
        _preview.TextureSet = null;
        ClearModelPreview();
        if (_current == null)
        {
            _fileInfo.Text = "No EVT file selected.";
            _play.Enabled = false;
            return;
        }
        string? modelText = ConfigureModelPreview();
        Ps2AudioInfo? audio = GetAudioInfo(_current);
        double duration = Math.Max(_current.DurationSeconds, audio?.DurationSeconds ?? 0);
        _preview.TimelineDuration = duration;
        string audioText = audio == null
            ? "No same-name VAG; timeline-only preview"
            : $"Paired VAG: {audio.SampleRate:N0} Hz, {audio.DurationSeconds:0.000} s";
        FacialEventTextureSet? textures = null;
        string textureText;
        try
        {
            textures = _archive.LoadTextureSet(_current);
            textureText = textures == null
                ? (_current.IsTalkie ? "drawn phoneme fallback" : "no matching face textures")
                : $"game textures: {textures.Eyes.Count} eye, {textures.Mouths.Count} mouth poses";
        }
        catch (Exception exception)
        {
            textureText = $"texture preview unavailable: {exception.Message}";
        }
        _preview.TextureSet = textures;
        SetModelPreviewVisible(modelText != null);
        _fileInfo.Text = $"{_current.SourcePath}\n{_current.Kind} — {_current.Events.Count:N0} events, " +
                         $"last event {_current.DurationSeconds:0.000} s — {audioText} — " +
                         $"{modelText ?? textureText}" +
                         (modelText != null && !_current.IsTalkie ? $" — {textureText}" : string.Empty);
        _play.Enabled = _current.Events.Count > 0;
    }

    private string? ConfigureModelPreview()
    {
        if (_current == null || _animationArchive == null) return null;
        IEnumerable<RenderWareAnimationFile> candidates = _current.IsTalkie
            ? FindTalkiePreviewAnimations(_current)
            : _animationArchive.Files.Where(candidate =>
                candidate.PairedEvent?.SourcePath.Equals(
                    _current.SourcePath, StringComparison.OrdinalIgnoreCase) == true);
        foreach (RenderWareAnimationFile animation in candidates)
        {
            RenderWareAnimationBinding? binding = _animationArchive.ResolveSkeleton(animation);
            if (binding == null) continue;
            RenderWareSkinnedModel? model = _animationArchive.LoadModel(binding);
            if (model == null) continue;

            _previewAnimation = animation;
            _modelPreview.Animation = animation;
            _modelPreview.Binding = binding;
            _modelPreview.Model = model;
            _modelPreview.FacialEvent = _current;
            SetPreviewPosition(_preview.PositionSeconds);
            return $"3D model: {Path.GetFileName(binding.ModelPath)}";
        }
        return null;
    }

    private IEnumerable<RenderWareAnimationFile> FindTalkiePreviewAnimations(FacialEventFile file)
    {
        string normalized = file.SourcePath.Replace('\\', '/');
        string[] parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        int talkies = Array.FindIndex(parts, part =>
            part.Equals("talkies", StringComparison.OrdinalIgnoreCase));
        string speaker = talkies >= 0 && talkies + 1 < parts.Length
            ? parts[talkies + 1]
            : string.Empty;
        string? commentatorAnimation = speaker.ToLowerInvariant() switch
        {
            "abner" => "abne_static.anm",
            "sunny" => "sunn_static.anm",
            _ => null
        };
        if (commentatorAnimation != null)
        {
            return _animationArchive!.Files.Where(candidate =>
                Path.GetFileName(candidate.SourcePath).Equals(
                    commentatorAnimation, StringComparison.OrdinalIgnoreCase));
        }

        string? code = ResolveTalkiePlayerCode(speaker);
        if (code == null) return Enumerable.Empty<RenderWareAnimationFile>();
        return _animationArchive!.Files
            .Where(candidate => AnimationPlayerCode(candidate.SourcePath)
                .Equals(code, StringComparison.OrdinalIgnoreCase) && candidate.TrackCount != 5)
            .OrderByDescending(TalkieAnimationScore)
            .ThenBy(candidate => candidate.SourcePath, StringComparer.OrdinalIgnoreCase);
    }

    private string? ResolveTalkiePlayerCode(string speaker)
    {
        if (_playerStats == null || _animationArchive == null) return null;
        string wanted = NormalizeIdentity(speaker);
        HashSet<string> availableCodes = _animationArchive.Files
            .Select(candidate => AnimationPlayerCode(candidate.SourcePath))
            .Where(code => code.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var matches = _playerStats.Players
            .Where(player => !player.IsClone)
            .Select(player => new
            {
                Code = StatsPlayerCode(player),
                Score = TalkieIdentityScore(player, wanted)
            })
            .Where(match => match.Score > 0 && availableCodes.Contains(match.Code))
            .OrderByDescending(match => match.Score)
            .ThenBy(match => match.Code, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return matches.FirstOrDefault()?.Code;
    }

    private static int TalkieIdentityScore(PlayerStatsRecord player, string speaker)
    {
        string code = NormalizeIdentity(StatsPlayerCode(player));
        string first = NormalizeIdentity(player.FirstName);
        string nickname = NormalizeIdentity(player.Nickname);
        string last = NormalizeIdentity(player.LastName);
        if (speaker == code) return 130;
        if (speaker == first) return 120;
        if (speaker == first + last) return 115;
        if (nickname.Length > 0 && speaker == nickname) return 110;
        if (speaker == last) return 100;
        if (first.Length > 0 && speaker.Contains(first, StringComparison.Ordinal)) return 70;
        if (last.Length > 0 && speaker.Contains(last, StringComparison.Ordinal)) return 60;
        return 0;
    }

    private static int TalkieAnimationScore(RenderWareAnimationFile animation)
    {
        string normalized = animation.SourcePath.Replace('\\', '/');
        string[] parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        string category = parts.Length > 1 ? parts[1].ToLowerInvariant() : string.Empty;
        string stem = Path.GetFileNameWithoutExtension(normalized);
        int score = category switch
        {
            "batting" => 500,
            "playercard" => 400,
            "kids" => 300,
            "baserunning" or "fieldanims" or "pitching" => 200,
            _ => 0
        };
        if (stem.Contains("batready", StringComparison.OrdinalIgnoreCase)) score += 80;
        else if (stem.Contains("inambient", StringComparison.OrdinalIgnoreCase)) score += 70;
        else if (stem.Contains("batprep", StringComparison.OrdinalIgnoreCase)) score += 60;
        else if (stem.Contains("walk", StringComparison.OrdinalIgnoreCase)) score += 40;
        return score;
    }

    private static string AnimationPlayerCode(string sourcePath)
    {
        string[] parts = sourcePath.Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 4 ? parts[2] : string.Empty;
    }

    private static string StatsPlayerCode(PlayerStatsRecord player) =>
        Path.GetFileNameWithoutExtension(player.SourceName)
            .Replace("_stats", string.Empty, StringComparison.OrdinalIgnoreCase);

    private static string NormalizeIdentity(string value) =>
        new(value.Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant).ToArray());

    private static PlayerStatsArchive? LoadPlayerStatsForPreview(
        RenderWareAnimationArchive? animationArchive,
        string metPath)
    {
        if (animationArchive == null) return null;
        try
        {
            return PlayerStatsArchive.Load(metPath);
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    private void ClearModelPreview()
    {
        _previewAnimation = null;
        _modelPreview.FacialEvent = null;
        _modelPreview.FacialEventPositionSeconds = null;
        _modelPreview.Model = null;
        _modelPreview.Binding = null;
        _modelPreview.Animation = null;
        SetModelPreviewVisible(false);
    }

    private void SetModelPreviewVisible(bool visible)
    {
        _modelPreview.Visible = visible;
        _preview.ShowFace = !visible;
        if (_previewLayout.ColumnStyles.Count < 2) return;
        _previewLayout.ColumnStyles[0].SizeType = SizeType.Percent;
        _previewLayout.ColumnStyles[0].Width = visible ? 44 : 0;
        _previewLayout.ColumnStyles[1].SizeType = SizeType.Percent;
        _previewLayout.ColumnStyles[1].Width = visible ? 56 : 100;
    }

    private void SetPreviewPosition(double seconds)
    {
        _preview.PositionSeconds = seconds;
        if (_previewAnimation == null) return;
        double motionTime = seconds;
        if (_current?.IsTalkie == true && _previewAnimation.DurationSeconds > 0)
            motionTime %= _previewAnimation.DurationSeconds;
        _modelPreview.PositionSeconds = motionTime;
        _modelPreview.FacialEventPositionSeconds = seconds;
    }

    private Ps2AudioInfo? GetAudioInfo(FacialEventFile file)
    {
        if (_audioInfo.TryGetValue(file.SourcePath, out Ps2AudioInfo? cached)) return cached;
        Ps2AudioInfo? info = file.PairedVagPath == null ? null : _archive.InspectPairedAudio(file);
        _audioInfo[file.SourcePath] = info;
        return info;
    }

    private void UpdatePositionLabel()
    {
        double duration = _preview.TimelineDuration;
        _position.Text = $"{_preview.PositionSeconds:0.000} / {duration:0.000} seconds";
    }

    private void UpdateStatus()
    {
        int changed = _archive.ChangedFileCount + (_gridDirty && _current is { IsChanged: false } ? 1 : 0);
        _status.Text = changed == 0
            ? $"Loaded {_archive.Files.Count:N0} EVT files. No unsaved changes."
            : $"{changed:N0} EVT file{(changed == 1 ? string.Empty : "s")} with unsaved changes.";
    }

    private void Save_Click(object? sender, EventArgs e)
    {
        if (!TryCommitGrid()) return;
        int changed = _archive.ChangedFileCount;
        if (changed == 0)
        {
            MessageBox.Show(this, "No EVT files were changed.", "Nothing to Save",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        List<string> overrun = new();
        foreach (FacialEventFile file in _archive.Files.Where(file => file.IsChanged && file.PairedVagPath != null))
        {
            Ps2AudioInfo? audio = GetAudioInfo(file);
            if (audio != null && file.DurationSeconds > audio.DurationSeconds + 0.05)
                overrun.Add($"{file.SourcePath}: {file.DurationSeconds:0.000} s vs {audio.DurationSeconds:0.000} s audio");
        }
        if (overrun.Count > 0 && MessageBox.Show(this,
                "Some events occur after their paired voice clip ends:\n\n" +
                string.Join("\n", overrun.Take(8)) +
                (overrun.Count > 8 ? $"\n...and {overrun.Count - 8} more" : string.Empty) +
                "\n\nSave them anyway?",
                "Events Exceed Audio Duration", MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning) != DialogResult.Yes) return;

        if (MessageBox.Show(this,
                $"Write {changed:N0} edited EVT file{(changed == 1 ? string.Empty : "s")} to DATA.MET?\n\n" +
                "A timestamped backup will be created first.",
                "Save Facial Events", MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) != DialogResult.Yes) return;

        try
        {
            StopPlayback(resetPosition: false);
            UseWaitCursor = true;
            Enabled = false;
            FacialEventSaveResult result = _archive.SaveWithBackup();
            string rebuilt = result.RebuiltArchive
                ? "\nThe archive was resized with sector alignment preserved."
                : string.Empty;
            MessageBox.Show(this,
                $"Saved {result.ChangedFileCount:N0} EVT file{(result.ChangedFileCount == 1 ? string.Empty : "s")}.\n\n" +
                $"Backup: {result.BackupPath}{rebuilt}",
                "Facial Events Saved", MessageBoxButtons.OK, MessageBoxIcon.Information);
            if (result.BackupPath != null)
                _status.Text = $"Backup: {result.BackupPath}{rebuilt}";
            _saved = true;
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception exception)
        {
            MessageBox.Show(this,
                $"The EVT changes could not be saved. The archive was restored if a backup was created.\n\n{exception.Message}",
                "Unable to Save Facial Events", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Enabled = true;
            UseWaitCursor = false;
        }
    }

    private void Editor_FormClosing(object? sender, FormClosingEventArgs e)
    {
        StopPlayback(resetPosition: false);
        if (_saved) return;
        if (!_gridDirty && _archive.ChangedFileCount == 0) return;
        if (MessageBox.Show(this, "Discard all unsaved EVT changes?", "Unsaved Facial Events",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            e.Cancel = true;
    }

    private static bool TryDouble(object? value, out double result)
    {
        string text = Convert.ToString(value) ?? string.Empty;
        return (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out result) ||
                double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out result)) &&
               double.IsFinite(result);
    }

    private static string PreferredType(IReadOnlyList<string> types) =>
        types.FirstOrDefault(value => value.Equals("STATIC", StringComparison.OrdinalIgnoreCase))
        ?? types.FirstOrDefault(value => !value.Equals("INVALID", StringComparison.OrdinalIgnoreCase))
        ?? types.FirstOrDefault()
        ?? "1";

    private static string NumericSort(string value) =>
        int.TryParse(value, out int number) ? number.ToString("D3") : value;

    private static string EventMeaning(FacialEvent item) => item.EventClass switch
    {
        "CLASS_TALKIES" => item.EventType switch
        {
            "STATIC" => "Neutral / resting mouth",
            "AI" => "Wide open vowel",
            "EE" => "Wide narrow vowel",
            "OH" => "Rounded open vowel",
            "OO" => "Small rounded vowel",
            "CDG" => "Consonant group C/D/G",
            "MM" => "Closed lips M/B/P",
            "FV" => "Teeth and lower lip F/V",
            "ROOT" => "Root / neutral facial state",
            _ => "Talkie mouth shape"
        },
        "CLASS_EYES" => $"Eye animation pose {item.EventType}",
        "CLASS_MOUTH" => $"Mouth animation pose {item.EventType}",
        _ => item.EventClass
    };

    private sealed record FacialEventListItem(FacialEventFile File)
    {
        public override string ToString()
        {
            string path = File.SourcePath.Replace('\\', '/');
            if (path.StartsWith("data/audio/talkies/", StringComparison.OrdinalIgnoreCase))
                return path["data/audio/talkies/".Length..];
            if (path.StartsWith("data/batting/", StringComparison.OrdinalIgnoreCase))
                return path["data/batting/".Length..];
            return path;
        }
    }
}
