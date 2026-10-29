using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using GooglePhotosAutoCompressor.App.ViewModels;
using GooglePhotosAutoCompressor.App.Views;

[assembly: AvaloniaTestApplication(typeof(GooglePhotosAutoCompressor.UiTests.TestApp))]

namespace GooglePhotosAutoCompressor.UiTests
{
    /// <summary>Real app styles, drawn with Skia without a screen (works on CI and Linux without a display).</summary>
    public static class TestApp
    {
        public static AppBuilder BuildAvaloniaApp() =>
            AppBuilder.Configure<App.App>()
                .UseSkia()
                .WithInterFont()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
    }

    /// <summary>Records what the window asked, instead of showing dialogs.</summary>
    internal sealed class FakeDialogs : IDialogs
    {
        public List<string> Asked { get; } = new();
        public bool Answer { get; set; } = true;

        public Task<bool> AskYesNo(string title, string message, string yes = "Yes", string no = "No")
        {
            Asked.Add(title);
            return Task.FromResult(Answer);
        }

        public Task<(bool ok, bool dontAskAgain)> ConfirmBin(string summary, IReadOnlyList<ItemRow> items) => Task.FromResult((Answer, false));
        public Task<string?> PickFolder() => Task.FromResult<string?>(null);
        public Task CopyText(string text) => Task.CompletedTask;
        public Task Open(string pathOrUrl) => Task.CompletedTask;
    }

    /// <summary>
    /// Opens the main window with a temp settings and progress file, checks what it shows, and saves a
    /// screenshot of every page to %TEMP%/gpac-ui-screens for a human (or Claude) to look at.
    /// </summary>
    public sealed class MainWindowTests : IDisposable
    {
        private static readonly string SCREENS = Path.Combine(Path.GetTempPath(), "gpac-ui-screens");
        private readonly string _dir = Directory.CreateTempSubdirectory("gpac-ui-").FullName;

        private readonly List<Window> _windows = new();

        public void Dispose()
        {
            foreach (Window window in _windows)
            {
                window.Close();
            }

            Dispatcher.UIThread.RunJobs();
            Directory.Delete(_dir, true);
        }

        private string SettingsPath => Path.Combine(_dir, "settings.json");
        private string StatePath => Path.Combine(_dir, "state.json");

        private void SeedState()
        {
            string copy = Path.Combine(_dir, "IMG_2_compressed_1a2b3c4d.mp4");
            File.WriteAllText(copy, "x");
            var data = new FileItemsData(StatePath);
            data.AddOrGet(new FileItem { MediaKey = "done", Date = new DateOnly(2024, 7, 4), Duration = TimeSpan.FromSeconds(28), Size = 94L << 20, CompressedSize = 21L << 20, DownloadPath = "IMG_1.MOV", OriginalDeleted = true, CompressUploaded = true, DateSet = true });
            data.AddOrGet(new FileItem { MediaKey = "bin", Date = new DateOnly(2025, 12, 13), Duration = TimeSpan.FromSeconds(130), Size = 63L << 20, CompressedSize = 15L << 20, CompressPath = copy, Verified = true, OriginalDeleted = true });
            data.AddOrGet(new FileItem { MediaKey = "wait", Date = new DateOnly(2026, 6, 8), Duration = TimeSpan.FromSeconds(24), Size = 60L << 20 });
            data.AddOrGet(new FileItem { MediaKey = "photo", MediaType = MediaType.Image, Date = new DateOnly(2023, 9, 1), Size = 5L << 20, CompressionSkipped = true, DownloadPath = "IMG_3.HEIC" });
            data.AddOrGet(new FileItem { MediaKey = "fail", Date = new DateOnly(2022, 5, 21), Duration = TimeSpan.FromSeconds(15), Size = 40L << 20, FailCount = 3, DownloadPath = "broken.mp4" });
            data.Save();
        }

        /// <param name="firstStart">False: the first-start page was answered earlier (as for most starts).</param>
        private (MainWindow window, MainViewModel vm, FakeDialogs dialogs) Open(bool firstStart = false)
        {
            if (firstStart == false && File.Exists(SettingsPath) == false)
            {
                new Settings { SetupDone = true }.Save(SettingsPath);
            }

            var dialogs = new FakeDialogs();
            var log = new TeeTextWriter(null, null);
            var vm = new MainViewModel(dialogs, log, SettingsPath, StatePath);
            var window = new MainWindow { DataContext = vm, Width = 1040, Height = 760 };
            _windows.Add(window);
            window.Show();
            Dispatcher.UIThread.RunJobs();
            return (window, vm, dialogs);
        }

        private static void Screenshot(Window window, string name)
        {
            Dispatcher.UIThread.RunJobs();
            Directory.CreateDirectory(SCREENS);
            string path = Path.Combine(SCREENS, name + ".png");
            window.CaptureRenderedFrame()?.Save(path);
            Console.WriteLine($"Screenshot: {path}");
        }

        private static void ShowTab(Window window, int index)
        {
            window.GetVisualDescendantsOfType<TabControl>().Single().SelectedIndex = index;
            Dispatcher.UIThread.RunJobs();
        }

        [AvaloniaFact]
        public void Run_page_shows_start_steps_and_counts()
        {
            SeedState();
            (MainWindow window, MainViewModel vm, _) = Open();

            Assert.Equal("Start", vm.StartStopText);
            Assert.Equal(6, vm.Steps.Count);
            Assert.Equal(1, vm.DoneCount);
            Assert.Equal(1, vm.InProgressCount);
            Assert.Equal(1, vm.WaitingCount);
            Assert.Equal(1, vm.LeftAloneCount);
            Assert.Equal(1, vm.FailedCount);
            Assert.Equal("73.0 MB", vm.SavedSpaceText); // 94 - 21, only for uploaded copies

            Button start = window.GetVisualDescendantsOfType<Button>().First(b => b.Content as string == "Start");
            Assert.True(start.IsEffectivelyEnabled);
            Screenshot(window, "1-run");
        }

        [AvaloniaFact]
        public void Items_page_lists_every_item_with_status_and_filters()
        {
            SeedState();
            (MainWindow window, MainViewModel vm, _) = Open();

            Assert.Equal(5, vm.Items.Count);
            Assert.Equal("Waiting", vm.Items[0].StatusText); // newest first
            Assert.Contains(vm.Items, r => r.StatusText == "Original in bin, upload next");
            Assert.Contains(vm.Items, r => r.StatusText.StartsWith("Failed"));
            ShowTab(window, 1);
            Screenshot(window, "2-items");

            vm.FilterIndex = 5; // failed
            Assert.Single(vm.Items);

            vm.FilterIndex = 0;
            vm.SearchText = "IMG_1";
            Assert.Single(vm.Items);
            Assert.Equal("Done", vm.Items[0].StatusText);
        }

        [AvaloniaFact]
        public void Settings_page_saves_changes_straight_away()
        {
            (MainWindow window, MainViewModel vm, _) = Open();
            ShowTab(window, 2);
            Screenshot(window, "3-settings");

            vm.Settings.ItemsPerBatch = 25;
            vm.Settings.VideoResolutionIndex = 2;
            vm.Settings.CompressPhotos = false;

            Settings saved = Settings.Load(SettingsPath);
            Assert.Equal(25, saved.ItemsPerBatch);
            Assert.Equal(VideoResolution.Max720p, saved.VideoResolution);
            Assert.False(saved.CompressPhotos);
            Assert.StartsWith("Saved", vm.Settings.SavedText);

            vm.Settings.ResetToDefaultsCommand.Execute(null);
            Assert.Equal(10, Settings.Load(SettingsPath).ItemsPerBatch);
        }

        [AvaloniaFact]
        public void Help_page_renders()
        {
            (MainWindow window, _, _) = Open();
            ShowTab(window, 3);
            Screenshot(window, "4-help");

            Assert.Contains(window.GetVisualDescendantsOfType<TextBlock>(), t => t.Text == "How it works");
        }

        [AvaloniaFact]
        public async Task Retry_failed_gives_failed_items_new_attempts()
        {
            SeedState();
            (_, MainViewModel vm, FakeDialogs dialogs) = Open();

            await vm.RetryFailedCommand.ExecuteAsync(null);

            Assert.Contains("Try failed items again?", dialogs.Asked);
            Assert.Equal(0, vm.FailedCount);
            Assert.Equal(0, new FileItemsData(StatePath).Items.Single(i => i.MediaKey == "fail").FailCount);
        }

        [AvaloniaFact]
        public void Bin_confirmation_dialog_lists_the_items()
        {
            SeedState();
            var rows = new FileItemsData(StatePath).Items.Select(i => new ItemRow(i)).ToList();
            var dialog = new DialogWindow("Move originals to the bin?", "2 compressed copies are checked and ready.", "Move to bin and upload copies", "Not now", rows, "Don't ask again");
            dialog.Show();
            Dispatcher.UIThread.RunJobs();
            Screenshot(dialog, "5-confirm-bin");

            Assert.Contains(dialog.GetVisualDescendantsOfType<Button>(), b => b.Content as string == "Not now");
            Assert.Contains(dialog.GetVisualDescendantsOfType<CheckBox>(), c => c.Content as string == "Don't ask again");
        }

        [AvaloniaFact]
        public void First_start_asks_the_main_choices_once_and_says_where_to_change_them()
        {
            (MainWindow window, MainViewModel vm, _) = Open(firstStart: true);
            Border setup = window.GetVisualDescendantsOfType<Border>().Single(b => b.Name == "SetupPage");

            Assert.True(setup.IsVisible);
            Assert.Contains(window.GetVisualDescendantsOfType<TextBlock>(), t => t.Text?.Contains("change all of this later on the Settings tab") == true);
            RadioButton stop = window.GetVisualDescendantsOfType<RadioButton>().Single(r => (r.Content as string)!.StartsWith("Stop"));
            Assert.True(stop.IsChecked); // safe default: stop after each batch
            Screenshot(window, "0-first-start");

            vm.Settings.RepeatUntilDone = true;
            Assert.False(stop.IsChecked);
            vm.Settings.ConfirmBeforeBin = false;
            vm.Settings.FinishSetupCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();

            Assert.False(setup.IsVisible);
            Settings saved = Settings.Load(SettingsPath);
            Assert.True(saved.SetupDone);
            Assert.True(saved.RepeatUntilDone);
            Assert.False(saved.ConfirmBeforeBin);

            // Next start: not shown again. It can be brought back from the Settings page.
            (MainWindow again, MainViewModel vm2, _) = Open();
            Assert.False(again.GetVisualDescendantsOfType<Border>().Single(b => b.Name == "SetupPage").IsVisible);
            vm2.Settings.ShowSetupAgainCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.True(again.GetVisualDescendantsOfType<Border>().Single(b => b.Name == "SetupPage").IsVisible);
        }

        [AvaloniaFact]
        public void Reset_to_defaults_does_not_bring_back_the_first_start_page()
        {
            (_, MainViewModel vm, _) = Open();
            vm.Settings.ResetToDefaultsCommand.Execute(null);

            Assert.False(vm.Settings.ShowSetup);
            Assert.True(Settings.Load(SettingsPath).SetupDone);
        }

        [AvaloniaFact]
        public void Empty_start_shows_zero_and_no_items()
        {
            (_, MainViewModel vm, _) = Open();

            Assert.Empty(vm.Items);
            Assert.Equal("0 B", vm.SavedSpaceText);
            Assert.StartsWith("Ready", vm.StatusText);
        }
    }

    internal static class VisualExtensions
    {
        public static IEnumerable<T> GetVisualDescendantsOfType<T>(this Avalonia.Visual root) =>
            Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(root).OfType<T>();
    }
}
