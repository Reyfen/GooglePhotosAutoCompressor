using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using GooglePhotosAutoCompressor.App.ViewModels;
using GooglePhotosAutoCompressor.App.Views;

namespace GooglePhotosAutoCompressor.App;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (Program.Lock == null)
            {
                desktop.MainWindow = MessageWindow.AlreadyRunning();
            }
            else
            {
                var window = new MainWindow();
                window.DataContext = new MainViewModel(new WindowDialogs(window), Program.Log);
                desktop.MainWindow = window;
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}
