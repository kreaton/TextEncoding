using TextEncoding.Core;

namespace TextEncoding;

internal sealed class MainForm : Form
{
    private const string AppTitle = "TextEncoding";
    private const int MaxPreviewChars = 2_000_000;
    private const string DropHint = "Drop a text file here, or click Browse…";

    private readonly TextBox _txtPath = new();
    private readonly Button _btnBrowse = new();
    private readonly Button _btnTestFiles = new();
    private readonly Button _btnAbout = new();
    private readonly Label _lblDetected = new();
    private readonly Label _lblReason = new();
    private readonly ComboBox _cboRead = new();
    private readonly TextBox _txtContent = new();
    private readonly CheckBox _chkWrap = new();
    private readonly ComboBox _cboTarget = new();
    private readonly CheckBox _chkBom = new();
    private readonly Button _btnSave = new();
    private readonly Button _btnSaveAs = new();
    private readonly ToolStripStatusLabel _status = new();
    private readonly ToolTip _tips = new();

    private string? _path;
    private DetectionResult? _detection;
    private bool _updating;
    private bool _busy;
    private TestFilesDialog? _testFiles;

    public MainForm(string? initialFile)
    {
        BuildUi();
        HookDragDrop(this);
        SetLoaded(false);

        if (initialFile is not null)
            Shown += (_, _) => LoadFile(initialFile);
    }

    // ---------------------------------------------------------------- UI construction

    private void BuildUi()
    {
        SuspendLayout();

        AutoScaleMode = AutoScaleMode.Dpi;
        Text = AppTitle;
        Icon = Program.AppIcon;
        MinimumSize = new Size(760, 520);
        ClientSize = new Size(940, 660);
        StartPosition = FormStartPosition.CenterScreen;

        // --- content (fill) -------------------------------------------------
        _txtContent.Multiline = true;
        _txtContent.ReadOnly = true;
        _txtContent.ScrollBars = ScrollBars.Both;
        _txtContent.WordWrap = false;
        _txtContent.Dock = DockStyle.Fill;
        _txtContent.Font = new Font("Consolas", 10f);
        _txtContent.BackColor = SystemColors.Window;
        _txtContent.MaxLength = 0;
        ShowHint();

        // --- top: file + detection -------------------------------------------
        var top = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 3,
            Padding = new Padding(10, 10, 10, 6),
        };
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        _txtPath.Dock = DockStyle.Fill;
        _txtPath.Margin = new Padding(3, 3, 3, 6);
        _txtPath.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            var p = _txtPath.Text.Trim().Trim('"');
            if (p.Length > 0) LoadFile(p);
        };

        _btnBrowse.Text = "Browse…";
        _btnBrowse.AutoSize = true;
        _btnBrowse.Click += (_, _) => Browse();

        _btnTestFiles.Text = "Test files…";
        _btnTestFiles.AutoSize = true;
        _btnTestFiles.Click += (_, _) => ShowTestFiles();

        _btnAbout.Text = "ⓘ";
        _btnAbout.AutoSize = true;
        _btnAbout.Font = new Font(Font.FontFamily, Font.Size * 1.2f);
        _btnAbout.Click += (_, _) =>
        {
            using var about = new AboutDialog();
            about.ShowDialog(this);
        };

        var fileButtons = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        fileButtons.Controls.Add(_btnBrowse);
        fileButtons.Controls.Add(_btnTestFiles);
        fileButtons.Controls.Add(_btnAbout);

        _lblDetected.AutoSize = true;
        _lblDetected.Font = new Font(Font, FontStyle.Bold);
        _lblDetected.Text = "—";

        _lblReason.AutoSize = false;
        _lblReason.Dock = DockStyle.Fill;
        _lblReason.Height = 36;
        _lblReason.ForeColor = SystemColors.GrayText;

        _cboRead.DropDownStyle = ComboBoxStyle.DropDownList;
        _cboRead.Width = 340;
        _cboRead.SelectedIndexChanged += (_, _) =>
        {
            if (_updating || _path is null) return;
            ShowPreview();
            UpdateBomDefault();
        };

        top.Controls.Add(Caption("File:"), 0, 0);
        top.Controls.Add(_txtPath, 1, 0);
        top.Controls.Add(fileButtons, 2, 0);

        top.Controls.Add(Caption("Detected:"), 0, 1);
        top.Controls.Add(_lblDetected, 1, 1);
        top.SetColumnSpan(_lblDetected, 2);

        top.Controls.Add(_lblReason, 1, 2);
        top.SetColumnSpan(_lblReason, 2);

        top.Controls.Add(Caption("Read as:"), 0, 3);
        top.Controls.Add(_cboRead, 1, 3);
        top.SetColumnSpan(_cboRead, 2);

        // --- bottom: save as -------------------------------------------------
        var bottom = new TableLayoutPanel
        {
            Dock = DockStyle.Bottom,
            AutoSize = true,
            ColumnCount = 5,
            Padding = new Padding(10, 6, 10, 10),
        };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        _cboTarget.DropDownStyle = ComboBoxStyle.DropDownList;
        _cboTarget.Width = 300;
        _cboTarget.SelectedIndexChanged += (_, _) =>
        {
            if (!_updating) UpdateBomDefault();
        };

        foreach (var e in EncodingCatalog.All)
        {
            _cboRead.Items.Add(e);
            _cboTarget.Items.Add(e);
        }

        _chkBom.Text = "Write BOM";
        _chkBom.AutoSize = true;
        _chkBom.Margin = new Padding(12, 6, 3, 3);

        _chkWrap.Text = "Wrap lines";
        _chkWrap.AutoSize = true;
        _chkWrap.CheckedChanged += (_, _) =>
        {
            _txtContent.WordWrap = _chkWrap.Checked;
            _txtContent.ScrollBars = _chkWrap.Checked ? ScrollBars.Vertical : ScrollBars.Both;
        };

        _btnSave.Text = "Save";
        _btnSave.AutoSize = true;
        _btnSave.Click += (_, _) => SaveOverwrite();

        _btnSaveAs.Text = "Save As…";
        _btnSaveAs.AutoSize = true;
        _btnSaveAs.Click += (_, _) => SaveAs();

        var buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        buttons.Controls.Add(_chkWrap);
        _chkWrap.Margin = new Padding(3, 6, 12, 3);
        buttons.Controls.Add(_btnSave);
        buttons.Controls.Add(_btnSaveAs);

        bottom.Controls.Add(Caption("Save as:"), 0, 0);
        bottom.Controls.Add(_cboTarget, 1, 0);
        bottom.Controls.Add(_chkBom, 2, 0);
        bottom.Controls.Add(buttons, 4, 0);

        _tips.SetToolTip(_btnSave, "Overwrite the opened file using the selected encoding");
        _tips.SetToolTip(_btnSaveAs, "Write the converted text to another file");
        _tips.SetToolTip(_chkBom, "Byte order mark: a few bytes at the start of the file that identify Unicode encodings");
        _tips.SetToolTip(_btnAbout, "About TextEncoding, and check for updates");
        _tips.SetToolTip(_btnTestFiles,"Write text you type as a set of files in different encodings");
        _tips.SetToolTip(_cboRead, "If the text looks wrong, choose the encoding the file was really written in");

        // --- status ------------------------------------------------------------
        var statusStrip = new StatusStrip();
        _status.Spring = true;
        _status.TextAlign = ContentAlignment.MiddleLeft;
        statusStrip.Items.Add(_status);

        // Fill control first, then the edge-docked ones (later = docked first = outermost).
        Controls.Add(_txtContent);
        Controls.Add(top);
        Controls.Add(bottom);
        Controls.Add(statusStrip);

        ResumeLayout(false);
        PerformLayout();
    }

    private static Label Caption(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Anchor = AnchorStyles.Left,
        Margin = new Padding(3, 6, 8, 6),
    };

    private void ShowHint()
    {
        _txtContent.ForeColor = SystemColors.GrayText;
        _txtContent.Text = DropHint;
    }

    // ---------------------------------------------------------------- drag & drop

    private void HookDragDrop(Control control)
    {
        control.AllowDrop = true;
        control.DragEnter += OnDragEnter;
        control.DragDrop += OnDragDrop;
        foreach (Control child in control.Controls)
            HookDragDrop(child);
    }

    private static string[]? DroppedFiles(DragEventArgs e) =>
        e.Data?.GetDataPresent(DataFormats.FileDrop) == true ? e.Data.GetData(DataFormats.FileDrop) as string[] : null;

    private static void OnDragEnter(object? sender, DragEventArgs e)
    {
        var files = DroppedFiles(e);
        e.Effect = files is { Length: > 0 } && File.Exists(files[0]) ? DragDropEffects.Copy : DragDropEffects.None;
    }

    private void OnDragDrop(object? sender, DragEventArgs e)
    {
        var file = DroppedFiles(e)?.FirstOrDefault(File.Exists);
        if (file is not null) LoadFile(file);
    }

    // ---------------------------------------------------------------- opening

    private void Browse()
    {
        using var dlg = new OpenFileDialog
        {
            Title = "Open text file",
            Filter = "All files (*.*)|*.*|Text files|*.txt;*.csv;*.log;*.md;*.srt;*.xml;*.json;*.ini;*.html;*.htm",
            CheckFileExists = true,
        };
        if (_path is not null) dlg.InitialDirectory = Path.GetDirectoryName(_path);
        if (dlg.ShowDialog(this) == DialogResult.OK) LoadFile(dlg.FileName);
    }

    private void LoadFile(string path)
    {
        if (_busy) return;

        string full;
        DetectionResult detection;
        try
        {
            full = Path.GetFullPath(path);
            detection = EncodingDetector.DetectFile(full); // streams the file; never loads it all
        }
        catch (Exception ex)
        {
            ShowError($"Can't open the file:\n\n{ex.Message}");
            return;
        }

        _path = full;
        _detection = detection;

        _updating = true;
        _txtPath.Text = full;
        SelectEncoding(_cboRead, detection.Encoding);
        SelectEncoding(_cboTarget, detection.Encoding);
        _updating = false;

        _lblDetected.Text = $"{detection.Description}   ({detection.Confidence} confidence)";
        _lblReason.Text = detection.Reason;
        Text = $"{AppTitle} – {Path.GetFileName(full)}";

        SetLoaded(true);
        ShowPreview();
        UpdateBomDefault();
    }

    private static void SelectEncoding(ComboBox box, EncodingEntry entry)
    {
        for (var i = 0; i < box.Items.Count; i++)
        {
            if (((EncodingEntry)box.Items[i]!).CodePage != entry.CodePage) continue;
            box.SelectedIndex = i;
            return;
        }

        box.SelectedIndex = box.Items.Add(entry);
    }

    private void SetLoaded(bool loaded)
    {
        _cboRead.Enabled = loaded;
        _cboTarget.Enabled = loaded;
        _btnSave.Enabled = loaded;
        _btnSaveAs.Enabled = loaded;
        _chkBom.Enabled = loaded;
    }

    // ---------------------------------------------------------------- test files

    /// <summary>Opens (or brings back) the modeless "Create test files" window.</summary>
    private void ShowTestFiles()
    {
        if (_testFiles is { IsDisposed: false })
        {
            _testFiles.Activate();
            return;
        }

        var folder = _path is not null ? Path.GetDirectoryName(_path) : null;
        _testFiles = new TestFilesDialog(folder, file =>
        {
            LoadFile(file);
            Activate();
        });
        _testFiles.FormClosed += (_, _) => _testFiles = null;
        _testFiles.Show(this);
    }

    // ---------------------------------------------------------------- showing text

    private EncodingEntry ReadEncoding => (EncodingEntry)_cboRead.SelectedItem!;
    private EncodingEntry TargetEncoding => (EncodingEntry)_cboTarget.SelectedItem!;
    private bool WantBom => _chkBom.Enabled && _chkBom.Checked;

    /// <summary>Shows the start of the file decoded with the "Read as" encoding. Only the preview is held in memory.</summary>
    private void ShowPreview()
    {
        if (_path is null) return;

        string preview;
        bool truncated;
        long size;
        try
        {
            using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            size = stream.Length;
            using var reader = EncodingConverter.OpenReader(stream, ReadEncoding, replaceInvalid: true, out _);

            var buffer = new char[MaxPreviewChars + 1];
            var n = reader.ReadBlock(buffer, 0, buffer.Length);
            truncated = n > MaxPreviewChars;
            preview = new string(buffer, 0, Math.Min(n, MaxPreviewChars));
        }
        catch (Exception ex)
        {
            ShowError($"Can't read the file:\n\n{ex.Message}");
            return;
        }

        var hadInvalid = preview.Contains('\uFFFD');

        // A WinForms TextBox needs CRLF to break lines and stops at the first NUL.
        var shown = preview.Replace("\r\n", "\n").Replace('\r', '\n').Replace("\n", "\r\n").Replace('\0', '\u2400');

        _txtContent.ForeColor = SystemColors.WindowText;
        _txtContent.Text = shown;
        _txtContent.Select(0, 0);

        var parts = new List<string> { $"{size:N0} bytes", $"read as {ReadEncoding.DisplayName}" };
        if (_detection is not null && ReadEncoding.CodePage != _detection.Encoding.CodePage)
            parts.Add("overriding detection");
        if (hadInvalid)
            parts.Add("� = bytes that are invalid in this encoding");
        if (truncated)
            parts.Add($"preview limited to the first {MaxPreviewChars:N0} characters (saving converts the whole file)");
        _status.Text = string.Join("  ·  ", parts);
    }

    private void UpdateBomDefault()
    {
        if (_path is null) return;

        var target = TargetEncoding;
        var sourceHadBom = false;
        try { sourceHadBom = EncodingConverter.HasBom(_path, ReadEncoding); }
        catch (IOException) { /* keep the default */ }

        _chkBom.Enabled = target.SupportsBom && !_busy;
        _chkBom.Checked = target.SupportsBom && EncodingConverter.DefaultBom(sourceHadBom, ReadEncoding, target);
    }

    // ---------------------------------------------------------------- saving

    private async void SaveOverwrite()
    {
        if (_path is null) return;

        var confirm = MessageBox.Show(this,
            $"Overwrite \"{Path.GetFileName(_path)}\" with {TargetEncoding.DisplayName}{(WantBom ? " (with BOM)" : "")}?",
            AppTitle, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
        if (confirm != DialogResult.Yes) return;

        await ConvertAsync(_path);
    }

    private async void SaveAs()
    {
        if (_path is null) return;

        // Default to the source's extension (".txt" if it has none): offered as the first filter, and added
        // automatically when you type a name without an extension.
        var extension = Path.GetExtension(_path);
        if (extension.Length <= 1) extension = ".txt";

        using var dlg = new SaveFileDialog
        {
            Title = "Save converted file",
            InitialDirectory = Path.GetDirectoryName(_path),
            FileName = Path.GetFileNameWithoutExtension(_path) + extension,
            DefaultExt = extension[1..],
            AddExtension = true,
            OverwritePrompt = true,
            Filter = $"{extension[1..].ToUpperInvariant()} files (*{extension})|*{extension}|All files (*.*)|*.*",
            FilterIndex = 1,
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        await ConvertAsync(dlg.FileName);
    }

    /// <summary>
    /// Converts the open file to <paramref name="destination"/> on a worker thread using the streaming
    /// converter. If the content can't be converted losslessly the user is asked whether to replace the
    /// offending characters and the conversion is run again.
    /// </summary>
    private async Task ConvertAsync(string destination)
    {
        var source = _path!;
        var read = ReadEncoding;
        var target = TargetEncoding;
        var bom = WantBom;

        var options = new ConvertOptions
        {
            Force = true, // overwriting was already confirmed (Save) or prompted by the dialog (Save As)
            SourceEncoding = read.Name,
            WriteBom = bom,
        };

        SetBusy(true);
        try
        {
            ConversionResult result;
            try
            {
                result = await Task.Run(() => EncodingConverter.ConvertFile(source, destination, target.Name, options));
            }
            catch (ConversionException ex) when (ex.IsDataError)
            {
                var answer = MessageBox.Show(this,
                    ex.Message + "\n\nReplace such characters ('?' when encoding, '�' when decoding) and save anyway?",
                    AppTitle, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
                if (answer != DialogResult.Yes)
                {
                    _status.Text = "Not saved.";
                    return;
                }

                var lossy = options with { ReplaceUnmappable = true };
                result = await Task.Run(() => EncodingConverter.ConvertFile(source, destination, target.Name, lossy));
            }

            var message = $"Saved \"{destination}\" as {target.DisplayName}{(result.WroteBom ? " with BOM" : "")} " +
                          $"({result.BytesWritten:N0} bytes){(result.HadReplacements ? " – some characters were replaced" : "")}.";

            // When the opened file itself was rewritten, reload it so the window shows what is on disk now.
            SetBusy(false);
            if (string.Equals(Path.GetFullPath(destination), source, StringComparison.OrdinalIgnoreCase))
                LoadFile(destination);

            _status.Text = message;
        }
        catch (Exception ex)
        {
            ShowError($"Can't save the file:\n\n{ex.Message}");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        UseWaitCursor = busy;
        _cboRead.Enabled = !busy && _path is not null;
        _cboTarget.Enabled = !busy && _path is not null;
        _btnSave.Enabled = !busy && _path is not null;
        _btnSaveAs.Enabled = !busy && _path is not null;
        _btnBrowse.Enabled = !busy;
        _btnAbout.Enabled = !busy; // an update restarts the app, which would cut a conversion short
        _txtPath.Enabled = !busy;
        if (busy)
        {
            _chkBom.Enabled = false;
            _status.Text = "Converting…";
        }
        else if (_path is not null)
        {
            _chkBom.Enabled = TargetEncoding.SupportsBom;
        }
    }

    private void ShowError(string message) =>
        MessageBox.Show(this, message, AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
}
