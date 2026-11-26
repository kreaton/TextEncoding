using System.Diagnostics;
using TextEncoding.Core;

namespace TextEncoding;

/// <summary>
/// Writes text you type into a folder as a set of files, one per selected encoding (see
/// <see cref="TestFileGenerator"/>). Modeless: double-click a created file to open it in the main window.
/// </summary>
internal sealed class TestFilesDialog : Form
{
    // Remembered while the app runs, so reopening the dialog continues where you left off.
    private static string? s_lastText;
    private static string? s_lastFolder;
    private static string s_lastBaseName = "sample";
    private static HashSet<TestFileSpec>? s_lastChecked;
    private static int s_lastLineEndings;
    private static int s_lastPolicy;

    private readonly Action<string> _openFile;

    private readonly TextBox _txtText = new();
    private readonly ListView _list = new();
    private readonly TextBox _txtFolder = new();
    private readonly TextBox _txtBaseName = new();
    private readonly ComboBox _cboLineEndings = new();
    private readonly ComboBox _cboPolicy = new();
    private readonly Button _btnCreate = new();
    private readonly Button _btnOpenFolder = new();
    private readonly Label _lblResult = new();
    private readonly System.Windows.Forms.Timer _refreshTimer = new() { Interval = 250 };

    private const int ColEncoding = 0, ColFile = 1, ColMissing = 2, ColResult = 3;

    public TestFilesDialog(string? initialFolder, Action<string> openFile)
    {
        _openFile = openFile;
        BuildUi();

        _txtText.Text = s_lastText ?? TestFileGenerator.SampleText;
        _txtFolder.Text = s_lastFolder ?? initialFolder ?? DefaultFolder();
        _txtBaseName.Text = s_lastBaseName;
        _cboLineEndings.SelectedIndex = s_lastLineEndings;
        _cboPolicy.SelectedIndex = s_lastPolicy;

        FillList(s_lastChecked ?? [.. TestFileGenerator.DefaultSpecs]);
        RefreshPreview();

        // The ListView re-inserts its items when its handle is created and raises ItemChecked midway
        // (with some slots still null), so settle the button state once the window is up.
        Shown += (_, _) => UpdateCreateButton();

        _refreshTimer.Tick += (_, _) =>
        {
            _refreshTimer.Stop();
            RefreshPreview();
        };
        FormClosed += (_, _) =>
        {
            Remember();
            _refreshTimer.Dispose();
        };
    }

    private static string DefaultFolder() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "TextEncoding test files");

    // ---------------------------------------------------------------- UI

    private void BuildUi()
    {
        SuspendLayout();

        AutoScaleMode = AutoScaleMode.Dpi;
        Text = "Create test files";
        Icon = Program.AppIcon;
        MinimumSize = new Size(720, 560);
        ClientSize = new Size(900, 700);
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MinimizeBox = false;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            Padding = new Padding(10),
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // text caption
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 40));   // text
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // encodings caption + buttons
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 60));   // encodings
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // options
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // buttons

        // --- text ----------------------------------------------------------------
        _txtText.Multiline = true;
        _txtText.AcceptsReturn = true;
        _txtText.AcceptsTab = true;
        _txtText.ScrollBars = ScrollBars.Both;
        _txtText.WordWrap = false;
        _txtText.Dock = DockStyle.Fill;
        _txtText.Font = new Font("Consolas", 11f);
        _txtText.MaxLength = 0;
        _txtText.TextChanged += (_, _) =>
        {
            _refreshTimer.Stop();
            _refreshTimer.Start();
        };

        var textHeader = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        textHeader.Controls.Add(Caption("Text to write (type or paste anything):"));
        textHeader.Controls.Add(LinkButton("Insert Danish / Swedish / German sample", () =>
        {
            _txtText.SelectedText = TestFileGenerator.SampleText;
            _txtText.Focus();
        }));

        // --- encodings -----------------------------------------------------------
        _list.View = View.Details;
        _list.CheckBoxes = true;
        _list.FullRowSelect = true;
        _list.HideSelection = false;
        _list.Dock = DockStyle.Fill;
        _list.Columns.Add("Encoding", 280);
        _list.Columns.Add("File name", 230);
        _list.Columns.Add("Can't hold", 150);
        _list.Columns.Add("Result", 170);
        _list.ItemChecked += (_, _) => UpdateCreateButton();
        _list.ItemActivate += (_, _) => OpenSelected();

        var listHeader = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        listHeader.Controls.Add(Caption("Encodings:"));
        listHeader.Controls.Add(LinkButton("Defaults", () => SetChecked(TestFileGenerator.DefaultSpecs.Contains)));
        listHeader.Controls.Add(LinkButton("Unicode", () => SetChecked(s => s.Encoding.SupportsBom)));
        listHeader.Controls.Add(LinkButton("All", () => SetChecked(_ => true)));
        listHeader.Controls.Add(LinkButton("None", () => SetChecked(_ => false)));
        listHeader.Controls.Add(LinkButton("Only those that fit the text", () =>
            SetChecked(s => TestFileGenerator.FindUnmappable(CurrentText(), s.Encoding).Count == 0)));

        // --- options -------------------------------------------------------------
        var options = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 5, Margin = new Padding(0, 6, 0, 0) };
        options.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        options.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        options.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        options.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        options.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        _txtFolder.Dock = DockStyle.Fill;
        var btnBrowse = new Button { Text = "Browse…", AutoSize = true };
        btnBrowse.Click += (_, _) => BrowseFolder();

        _txtBaseName.Width = 160;
        _txtBaseName.TextChanged += (_, _) => RefreshFileNames();

        _cboLineEndings.DropDownStyle = ComboBoxStyle.DropDownList;
        _cboLineEndings.Items.AddRange(["CR LF (Windows)", "LF (Unix, macOS)"]);
        _cboLineEndings.Width = 160;

        _cboPolicy.DropDownStyle = ComboBoxStyle.DropDownList;
        _cboPolicy.Items.AddRange(["Skip that file", "Write '?' instead"]);
        _cboPolicy.Width = 160;

        options.Controls.Add(Caption("Folder:"), 0, 0);
        options.Controls.Add(_txtFolder, 1, 0);
        options.SetColumnSpan(_txtFolder, 3);
        options.Controls.Add(btnBrowse, 4, 0);

        options.Controls.Add(Caption("File name:"), 0, 1);
        var nameRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        nameRow.Controls.Add(_txtBaseName);
        nameRow.Controls.Add(new Label
        {
            Text = "-<encoding>.txt",
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(0, 6, 3, 3),
        });
        options.Controls.Add(nameRow, 1, 1);
        options.SetColumnSpan(nameRow, 4);

        options.Controls.Add(Caption("Line endings:"), 0, 2);
        var row3 = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        row3.Controls.Add(_cboLineEndings);
        row3.Controls.Add(Caption("Characters an encoding can't hold:"));
        row3.Controls.Add(_cboPolicy);
        options.Controls.Add(row3, 1, 2);
        options.SetColumnSpan(row3, 4);

        // --- buttons -------------------------------------------------------------
        var bottom = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 4, Margin = new Padding(0, 8, 0, 0) };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        _lblResult.AutoSize = true;
        _lblResult.Anchor = AnchorStyles.Left;
        _lblResult.ForeColor = SystemColors.GrayText;
        _lblResult.Text = "Double-click a created file to open it in the main window.";

        _btnCreate.Text = "Create files";
        _btnCreate.AutoSize = true;
        _btnCreate.Click += (_, _) => Create();

        _btnOpenFolder.Text = "Open folder";
        _btnOpenFolder.AutoSize = true;
        _btnOpenFolder.Click += (_, _) => OpenFolder();

        var btnClose = new Button { Text = "Close", AutoSize = true };
        btnClose.Click += (_, _) => Close();
        CancelButton = btnClose;

        bottom.Controls.Add(_lblResult, 0, 0);
        bottom.Controls.Add(_btnCreate, 1, 0);
        bottom.Controls.Add(_btnOpenFolder, 2, 0);
        bottom.Controls.Add(btnClose, 3, 0);

        root.Controls.Add(textHeader, 0, 0);
        root.Controls.Add(_txtText, 0, 1);
        root.Controls.Add(listHeader, 0, 2);
        root.Controls.Add(_list, 0, 3);
        root.Controls.Add(options, 0, 4);
        root.Controls.Add(bottom, 0, 5);
        Controls.Add(root);

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

    private static LinkLabel LinkButton(string text, Action onClick)
    {
        var link = new LinkLabel { Text = text, AutoSize = true, Margin = new Padding(6, 6, 6, 6) };
        link.LinkClicked += (_, _) => onClick();
        return link;
    }

    // ---------------------------------------------------------------- list

    private void FillList(IReadOnlySet<TestFileSpec> checkedSpecs)
    {
        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var spec in TestFileGenerator.AllSpecs)
        {
            var item = new ListViewItem([spec.DisplayName, "", "", ""]) { Tag = spec, Checked = checkedSpecs.Contains(spec) };
            _list.Items.Add(item);
        }
        _list.EndUpdate();
    }

    // OfType, not Cast: while the handle is being created the collection can briefly contain nulls.
    private IEnumerable<ListViewItem> Items => _list.Items.OfType<ListViewItem>();

    private static TestFileSpec SpecOf(ListViewItem item) => (TestFileSpec)item.Tag!;

    private void SetChecked(Func<TestFileSpec, bool> predicate)
    {
        _list.BeginUpdate();
        foreach (var item in Items) item.Checked = predicate(SpecOf(item));
        _list.EndUpdate();
    }

    private string CurrentText() =>
        TestFileGenerator.NormalizeLineEndings(_txtText.Text,
            _cboLineEndings.SelectedIndex == 1 ? LineEndings.Lf : LineEndings.CrLf);

    /// <summary>Updates the "Can't hold" column for the current text and clears old results.</summary>
    private void RefreshPreview()
    {
        var text = CurrentText();
        var missingCache = new Dictionary<int, IReadOnlyList<string>>();

        _list.BeginUpdate();
        foreach (var item in Items)
        {
            var spec = SpecOf(item);
            if (!missingCache.TryGetValue(spec.Encoding.CodePage, out var missing))
                missingCache[spec.Encoding.CodePage] = missing = TestFileGenerator.FindUnmappable(text, spec.Encoding);

            item.SubItems[ColMissing].Text = Summarize(missing);
            item.SubItems[ColResult].Text = "";
            item.ForeColor = missing.Count > 0 ? SystemColors.GrayText : SystemColors.WindowText;
        }
        _list.EndUpdate();

        RefreshFileNames();
        UpdateCreateButton();
    }

    private void RefreshFileNames()
    {
        var baseName = _txtBaseName.Text.Trim();
        _list.BeginUpdate();
        foreach (var item in Items)
            item.SubItems[ColFile].Text = baseName.Length > 0 ? TestFileGenerator.FileName(baseName, SpecOf(item)) : "";
        _list.EndUpdate();
    }

    private static string Summarize(IReadOnlyList<string> missing) =>
        missing.Count == 0 ? "" : string.Join(" ", missing.Take(8)) + (missing.Count > 8 ? $" (+{missing.Count - 8})" : "");

    private void UpdateCreateButton() => _btnCreate.Enabled = Items.Any(i => i.Checked) && _txtText.TextLength > 0;

    // ---------------------------------------------------------------- actions

    private void BrowseFolder()
    {
        using var dlg = new FolderBrowserDialog
        {
            Description = "Folder for the test files",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true,
        };
        var current = _txtFolder.Text.Trim();
        if (Directory.Exists(current)) dlg.InitialDirectory = current;
        if (dlg.ShowDialog(this) == DialogResult.OK) _txtFolder.Text = dlg.SelectedPath;
    }

    private void Create()
    {
        RefreshPreview(); // in case the debounce timer hasn't fired yet

        var folder = _txtFolder.Text.Trim().Trim('"');
        if (folder.Length == 0)
        {
            ShowError("Choose a folder.");
            return;
        }

        var baseName = _txtBaseName.Text.Trim();
        var checkedItems = Items.Where(i => i.Checked).ToList();
        var specs = checkedItems.Select(SpecOf).ToList();

        var options = new TestFileOptions
        {
            LineEndings = _cboLineEndings.SelectedIndex == 1 ? LineEndings.Lf : LineEndings.CrLf,
            Unmappable = _cboPolicy.SelectedIndex == 1 ? UnmappablePolicy.Replace : UnmappablePolicy.Skip,
        };

        try
        {
            if (baseName.Length > 0 && baseName.IndexOfAny(Path.GetInvalidFileNameChars()) < 0)
            {
                var existing = specs.Count(s => File.Exists(Path.Combine(folder, TestFileGenerator.FileName(baseName, s))));
                if (existing > 0)
                {
                    var answer = MessageBox.Show(this,
                        $"{existing} of the {specs.Count} files already exist in\n{folder}\n\nOverwrite them?",
                        Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
                    if (answer != DialogResult.Yes) return;
                    options = options with { Overwrite = true };
                }
            }

            var results = TestFileGenerator.Generate(folder, baseName, _txtText.Text, specs, options);
            ShowResults(checkedItems, results);
        }
        catch (Exception ex) when (ex is ConversionException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            ShowError($"Can't create the files:\n\n{ex.Message}");
        }
    }

    private void ShowResults(List<ListViewItem> items, IReadOnlyList<GeneratedTestFile> results)
    {
        _list.BeginUpdate();
        foreach (var item in Items) item.SubItems[ColResult].Text = "";
        for (var i = 0; i < items.Count; i++)
        {
            var r = results[i];
            items[i].SubItems[ColResult].Text = r.Outcome switch
            {
                TestFileOutcome.Written => $"written, {r.Bytes:N0} bytes",
                TestFileOutcome.WrittenWithReplacements => $"written with '?', {r.Bytes:N0} bytes",
                _ => "skipped",
            };
        }
        _list.EndUpdate();

        var written = results.Count(r => r.Outcome != TestFileOutcome.Skipped);
        var replaced = results.Count(r => r.Outcome == TestFileOutcome.WrittenWithReplacements);
        var skipped = results.Count - written;

        var parts = new List<string> { $"{written} file{(written == 1 ? "" : "s")} written" };
        if (replaced > 0) parts.Add($"{replaced} with '?' replacements");
        if (skipped > 0) parts.Add($"{skipped} skipped (the encoding can't hold some characters)");
        _lblResult.ForeColor = SystemColors.ControlText;
        _lblResult.Text = string.Join(", ", parts) + ". Double-click one to open it.";
    }

    private void OpenSelected()
    {
        if (_list.SelectedItems.Count == 0) return;
        var item = _list.SelectedItems[0];
        var name = item.SubItems[ColFile].Text;
        if (name.Length == 0) return;

        var path = Path.Combine(_txtFolder.Text.Trim().Trim('"'), name);
        if (File.Exists(path)) _openFile(path);
        else _lblResult.Text = $"{name} hasn't been created.";
    }

    private void OpenFolder()
    {
        var folder = _txtFolder.Text.Trim().Trim('"');
        if (!Directory.Exists(folder))
        {
            ShowError($"The folder doesn't exist yet:\n{folder}");
            return;
        }

        Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
    }

    private void Remember()
    {
        s_lastText = _txtText.Text;
        s_lastFolder = _txtFolder.Text.Trim();
        s_lastBaseName = _txtBaseName.Text.Trim() is { Length: > 0 } n ? n : "sample";
        s_lastChecked = [.. Items.Where(i => i.Checked).Select(SpecOf)];
        s_lastLineEndings = Math.Max(0, _cboLineEndings.SelectedIndex);
        s_lastPolicy = Math.Max(0, _cboPolicy.SelectedIndex);
    }

    private void ShowError(string message) =>
        MessageBox.Show(this, message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
}
