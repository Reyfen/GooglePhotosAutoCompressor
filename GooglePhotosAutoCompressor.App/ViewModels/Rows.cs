using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace GooglePhotosAutoCompressor.App.ViewModels;

/// <summary>One of the six steps shown at the top of the Run page.</summary>
public partial class StepViewModel : ObservableObject
{
    public StepViewModel(int number, string name)
    {
        Number = number;
        Name = name;
    }

    public int Number { get; }
    public string Name { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Badge), nameof(IsActive), nameof(IsDone), nameof(IsPending))]
    private StepState _state;

    public bool IsActive => State == StepState.Active;
    public bool IsDone => State == StepState.Done;
    public bool IsPending => State == StepState.Pending;
    public string Badge => State == StepState.Done ? "✓" : Number.ToString();

    // The line to the previous step (left half) and to the next step (right half). Two halves make one
    // connector; see MainViewModel.UpdateConnectors.
    public bool HasLineIn => Number > 1;
    public bool HasLineOut { get; init; } = true;
    [ObservableProperty] private double _lineIn;
    [ObservableProperty] private bool _lineInMoving;
    [ObservableProperty] private double _lineOut;
    [ObservableProperty] private bool _lineOutMoving;
}

public enum StepState
{
    Pending,
    Active,
    Done,
}

/// <summary>One line in the Items list.</summary>
public sealed class ItemRow
{
    private static readonly IBrush GREEN = new SolidColorBrush(Color.Parse("#1FA67A"));
    private static readonly IBrush BLUE = new SolidColorBrush(Color.Parse("#3B7DE9"));
    private static readonly IBrush AMBER = new SolidColorBrush(Color.Parse("#D98E04"));
    private static readonly IBrush RED = new SolidColorBrush(Color.Parse("#D64545"));
    private static readonly IBrush GREY = new SolidColorBrush(Color.Parse("#8A94A3"));

    internal ItemRow(FileItem item)
    {
        Status = ItemRules.Describe(item);
        Date = item.Date == default ? "" : item.Date.ToString("d MMM yyyy");
        SortDate = item.Date;
        Kind = item.MediaType == MediaType.Image ? "Photo" : $"Video {FormatDuration(item.Duration)}";
        Name = Path.GetFileName(item.DownloadPath ?? item.CompressPath ?? "") is { Length: > 0 } name ? name : item.MediaKey[..Math.Min(12, item.MediaKey.Length)] + "…";
        Size = Format.Bytes(item.Size);
        CopySize = item.CompressedSize > 0 ? Format.Bytes(item.CompressedSize) : "";
        Saved = item.CompressedSize > 0 && item.Size > item.CompressedSize ? $"−{100 - item.CompressedSize * 100 / item.Size}%" : "";
        (StatusText, StatusBrush) = Status switch
        {
            ItemStatus.Done => ("Done", GREEN),
            ItemStatus.Uploaded => ("Uploaded, date next", BLUE),
            ItemStatus.InBin => ("Original in bin, upload next", BLUE),
            ItemStatus.Compressed => ("Compressed", BLUE),
            ItemStatus.Downloaded => ("Downloaded", BLUE),
            ItemStatus.Waiting => ("Waiting", GREY),
            ItemStatus.Small => ("Already small", GREY),
            ItemStatus.Skipped => ("Left alone", AMBER),
            _ => ($"Failed ({item.FailCount}×)", RED),
        };
    }

    internal ItemStatus Status { get; }
    internal DateOnly SortDate { get; }
    public string Date { get; }
    public string Kind { get; }
    public string Name { get; }
    public string Size { get; }
    public string CopySize { get; }
    public string Saved { get; }
    public string StatusText { get; }
    public IBrush StatusBrush { get; }

    private static string FormatDuration(TimeSpan d) => d.TotalHours >= 1 ? d.ToString(@"h\:mm\:ss") : d.ToString(@"m\:ss");
}

internal static class Format
{
    public static string Bytes(long bytes) => bytes switch
    {
        >= 1L << 30 => $"{bytes / (double)(1L << 30):F2} GB",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):F1} MB",
        >= 1L << 10 => $"{bytes / 1024.0:F0} KB",
        _ => $"{bytes} B",
    };
}
