using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using Velopack;

namespace TextEncoding;

internal static class Program
{
    /// <summary>The app icon (all sizes), for window title bars and the taskbar.</summary>
    public static Icon AppIcon { get; } = LoadIcon();

    private static Icon LoadIcon()
    {
        using var stream = typeof(Program).Assembly.GetManifestResourceStream("TextEncoding.icon.ico");
        return stream is null ? SystemIcons.Application : new Icon(stream);
    }

    [STAThread]
    private static int Main(string[] args)
    {
        // Must be the first thing to run: handles Velopack's install/update/uninstall hooks
        // (which arrive as --veloapp-* arguments) and may exit the process. Don't swallow exceptions here.
        VelopackApp.Build().Run();

        if (Cli.IsCliInvocation(args))
            return RunCli(args);

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm(args.Length == 1 ? args[0] : null));
        return 0;
    }

    /// <summary>
    /// The exe is a GUI-subsystem program (so double-clicking never flashes a console window).
    /// For command-line mode it attaches to the parent console, or to redirected output handles
    /// if there are any. With no console at all (e.g. started from a shortcut) the output is
    /// shown in a message box instead.
    /// </summary>
    private static int RunCli(string[] args)
    {
        var attached = NativeMethods.AttachConsole(NativeMethods.AttachParentProcess);

        var stdout = OpenWriter(NativeMethods.StdOutputHandle, attached, error: false);
        var stderr = OpenWriter(NativeMethods.StdErrorHandle, attached, error: true);

        if (stdout is not null && stderr is not null)
            return Cli.Run(args, stdout, stderr);

        var outBuffer = new StringWriter();
        var errBuffer = new StringWriter();
        var code = Cli.Run(args, stdout ?? outBuffer, stderr ?? errBuffer);

        var text = (outBuffer.ToString() + errBuffer.ToString()).Trim();
        if (text.Length > 0)
        {
            MessageBox.Show(text, "TextEncoding", MessageBoxButtons.OK,
                code == Cli.ExitOk ? MessageBoxIcon.Information : MessageBoxIcon.Error);
        }

        return code;
    }

    private static TextWriter? OpenWriter(int stdHandleId, bool attached, bool error)
    {
        // Redirected to a file/pipe, or the console already provided a handle.
        if (NativeMethods.HasUsableStdHandle(stdHandleId))
            return error ? Console.Error : Console.Out;

        if (!attached) return null;

        // Attached to the parent's console, but this GUI process got no standard handle: open the console directly.
        var handle = NativeMethods.CreateFileW("CONOUT$", NativeMethods.GenericWrite, NativeMethods.FileShareWrite,
            IntPtr.Zero, NativeMethods.OpenExisting, 0, IntPtr.Zero);
        if (handle.IsInvalid) return null;

        var stream = new FileStream(handle, FileAccess.Write, bufferSize: 1, isAsync: false);
        var encoding = Console.OutputEncoding.CodePage == 65001 ? new UTF8Encoding(false) : Console.OutputEncoding;
        return new StreamWriter(stream, encoding, bufferSize: 256) { AutoFlush = true };
    }

    private static class NativeMethods
    {
        public const int AttachParentProcess = -1;
        public const int StdOutputHandle = -11;
        public const int StdErrorHandle = -12;
        public const uint GenericWrite = 0x40000000;
        public const uint FileShareWrite = 0x2;
        public const uint OpenExisting = 3;

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool AttachConsole(int processId);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetStdHandle(int stdHandle);

        [DllImport("kernel32.dll")]
        private static extern uint GetFileType(IntPtr handle);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern SafeFileHandle CreateFileW(string fileName, uint desiredAccess, uint shareMode,
            IntPtr securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

        public static bool HasUsableStdHandle(int id)
        {
            var h = GetStdHandle(id);
            return h != IntPtr.Zero && h != new IntPtr(-1) && GetFileType(h) != 0 /* FILE_TYPE_UNKNOWN */;
        }
    }
}
