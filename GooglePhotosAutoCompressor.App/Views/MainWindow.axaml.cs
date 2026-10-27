using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Threading;
using GooglePhotosAutoCompressor.App.ViewModels;

namespace GooglePhotosAutoCompressor.App.Views;

public partial class MainWindow : Window
{
    private bool _closingAfterStop;

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is MainViewModel vm)
            {
                vm.LogLines.CollectionChanged += ScrollLogToEnd;
            }
        };
    }

    private void ScrollLogToEnd(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add && LogList.ItemCount > 0)
        {
            Dispatcher.UIThread.Post(() => LogList.ScrollIntoView(LogList.ItemCount - 1), DispatcherPriority.Background);
        }
    }

    /// <summary>Closing during a run: ask, then stop at a safe point before closing.</summary>
    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (_closingAfterStop || DataContext is not MainViewModel { IsRunning: true } vm)
        {
            return;
        }

        e.Cancel = true;
        var dialogs = new WindowDialogs(this);
        if (await dialogs.AskYesNo("Stop and close?",
                "A run is going. It will stop at the next safe point and close. Nothing is lost: the next run carries on.",
                "Stop and close", "Keep running"))
        {
            await vm.StopAndWait();
            _closingAfterStop = true;
            Close();
        }
    }
}
