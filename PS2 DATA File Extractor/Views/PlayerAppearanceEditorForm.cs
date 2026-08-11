using System.Diagnostics;
using PS2_DATA_File_Extractor.FileOperations;

namespace PS2_DATA_File_Extractor;

public sealed class PlayerAppearanceEditorForm : Form
{
    private static readonly HashSet<string> PlayerModelCategories = new(
        new[] { "batting", "fielding", "baserunning", "playercard", "kids" },
        StringComparer.OrdinalIgnoreCase);

    private readonly RenderWareAnimationArchive _archive;
    private readonly List<PlayerAppearanceItem> _players;
    private readonly TextBox _search = new() { Dock = DockStyle.Top, PlaceholderText = "Search players..." };
    private readonly ListBox _playerList = new() { Dock = DockStyle.Fill, IntegralHeight = false };
    private readonly ComboBox _modelList = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _animationList = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly AnimationPosePreviewControl _preview = new() { Dock = DockStyle.Fill };
    private readonly ListBox _textures = new() { Dock = DockStyle.Fill, IntegralHeight = false };
    private readonly PictureBox _texturePreview = new()
    {
        Dock = DockStyle.Top, Height = 245, SizeMode = PictureBoxSizeMode.Zoom,
        BackColor = Color.FromArgb(44, 48, 54), BorderStyle = BorderStyle.FixedSingle
    };
    private readonly Label _textureInfo = new()
    {
        Dock = DockStyle.Top, Height = 72, Padding = new Padding(5), AutoEllipsis = true
    };
    private readonly TrackBar _scrubber = new()
    {
        Minimum = 0, Maximum = 10000, TickStyle = TickStyle.None,
        Width = 330, Height = 28, AutoSize = false
    };
    private readonly Label _position = new() { Width = 130, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Button _play = new() { Text = "Play", AutoSize = true };
    private readonly Button _stop = new() { Text = "Stop", AutoSize = true, Enabled = false };
    private readonly Button _export = new() { Text = "Export...", AutoSize = true, Enabled = false };
    private readonly Button _replace = new() { Text = "Replace...", AutoSize = true, Enabled = false };
    private readonly Button _resetTexture = new() { Text = "Reset Texture", AutoSize = true, Enabled = false };
    private readonly Label _status = new()
    {
        Dock = DockStyle.Bottom, Height = 29, Padding = new Padding(12, 5, 12, 2), AutoEllipsis = true
    };
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 16 };
    private readonly Stopwatch _clock = new();
    private RenderWareAnimationFile? _animation;
    private RenderWareAnimationBinding? _binding;
    private RenderWareSkinnedModel? _model;
    private TextureItem? _texture;
    private double _playStart;
    private bool _loading;
    private bool _saved;

    public PlayerAppearanceEditorForm(
        RenderWareAnimationArchive archive,
        PlayerStatsArchive playerStats,
        string metPath)
    {
        _archive = archive;
        _players = BuildCatalog(archive, playerStats);
        if (_players.Count == 0)
            throw new InvalidDataException("No textured retail player models could be matched to the player roster.");

        Text = "3D Player Appearance Editor - DATA.MET";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(1420, 840);
        MinimumSize = new Size(1050, 700);
        AutoScaleMode = AutoScaleMode.Dpi;

        Label heading = new()
        {
            Dock = DockStyle.Top, Height = 48, Padding = new Padding(12, 7, 12, 3),
            Text = "Preview each player's original 3D models and textures. Export or replace PNG textures; imported PNG, BMP, and JPEG images are converted and resized to the required game dimensions."
        };
        Label path = new()
        {
            Dock = DockStyle.Top, Height = 27, Padding = new Padding(12, 2, 12, 2),
            Text = metPath, AutoEllipsis = true
        };

        SplitContainer players = new()
        {
            Dock = DockStyle.Fill, Size = new Size(1400, 700), SplitterDistance = 245,
            FixedPanel = FixedPanel.Panel1, Panel1MinSize = 210, Panel2MinSize = 760
        };
        players.Panel1.Padding = new Padding(8, 5, 5, 5);
        players.Panel1.Controls.Add(_playerList);
        players.Panel1.Controls.Add(_search);
        players.Panel2.Padding = new Padding(5, 5, 8, 5);
        players.Panel2.Controls.Add(BuildEditorArea());

        FlowLayoutPanel bottom = new()
        {
            Dock = DockStyle.Bottom, Height = 56, FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(8), WrapContents = false
        };
        Button close = new() { Text = "Close", AutoSize = true, DialogResult = DialogResult.Cancel };
        Button save = new() { Text = "Save Textures to DATA.MET", AutoSize = true };
        Button resetAll = new() { Text = "Reset All Unsaved Textures", AutoSize = true };
        save.Click += Save_Click;
        resetAll.Click += (_, _) => ResetAll();
        bottom.Controls.AddRange(new Control[] { close, save, resetAll });

        Controls.Add(players);
        Controls.Add(_status);
        Controls.Add(bottom);
        Controls.Add(path);
        Controls.Add(heading);
        CancelButton = close;

        _search.TextChanged += (_, _) => RefreshPlayers();
        _playerList.SelectedIndexChanged += (_, _) => PlayerChanged();
        _modelList.SelectedIndexChanged += (_, _) => ModelChanged();
        _animationList.SelectedIndexChanged += (_, _) => AnimationChanged();
        _textures.SelectedIndexChanged += (_, _) => TextureChanged();
        _scrubber.Scroll += (_, _) => SetPosition(ScrubberToTime());
        _play.Click += (_, _) => StartPlayback();
        _stop.Click += (_, _) => StopPlayback();
        _timer.Tick += (_, _) => PlaybackTick();
        _export.Click += (_, _) => ExportTexture();
        _replace.Click += (_, _) => ReplaceTexture();
        _resetTexture.Click += (_, _) => ResetSelectedTexture();
        FormClosing += Appearance_FormClosing;
        RefreshPlayers();
        UpdateStatus();
    }

    private Control BuildEditorArea()
    {
        SplitContainer work = new()
        {
            Dock = DockStyle.Fill, Size = new Size(1120, 700), SplitterDistance = 745,
            Panel1MinSize = 500, Panel2MinSize = 300
        };
        work.Panel1.Padding = new Padding(0, 0, 5, 0);
        work.Panel2.Padding = new Padding(5, 0, 0, 0);

        Panel modelPanel = new() { Dock = DockStyle.Fill };
        TableLayoutPanel selectors = new()
        {
            Dock = DockStyle.Top, Height = 68, ColumnCount = 2, RowCount = 2,
            Padding = new Padding(2, 2, 2, 4)
        };
        selectors.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 105));
        selectors.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        selectors.Controls.Add(new Label { Text = "Model / context:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        selectors.Controls.Add(_modelList, 1, 0);
        selectors.Controls.Add(new Label { Text = "Preview motion:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 1);
        selectors.Controls.Add(_animationList, 1, 1);
        FlowLayoutPanel transport = new()
        {
            Dock = DockStyle.Bottom, Height = 42, Padding = new Padding(3, 5, 3, 2), WrapContents = false
        };
        transport.Controls.AddRange(new Control[] { _play, _stop, _scrubber, _position });
        modelPanel.Controls.Add(_preview);
        modelPanel.Controls.Add(transport);
        modelPanel.Controls.Add(selectors);

        Panel texturePanel = new() { Dock = DockStyle.Fill };
        FlowLayoutPanel textureButtons = new()
        {
            Dock = DockStyle.Bottom, Height = 72, Padding = new Padding(2, 5, 2, 2), WrapContents = true
        };
        textureButtons.Controls.AddRange(new Control[] { _export, _replace, _resetTexture });
        Label title = new()
        {
            Text = "Model textures", Dock = DockStyle.Top, Height = 28,
            Font = new Font("Segoe UI", 9F, FontStyle.Bold), Padding = new Padding(3, 5, 3, 2)
        };
        texturePanel.Controls.Add(_textures);
        texturePanel.Controls.Add(textureButtons);
        texturePanel.Controls.Add(_texturePreview);
        texturePanel.Controls.Add(_textureInfo);
        texturePanel.Controls.Add(title);

        work.Panel1.Controls.Add(modelPanel);
        work.Panel2.Controls.Add(texturePanel);
        return work;
    }

    private static List<PlayerAppearanceItem> BuildCatalog(
        RenderWareAnimationArchive archive, PlayerStatsArchive stats)
    {
        Dictionary<string, PlayerStatsRecord> names = stats.Players
            .Where(player => !player.IsClone)
            .GroupBy(PlayerCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        Dictionary<string, ModelBuilder> models = new(StringComparer.OrdinalIgnoreCase);
        foreach (RenderWareAnimationFile animation in archive.Files)
        {
            string[] animationParts = Normalize(animation.SourcePath)
                .Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (animationParts.Length < 3 || !PlayerModelCategories.Contains(animationParts[1])) continue;
            RenderWareAnimationBinding? binding = archive.ResolveSkeleton(animation);
            if (binding == null) continue;
            string[] modelParts = Normalize(binding.ModelPath)
                .Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (modelParts.Length < 3 || !PlayerModelCategories.Contains(modelParts[1])) continue;
            string code = modelParts[2];
            if (!names.ContainsKey(code)) continue;
            if (!models.TryGetValue(binding.ModelPath, out ModelBuilder? model))
            {
                model = new ModelBuilder(code, ContextName(modelParts[1]), binding);
                models[binding.ModelPath] = model;
            }
            model.Animations.Add(animation);
        }

        return models.Values
            .GroupBy(model => model.Code, StringComparer.OrdinalIgnoreCase)
            .Select(group => new PlayerAppearanceItem(
                names[group.Key], group.OrderBy(model => ContextOrder(model.Context))
                    .ThenBy(model => model.Binding.ModelPath)
                    .Select(model => new AppearanceModelItem(model.Context, model.Binding,
                        model.Animations.OrderByDescending(AnimationScore)
                            .ThenBy(animation => animation.SourcePath)
                            .ToList()))
                    .ToList()))
            .OrderBy(item => item.Player.LastName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Player.FirstName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private void RefreshPlayers()
    {
        string? selectedCode = (_playerList.SelectedItem as PlayerAppearanceItem)?.Code;
        string search = _search.Text.Trim();
        List<PlayerAppearanceItem> visible = _players.Where(item =>
            search.Length == 0 || item.Player.DisplayName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
            item.Code.Contains(search, StringComparison.OrdinalIgnoreCase)).ToList();
        _loading = true;
        _playerList.BeginUpdate();
        _playerList.Items.Clear();
        foreach (PlayerAppearanceItem item in visible) _playerList.Items.Add(item);
        _playerList.EndUpdate();
        int selected = visible.FindIndex(item => item.Code.Equals(selectedCode, StringComparison.OrdinalIgnoreCase));
        _playerList.SelectedIndex = selected >= 0 ? selected : (visible.Count > 0 ? 0 : -1);
        _loading = false;
        PlayerChanged();
    }

    private void PlayerChanged()
    {
        if (_loading) return;
        StopPlayback();
        PlayerAppearanceItem? player = _playerList.SelectedItem as PlayerAppearanceItem;
        _loading = true;
        _modelList.Items.Clear();
        if (player != null)
            foreach (AppearanceModelItem model in player.Models) _modelList.Items.Add(model);
        _modelList.SelectedIndex = _modelList.Items.Count > 0 ? 0 : -1;
        _loading = false;
        ModelChanged();
    }

    private void ModelChanged()
    {
        if (_loading) return;
        StopPlayback();
        AppearanceModelItem? selected = _modelList.SelectedItem as AppearanceModelItem;
        _binding = selected?.Binding;
        _model = _binding == null ? null : _archive.LoadModel(_binding);
        _loading = true;
        _animationList.Items.Clear();
        if (selected != null)
            foreach (RenderWareAnimationFile animation in selected.Animations)
                _animationList.Items.Add(new AnimationItem(animation));
        _animationList.SelectedIndex = _animationList.Items.Count > 0 ? 0 : -1;
        _loading = false;
        RefreshTextures();
        AnimationChanged();
    }

    private void AnimationChanged()
    {
        if (_loading) return;
        StopPlayback();
        _animation = (_animationList.SelectedItem as AnimationItem)?.File;
        _preview.Animation = _animation;
        _preview.Binding = _binding;
        _preview.Model = _model;
        _preview.FacialEvent = _animation?.PairedEvent;
        _preview.SelectedTrack = 0;
        SetPosition(0);
    }

    private void RefreshTextures(string? selectedName = null)
    {
        selectedName ??= (_textures.SelectedItem as TextureItem)?.Name;
        List<TextureItem> items = _model?.Textures
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Value.SourcePath))
            .Select(pair => new TextureItem(pair.Key, pair.Value, _archive.IsTextureChanged(pair.Value.SourcePath)))
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToList() ?? new List<TextureItem>();
        _textures.BeginUpdate();
        _textures.Items.Clear();
        foreach (TextureItem item in items) _textures.Items.Add(item);
        _textures.EndUpdate();
        int selected = items.FindIndex(item => item.Name.Equals(selectedName, StringComparison.OrdinalIgnoreCase));
        _textures.SelectedIndex = selected >= 0 ? selected : (items.Count > 0 ? 0 : -1);
        TextureChanged();
    }

    private void TextureChanged()
    {
        _texture = _textures.SelectedItem as TextureItem;
        Image? old = _texturePreview.Image;
        _texturePreview.Image = null;
        old?.Dispose();
        bool available = _texture != null;
        _export.Enabled = available;
        _replace.Enabled = available;
        _resetTexture.Enabled = available && _texture!.Changed;
        if (_texture == null)
        {
            _textureInfo.Text = _model == null
                ? "This DFF does not expose a supported skinned model."
                : "This model has no resolved standalone PNG textures.";
            return;
        }
        try
        {
            byte[] data = _archive.GetTextureBytes(_texture.Texture);
            using MemoryStream stream = new(data, writable: false);
            using Image image = Image.FromStream(stream);
            _texturePreview.Image = new Bitmap(image);
            _textureInfo.Text = $"{_texture.Name}  |  {_texture.Texture.Width} x {_texture.Texture.Height}" +
                                (_texture.Changed ? "  |  unsaved replacement" : string.Empty) +
                                $"\n{_texture.Texture.SourcePath}";
        }
        catch (Exception exception)
        {
            _textureInfo.Text = exception.Message;
        }
    }

    private void ExportTexture()
    {
        if (_texture == null) return;
        using SaveFileDialog dialog = new()
        {
            Filter = "PNG image (*.png)|*.png|All files (*.*)|*.*",
            FileName = Path.GetFileName(_texture.Texture.SourcePath),
            Title = "Export Player Texture"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            File.WriteAllBytes(dialog.FileName, _archive.GetTextureBytes(_texture.Texture));
            _status.Text = $"Exported {_texture.Texture.SourcePath} to {dialog.FileName}.";
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Unable to Export Texture",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ReplaceTexture()
    {
        if (_texture == null) return;
        using OpenFileDialog dialog = new()
        {
            Filter = "Image files (*.png;*.bmp;*.jpg;*.jpeg)|*.png;*.bmp;*.jpg;*.jpeg|All files (*.*)|*.*",
            Title = $"Replace {_texture.Name}"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            byte[] input = File.ReadAllBytes(dialog.FileName);
            using MemoryStream stream = new(input, writable: false);
            using Image image = Image.FromStream(stream, useEmbeddedColorManagement: false,
                validateImageData: true);
            string resizing = image.Width == _texture.Texture.Width && image.Height == _texture.Texture.Height
                ? "The image dimensions already match."
                : $"It will be resized from {image.Width} x {image.Height} to " +
                  $"{_texture.Texture.Width} x {_texture.Texture.Height}.";
            if (MessageBox.Show(this,
                    $"Replace {_texture.Texture.SourcePath}?\n\n{resizing}\n" +
                    "The file will be converted to a game-compatible PNG and previewed immediately.",
                    "Replace Player Texture", MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question) != DialogResult.Yes) return;
            string name = _texture.Name;
            TextureReplacementResult result = _archive.StageTextureReplacement(_texture.Texture, input);
            RefreshTextures(name);
            _preview.Invalidate();
            string resized = result.WasResized
                ? $" Resized {result.SourceWidth} x {result.SourceHeight} to {result.TargetWidth} x {result.TargetHeight}."
                : string.Empty;
            _status.Text = $"Staged {result.SourcePath}.{resized}";
            UpdateStatus(keepDetail: true);
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Unable to Replace Texture",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ResetSelectedTexture()
    {
        if (_texture == null || !_texture.Changed) return;
        string name = _texture.Name;
        _archive.ResetTexture(_texture.Texture.SourcePath);
        RefreshTextures(name);
        _preview.Invalidate();
        UpdateStatus();
    }

    private void ResetAll()
    {
        if (_archive.ChangedTextureCount == 0) return;
        if (MessageBox.Show(this, "Discard every unsaved player texture replacement?",
                "Reset Player Textures", MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) != DialogResult.Yes) return;
        _archive.ResetAll();
        RefreshTextures();
        _preview.Invalidate();
        UpdateStatus();
    }

    private void Save_Click(object? sender, EventArgs e)
    {
        if (_archive.ChangedTextureCount == 0)
        {
            MessageBox.Show(this, "There are no player texture changes to save.",
                "Player Appearance Editor", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (MessageBox.Show(this,
                $"Write {_archive.ChangedTextureCount:N0} texture replacement(s) to DATA.MET? " +
                "A timestamped backup will be created first.",
                "Save Player Textures", MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) != DialogResult.Yes) return;
        try
        {
            StopPlayback();
            AnimationSaveResult result = _archive.SaveTextureChangesWithBackup();
            _saved = true;
            string rebuild = result.RebuiltArchive ? " DATA.MET was rebuilt because a PNG grew." : string.Empty;
            MessageBox.Show(this,
                $"Saved {result.ChangedFileCount:N0} player texture(s).{rebuild}\n\nBackup: {result.BackupPath}",
                "Player Textures Saved", MessageBoxButtons.OK, MessageBoxIcon.Information);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Unable to Save Player Textures",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void StartPlayback()
    {
        if (_animation == null) return;
        _playStart = _preview.PositionSeconds >= _animation.DurationSeconds - 0.0001 ? 0 : _preview.PositionSeconds;
        _clock.Restart();
        _timer.Start();
        _play.Enabled = false;
        _stop.Enabled = true;
    }

    private void PlaybackTick()
    {
        if (_animation == null) return;
        double position = _playStart + _clock.Elapsed.TotalSeconds;
        if (position >= _animation.DurationSeconds)
        {
            position %= _animation.DurationSeconds;
            _playStart = position;
            _clock.Restart();
        }
        SetPosition(position);
    }

    private void StopPlayback()
    {
        _timer.Stop();
        _clock.Reset();
        _play.Enabled = _animation != null;
        _stop.Enabled = false;
    }

    private void SetPosition(double seconds)
    {
        seconds = Math.Clamp(seconds, 0, _animation?.DurationSeconds ?? 0);
        _preview.PositionSeconds = seconds;
        _scrubber.Value = _animation == null || _animation.DurationSeconds <= 0 ? 0 :
            Math.Clamp((int)Math.Round(seconds / _animation.DurationSeconds * _scrubber.Maximum),
                _scrubber.Minimum, _scrubber.Maximum);
        _position.Text = $"{seconds:0.000} / {_animation?.DurationSeconds ?? 0:0.000}s";
    }

    private double ScrubberToTime() => _animation == null ? 0 :
        (double)_scrubber.Value / _scrubber.Maximum * _animation.DurationSeconds;

    private void UpdateStatus(bool keepDetail = false)
    {
        if (keepDetail && _archive.ChangedTextureCount > 0)
        {
            _status.Text += $"  {_archive.ChangedTextureCount:N0} texture(s) now have unsaved changes.";
            return;
        }
        _status.Text = _archive.ChangedTextureCount == 0
            ? $"Loaded {_players.Count:N0} players with editable 3D model contexts."
            : $"{_archive.ChangedTextureCount:N0} texture(s) have unsaved replacements.";
    }

    private void Appearance_FormClosing(object? sender, FormClosingEventArgs e)
    {
        StopPlayback();
        if (_saved || _archive.ChangedTextureCount == 0) return;
        if (MessageBox.Show(this, "Close and discard all unsaved player texture replacements?",
                "Unsaved Player Textures", MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning) == DialogResult.Yes) return;
        e.Cancel = true;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
            _texturePreview.Image?.Dispose();
        }
        base.Dispose(disposing);
    }

    private static string PlayerCode(PlayerStatsRecord player) =>
        Path.GetFileNameWithoutExtension(player.SourceName)
            .Replace("_stats", string.Empty, StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string path) => path.Replace('\\', '/');

    private static string ContextName(string category) => category.ToLowerInvariant() switch
    {
        "batting" => "Batting",
        "fielding" => "Fielding",
        "baserunning" => "Baserunning",
        "playercard" => "Player card",
        "kids" => "Interview",
        _ => category
    };

    private static int ContextOrder(string context) => context switch
    {
        "Batting" => 0,
        "Fielding" => 1,
        "Baserunning" => 2,
        "Player card" => 3,
        "Interview" => 4,
        _ => 5
    };

    private static int AnimationScore(RenderWareAnimationFile animation)
    {
        string stem = Path.GetFileNameWithoutExtension(animation.SourcePath);
        if (stem.Contains("batready", StringComparison.OrdinalIgnoreCase)) return 100;
        if (stem.Contains("inambient", StringComparison.OrdinalIgnoreCase)) return 90;
        if (stem.Contains("batprep", StringComparison.OrdinalIgnoreCase)) return 80;
        if (stem.Contains("walk", StringComparison.OrdinalIgnoreCase)) return 70;
        return animation.PairedEvent != null ? 50 : 0;
    }

    private sealed class ModelBuilder
    {
        public ModelBuilder(string code, string context, RenderWareAnimationBinding binding)
        {
            Code = code;
            Context = context;
            Binding = binding;
        }
        public string Code { get; }
        public string Context { get; }
        public RenderWareAnimationBinding Binding { get; }
        public List<RenderWareAnimationFile> Animations { get; } = new();
    }

    private sealed record PlayerAppearanceItem(PlayerStatsRecord Player, IReadOnlyList<AppearanceModelItem> Models)
    {
        public string Code => PlayerCode(Player);
        public override string ToString() => Player.DisplayName;
    }

    private sealed record AppearanceModelItem(
        string Context,
        RenderWareAnimationBinding Binding,
        IReadOnlyList<RenderWareAnimationFile> Animations)
    {
        public override string ToString() => $"{Context} — {Path.GetFileName(Binding.ModelPath)}";
    }

    private sealed record AnimationItem(RenderWareAnimationFile File)
    {
        public override string ToString() =>
            $"{Path.GetFileName(File.SourcePath)}  [{File.DurationSeconds:0.###}s]";
    }

    private sealed record TextureItem(string Name, RenderWareTexture Texture, bool Changed)
    {
        public override string ToString() =>
            $"{Name}  [{Texture.Width} x {Texture.Height}]" + (Changed ? "  *" : string.Empty);
    }
}
