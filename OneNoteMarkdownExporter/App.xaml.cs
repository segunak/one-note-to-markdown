using System;
using System.Configuration;
using System.Data;
using System.Runtime.InteropServices;
using System.Windows;
using OneNoteMarkdownExporter.Services;

namespace OneNoteMarkdownExporter;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : System.Windows.Application
{
    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int dwProcessId);

    [DllImport("kernel32.dll")]
    private static extern bool AllocConsole();

    [DllImport("kernel32.dll")]
    private static extern bool FreeConsole();

    private const int ATTACH_PARENT_PROCESS = -1;

    protected override async void OnStartup(StartupEventArgs e)
    {
        if (CliHandler.ShouldRunCli(e.Args))
        {
            // CLI mode - attach to parent console or allocate a new one
            if (!AttachConsole(ATTACH_PARENT_PROCESS))
            {
                AllocConsole();
            }

            try
            {
                int exitCode = await CliHandler.RunAsync(e.Args);
                Environment.Exit(exitCode);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Fatal error: {ex.Message}");
                Environment.Exit(1);
            }
        }
        else
        {
            // GUI mode - normal WPF startup
            base.OnStartup(e);
        }
    }
}

