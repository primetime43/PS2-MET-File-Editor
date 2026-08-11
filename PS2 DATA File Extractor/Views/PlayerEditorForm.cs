using PS2_DATA_File_Extractor.FileOperations;

namespace PS2_DATA_File_Extractor;

public sealed class PlayerEditorForm : Form
{
    private static readonly StatDefinition[] CoreStats =
    {
        new(0, "Power component A", "Used by the game's balanced Power setter."),
        new(1, "Batting power", "Headline Power rating; also controls bat speed."),
        new(2, "Fielding component A", "Part of the displayed Fielding rating."),
        new(3, "Coordination / contact A", "Part of the displayed Contact rating."),
        new(4, "Contact component B", "Part of the displayed Contact rating."),
        new(5, "Throw speed / pitching base", "Throw speed and half of the displayed Pitching rating."),
        new(6, "Fielding component B", "Part of the displayed Fielding rating."),
        new(7, "Run speed", "Headline Running rating."),
        new(8, "Contact component C", "Part of the displayed Contact rating."),
        new(9, "Reaction / fielding C", "Reaction time and part of the Fielding rating."),
        new(10, "Power component B", "Used by the game's balanced Power setter."),
        new(12, "Acceleration penalty", "Acceleration is calculated as 100 minus this value; lower is faster."),
        new(13, "Running component", "Used by the game's balanced Running setter.")
    };

    private static readonly StatDefinition[] PitchStats =
    {
        new(14, "Fastball", "Regular pitch type 0."),
        new(15, "Screwball", "Regular pitch type 1."),
        new(16, "Curveball", "Regular pitch type 2."),
        new(17, "Changeup", "Regular pitch type 3."),
        new(18, "Power pitch type 4", "Power-pitch slot; exact retail name is not present in the stats record."),
        new(19, "Power pitch type 5", "Power-pitch slot; exact retail name is not present in the stats record."),
        new(20, "Power pitch type 6", "Power-pitch slot; exact retail name is not present in the stats record."),
        new(21, "Power pitch type 7", "Power-pitch slot; exact retail name is not present in the stats record."),
        new(22, "Power pitch type 8", "Power-pitch slot; exact retail name is not present in the stats record."),
        new(23, "Power pitch type 9", "Power-pitch slot; exact retail name is not present in the stats record."),
        new(24, "Power pitch type 10", "Power-pitch slot; exact retail name is not present in the stats record."),
        new(25, "Power pitch type 11", "Power-pitch slot; exact retail name is not present in the stats record.")
    };

    private static readonly CloneDefinition[] CloneFields =
    {
        new(0, "Appearance slot 0", "Observed retail range: 0-6."),
        new(1, "Appearance slot 1", "Observed retail range: 0-12."),
        new(2, "Body height class", "Confirmed by SetCloneData; observed retail range: 0-2."),
        new(3, "Appearance slot 3", "Observed retail range: 0-7."),
        new(4, "Appearance slot 4", "Observed retail range: 0-8."),
        new(5, "Appearance slot 5", "Observed retail range: 0-10."),
        new(6, "Appearance slot 6", "Observed retail range: 0-6."),
        new(7, "Appearance slot 7", "Observed retail range: 0-4.")
    };

    private readonly PlayerStatsArchive _archive;
    private readonly string _metPath;
    private PlayerPortraitArchive _portraits;
    private readonly TextBox _search = new() { Dock = DockStyle.Top, PlaceholderText = "Search players..." };
    private readonly ListBox _playerList = new() { Dock = DockStyle.Fill, IntegralHeight = false };
    private readonly PictureBox _portrait = new()
    {
        Dock = DockStyle.Fill,
        BackColor = SystemColors.Window,
        BorderStyle = BorderStyle.FixedSingle,
        SizeMode = PictureBoxSizeMode.Zoom
    };
    private readonly Label _portraitMessage = new()
    {
        Dock = DockStyle.Fill,
        BackColor = SystemColors.Window,
        TextAlign = ContentAlignment.MiddleCenter
    };
    private readonly ToolTip _portraitToolTip = new();
    private readonly ComboBox _portraitSelector = new()
    {
        Dock = DockStyle.Fill,
        DropDownStyle = ComboBoxStyle.DropDownList,
        DropDownWidth = 360,
        IntegralHeight = false,
        MaxDropDownItems = 18
    };
    private readonly Button _previousPortraitButton = new()
    {
        Text = "<", Dock = DockStyle.Fill, Margin = new Padding(0, 1, 2, 1)
    };
    private readonly Button _nextPortraitButton = new()
    {
        Text = ">", Dock = DockStyle.Fill, Margin = new Padding(2, 1, 0, 1)
    };
    private readonly Button _exportPortraitButton = new()
    {
        Text = "Export...", Dock = DockStyle.Fill, Margin = new Padding(2, 3, 2, 3)
    };
    private readonly Button _replacePortraitButton = new()
    {
        Text = "Replace...", Dock = DockStyle.Fill, Margin = new Padding(2, 3, 2, 3)
    };
    private readonly TextBox _firstName = new() { Width = 145, MaxLength = PlayerStatsRecord.MaxNameLength };
    private readonly TextBox _nickname = new() { Width = 145, MaxLength = PlayerStatsRecord.MaxNameLength };
    private readonly TextBox _lastName = new() { Width = 145, MaxLength = PlayerStatsRecord.MaxNameLength };
    private readonly NumericUpDown _height = CreateShortEditor(90);
    private readonly NumericUpDown _birthMonth = new() { Width = 65, Minimum = 1, Maximum = 12 };
    private readonly NumericUpDown _birthDay = new() { Width = 65, Minimum = 1, Maximum = 31 };
    private readonly ComboBox _gender = CreateChoiceBox(new ValueChoice(1, "Female"), new ValueChoice(2, "Male"));
    private readonly ComboBox _batHand = CreateChoiceBox(new ValueChoice(1, "Right"), new ValueChoice(2, "Left"));
    private readonly ComboBox _throwHand = CreateChoiceBox(new ValueChoice(1, "Right"), new ValueChoice(2, "Left"));
    private readonly Label _source = new() { AutoSize = true, ForeColor = SystemColors.GrayText };
    private readonly Label _ratings = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Label _status = new() { Dock = DockStyle.Bottom, Height = 30, Padding = new Padding(12, 5, 12, 2) };
    private readonly TabControl _tabs = new() { Dock = DockStyle.Fill };
    private readonly DataGridView _coreGrid;
    private readonly DataGridView _pitchGrid;
    private readonly DataGridView _cloneGrid;
    private readonly TabPage _clonePage = new("Clone Appearance");
    private PlayerStatsRecord? _current;
    private PlayerImage? _currentPlayerImage;
    private readonly Dictionary<string, Bitmap> _animationPreviewCache =
        new(StringComparer.OrdinalIgnoreCase);
    private bool _loading;

    public bool ArchiveWasModified { get; private set; }

    public PlayerEditorForm(PlayerStatsArchive archive, string metPath)
    {
        _archive = archive;
        _metPath = metPath;
        _portraits = PlayerPortraitArchive.Load(metPath);
        Text = "Player Editor - DATA.MET";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(1120, 760);
        MinimumSize = new Size(880, 620);
        AutoScaleMode = AutoScaleMode.Dpi;

        Label instructions = new()
        {
            Dock = DockStyle.Top,
            Height = 58,
            Padding = new Padding(12, 8, 12, 4),
            Text = "Edit the player records stored in data/kids/stats/*_stats.dat. Retail skill values are 0-100, " +
                   "but signed 16-bit modded values are accepted. Saving creates one timestamped DATA.MET backup."
        };
        Label path = new()
        {
            Dock = DockStyle.Top,
            Height = 28,
            Padding = new Padding(12, 2, 12, 2),
            AutoEllipsis = true,
            Text = metPath
        };

        SplitContainer split = new()
        {
            Dock = DockStyle.Fill,
            SplitterDistance = 280,
            FixedPanel = FixedPanel.Panel1
        };
        split.Panel1.Padding = new Padding(8);
        split.Panel1.Controls.Add(_playerList);
        split.Panel1.Controls.Add(_search);

        Panel details = new() { Dock = DockStyle.Fill, Padding = new Padding(8) };
        Control identity = BuildIdentityArea();
        TableLayoutPanel summary = new()
        {
            Dock = DockStyle.Top,
            Height = 64,
            ColumnCount = 2,
            Padding = new Padding(4)
        };
        summary.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        summary.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        summary.Controls.Add(_ratings, 0, 0);
        Button maxSkills = new() { Text = "Max This Player's Skills", AutoSize = true, Anchor = AnchorStyles.Right };
        maxSkills.Click += (_, _) => MaxCurrentSkills();
        summary.Controls.Add(maxSkills, 1, 0);

        _coreGrid = CreateStatGrid(CoreStats.Select(definition =>
            new ValueBinding(false, definition.Index, definition.Name, definition.Note)));
        _pitchGrid = CreateStatGrid(PitchStats.Select(definition =>
            new ValueBinding(false, definition.Index, definition.Name, definition.Note)));
        _cloneGrid = CreateStatGrid(CloneFields.Select(definition =>
            new ValueBinding(true, definition.Index, definition.Name, definition.Note)));
        TabPage corePage = new("Core & Movement") { Padding = new Padding(5) };
        TabPage pitchPage = new("Pitch Ratings") { Padding = new Padding(5) };
        _clonePage.Padding = new Padding(5);
        corePage.Controls.Add(_coreGrid);
        pitchPage.Controls.Add(_pitchGrid);
        _clonePage.Controls.Add(_cloneGrid);
        _tabs.TabPages.AddRange(new[] { corePage, pitchPage, _clonePage });

        details.Controls.Add(_tabs);
        details.Controls.Add(summary);
        details.Controls.Add(identity);
        split.Panel2.Controls.Add(details);

        FlowLayoutPanel buttons = new()
        {
            Dock = DockStyle.Bottom,
            Height = 56,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(8),
            WrapContents = false
        };
        Button cancel = new() { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        Button save = new() { Text = "Save Players to DATA.MET", AutoSize = true };
        Button reset = new() { Text = "Reset All Unsaved Changes", AutoSize = true };
        save.Click += Save_Click;
        reset.Click += (_, _) => ResetAll();
        buttons.Controls.AddRange(new Control[] { cancel, save, reset });

        Controls.Add(split);
        Controls.Add(_status);
        Controls.Add(buttons);
        Controls.Add(path);
        Controls.Add(instructions);
        AcceptButton = save;
        CancelButton = cancel;

        _search.TextChanged += (_, _) => PopulatePlayerList();
        _playerList.SelectedIndexChanged += (_, _) => LoadSelectedPlayer();
        HookIdentityEvents();
        _exportPortraitButton.Click += ExportPortrait_Click;
        _replacePortraitButton.Click += ReplacePortrait_Click;
        _portraitSelector.SelectedIndexChanged += (_, _) =>
        {
            if (!_loading) LoadSelectedPlayerImage();
        };
        _previousPortraitButton.Click += (_, _) => MovePortrait(-1);
        _nextPortraitButton.Click += (_, _) => MovePortrait(1);
        PopulatePlayerList();
        Shown += (_, _) => SetInitialPlayerListWidth(split);
        FormClosed += (_, _) =>
        {
            _portrait.Image?.Dispose();
            foreach (Bitmap preview in _animationPreviewCache.Values) preview.Dispose();
            _portraitToolTip.Dispose();
        };
        _status.Text = $"Loaded {_archive.Players.Count} players ({_archive.Players.Count(player => player.IsClone)} clones) and {_portraits.PortraitCount} portraits ({_portraits.PackedPortraitCount} game-texture mappings).";
    }

    private Control BuildIdentityArea()
    {
        TableLayoutPanel area = new()
        {
            Dock = DockStyle.Top,
            Height = 235,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(4)
        };
        area.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        area.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 270F));
        area.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        FlowLayoutPanel identity = BuildIdentityPanel();
        TableLayoutPanel portraitPanel = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Margin = new Padding(8, 0, 0, 0)
        };
        portraitPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        portraitPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 20F));
        portraitPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 31F));
        portraitPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        portraitPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
        portraitPanel.Controls.Add(new Label { Text = "Player images", Dock = DockStyle.Fill }, 0, 0);
        TableLayoutPanel portraitNavigation = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            Margin = Padding.Empty
        };
        portraitNavigation.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 30F));
        portraitNavigation.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        portraitNavigation.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 30F));
        portraitNavigation.Controls.Add(_previousPortraitButton, 0, 0);
        portraitNavigation.Controls.Add(_portraitSelector, 1, 0);
        portraitNavigation.Controls.Add(_nextPortraitButton, 2, 0);
        portraitPanel.Controls.Add(portraitNavigation, 0, 1);
        Panel imageHost = new() { Dock = DockStyle.Fill };
        imageHost.Controls.Add(_portrait);
        imageHost.Controls.Add(_portraitMessage);
        portraitPanel.Controls.Add(imageHost, 0, 2);
        TableLayoutPanel portraitButtons = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Padding = Padding.Empty
        };
        portraitButtons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        portraitButtons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        portraitButtons.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        portraitButtons.Controls.Add(_exportPortraitButton, 0, 0);
        portraitButtons.Controls.Add(_replacePortraitButton, 1, 0);
        portraitPanel.Controls.Add(portraitButtons, 0, 3);
        area.Controls.Add(identity, 0, 0);
        area.Controls.Add(portraitPanel, 1, 0);
        return area;
    }

    private FlowLayoutPanel BuildIdentityPanel()
    {
        FlowLayoutPanel panel = new()
        {
            Dock = DockStyle.Fill,
            Height = 142,
            Padding = new Padding(4),
            WrapContents = true,
            AutoScroll = true
        };
        panel.Controls.AddRange(new Control[]
        {
            Labeled("First name", _firstName), Labeled("Nickname", _nickname), Labeled("Last name", _lastName),
            Labeled("Height", _height), Labeled("Birth month", _birthMonth), Labeled("Birth day", _birthDay),
            Labeled("Gender", _gender), Labeled("Bats", _batHand), Labeled("Throws", _throwHand),
            Labeled("Archive entry", _source, 300)
        });
        return panel;
    }

    private void SetInitialPlayerListWidth(SplitContainer split)
    {
        int widestItem = _playerList.Items.Cast<object>()
            .Select(item => TextRenderer.MeasureText(item.ToString() ?? string.Empty, _playerList.Font).Width)
            .DefaultIfEmpty(200)
            .Max();
        int desiredWidth = Math.Clamp(widestItem + SystemInformation.VerticalScrollBarWidth + 30, 240, 420);
        int maximumWidth = Math.Max(240, split.ClientSize.Width - 560 - split.SplitterWidth);
        int initialWidth = Math.Min(desiredWidth, maximumWidth);

        split.SplitterDistance = initialWidth;
        split.Panel1MinSize = Math.Min(220, initialWidth);
        split.Panel2MinSize = Math.Min(520, split.ClientSize.Width - initialWidth - split.SplitterWidth);
    }

    private void HookIdentityEvents()
    {
        _firstName.TextChanged += (_, _) => UpdateName(record => record.FirstName = _firstName.Text);
        _nickname.TextChanged += (_, _) => UpdateName(record => record.Nickname = _nickname.Text);
        _lastName.TextChanged += (_, _) => UpdateName(record => record.LastName = _lastName.Text);
        _height.ValueChanged += (_, _) => UpdateBaseValue(11, (short)_height.Value);
        _birthMonth.ValueChanged += (_, _) => UpdateBaseValue(26, (short)_birthMonth.Value);
        _birthDay.ValueChanged += (_, _) => UpdateBaseValue(27, (short)_birthDay.Value);
        _gender.SelectedIndexChanged += (_, _) => UpdateChoice(_gender, 28);
        _batHand.SelectedIndexChanged += (_, _) => UpdateChoice(_batHand, 29);
        _throwHand.SelectedIndexChanged += (_, _) => UpdateChoice(_throwHand, 30);
    }

    private void PopulatePlayerList()
    {
        PlayerStatsRecord? selected = _current;
        string filter = _search.Text.Trim();
        _loading = true;
        _playerList.BeginUpdate();
        _playerList.Items.Clear();
        foreach (PlayerStatsRecord player in _archive.Players.Where(player =>
                     filter.Length == 0 || player.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                     player.SourceName.Contains(filter, StringComparison.OrdinalIgnoreCase)))
        {
            _playerList.Items.Add(new PlayerListItem(player));
        }
        _playerList.EndUpdate();
        int selectedIndex = selected == null ? -1 : _playerList.Items.Cast<PlayerListItem>()
            .ToList().FindIndex(item => ReferenceEquals(item.Player, selected));
        _playerList.SelectedIndex = selectedIndex >= 0 ? selectedIndex : (_playerList.Items.Count > 0 ? 0 : -1);
        _loading = false;
        LoadSelectedPlayer();
    }

    private void LoadSelectedPlayer()
    {
        if (_loading) return;
        _current = (_playerList.SelectedItem as PlayerListItem)?.Player;
        if (_current == null) return;
        _loading = true;
        _firstName.Text = _current.FirstName;
        _nickname.Text = _current.Nickname;
        _lastName.Text = _current.LastName;
        _height.Value = _current.BaseValues[11];
        _birthMonth.Value = Clamp(_current.BaseValues[26], _birthMonth.Minimum, _birthMonth.Maximum);
        _birthDay.Value = Clamp(_current.BaseValues[27], _birthDay.Minimum, _birthDay.Maximum);
        SelectChoice(_gender, _current.BaseValues[28]);
        SelectChoice(_batHand, _current.BaseValues[29]);
        SelectChoice(_throwHand, _current.BaseValues[30]);
        _source.Text = _current.SourcePath;
        PopulatePlayerImages();
        LoadGridValues(_coreGrid);
        LoadGridValues(_pitchGrid);
        LoadGridValues(_cloneGrid);
        _clonePage.Enabled = _current.IsClone;
        _loading = false;
        UpdateSummaryAndStatus();
    }

    private void PopulatePlayerImages(string? selectedPath = null)
    {
        bool wasLoading = _loading;
        _loading = true;
        _portraitSelector.BeginUpdate();
        _portraitSelector.Items.Clear();
        if (_current != null)
        {
            foreach (PlayerImageInfo info in _portraits.GetPlayerImages(_current))
                _portraitSelector.Items.Add(new PlayerImageListItem(info));
        }
        _portraitSelector.EndUpdate();

        int selectedIndex = selectedPath == null
            ? -1
            : _portraitSelector.Items.Cast<PlayerImageListItem>().ToList()
                .FindIndex(item => item.Info.SourcePath.Equals(selectedPath, StringComparison.OrdinalIgnoreCase));
        _portraitSelector.SelectedIndex = selectedIndex >= 0
            ? selectedIndex
            : (_portraitSelector.Items.Count > 0 ? 0 : -1);
        _loading = wasLoading;
        UpdatePortraitNavigationButtons();
        LoadSelectedPlayerImage();
    }

    private void MovePortrait(int direction)
    {
        int count = _portraitSelector.Items.Count;
        if (count == 0) return;
        int index = _portraitSelector.SelectedIndex;
        _portraitSelector.SelectedIndex = index < 0
            ? (direction < 0 ? count - 1 : 0)
            : (index + direction + count) % count;
    }

    private void LoadSelectedPlayerImage()
    {
        _currentPlayerImage = null;
        _exportPortraitButton.Enabled = false;
        _replacePortraitButton.Enabled = false;
        _exportPortraitButton.Text = "Export...";
        _replacePortraitButton.Text = "Replace...";

        Image? oldImage = _portrait.Image;
        _portrait.Image = null;
        oldImage?.Dispose();
        _portraitMessage.Visible = true;
        _portraitMessage.Text = _current?.IsClone == true
            ? "No dedicated images\nfor clone players"
            : "No stored player images";
        _portraitMessage.BringToFront();
        _portraitToolTip.SetToolTip(_portrait, string.Empty);
        UpdatePortraitNavigationButtons();

        if (_portraitSelector.SelectedItem is not PlayerImageListItem selected) return;
        PlayerImage? image = _portraits.GetPlayerImage(selected.Info);
        if (image == null)
        {
            _portraitMessage.Text = "Player image entry not found";
            return;
        }

        _currentPlayerImage = image;
        _exportPortraitButton.Enabled = true;
        _replacePortraitButton.Enabled = true;
        _exportPortraitButton.Text = image.Info.IsAnimated ? "Export PSS..." : "Export...";
        _replacePortraitButton.Text = image.Info.IsAnimated ? "Replace PSS..." : "Replace...";

        if (image.Info.IsAnimated)
        {
            try
            {
                if (_animationPreviewCache.TryGetValue(image.Info.SourcePath, out Bitmap? cached))
                {
                    _portrait.Image = new Bitmap(cached);
                    _portraitMessage.Visible = false;
                }
                else if (PssPreview.TryCreate(image.Data, out Bitmap? preview, out string? reason) &&
                         preview != null)
                {
                    _animationPreviewCache[image.Info.SourcePath] = new Bitmap(preview);
                    _portrait.Image = preview;
                    _portraitMessage.Visible = false;
                }
                else
                {
                    _portraitMessage.Text = reason ?? "Animation preview unavailable";
                }
            }
            catch (Exception)
            {
                _portraitMessage.Text = "Animation preview unavailable\nThe PSS can still be exported.";
            }

            _portraitToolTip.SetToolTip(
                _portrait,
                $"{image.Info.SourcePath}\nAnimated 256 x 256 player-selection portrait.");
            return;
        }

        try
        {
            using MemoryStream stream = new(image.Data, writable: false);
            using Image source = Image.FromStream(stream);
            _portrait.Image = new Bitmap(source);
            _portraitMessage.Visible = false;
            _portraitToolTip.SetToolTip(
                _portrait,
                image.Info.SourcePath +
                (image.Info.HasPackedGameTexture
                    ? "\nReplacement also updates the packed textures used in-game."
                    : string.Empty));
        }
        catch (ArgumentException)
        {
            _portraitMessage.Text = "Portrait could not\nbe loaded";
        }
    }

    private void UpdatePortraitNavigationButtons()
    {
        bool canMove = _portraitSelector.Items.Count > 1;
        _previousPortraitButton.Enabled = canMove;
        _nextPortraitButton.Enabled = canMove;
    }

    private void ExportPortrait_Click(object? sender, EventArgs e)
    {
        if (_currentPlayerImage == null) return;
        bool animated = _currentPlayerImage.Info.IsAnimated;
        using SaveFileDialog dialog = new()
        {
            Title = animated ? "Export player selection animation" : "Export player polaroid",
            Filter = animated
                ? "PlayStation 2 PSS video (*.pss)|*.pss|All files (*.*)|*.*"
                : "PNG image (*.png)|*.png|All files (*.*)|*.*",
            FileName = Path.GetFileName(_currentPlayerImage.Info.SourcePath),
            AddExtension = true,
            DefaultExt = animated ? "pss" : "png"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            File.WriteAllBytes(dialog.FileName, _currentPlayerImage.Data);
            _status.Text = $"Exported {Path.GetFileName(_currentPlayerImage.Info.SourcePath)} to {dialog.FileName}.";
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, $"The player image could not be exported.\n\n{exception.Message}",
                "Unable to Export Player Image", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ReplacePortrait_Click(object? sender, EventArgs e)
    {
        if (_currentPlayerImage == null ||
            _portraitSelector.SelectedItem is not PlayerImageListItem selected)
            return;

        bool animated = selected.Info.IsAnimated;
        using OpenFileDialog dialog = new()
        {
            Title = animated
                ? "Choose a replacement 256 x 256 PSS animation"
                : "Choose a replacement player polaroid",
            Filter = animated
                ? "PlayStation 2 PSS video (*.pss)|*.pss|All files (*.*)|*.*"
                : "Image files (*.png;*.bmp;*.jpg;*.jpeg)|*.png;*.bmp;*.jpg;*.jpeg|All files (*.*)|*.*"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        byte[] replacementData;
        try
        {
            if (animated)
            {
                replacementData = File.ReadAllBytes(dialog.FileName);
            }
            else
            {
                using Image source = Image.FromFile(dialog.FileName);
                if (source.Width > 4096 || source.Height > 4096)
                    throw new InvalidDataException("Portrait dimensions cannot exceed 4096 by 4096 pixels.");
                int targetWidth = _portrait.Image?.Width ?? source.Width;
                int targetHeight = _portrait.Image?.Height ?? source.Height;
                using Bitmap fitted = FitPortrait(source, targetWidth, targetHeight);
                using MemoryStream stream = new();
                fitted.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
                replacementData = stream.ToArray();
            }
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                $"The selected {(animated ? "PSS video" : "image")} could not be read.\n\n{exception.Message}",
                animated ? "Invalid PSS Video" : "Invalid Portrait Image",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }

        AssetReplacementValidation validation = AssetReplacementValidator.Validate(
            selected.Info.SourcePath,
            _currentPlayerImage.Data,
            replacementData);
        if (!validation.IsValid)
        {
            MessageBox.Show(
                this,
                $"The selected file is not compatible with {selected.Info.SourcePath}.\n\n" +
                validation.FormatErrors(),
                "Invalid Player Image Replacement",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }

        string detail = animated
            ? "The replacement must be a compatible 256 x 256 PS2 PSS animation. Motion and audio are kept from the imported PSS."
            : selected.Info.HasPackedGameTexture
                ? "The raw polaroid and its packed in-game texture regions will both be updated."
                : "The raw polaroid will be updated.";
        detail += $"\n\nFormat check: {validation.Description}";
        if (validation.Warnings.Count > 0)
            detail += $"\n\nWarnings:\n{validation.FormatWarnings()}";
        if (MessageBox.Show(this,
                $"Replace {selected.Info.SourcePath}?\n\n{detail}\n\n" +
                "The change is written to DATA.MET immediately and a timestamped backup is created first.",
                "Replace Player Image", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;

        try
        {
            UseWaitCursor = true;
            PlayerPortraitSaveResult result =
                _portraits.ReplacePlayerImageWithBackup(selected.Info, replacementData);
            ArchiveWasModified = true;

            if (_animationPreviewCache.Remove(selected.Info.SourcePath, out Bitmap? oldPreview))
                oldPreview.Dispose();

            string selectedPath = selected.Info.SourcePath;
            _portraits = PlayerPortraitArchive.Load(_metPath);
            PopulatePlayerImages(selectedPath);
            string rebuild = result.RebuiltArchive
                ? "\nThe archive was resized with sector alignment preserved."
                : string.Empty;
            MessageBox.Show(this,
                $"Replaced {result.SourcePath}.\n" +
                (result.PackedTextureCount > 0
                    ? $"Updated {result.PackedTextureCount} packed in-game texture page{(result.PackedTextureCount == 1 ? string.Empty : "s")}.\n\n"
                    : "\n") +
                $"Backup: {result.BackupPath}{rebuild}",
                "Player Image Replaced", MessageBoxButtons.OK, MessageBoxIcon.Information);
            _status.Text = $"Replaced {Path.GetFileName(result.SourcePath)}.";
        }
        catch (Exception exception)
        {
            MessageBox.Show(this,
                $"The player image could not be replaced. The archive was restored if a backup was created.\n\n{exception.Message}",
                "Unable to Replace Player Image", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            UseWaitCursor = false;
        }
    }
    private static Bitmap FitPortrait(Image source, int targetWidth, int targetHeight)
    {
        Bitmap result = new(targetWidth, targetHeight, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
        using Graphics graphics = Graphics.FromImage(result);
        graphics.Clear(Color.White);
        graphics.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
        graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;

        double scale = Math.Min((double)targetWidth / source.Width, (double)targetHeight / source.Height);
        int width = Math.Max(1, (int)Math.Round(source.Width * scale));
        int height = Math.Max(1, (int)Math.Round(source.Height * scale));
        int left = (targetWidth - width) / 2;
        int top = (targetHeight - height) / 2;
        graphics.DrawImage(source, new Rectangle(left, top, width, height));
        return result;
    }

    private static decimal Clamp(short value, decimal minimum, decimal maximum) =>
        Math.Min(maximum, Math.Max(minimum, value));

    private void LoadGridValues(DataGridView grid)
    {
        foreach (DataGridViewRow row in grid.Rows)
        {
            ValueBinding binding = (ValueBinding)row.Tag!;
            row.Cells[1].Value = binding.IsCloneField
                ? (_current!.IsClone ? _current.CloneAppearance[binding.Index] : null)
                : _current!.BaseValues[binding.Index];
        }
    }

    private DataGridView CreateStatGrid(IEnumerable<ValueBinding> bindings)
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
            SelectionMode = DataGridViewSelectionMode.CellSelect,
            AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle { BackColor = Color.FromArgb(246, 248, 250) }
        };
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Field", ReadOnly = true, Width = 230
        });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Value", Width = 100 });
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Game behavior / notes", ReadOnly = true,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, MinimumWidth = 260
        });
        foreach (ValueBinding binding in bindings)
        {
            int rowIndex = grid.Rows.Add(binding.Name, 0, binding.Note);
            grid.Rows[rowIndex].Tag = binding;
        }
        grid.CellValidating += Grid_CellValidating;
        grid.CellValueChanged += Grid_CellValueChanged;
        grid.DataError += (_, _) => { };
        return grid;
    }

    private void Grid_CellValidating(object? sender, DataGridViewCellValidatingEventArgs e)
    {
        if (_loading || e.RowIndex < 0 || e.ColumnIndex != 1) return;
        if (!short.TryParse(Convert.ToString(e.FormattedValue), out _))
        {
            e.Cancel = true;
            ((DataGridView)sender!).Rows[e.RowIndex].ErrorText = "Enter a signed 16-bit integer (-32768 to 32767).";
        }
        else
        {
            ((DataGridView)sender!).Rows[e.RowIndex].ErrorText = string.Empty;
        }
    }

    private void Grid_CellValueChanged(object? sender, DataGridViewCellEventArgs e)
    {
        if (_loading || _current == null || e.RowIndex < 0 || e.ColumnIndex != 1) return;
        DataGridView grid = (DataGridView)sender!;
        if (!short.TryParse(Convert.ToString(grid.Rows[e.RowIndex].Cells[1].Value), out short value)) return;
        ValueBinding binding = (ValueBinding)grid.Rows[e.RowIndex].Tag!;
        if (binding.IsCloneField)
        {
            if (_current.IsClone) _current.CloneAppearance[binding.Index] = value;
        }
        else
        {
            _current.BaseValues[binding.Index] = value;
        }
        UpdateSummaryAndStatus();
    }

    private void UpdateName(Action<PlayerStatsRecord> update)
    {
        if (_loading || _current == null) return;
        update(_current);
        UpdateSummaryAndStatus();
    }

    private void UpdateBaseValue(int index, short value)
    {
        if (_loading || _current == null) return;
        _current.BaseValues[index] = value;
        UpdateSummaryAndStatus();
    }

    private void UpdateChoice(ComboBox combo, int index)
    {
        if (_loading || _current == null || combo.SelectedItem is not ValueChoice choice) return;
        _current.BaseValues[index] = choice.Value;
        UpdateSummaryAndStatus();
    }

    private void UpdateSummaryAndStatus()
    {
        if (_current == null) return;
        _ratings.Text = $"Derived game ratings — Power: {_current.PowerRating}   Contact: {_current.ContactRating}   " +
                        $"Fielding: {_current.FieldingRating}   Running: {_current.RunningRating}   Pitching: {_current.PitchingRating}";
        int changed = _archive.ChangedPlayerCount;
        _status.Text = changed == 0 ? "No unsaved player changes."
            : $"{changed} player record{(changed == 1 ? string.Empty : "s")} changed.";
    }

    private void MaxCurrentSkills()
    {
        if (_current == null) return;
        _current.MaximizeSkills();
        _loading = true;
        LoadGridValues(_coreGrid);
        LoadGridValues(_pitchGrid);
        _loading = false;
        UpdateSummaryAndStatus();
    }

    private void ResetAll()
    {
        _archive.ResetAll();
        PopulatePlayerList();
        _status.Text = "All unsaved player changes were reset.";
    }

    private void Save_Click(object? sender, EventArgs e)
    {
        _coreGrid.EndEdit();
        _pitchGrid.EndEdit();
        _cloneGrid.EndEdit();
        int changed = _archive.ChangedPlayerCount;
        if (changed == 0)
        {
            MessageBox.Show(this, "No player records were changed.", "Nothing to Save",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            foreach (PlayerStatsRecord player in _archive.Players.Where(player => player.IsChanged)) player.Serialize();
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Invalid Player Record",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (MessageBox.Show(this,
                $"Write changes to {changed} player record{(changed == 1 ? string.Empty : "s")} in DATA.MET?\n\n" +
                "A timestamped backup will be created first.",
                "Save Player Changes", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;

        try
        {
            UseWaitCursor = true;
            Enabled = false;
            PlayerStatsSaveResult result = _archive.SaveWithBackup();
            string rebuild = result.RebuiltArchive ? "\nThe archive was resized with sector alignment preserved." : string.Empty;
            MessageBox.Show(this,
                $"Saved {result.ChangedPlayerCount} player record{(result.ChangedPlayerCount == 1 ? string.Empty : "s")}.\n\n" +
                $"Backup: {result.BackupPath}{rebuild}",
                "Player Changes Saved", MessageBoxButtons.OK, MessageBoxIcon.Information);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception exception)
        {
            MessageBox.Show(this,
                $"The player changes could not be saved. The archive was restored if a backup was created.\n\n{exception.Message}",
                "Unable to Save Players", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Enabled = true;
            UseWaitCursor = false;
        }
    }

    private static Panel Labeled(string label, Control control, int width = 165)
    {
        Panel panel = new() { Width = width, Height = 54, Margin = new Padding(3) };
        Label caption = new() { Text = label, Dock = DockStyle.Top, Height = 20 };
        control.Dock = DockStyle.Top;
        panel.Controls.Add(control);
        panel.Controls.Add(caption);
        return panel;
    }

    private static NumericUpDown CreateShortEditor(int width) => new()
    {
        Width = width,
        Minimum = short.MinValue,
        Maximum = short.MaxValue
    };

    private static ComboBox CreateChoiceBox(params ValueChoice[] choices)
    {
        ComboBox box = new() { Width = 100, DropDownStyle = ComboBoxStyle.DropDownList };
        box.Items.AddRange(choices.Cast<object>().ToArray());
        return box;
    }

    private static void SelectChoice(ComboBox box, short value)
    {
        box.SelectedItem = box.Items.Cast<ValueChoice>().FirstOrDefault(choice => choice.Value == value);
    }

    private sealed record StatDefinition(int Index, string Name, string Note);
    private sealed record CloneDefinition(int Index, string Name, string Note);
    private sealed record ValueBinding(bool IsCloneField, int Index, string Name, string Note);
    private sealed record ValueChoice(short Value, string Name)
    {
        public override string ToString() => Name;
    }
    private sealed record PlayerImageListItem(PlayerImageInfo Info)
    {
        public override string ToString() => Info.Label;
    }

    private sealed record PlayerListItem(PlayerStatsRecord Player)
    {
        public override string ToString() => Player.DisplayName + (Player.IsClone ? "  [clone]" : string.Empty);
    }
}
