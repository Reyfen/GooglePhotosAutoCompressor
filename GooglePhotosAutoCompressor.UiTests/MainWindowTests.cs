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

        /// <summary>Answer of the next three-way dialog; null: Yes or Cancel as <see cref="Answer"/> says.</summary>
        public DialogChoice? Choice { get; set; }

        public Task<DialogChoice> AskWithDanger(string title, string message, string yes, string no, string danger)
        {
            Asked.Add(title);
            DialogChoice choice = Choice ?? (Answer ? DialogChoice.Yes : DialogChoice.Cancel);
            Choice = null;
            return Task.FromResult(choice);
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
        /// <summary>The account the tests "used last": its progress file is what the window shows.</summary>
        private const string ACCOUNT = "tester@example.com";
        private string StatePath => Accounts.ProgressPath(ACCOUNT, _dir);
        /// <summary>Never the real profile: Sign out deletes it.</summary>
        private string ProfileDir => Path.Combine(_dir, "chrome-profile");

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
        private (MainWindow window, MainViewModel vm, FakeDialogs dialogs) Open(bool firstStart = false, Func<string, Task<string?>>? signOut = null)
        {
            if (firstStart == false && File.Exists(SettingsPath) == false)
            {
                new Settings { SetupDone = true, LastAccount = ACCOUNT }.Save(SettingsPath);
            }

            var dialogs = new FakeDialogs();
            var log = new TeeTextWriter(null, null);
            var vm = new MainViewModel(dialogs, log, SettingsPath, _dir, ProfileDir, signOut ?? (_ => throw new InvalidOperationException("UI tests must not sign out of Google")));
            var window = new MainWindow { DataContext = vm, Width = 1040, Height = 760 };
            _windows.Add(window);
            window.Show();
            Dispatcher.UIThread.RunJobs();
            return (window, vm, dialogs);
        }

        private static void Screenshot(Window window, string name)
        {
            Dispatcher.UIThread.RunJobs();
            // Draw a new frame: with a running animation the last frame can otherwise be returned as it was.
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
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
            Assert.Matches(@"^v\d+\.\d+\.\d+", vm.VersionText); // "v1.0.0", as in the file names
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
        public void Step_lines_fill_up_to_the_running_step_and_long_log_lines_wrap()
        {
            (MainWindow window, MainViewModel vm, _) = Open();
            vm.IsRunning = true;
            vm.Steps[0].State = StepState.Done;
            vm.Steps[1].State = StepState.Done;
            vm.Steps[2].State = StepState.Active; // compressing, 60 % through the step

            vm.UpdateConnectors(0.6);

            Assert.Equal(new double[] { 100, 60, 0, 0, 0 }, vm.Connectors.Select(c => c.Value));
            Assert.Equal(new[] { false, true, false, false, false }, vm.Connectors.Select(c => c.IsActive));
            Assert.Equal(5, vm.Connectors.Count);

            // Every bar sits at the middle height of the circles.
            Dispatcher.UIThread.RunJobs();
            var circles = window.GetVisualDescendantsOfType<Border>().Where(b => b.Classes.Contains("step")).ToList();
            var bars = window.GetVisualDescendantsOfType<ProgressBar>().Where(b => b.Classes.Contains("connector")).ToList();
            Assert.Equal(6, circles.Count);
            Assert.Equal(5, bars.Count);
            double CenterY(Visual v) => v.TranslatePoint(new Point(0, v.Bounds.Height / 2), window)!.Value.Y;
            foreach (ProgressBar bar in bars)
            {
                Assert.Equal(CenterY(circles[0]), CenterY(bar), 1);
            }

            string longLine = "[10/10] ok C:\\Users\\someone\\Downloads\\GooglePhotosAutoCompressor\\Compressed\\PXL_20260613_005715199_compressed_e06f7b84.mp4      48.3 MB -> 12.1 MB";
            vm.LogLines.Add(longLine);
            vm.LogLines.Add("ReadTakenTimes");
            Dispatcher.UIThread.RunJobs();
            Screenshot(window, "1b-run-progress");
            Application.Current!.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
            Screenshot(window, "1c-run-progress-dark");
            Application.Current.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Default;

            TextBlock line = window.GetVisualDescendantsOfType<TextBlock>().Single(t => t.Text == longLine);
            Assert.True(line.Bounds.Height > line.FontSize * 2, "a long log line should wrap onto more lines");
            ListBox log = window.GetVisualDescendantsOfType<ListBox>().Single(l => l.Name == "LogList");
            Assert.True(line.Bounds.Width <= log.Bounds.Width, "the log line should not be wider than the log");
            vm.IsRunning = false;
        }

        [AvaloniaFact]
        public void Run_page_fits_without_scrolling_and_the_activity_panel_grows_with_the_window()
        {
            (MainWindow window, MainViewModel vm, _) = Open();
            for (int i = 0; i < 200; i++) vm.LogLines.Add($"line {i}"); // a long log scrolls inside its panel only
            Border panel = window.GetVisualDescendantsOfType<Border>().Single(b => b.Name == "LogPanel");

            double PanelHeightAt(double width, double height)
            {
                window.Width = width;
                window.Height = height;
                Dispatcher.UIThread.RunJobs();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                Dispatcher.UIThread.RunJobs();
                // The whole panel is inside the window: nothing below it needs page scrolling.
                double bottom = panel.TranslatePoint(new Point(0, panel.Bounds.Height), window)!.Value.Y;
                Assert.True(bottom <= window.Bounds.Height, $"activity panel ends at {bottom:0}, below the {height:0} px window");
                Console.WriteLine($"{width}x{height}: activity panel {panel.Bounds.Height:0} px");
                return panel.Bounds.Height;
            }

            // No ScrollViewer around the page: the only one above the log lines is the log's own.
            Assert.DoesNotContain(window.GetVisualDescendantsOfType<ScrollViewer>(), sv => sv.IsEffectivelyVisible && sv.GetVisualDescendantsOfType<Border>().Contains(panel));

            double atDefault = PanelHeightAt(1040, 760);
            Screenshot(window, "1d-run-default-size");
            double atTall = PanelHeightAt(1040, 1000);
            double atMinimum = PanelHeightAt(window.MinWidth, window.MinHeight);

            Assert.True(atDefault > 160);
            Assert.Equal(atDefault + 240, atTall, 1); // all extra height goes to the activity panel
            Assert.True(atMinimum >= panel.MinHeight - 0.5, $"at the minimum window size the panel is {atMinimum:0} px");
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
        public void Advanced_settings_start_closed_and_open_on_click()
        {
            (MainWindow window, _, _) = Open();
            ShowTab(window, 2);
            Expander advanced = window.GetVisualDescendantsOfType<Expander>().Single(e => e.Name == "AdvancedSettings");

            Assert.False(advanced.IsExpanded);
            Assert.DoesNotContain(window.GetVisualDescendantsOfType<TextBlock>(), t => t.Text == "Photo quality" && t.IsEffectivelyVisible);

            advanced.IsExpanded = true;
            Dispatcher.UIThread.RunJobs();
            Screenshot(window, "3b-settings-advanced");
            Assert.Contains(window.GetVisualDescendantsOfType<TextBlock>(), t => t.Text == "Photo quality" && t.IsEffectivelyVisible);
        }

        [AvaloniaFact]
        public void During_a_run_batch_settings_still_change_and_the_rest_is_locked()
        {
            (MainWindow window, MainViewModel vm, _) = Open();
            ShowTab(window, 2);
            vm.IsRunning = true;
            Dispatcher.UIThread.RunJobs();
            Screenshot(window, "3c-settings-during-run");

            CheckBox Box(string text) => window.GetVisualDescendantsOfType<CheckBox>().First(c => c.Content as string == text);
            Assert.False(Box("Videos").IsEffectivelyEnabled);                                      // what to compress: locked
            Assert.True(Box("Carry on with the next batch until nothing is left").IsEffectivelyEnabled);
            Assert.True(Box("Show what will be moved and ask first").IsEffectivelyEnabled);
            Assert.True(Box("Delete its files from this PC").IsEffectivelyEnabled);

            vm.Settings.RepeatUntilDone = true;
            vm.Settings.ItemsPerBatch = 30;

            Assert.True(Settings.Current.RepeatUntilDone);     // the running pipeline reads it at the end of the batch
            Assert.Equal(30, Settings.Current.ItemsPerBatch);  // and this at the next batch's download step
            Assert.True(Settings.Load(SettingsPath).RepeatUntilDone);
            Assert.Contains("next batch", vm.Settings.SavedText);

            // The same switch is on the Run page.
            ShowTab(window, 0);
            Assert.True(Box("When this batch is done, carry on with the next one").IsChecked);
            vm.IsRunning = false;
            Assert.False(vm.Settings.IsLocked);
        }

        [AvaloniaFact]
        public async Task Clean_asks_first_then_deletes_work_files_but_keeps_copies_waiting_for_upload()
        {
            string work = Path.Combine(_dir, "work");
            string Make(string relative)
            {
                string path = Path.Combine(work, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, "x");
                return path;
            }
            string original = Make("Originals/a1/A.mp4");
            string waitingCopy = Make("Compressed/B_compressed_b2.mp4");
            var state = new FileItemsData(StatePath);
            state.AddOrGet(new FileItem { MediaKey = "wait", CompressPath = waitingCopy, Verified = true, OriginalDeleted = true });
            state.Save();

            (MainWindow window, MainViewModel vm, FakeDialogs dialogs) = Open();
            vm.Settings.WorkFolder = work;
            ShowTab(window, 2);
            Assert.Contains(window.GetVisualDescendantsOfType<Button>(), b => b.Content as string == "Open");
            Assert.Contains(window.GetVisualDescendantsOfType<Button>(), b => b.Content as string == "Clean…");

            dialogs.Answer = false;
            await vm.CleanWorkFolderCommand.ExecuteAsync(null);
            Assert.Contains("Delete the work files for good?", dialogs.Asked);
            Assert.True(File.Exists(original)); // cancelled: nothing deleted

            dialogs.Answer = true;
            await vm.CleanWorkFolderCommand.ExecuteAsync(null);
            Assert.False(File.Exists(original));
            Assert.True(File.Exists(waitingCopy)); // its original is in the Google bin: kept
            Assert.StartsWith("Deleted 1 file", vm.StatusText);

            vm.IsRunning = true;
            Assert.False(vm.CleanWorkFolderCommand.CanExecute(null)); // never while a run uses the files
            vm.IsRunning = false;
        }

        [AvaloniaFact]
        public async Task Sign_out_asks_first_signs_out_through_google_and_keeps_the_profile()
        {
            Directory.CreateDirectory(Path.Combine(ProfileDir, "Default"));
            File.WriteAllText(Path.Combine(ProfileDir, "Default", "Cookies"), "x");
            new FileItemsData(StatePath).Save();
            var signedOut = new List<string>();
            var release = new TaskCompletionSource<string?>();
            Func<string, Task<string?>> fakeSignOut = dir => { signedOut.Add(dir); return release.Task; };

            (MainWindow window, MainViewModel vm, FakeDialogs dialogs) = Open(signOut: fakeSignOut);
            Button signOut = window.GetVisualDescendantsOfType<Button>().Single(b => b.Content as string == "Sign out");
            Assert.True(signOut.IsEffectivelyVisible);
            Screenshot(window, "1e-header-sign-out");

            dialogs.Answer = false;
            await vm.SignOutCommand.ExecuteAsync(null);
            Assert.Contains("Sign out of Google?", dialogs.Asked);
            Assert.Empty(signedOut); // cancelled

            vm.IsRunning = true;
            Assert.False(vm.SignOutCommand.CanExecute(null)); // Chrome uses the profile during a run
            vm.IsRunning = false;

            dialogs.Answer = true;
            Task running = vm.SignOutCommand.ExecuteAsync(null);
            Assert.Equal(new[] { ProfileDir }, signedOut);
            Assert.False(vm.CanPressStart);              // no Start while the profile is in use
            Assert.Equal("Signing out…", vm.SignOutText);
            release.SetResult(null);
            await running;

            Assert.True(vm.CanPressStart);
            Assert.True(Directory.Exists(ProfileDir));    // the profile and its account list stay
            Assert.True(File.Exists(StatePath));
            Assert.StartsWith("Signed out", vm.StatusText);
        }

        [AvaloniaFact]
        public async Task Delete_all_login_data_asks_twice_and_deletes_the_apps_browser_profile()
        {
            Directory.CreateDirectory(Path.Combine(ProfileDir, "Default"));
            File.WriteAllText(Path.Combine(ProfileDir, "Default", "Cookies"), "x");
            new FileItemsData(StatePath).Save();
            (_, MainViewModel vm, FakeDialogs dialogs) = Open(signOut: _ => throw new InvalidOperationException("must not sign out"));

            // Red button, then "Cancel" in the second question: nothing happens.
            dialogs.Choice = DialogChoice.Danger;
            dialogs.Answer = false;
            await vm.SignOutCommand.ExecuteAsync(null);
            Assert.Contains("Delete all login data?", dialogs.Asked);
            Assert.True(Directory.Exists(ProfileDir));

            // Red button, then "Delete permanently".
            dialogs.Choice = DialogChoice.Danger;
            dialogs.Answer = true;
            await vm.SignOutCommand.ExecuteAsync(null);
            Assert.False(Directory.Exists(ProfileDir));
            Assert.False(File.Exists(StatePath)); // owner's choice: progress and stats go too
            Assert.Equal("", vm.Settings.LastAccount);
            Assert.Equal(0, vm.DoneCount);
            Assert.False(vm.IsSignedIn);
            Assert.StartsWith("All login data, progress and stats deleted", vm.StatusText);
        }

        [AvaloniaFact]
        public void Sign_out_dialog_shows_a_red_delete_button_on_the_left()
        {
            var dialog = new DialogWindow("Sign out of Google?", "Signs the app's browser out of Google.", "Sign out", "Cancel",
                items: null, dontAskText: null, danger: "Delete all login data");
            dialog.Show();
            Dispatcher.UIThread.RunJobs();
            Screenshot(dialog, "5b-sign-out-dialog");

            Button red = dialog.GetVisualDescendantsOfType<Button>().Single(b => b.Content as string == "Delete all login data");
            Button signOut = dialog.GetVisualDescendantsOfType<Button>().Single(b => b.Content as string == "Sign out");
            Assert.Contains("danger", red.Classes);
            Assert.True(red.TranslatePoint(new Point(0, 0), dialog)!.Value.X < signOut.TranslatePoint(new Point(0, 0), dialog)!.Value.X);
            Assert.Equal(DialogChoice.Cancel, dialog.Choice); // closing without a click is Cancel
        }

        [AvaloniaFact]
        public async Task Stats_belong_to_the_last_account_and_show_zero_after_sign_out()
        {
            SeedState();
            Directory.CreateDirectory(ProfileDir);
            File.WriteAllText(Path.Combine(ProfileDir, "Cookies"), "x");
            (MainWindow window, MainViewModel vm, FakeDialogs dialogs) = Open(signOut: _ => Task.FromResult<string?>(null));
            Assert.Equal(1, vm.DoneCount);
            Assert.Contains(window.GetVisualDescendantsOfType<TextBlock>(), t => t.Text == ACCOUNT && t.IsEffectivelyVisible);

            dialogs.Answer = true;
            await vm.SignOutCommand.ExecuteAsync(null);

            Assert.Equal(0, vm.DoneCount);                 // signed out: zero
            Assert.Empty(vm.Items);
            Assert.True(File.Exists(StatePath));           // the account's progress stays
            Assert.Equal("", Settings.Load(SettingsPath).LastAccount);

            // Signing in again with that account (a run reports it) brings its stats back.
            vm.Settings.LastAccount = ACCOUNT;
            (_, MainViewModel again, _) = Open();
            Assert.Equal(1, again.DoneCount);
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
