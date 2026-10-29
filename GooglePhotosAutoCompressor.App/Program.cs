using System.Runtime.InteropServices;
using Avalonia;

namespace GooglePhotosAutoCompressor.App;

internal static class Program
{
    /// <summary>Held for the whole session; null when another copy of the app is running.</summary>
    public static AppLock? Lock { get; private set; }

    /// <summary>Everything written with Console.WriteLine, also saved to a log file.</summary>
    public static TeeTextWriter Log { get; private set; } = null!;

    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Contains("--check"))
        {
            // Checks the setup without touching Google Photos. Exit code 0 when everything works.
            ConsoleWindow.AttachToParent();
            Settings.Current = Settings.Load(Settings.DEFAULT_PATH);
            IReadOnlyList<CheckResult> results = SetupCheck.RunAsync(includeBrowser: args.Contains("--no-browser") == false).GetAwaiter().GetResult();
            Console.WriteLine(SetupCheck.Format(results));
            return results.All(r => r.Ok || r.Name == "Internet") ? 0 : 1;
        }

        bool cli = args.Contains("--cli");
        if (cli)
        {
            ConsoleWindow.AttachToParent();
        }

        Lock = AppLock.TryAcquire(AppLock.DEFAULT_PATH);
        Log = RunLog.Start(cli ? Console.Out : null);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Console.WriteLine($"Unhandled error: {e.ExceptionObject}");
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Console.WriteLine($"Background error: {e.Exception}");
            e.SetObserved();
        };

        try
        {
            if (Lock != null)
            {
                // Only the copy that holds the lock may move files.
                Settings.Current = Settings.Load(Settings.DEFAULT_PATH);
                AppFolderMigration.Run();
            }

            if (cli)
            {
                return RunCli(args).GetAwaiter().GetResult();
            }

            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            Lock?.Dispose();
            Log.Dispose();
        }
    }

    // Also used by the Avalonia designer.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    /// <summary>
    /// Runs one session without a window, for terminals and servers:
    ///   GooglePhotosAutoCompressor --cli [--yes]
    /// Uses the settings saved by the window. --yes moves originals to the bin without asking.
    /// </summary>
    private static async Task<int> RunCli(string[] args)
    {
        if (Lock == null)
        {
            Console.WriteLine("Google Photos Auto Compressor is already running.");
            return 2;
        }

        bool yes = args.Contains("--yes");
        using CancellationTokenSource stop = new();
        Console.CancelKeyPress += (_, e) =>
        {
            // First Ctrl+C stops at the next safe point; a second one ends the process.
            if (stop.IsCancellationRequested == false)
            {
                e.Cancel = true;
                Console.WriteLine("Stopping at the next safe point (press Ctrl+C again to quit now)...");
                stop.Cancel();
            }
        };

        var pipeline = new Pipeline(new PipelineHost
        {
            ConfirmBin = items =>
            {
                long total = items.Sum(i => i.Size);
                string question = $"Move {items.Count} original(s), {total / 1024.0 / 1024 / 1024:F2} GB, to the Google Photos bin?";
                return yes ? Task.FromResult(true) : FfmpegInstaller.AskOnConsole(question);
            },
        });

        RunResult result = await pipeline.RunAsync(Settings.Load(Settings.DEFAULT_PATH), stop.Token);
        Console.WriteLine($"Log: {RunLog.CurrentPath}");
        return result == RunResult.Finished ? 0 : 1;
    }
}

/// <summary>A Windows GUI exe has no console; with --cli, use the terminal it was started from.</summary>
internal static class ConsoleWindow
{
    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int processId);

    [DllImport("kernel32.dll")]
    private static extern bool AllocConsole();

    public static void AttachToParent()
    {
        if (OperatingSystem.IsWindows() && AttachConsole(-1) == false)
        {
            AllocConsole();
        }
    }
}
