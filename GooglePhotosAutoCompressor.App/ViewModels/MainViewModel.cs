using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GooglePhotosAutoCompressor.Modules;

namespace GooglePhotosAutoCompressor.App.ViewModels;

/// <summary>What the window needs from the outside world (dialogs, folders, clipboard).</summary>
public interface IDialogs
{
    Task<bool> AskYesNo(string title, string message, string yes = "Yes", string no = "No");

    /// <summary>Returns (move to bin?, stop asking?).</summary>
    Task<(bool ok, bool dontAskAgain)> ConfirmBin(string summary, IReadOnlyList<ItemRow> items);

    Task<string?> PickFolder();

    Task CopyText(string text);

    Task Open(string pathOrUrl);
}

public partial class MainViewModel : ObservableObject
{
    private const int MAX_LOG_LINES = 3000;

    private readonly IDialogs _dialogs;
    private readonly DispatcherTimer _clock;
    private readonly DispatcherTimer _refreshThrottle;
    private CancellationTokenSource? _stop;
    private Task? _run;
    private FileItemsData? _state;
    private DateTime _startedAt;
    private FileItem? _compressing;

    private readonly string _statePath;

    /// <param name="settingsPath">Settings file (tests pass a temp file).</param>
    /// <param name="statePath">Progress file shown while idle (tests pass a temp file).</param>
    internal MainViewModel(IDialogs dialogs, TeeTextWriter log, string? settingsPath = null, string? statePath = null)
    {
        _dialogs = dialogs;
        _statePath = statePath ?? FileItemsData.DEFAULT_PATH;
        Settings = new SettingsViewModel(settingsPath ?? Settings_DEFAULT_PATH, dialogs.PickFolder);

        Steps = new ObservableCollection<StepViewModel>(new[]
        {
            new StepViewModel(1, "Sign in"),
            new StepViewModel(2, "Download"),
            new StepViewModel(3, "Compress"),
            new StepViewModel(4, "Move to bin"),
            new StepViewModel(5, "Upload"),
            new StepViewModel(6, "Set dates") { HasLineOut = false },
        });

        log.LineWritten += line => Dispatcher.UIThread.Post(() => AddLogLine(line));
        Compressor.Progress += OnCompressProgress;

        _clock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _clock.Tick += (_, _) => ElapsedText = IsRunning ? $"Running for {DateTime.Now - _startedAt:hh\\:mm\\:ss}" : "";

        // Saves happen many times a second during downloads; refresh the lists at most twice a second.
        _refreshThrottle = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _refreshThrottle.Tick += (_, _) =>
        {
            _refreshThrottle.Stop();
            RefreshItems();
        };

        LoadStateForDisplay();
    }

    private static string Settings_DEFAULT_PATH => global::GooglePhotosAutoCompressor.Settings.DEFAULT_PATH;

    /// <summary>"v1.0.0" (CI builds: "v1.0.0-ci.12"), without the commit id .NET appends.</summary>
    public string VersionText => VERSION;

    private static readonly string VERSION = "v" + (typeof(MainViewModel).Assembly
        .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
        .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion
        ?? "0.0.0").Split('+')[0];

    public SettingsViewModel Settings { get; }
    public ObservableCollection<StepViewModel> Steps { get; }
    public ObservableCollection<string> LogLines { get; } = new();
    public ObservableCollection<ItemRow> Items { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle), nameof(StartStopText))]
    [NotifyCanExecuteChangedFor(nameof(RetryFailedCommand), nameof(FreeDiskSpaceCommand), nameof(CheckSetupCommand))]
    private bool _isRunning;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StartStopText))]
    private bool _isStopping;

    [ObservableProperty] private string _statusText = "Ready. Press Start to free up space in Google Photos.";
    [ObservableProperty] private bool _statusIsWarning;
    [ObservableProperty] private bool _statusIsError;
    [ObservableProperty] private string _elapsedText = "";
    [ObservableProperty] private double _overallProgress;
    [ObservableProperty] private bool _overallIndeterminate;
    [ObservableProperty] private string _currentItemText = "";
    [ObservableProperty] private double _currentItemProgress;
    [ObservableProperty] private bool _showCurrentItem;

    [ObservableProperty] private string _savedSpaceText = "0 MB";
    [ObservableProperty] private int _doneCount;
    [ObservableProperty] private int _inProgressCount;
    [ObservableProperty] private int _waitingCount;
    [ObservableProperty] private int _leftAloneCount;
    [ObservableProperty] private int _failedCount;
    [ObservableProperty] private string _cacheSizeText = "";
    [ObservableProperty] private string _itemsSummary = "";

    [ObservableProperty] private int _filterIndex;
    [ObservableProperty] private string _searchText = "";

    public bool IsIdle => IsRunning == false;
    public string StartStopText => IsStopping ? "Stopping…" : IsRunning ? "Stop" : "Start";

    public string[] FilterOptions { get; } = { "All items", "In progress", "Waiting", "Done", "Left alone or small", "Failed" };

    public string LogPath => RunLog.CurrentPath ?? RunLog.LOG_DIR;

    partial void OnFilterIndexChanged(int value) => RefreshItems();
    partial void OnSearchTextChanged(string value) => RefreshItems();

    [RelayCommand]
    private async Task StartStop()
    {
        if (IsRunning)
        {
            RequestStop();
            return;
        }

        await StartRun();
    }

    private Task StartRun()
    {
        _stop = new CancellationTokenSource();
        CancellationToken token = _stop.Token;
        var settings = Settings.ToSettings();

        IsRunning = true;
        IsStopping = false;
        _startedAt = DateTime.Now;
        _clock.Start();
        foreach (StepViewModel step in Steps) step.State = StepState.Pending;
        UpdateConnectors();
        SetStatus("Starting…");
        OverallProgress = 0;
        KeepAwake.Start();

        var host = new PipelineHost
        {
            AskYesNo = question => OnUi(() => _dialogs.AskYesNo("ffmpeg needed", question, "Download", "Cancel")),
            ConfirmBin = items => OnUi(() => ConfirmBin(items)),
            StageChanged = (stage, message) => Dispatcher.UIThread.Post(() => OnStage(stage, message)),
            StateLoaded = state => Dispatcher.UIThread.Post(() => AttachState(state)),
        };
        var pipeline = new Pipeline(host);

        _run = Task.Run(async () =>
        {
            RunResult result;
            try
            {
                result = await pipeline.RunAsync(settings, token);
            }
            catch (Exception e)
            {
                Console.WriteLine($"Unexpected error: {e}");
                result = RunResult.Failed;
                host.StageChanged(PipelineStage.Finished, $"Unexpected error: {e.Message}");
            }

            await Dispatcher.UIThread.InvokeAsync(() => OnFinished(result, pipeline.LastError));
        });

        return Task.CompletedTask;
    }

    private void RequestStop()
    {
        if (_stop == null || IsStopping)
        {
            return;
        }

        IsStopping = true;
        SetStatus("Stopping at the next safe point… (nothing is lost; the next run carries on)", warning: true);
        _stop.Cancel();
    }

    /// <summary>For closing the window during a run: stop and wait (up to a minute) for a safe point.</summary>
    public async Task StopAndWait()
    {
        RequestStop();
        if (_run != null)
        {
            await Task.WhenAny(_run, Task.Delay(TimeSpan.FromMinutes(1)));
        }
    }

    private void OnFinished(RunResult result, string? error)
    {
        IsRunning = false;
        IsStopping = false;
        _clock.Stop();
        ElapsedText = "";
        ShowCurrentItem = false;
        OverallIndeterminate = false;
        KeepAwake.Stop();

        switch (result)
        {
            case RunResult.Finished:
                foreach (StepViewModel step in Steps) step.State = StepState.Done;
                OverallProgress = 100;
                SetStatus(Settings.RepeatUntilDone ? "Finished: nothing left to do for now." : "Batch finished. Press Start for the next batch.");
                break;
            case RunResult.Stopped:
                SetStatus("Stopped. Press Start to carry on where it left off.", warning: true);
                break;
            default:
                SetStatus(error ?? "Finished with errors. See the log below.", error: true);
                break;
        }

        UpdateConnectors();

        RefreshItems();
        _stop?.Dispose();
        _stop = null;
    }

    private void OnStage(PipelineStage stage, string message)
    {
        int index = stage switch
        {
            PipelineStage.Preparing or PipelineStage.SigningIn => 0,
            PipelineStage.Downloading => 1,
            PipelineStage.Compressing => 2,
            PipelineStage.ReadingDates or PipelineStage.ConfirmingBin or PipelineStage.MovingToBin => 3,
            PipelineStage.Uploading => 4,
            PipelineStage.SettingDates or PipelineStage.CleaningUp => 5,
            _ => -1,
        };

        if (stage == PipelineStage.Finished)
        {
            return; // OnFinished shows the result
        }

        bool waiting = stage is PipelineStage.WaitingForInternet or PipelineStage.ConfirmingBin
                       || (stage == PipelineStage.SigningIn && message.StartsWith("Sign in", StringComparison.Ordinal));
        if (IsStopping == false)
        {
            SetStatus(message, warning: waiting);
        }

        if (index >= 0)
        {
            for (int i = 0; i < Steps.Count; i++)
            {
                Steps[i].State = i < index ? StepState.Done : i == index ? StepState.Active : StepState.Pending;
            }

            OverallProgress = index * 100.0 / Steps.Count;
            UpdateConnectors();
        }

        OverallIndeterminate = stage is PipelineStage.Preparing or PipelineStage.SigningIn or PipelineStage.Downloading or PipelineStage.WaitingForInternet;
        if (stage != PipelineStage.Compressing)
        {
            ShowCurrentItem = false;
        }
    }

    private void OnCompressProgress(FileItem item, double fraction)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (ReferenceEquals(_compressing, item) == false)
            {
                _compressing = item;
                CurrentItemText = $"Compressing {Path.GetFileName(item.DownloadPath)} ({Format.Bytes(item.Size)})";
            }

            ShowCurrentItem = true;
            CurrentItemProgress = fraction * 100;
            OverallProgress = (2 + fraction) * 100.0 / Steps.Count;
            UpdateConnectors(fraction);
        });
    }

    /// <summary>
    /// The lines between the step circles: full up to the last finished step, then the line into the
    /// running step shows its progress (the compress percentage when known, otherwise a moving bar),
    /// empty after it. Each line is drawn as two halves: the right half of one step and the left half of the next.
    /// </summary>
    internal void UpdateConnectors(double? fraction = null)
    {
        for (int k = 0; k + 1 < Steps.Count; k++)
        {
            StepViewModel from = Steps[k], to = Steps[k + 1];
            bool moving = to.IsActive && IsRunning && fraction == null;
            double filled = to.IsDone ? 1 : to.IsActive ? fraction ?? 0 : 0;

            from.LineOut = Math.Clamp(filled * 2, 0, 1) * 100;
            to.LineIn = Math.Clamp(filled * 2 - 1, 0, 1) * 100;
            from.LineOutMoving = moving;
            to.LineInMoving = moving;
        }
    }

    private async Task<bool> ConfirmBin(IReadOnlyList<FileItem> items)
    {
        long originals = items.Sum(i => i.Size);
        long copies = items.Sum(i => i.CompressedSize > 0 ? i.CompressedSize : 0);
        string summary =
            $"{items.Count} compressed cop{(items.Count == 1 ? "y is" : "ies are")} checked and ready.\n\n" +
            $"The originals ({Format.Bytes(originals)}) will be moved to the Google Photos bin, then the copies " +
            $"({Format.Bytes(copies)}) are uploaded. This saves about {Format.Bytes(originals - copies)}.\n\n" +
            "Google keeps the bin for 60 days, so you can still restore an original.";

        (bool ok, bool dontAskAgain) = await _dialogs.ConfirmBin(summary, items.Select(i => new ItemRow(i)).ToList());
        if (ok && dontAskAgain)
        {
            Settings.ConfirmBeforeBin = false;
        }

        return ok;
    }

    private void SetStatus(string text, bool warning = false, bool error = false)
    {
        StatusText = text;
        StatusIsWarning = warning && error == false;
        StatusIsError = error;
    }

    private void AddLogLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line) || line == "===")
        {
            return;
        }

        LogLines.Add(line);
        while (LogLines.Count > MAX_LOG_LINES)
        {
            LogLines.RemoveAt(0);
        }
    }

    private void AttachState(FileItemsData state)
    {
        if (_state != null)
        {
            _state.Saved -= OnStateSaved;
        }

        _state = state;
        _state.Saved += OnStateSaved;
        RefreshItems();
    }

    private void OnStateSaved() => Dispatcher.UIThread.Post(() =>
    {
        if (_refreshThrottle.IsEnabled == false)
        {
            _refreshThrottle.Start();
        }
    });

    /// <summary>Shows the items from the last run while nothing is running.</summary>
    private void LoadStateForDisplay()
    {
        try
        {
            if (File.Exists(_statePath) || File.Exists(_statePath + ".bak"))
            {
                AttachState(new FileItemsData(_statePath));
            }
            else
            {
                RefreshItems();
            }
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            SetStatus($"Progress file problem: {e.Message}", error: true);
        }
    }

    private void RefreshItems()
    {
        FileItem[] all = _state?.Snapshot() ?? Array.Empty<FileItem>();
        List<ItemRow> rows = all.Select(i => new ItemRow(i)).ToList();

        DoneCount = rows.Count(r => r.Status == ItemStatus.Done);
        InProgressCount = rows.Count(r => r.Status is ItemStatus.Downloaded or ItemStatus.Compressed or ItemStatus.InBin or ItemStatus.Uploaded);
        WaitingCount = rows.Count(r => r.Status == ItemStatus.Waiting);
        LeftAloneCount = rows.Count(r => r.Status is ItemStatus.Skipped or ItemStatus.Small);
        FailedCount = rows.Count(r => r.Status == ItemStatus.Failed);

        long saved = all.Where(i => i.CompressUploaded && i.CompressedSize > 0 && i.Size > i.CompressedSize)
                        .Sum(i => i.Size - i.CompressedSize);
        SavedSpaceText = Format.Bytes(saved);
        ItemsSummary = $"{all.Length} items found in \"Large photos and videos\"";

        IEnumerable<ItemRow> shown = FilterIndex switch
        {
            1 => rows.Where(r => r.Status is ItemStatus.Downloaded or ItemStatus.Compressed or ItemStatus.InBin or ItemStatus.Uploaded),
            2 => rows.Where(r => r.Status == ItemStatus.Waiting),
            3 => rows.Where(r => r.Status == ItemStatus.Done),
            4 => rows.Where(r => r.Status is ItemStatus.Skipped or ItemStatus.Small),
            5 => rows.Where(r => r.Status == ItemStatus.Failed),
            _ => rows,
        };

        if (string.IsNullOrWhiteSpace(SearchText) == false)
        {
            shown = shown.Where(r => r.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase) || r.Date.Contains(SearchText, StringComparison.OrdinalIgnoreCase));
        }

        Items.Clear();
        foreach (ItemRow row in shown.OrderByDescending(r => r.SortDate))
        {
            Items.Add(row);
        }

        if (IsRunning == false)
        {
            long cache = LocalFiles.CacheSize();
            CacheSizeText = cache > 0 ? $"Files on this PC: {Format.Bytes(cache)}" : "";
        }
    }

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task RetryFailed()
    {
        if (_state == null)
        {
            return;
        }

        FileItem[] failed = _state.Snapshot().Where(i => ItemRules.Describe(i) == ItemStatus.Failed).ToArray();
        if (failed.Length == 0)
        {
            return;
        }

        int maxFails = global::GooglePhotosAutoCompressor.Settings.Current.MaxFails;
        if (await _dialogs.AskYesNo("Try failed items again?", $"{failed.Length} item(s) failed {maxFails} times and are left alone. Give them new attempts in the next run?", "Try again", "Cancel") == false)
        {
            return;
        }

        foreach (FileItem item in failed)
        {
            item.FailCount = 0;
        }

        _state.Save();
        RefreshItems();
    }

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task FreeDiskSpace()
    {
        if (_state == null)
        {
            return;
        }

        if (await _dialogs.AskYesNo("Free up disk space?",
                "Deletes the downloaded originals and compressed copies of finished items from this PC. " +
                "The copies are in Google Photos and the originals are in its bin for 60 days. Items still in progress are not touched.",
                "Delete", "Cancel") == false)
        {
            return;
        }

        long freed = LocalFiles.DeleteFinished(_state);
        SetStatus($"Freed {Format.Bytes(freed)} on this PC.");
        RefreshItems();
    }

    [RelayCommand]
    private Task OpenWorkFolder()
    {
        string dir = global::GooglePhotosAutoCompressor.Settings.Current.WorkFolderOrDefault;
        return _dialogs.Open(dir);
    }

    [RelayCommand]
    private Task OpenLog() => _dialogs.Open(RunLog.CurrentPath ?? RunLog.LOG_DIR);

    [RelayCommand]
    private Task CopyLog() => _dialogs.CopyText(string.Join(Environment.NewLine, LogLines));

    [RelayCommand]
    private Task OpenBin() => _dialogs.Open("https://photos.google.com/trash");

    [RelayCommand]
    private Task OpenLargeFiles() => _dialogs.Open("https://photos.google.com/quotamanagement/large");

    [RelayCommand]
    private Task OpenProjectPage() => _dialogs.Open("https://github.com/Reyfen/GooglePhotosAutoCompressor");

    [ObservableProperty] private string _setupCheckText = "";
    [ObservableProperty] private bool _isCheckingSetup;

    /// <summary>Checks ffmpeg, photos, browser, folders and internet without touching Google Photos.</summary>
    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task CheckSetup()
    {
        IsCheckingSetup = true;
        SetupCheckText = "Checking…";
        try
        {
            IReadOnlyList<CheckResult> results = await Task.Run(() => SetupCheck.RunAsync());
            SetupCheckText = string.Join(Environment.NewLine, results.Select(r => $"{(r.Ok ? "✓" : "✗")}  {r.Name}: {r.Detail}"));
        }
        finally
        {
            IsCheckingSetup = false;
        }
    }

    private static async Task<T> OnUi<T>(Func<Task<T>> action) => await Dispatcher.UIThread.InvokeAsync(action);
}

/// <summary>Stops the PC from going to sleep during a run (a sleeping PC drops downloads and uploads).</summary>
internal static class KeepAwake
{
    private const uint ES_CONTINUOUS = 0x80000000;
    private const uint ES_SYSTEM_REQUIRED = 0x00000001;
    private static Process? _inhibitor;

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern uint SetThreadExecutionState(uint flags);

    /// <summary>Call on the UI thread (Windows ties the request to the calling thread).</summary>
    public static void Start()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                SetThreadExecutionState(ES_CONTINUOUS | ES_SYSTEM_REQUIRED);
            }
            else if (OperatingSystem.IsLinux() && _inhibitor == null)
            {
                _inhibitor = Process.Start(new ProcessStartInfo("systemd-inhibit",
                    "--what=sleep:idle --who=\"Google Photos Auto Compressor\" --why=\"Compressing and uploading\" sleep infinity")
                {
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                });
            }
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // No systemd-inhibit: the PC may sleep, the next run carries on.
        }
    }

    public static void Stop()
    {
        if (OperatingSystem.IsWindows())
        {
            SetThreadExecutionState(ES_CONTINUOUS);
        }

        try
        {
            _inhibitor?.Kill();
        }
        catch (InvalidOperationException)
        {
        }

        _inhibitor?.Dispose();
        _inhibitor = null;
    }
}
