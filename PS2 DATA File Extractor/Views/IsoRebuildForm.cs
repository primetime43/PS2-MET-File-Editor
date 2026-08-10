using PS2_DATA_File_Extractor.FileOperations;

namespace PS2_DATA_File_Extractor;

public sealed class IsoRebuildForm : Form
{
    private readonly TextBox _sourceFolder = new TextBox();
    private readonly TextBox _outputIso = new TextBox();
    private readonly TextBox _volumeLabel = new TextBox();
    private readonly TextBox _imgBurnPath = new TextBox();
    private readonly Label _validation = new Label();
    private readonly Label _status = new Label();
    private readonly ProgressBar _progress = new ProgressBar();
    private readonly Button _build = new Button();
    private bool _isBuilding;

    public IsoRebuildForm(string? initialSourceDirectory = null)
    {
        Text = "Rebuild PS2 Game ISO";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(780, 440);
        MinimumSize = new Size(680, 420);

        TableLayoutPanel layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 8,
            Padding = new Padding(12)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 125));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));

        Label instructions = new Label
        {
            Dock = DockStyle.Fill,
            Text = "Build an ISO9660 + UDF 1.02 image from the extracted game folder. " +
                   "The folder must contain SYSTEM.CNF and DATA.MET; the boot executable is validated from SYSTEM.CNF."
        };
        layout.Controls.Add(instructions, 0, 0);
        layout.SetColumnSpan(instructions, 3);

        AddPathRow(layout, 1, "Game folder:", _sourceFolder, "Browse...", BrowseSource_Click);
        AddPathRow(layout, 2, "Output ISO:", _outputIso, "Browse...", BrowseOutput_Click);
        AddPathRow(layout, 3, "Volume label:", _volumeLabel, null, null);
        AddPathRow(layout, 4, "ImgBurn:", _imgBurnPath, "Browse...", BrowseImgBurn_Click);

        _validation.Dock = DockStyle.Fill;
        _validation.AutoEllipsis = true;
        _validation.Padding = new Padding(4, 8, 4, 4);
        layout.Controls.Add(_validation, 0, 5);
        layout.SetColumnSpan(_validation, 3);

        Panel statusPanel = new Panel { Dock = DockStyle.Fill };
        _status.Dock = DockStyle.Top;
        _status.Height = 44;
        _status.Text = "Ready.";
        _progress.Dock = DockStyle.Top;
        _progress.Height = 22;
        _progress.Style = ProgressBarStyle.Blocks;
        statusPanel.Controls.Add(_progress);
        statusPanel.Controls.Add(_status);
        layout.Controls.Add(statusPanel, 0, 6);
        layout.SetColumnSpan(statusPanel, 3);

        FlowLayoutPanel buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(0, 8, 0, 0)
        };
        Button close = new Button { Text = "Close", AutoSize = true, DialogResult = DialogResult.Cancel };
        _build.Text = "Build ISO";
        _build.AutoSize = true;
        _build.Click += Build_Click;
        buttons.Controls.Add(close);
        buttons.Controls.Add(_build);
        layout.Controls.Add(buttons, 0, 7);
        layout.SetColumnSpan(buttons, 3);

        Controls.Add(layout);
        CancelButton = close;
        FormClosing += IsoRebuildForm_FormClosing;

        _imgBurnPath.Text = IsoBuildService.FindImgBurn() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(initialSourceDirectory) &&
            Directory.Exists(initialSourceDirectory))
        {
            SetSourceDirectory(initialSourceDirectory);
        }
        else
        {
            _volumeLabel.Text = "BACKYARD_BASEBALL";
        }
    }

    private static void AddPathRow(
        TableLayoutPanel layout,
        int row,
        string labelText,
        TextBox textBox,
        string? buttonText,
        EventHandler? handler)
    {
        Label label = new Label
        {
            Text = labelText,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        };
        textBox.Dock = DockStyle.Fill;
        layout.Controls.Add(label, 0, row);
        layout.Controls.Add(textBox, 1, row);

        if (buttonText != null && handler != null)
        {
            Button button = new Button { Text = buttonText, Dock = DockStyle.Fill };
            button.Click += handler;
            layout.Controls.Add(button, 2, row);
        }
        else
        {
            layout.SetColumnSpan(textBox, 2);
        }
    }

    private void BrowseSource_Click(object? sender, EventArgs e)
    {
        using FolderBrowserDialog dialog = new FolderBrowserDialog
        {
            Description = "Select the extracted Backyard Baseball game folder",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false,
            SelectedPath = Directory.Exists(_sourceFolder.Text) ? _sourceFolder.Text : string.Empty
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            SetSourceDirectory(dialog.SelectedPath);
        }
    }

    private void SetSourceDirectory(string directory)
    {
        _sourceFolder.Text = Path.GetFullPath(directory);
        _volumeLabel.Text = IsoBuildService.NormalizeVolumeLabel(null, directory);

        DirectoryInfo source = new DirectoryInfo(directory);
        string parent = source.Parent?.FullName ?? source.FullName;
        _outputIso.Text = Path.Combine(parent, $"{source.Name}-modded.iso");

        try
        {
            GameFolderValidation validation = IsoBuildService.ValidateGameFolder(directory);
            _validation.ForeColor = Color.DarkGreen;
            _validation.Text =
                $"Valid game folder | Boot: {Path.GetFileName(validation.BootExecutablePath)}";
        }
        catch (Exception exception)
        {
            _validation.ForeColor = Color.DarkRed;
            _validation.Text = exception.Message;
        }
    }

    private void BrowseOutput_Click(object? sender, EventArgs e)
    {
        using SaveFileDialog dialog = new SaveFileDialog
        {
            Title = "Save rebuilt PS2 ISO",
            Filter = "ISO images (*.iso)|*.iso",
            DefaultExt = "iso",
            AddExtension = true,
            OverwritePrompt = true,
            FileName = Path.GetFileName(_outputIso.Text),
            InitialDirectory = Path.GetDirectoryName(_outputIso.Text)
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _outputIso.Text = dialog.FileName;
        }
    }

    private void BrowseImgBurn_Click(object? sender, EventArgs e)
    {
        using OpenFileDialog dialog = new OpenFileDialog
        {
            Title = "Locate ImgBurn.exe",
            Filter = "ImgBurn (ImgBurn.exe)|ImgBurn.exe|Executables (*.exe)|*.exe",
            FileName = "ImgBurn.exe"
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _imgBurnPath.Text = dialog.FileName;
        }
    }

    private async void Build_Click(object? sender, EventArgs e)
    {
        if (File.Exists(_outputIso.Text))
        {
            DialogResult overwrite = MessageBox.Show(
                this,
                "The output ISO already exists. It will be moved to a timestamped backup before building. Continue?",
                "Replace Existing ISO",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);
            if (overwrite != DialogResult.Yes)
            {
                return;
            }
        }

        IsoBuildRequest request = new IsoBuildRequest(
            _sourceFolder.Text,
            _outputIso.Text,
            _volumeLabel.Text,
            _imgBurnPath.Text);

        SetBuildingState(true);
        try
        {
            Progress<string> progress = new Progress<string>(message => _status.Text = message);
            IsoBuildResult result = await IsoBuildService.BuildAsync(request, progress);
            string backupMessage = result.PreviousImageBackupPath == null
                ? string.Empty
                : $"\n\nPrevious ISO backup: {result.PreviousImageBackupPath}";
            _status.Text = $"ISO created successfully: {result.ImageSize:N0} bytes";
            MessageBox.Show(
                this,
                $"ISO created and validated.\n\n{result.OutputPath}{backupMessage}",
                "ISO Build Complete",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception exception)
        {
            _status.Text = "ISO build failed.";
            MessageBox.Show(
                this,
                exception.Message,
                "Unable to Build ISO",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            SetBuildingState(false);
        }
    }

    private void SetBuildingState(bool isBuilding)
    {
        _isBuilding = isBuilding;
        _build.Enabled = !isBuilding;
        _progress.Style = isBuilding ? ProgressBarStyle.Marquee : ProgressBarStyle.Blocks;
    }

    private void IsoRebuildForm_FormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_isBuilding)
        {
            e.Cancel = true;
            MessageBox.Show(
                this,
                "ImgBurn is still building the image. Close it or wait for it to finish.",
                "Build In Progress",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
    }
}
