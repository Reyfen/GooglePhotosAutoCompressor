using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace GooglePhotosAutoCompressor.App.ViewModels;

/// <summary>The settings page. Every change is saved straight away and used from the next run.</summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly string _path;
    private readonly Func<Task<string?>> _pickFolder;
    private bool _loading;

    internal SettingsViewModel(string path, Func<Task<string?>> pickFolder)
    {
        _path = path;
        _pickFolder = pickFolder;
        Load(Settings.Load(path));
    }

    [ObservableProperty] private bool _compressVideos;
    [ObservableProperty] private bool _compressPhotos;
    [ObservableProperty] private decimal _minVideoMbPerSecond;
    [ObservableProperty] private decimal _minPhotoMb;

    /// <summary>0 = half size, 1 = max 1080p, 2 = max 720p.</summary>
    [ObservableProperty] private int _videoResolutionIndex;
    [ObservableProperty] private int _videoCrf;
    /// <summary>0 = leave alone, 1 = keep the motion, 2 = still photo (see MotionPhotoMode).</summary>
    [ObservableProperty] private int _motionPhotosIndex;
    [ObservableProperty] private decimal _maxPhotoMegapixels;
    [ObservableProperty] private int _photoJpegQuality;
    [ObservableProperty] private int _minSavingPercent;

    [ObservableProperty] private decimal _itemsPerBatch;
    [ObservableProperty] private bool _repeatUntilDone;
    [ObservableProperty] private decimal _parallelDownloads;
    [ObservableProperty] private decimal _minFreeDiskGb;
    [ObservableProperty] private decimal _maxFails;

    [ObservableProperty] private bool _confirmBeforeBin;
    [ObservableProperty] private bool _deleteLocalFilesWhenDone;
    [ObservableProperty] private string _workFolder = "";
    [ObservableProperty] private bool _setupDone;

    /// <summary>Google account of the last run: the window shows its stats. Set by runs, cleared by Sign out.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AccountText))]
    private string _lastAccount = "";

    public string AccountText => string.IsNullOrEmpty(LastAccount) ? "" : LastAccount;

    [ObservableProperty] private string _savedText = "";

    /// <summary>
    /// A run is going (set by the main window). Only the settings the pipeline reads again at each batch or
    /// item can change then: carry on with the next batch, items per batch, ask before the bin, delete local
    /// files, parallel downloads and free disk space. The rest (what to compress, limits, quality, attempts,
    /// work folder) stay locked, so one run never treats items differently or moves its files.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLocked))]
    private bool _runIsGoing;

    /// <summary>For the settings that can't change during a run.</summary>
    public bool IsLocked => RunIsGoing;

    public string[] VideoResolutionOptions { get; } =
    {
        "Half size (e.g. 4K → 1080p, 1080p → 540p)",
        "At most 1080p (Full HD)",
        "At most 720p (HD)",
    };

    public string[] MotionPhotosOptions { get; } =
    {
        "Leave them alone",
        "Compress the photo, keep the motion (recommended)",
        "Compress to a still photo (the motion is lost)",
    };

    public string VideoQualityText => VideoCrf switch
    {
        <= 18 => $"{VideoCrf} – almost no visible loss, bigger files",
        <= 22 => $"{VideoCrf} – very good (recommended)",
        <= 26 => $"{VideoCrf} – good, smaller files",
        _ => $"{VideoCrf} – visible loss, smallest files",
    };

    public string PhotoQualityText => PhotoJpegQuality switch
    {
        >= 92 => $"{PhotoJpegQuality} – near original, small saving",
        >= 82 => $"{PhotoJpegQuality} – very good (recommended)",
        >= 70 => $"{PhotoJpegQuality} – good",
        _ => $"{PhotoJpegQuality} – visible loss",
    };

    public string WorkFolderHint => string.IsNullOrWhiteSpace(WorkFolder)
        ? $"Default: {Const.APP_DOWNLOADS_PATH}"
        : "Downloads and compressed copies are kept here until they are finished.";

    internal Settings ToSettings() => new Settings
    {
        CompressVideos = CompressVideos,
        CompressPhotos = CompressPhotos,
        MinVideoMbPerSecond = (double)MinVideoMbPerSecond,
        MinPhotoMb = (double)MinPhotoMb,
        VideoResolution = (VideoResolution)Math.Clamp(VideoResolutionIndex, 0, 2),
        VideoCrf = VideoCrf,
        MotionPhotos = (MotionPhotoMode)Math.Clamp(MotionPhotosIndex, 0, 2),
        MaxPhotoMegapixels = (double)MaxPhotoMegapixels,
        PhotoJpegQuality = PhotoJpegQuality,
        MinSavingPercent = MinSavingPercent,
        ItemsPerBatch = (int)ItemsPerBatch,
        RepeatUntilDone = RepeatUntilDone,
        ParallelDownloads = (int)ParallelDownloads,
        MinFreeDiskGb = (double)MinFreeDiskGb,
        MaxFails = (int)MaxFails,
        ConfirmBeforeBin = ConfirmBeforeBin,
        DeleteLocalFilesWhenDone = DeleteLocalFilesWhenDone,
        WorkFolder = WorkFolder ?? "",
        SetupDone = SetupDone,
        LastAccount = LastAccount ?? "",
    }.Normalized();

    private void Load(Settings s)
    {
        _loading = true;
        CompressVideos = s.CompressVideos;
        CompressPhotos = s.CompressPhotos;
        MinVideoMbPerSecond = (decimal)s.MinVideoMbPerSecond;
        MinPhotoMb = (decimal)s.MinPhotoMb;
        VideoResolutionIndex = (int)s.VideoResolution;
        VideoCrf = s.VideoCrf;
        MotionPhotosIndex = (int)s.MotionPhotos;
        MaxPhotoMegapixels = (decimal)s.MaxPhotoMegapixels;
        PhotoJpegQuality = s.PhotoJpegQuality;
        MinSavingPercent = s.MinSavingPercent;
        ItemsPerBatch = s.ItemsPerBatch;
        RepeatUntilDone = s.RepeatUntilDone;
        ParallelDownloads = s.ParallelDownloads;
        MinFreeDiskGb = (decimal)s.MinFreeDiskGb;
        MaxFails = s.MaxFails;
        ConfirmBeforeBin = s.ConfirmBeforeBin;
        DeleteLocalFilesWhenDone = s.DeleteLocalFilesWhenDone;
        WorkFolder = s.WorkFolder;
        SetupDone = s.SetupDone;
        LastAccount = s.LastAccount;
        _loading = false;
        Settings.Current = s.Normalized();
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (e.PropertyName == nameof(VideoCrf)) OnPropertyChanged(nameof(VideoQualityText));
        if (e.PropertyName == nameof(PhotoJpegQuality)) OnPropertyChanged(nameof(PhotoQualityText));
        if (e.PropertyName == nameof(WorkFolder)) OnPropertyChanged(nameof(WorkFolderHint));
        if (e.PropertyName == nameof(RepeatUntilDone)) OnPropertyChanged(nameof(StopAfterEachBatch));
        if (e.PropertyName == nameof(SetupDone)) OnPropertyChanged(nameof(ShowSetup));

        if (_loading || e.PropertyName is nameof(SavedText) or nameof(VideoQualityText) or nameof(PhotoQualityText) or nameof(WorkFolderHint)
                or nameof(StopAfterEachBatch) or nameof(ShowSetup) or nameof(RunIsGoing) or nameof(IsLocked) or nameof(AccountText))
        {
            return;
        }

        try
        {
            Settings settings = ToSettings();
            settings.Save(_path);
            Settings.Current = settings;
            if (e.PropertyName != nameof(LastAccount)) // not something the user changed on this page
            {
                SavedText = RunIsGoing ? "Saved. Used by this run from the next batch." : "Saved. Used from the next run.";
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SavedText = $"Could not save: {ex.Message}";
        }
    }

    /// <summary>The first-start page: shown until it is answered, and again from the Settings page on request.</summary>
    public bool ShowSetup => SetupDone == false;

    /// <summary>The other choice of "after each batch" on the first-start page.</summary>
    public bool StopAfterEachBatch
    {
        get => RepeatUntilDone == false;
        set => RepeatUntilDone = value == false;
    }

    [RelayCommand]
    private void FinishSetup() => SetupDone = true;

    [RelayCommand]
    private void ShowSetupAgain() => SetupDone = false;

    [RelayCommand]
    private void ResetToDefaults()
    {
        Settings defaults = new() { SetupDone = true };
        Load(defaults);
        defaults.Save(_path);
        SavedText = "Defaults restored.";
    }

    [RelayCommand]
    private async Task BrowseWorkFolder()
    {
        string? folder = await _pickFolder();
        if (folder != null)
        {
            WorkFolder = folder;
        }
    }
}
