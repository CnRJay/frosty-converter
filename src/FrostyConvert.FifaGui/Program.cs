using System.Runtime.InteropServices;
using FrostyConvert.Core.Cli;

namespace FrostyConvert.FifaGui;

internal static class Program
{
    private const int SwHide = 0;

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetConsoleWindow();

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [STAThread]
    private static int Main(string[] args)
    {
        // Args present → full CLI (stdout/stderr work for terminals and pipes).
        // No args → hide the console window and open the GUI.
        if (args.Length > 0)
            return CliRunner.Run(args);

        var console = GetConsoleWindow();
        if (console != IntPtr.Zero)
            ShowWindow(console, SwHide);

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
        return 0;
    }
}
