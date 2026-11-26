using System.Diagnostics;
using Velopack;
using Velopack.Sources;

namespace TextEncoding;

/// <summary>Version, credits and the Velopack update check (behind the ⓘ button in the main window).</summary>
internal sealed class AboutDialog : Form
{
    public const string RepoUrl = "https://github.com/kreaton/TextEncoding";

    private readonly UpdateManager _updateManager = new(new GithubSource(RepoUrl, accessToken: null, prerelease: false));
    private readonly Button _btnUpdate = new();
    private readonly Label _lblUpdate = new();

    public AboutDialog()
    {
        SuspendLayout();

        AutoScaleMode = AutoScaleMode.Dpi;
        Text = "About TextEncoding";
        Icon = Program.AppIcon;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(16);

        var root = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Dock = DockStyle.Fill };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var logo = new PictureBox
        {
            Image = new Icon(Program.AppIcon, 96, 96).ToBitmap(),
            SizeMode = PictureBoxSizeMode.Zoom,
            Size = new Size(96, 96),
            Margin = new Padding(0, 0, 16, 0),
        };

        var text = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Margin = Padding.Empty,
        };

        text.Controls.Add(new Label
        {
            Text = "TextEncoding",
            AutoSize = true,
            Font = new Font(Font.FontFamily, Font.Size * 1.6f, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 2),
        });
        text.Controls.Add(Line(BuildInfo.Display, SystemColors.GrayText));
        text.Controls.Add(Line("Detect, preview and convert the encoding of text files.", null, top: 10));
        text.Controls.Add(Line(
            "Made by Per Hejndorf, with Claude (Anthropic).\n" +
            "Installs and updates by Velopack (MIT).\n" +
            "The ß in the logo is from Cascadia Code (SIL Open Font License 1.1).", null, top: 10));
        text.Controls.Add(Line(BuildInfo.Copyright, null, top: 10));

        var link = new LinkLabel { Text = RepoUrl, AutoSize = true, Margin = new Padding(0, 10, 0, 0) };
        link.LinkClicked += (_, _) => Process.Start(new ProcessStartInfo(RepoUrl) { UseShellExecute = true });
        text.Controls.Add(link);

        _lblUpdate.AutoSize = true;
        _lblUpdate.MaximumSize = new Size(420, 0);
        _lblUpdate.ForeColor = SystemColors.GrayText;
        _lblUpdate.Margin = new Padding(0, 10, 0, 0);
        text.Controls.Add(_lblUpdate);

        var buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 16, 0, 0),
        };
        var btnClose = new Button { Text = "Close", AutoSize = true, DialogResult = DialogResult.OK };
        _btnUpdate.Text = "Check for updates";
        _btnUpdate.AutoSize = true;
        _btnUpdate.Click += async (_, _) => await CheckForUpdatesAsync();
        buttons.Controls.Add(btnClose);
        buttons.Controls.Add(_btnUpdate);
        AcceptButton = btnClose;
        CancelButton = btnClose;

        root.Controls.Add(logo, 0, 0);
        root.Controls.Add(text, 1, 0);
        root.Controls.Add(buttons, 0, 1);
        root.SetColumnSpan(buttons, 2);
        Controls.Add(root);

        ResumeLayout(false);
        PerformLayout();
    }

    private static Label Line(string text, Color? color, int top = 0) => new()
    {
        Text = text,
        AutoSize = true,
        MaximumSize = new Size(420, 0),
        ForeColor = color ?? SystemColors.ControlText,
        Margin = new Padding(0, top, 0, 0),
    };

    /// <summary>Asks GitHub for a newer release and, if the user agrees, installs it and restarts.</summary>
    private async Task CheckForUpdatesAsync()
    {
        if (!_updateManager.IsInstalled)
        {
            _lblUpdate.Text = "Updates are only available in the installed app (from the Setup.exe on GitHub).";
            return;
        }

        _btnUpdate.Enabled = false;
        UseWaitCursor = true;
        _lblUpdate.Text = "Checking for updates…";
        try
        {
            var update = await _updateManager.CheckForUpdatesAsync();
            if (update is null)
            {
                _lblUpdate.Text = $"You have the latest version ({BuildInfo.Version}).";
                return;
            }

            var version = update.TargetFullRelease.Version;
            var answer = MessageBox.Show(this,
                $"Version {version} is available (you have {BuildInfo.Version}).\n\nDownload and install it now? " +
                "TextEncoding restarts afterwards.",
                "Update available", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (answer != DialogResult.Yes)
            {
                _lblUpdate.Text = $"Version {version} is available.";
                return;
            }

            _lblUpdate.Text = $"Downloading version {version}…";
            await _updateManager.DownloadUpdatesAsync(update);
            _updateManager.ApplyUpdatesAndRestart(update);
        }
        catch (Exception ex)
        {
            _lblUpdate.Text = $"Update failed: {ex.Message}";
        }
        finally
        {
            UseWaitCursor = false;
            _btnUpdate.Enabled = true;
        }
    }
}
