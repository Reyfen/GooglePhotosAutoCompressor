using Avalonia.Styling;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using GooglePhotosAutoCompressor.App.ViewModels;

namespace GooglePhotosAutoCompressor.App.Views;

/// <summary>Dialogs, folder picker, clipboard and "open" for the main window.</summary>
public sealed class WindowDialogs : IDialogs
{
    private readonly Window _owner;

    public WindowDialogs(Window owner)
    {
        _owner = owner;
    }

    public async Task<bool> AskYesNo(string title, string message, string yes = "Yes", string no = "No")
    {
        var dialog = new DialogWindow(title, message, yes, no, items: null, dontAskText: null);
        await dialog.ShowDialog(_owner);
        return dialog.Ok;
    }

    public async Task<(bool ok, bool dontAskAgain)> ConfirmBin(string summary, IReadOnlyList<ItemRow> items)
    {
        _owner.Activate();
        var dialog = new DialogWindow("Move originals to the bin?", summary, "Move to bin and upload copies", "Not now",
            items, "Don't ask again (you can turn this back on in Settings)");
        await dialog.ShowDialog(_owner);
        return (dialog.Ok, dialog.DontAskAgain);
    }

    public async Task<string?> PickFolder()
    {
        IReadOnlyList<IStorageFolder> folders = await _owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose the work folder",
            AllowMultiple = false,
        });
        return folders.FirstOrDefault()?.TryGetLocalPath();
    }

    public async Task CopyText(string text)
    {
        if (_owner.Clipboard != null)
        {
            await _owner.Clipboard.SetTextAsync(text);
        }
    }

    public async Task Open(string pathOrUrl)
    {
        if (pathOrUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            await _owner.Launcher.LaunchUriAsync(new Uri(pathOrUrl));
            return;
        }

        if (Directory.Exists(pathOrUrl))
        {
            await _owner.Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(pathOrUrl));
        }
        else if (File.Exists(pathOrUrl))
        {
            await _owner.Launcher.LaunchFileInfoAsync(new FileInfo(pathOrUrl));
        }
    }
}

/// <summary>A small modal window: message, optional item list and "don't ask again", two buttons.</summary>
internal sealed class DialogWindow : Window
{
    private readonly CheckBox? _dontAsk;

    public DialogWindow(string title, string message, string yes, string? no, IReadOnlyList<ItemRow>? items, string? dontAskText)
    {
        Title = title;
        Width = items == null ? 460 : 620;
        SizeToContent = SizeToContent.Height;
        MaxHeight = 720;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Icon = new WindowIcon(Avalonia.Platform.AssetLoader.Open(new Uri("avares://GooglePhotosAutoCompressor/Assets/icon.ico")));

        var panel = new StackPanel { Margin = new Thickness(22), Spacing = 14 };
        panel.Children.Add(new TextBlock { Text = title, FontSize = 17, FontWeight = FontWeight.SemiBold });
        panel.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap });

        if (items is { Count: > 0 })
        {
            var list = new ListBox
            {
                MaxHeight = 220,
                ItemsSource = items.Select(i => $"{i.Date,-12} {i.Kind,-14} {i.Size,10} → {(i.CopySize.Length > 0 ? i.CopySize : "?"),10}   {i.Name}").ToList(),
                FontFamily = new FontFamily("Cascadia Mono,Consolas,DejaVu Sans Mono,Menlo,monospace"),
                FontSize = 12,
            };
            list.Styles.Add(new Style(x => x.OfType<ListBoxItem>())
            {
                Setters =
                {
                    new Setter(ListBoxItem.PaddingProperty, new Thickness(8, 2)),
                    new Setter(MinHeightProperty, 0.0),
                },
            });
            panel.Children.Add(list);
        }

        if (dontAskText != null)
        {
            _dontAsk = new CheckBox { Content = dontAskText };
            panel.Children.Add(_dontAsk);
        }

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        var yesButton = new Button { Content = yes, IsDefault = true };
        yesButton.Classes.Add("primary");
        yesButton.Click += (_, _) =>
        {
            Ok = true;
            Close();
        };

        if (no != null)
        {
            var noButton = new Button { Content = no, IsCancel = true };
            noButton.Click += (_, _) => Close();
            buttons.Children.Add(noButton);
        }

        buttons.Children.Add(yesButton);
        panel.Children.Add(buttons);
        Content = panel;
    }

    public bool Ok { get; private set; }

    public bool DontAskAgain => _dontAsk?.IsChecked == true;
}

internal static class MessageWindow
{
    /// <summary>Shown instead of the main window when a second copy is started.</summary>
    public static Window AlreadyRunning() => new DialogWindow(
        "Already running",
        "Google Photos Auto Compressor is already open. Switch to that window (check the taskbar).",
        "OK", no: null, items: null, dontAskText: null)
    {
        WindowStartupLocation = WindowStartupLocation.CenterScreen,
    };
}
