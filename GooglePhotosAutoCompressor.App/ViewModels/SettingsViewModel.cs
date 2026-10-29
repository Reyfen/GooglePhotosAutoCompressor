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

    [ObservableProperty] private string _savedText = "";

    public string[] VideoResolutionOptions { get; } =
    {
        "Half size (e.g. 4K → 1080p, 1080p → 540p)",
        "At most 1080p (Full HD)",
        "At most 720p (HD)",
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
                or nameof(StopAfterEachBatch) or nameof(ShowSetup))
        {
            return;
        }

        try
        {
            Settings settings = ToSettings();
            settings.Save(_path);
            Settings.Current = settings;
            SavedText = "Saved. Used from the next run.";
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
