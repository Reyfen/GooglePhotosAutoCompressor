using GooglePhotosAutoCompressor.Modules;
using Microsoft.Playwright;

namespace GooglePhotosAutoCompressor.Tests.Live
{
    /// <summary>
    /// Opens Google Photos once for all live page tests, with the app's signed-in browser profile.
    /// When something needed is missing, <see cref="SkipReason"/> says exactly what to do.
    /// </summary>
    public sealed class LivePhotosFixture : IAsyncLifetime
    {
        public const string HOME_URL = "https://photos.google.com/?hl=" + Const.LOCALE;
        public const string LARGE_FILES_URL = "https://photos.google.com/quotamanagement/large?hl=" + Const.LOCALE;

        public string? SkipReason { get; private set; }
        public IPage Page { get; private set; } = null!;
        public string ProfileDir { get; private set; } = "";
        /// <summary>Screenshots of every check are kept here, so a failure can be looked at afterwards.</summary>
        public string ScreenshotDir { get; } = Path.Combine(Path.GetTempPath(), "gpac-live-screens", DateTime.Now.ToString("yyyyMMdd-HHmmss"));

        private IPlaywright? _playwright;
        private IBrowserContext? _context;

        public async Task InitializeAsync()
        {
            if (Environment.GetEnvironmentVariable("GPAC_TEST_LIVE") == "0")
            {
                SkipReason = "Live Google Photos tests are switched off (GPAC_TEST_LIVE=0).";
                return;
            }

            if (ClaudeCode.ExecutablePath == null)
            {
                SkipReason = ClaudeCode.MISSING_MESSAGE;
                return;
            }

            string? profile = FindProfileDir();
            if (profile == null)
            {
                SkipReason =
                    "Live Google Photos test skipped: no signed-in browser profile found. " +
                    "Run the app once (dotnet run --project GooglePhotosAutoCompressor), sign in to Google in the window it opens, " +
                    "then close it. Or set GPAC_TEST_PROFILE_DIR to the profile folder (the 'chrome-profile' folder next to the app). " +
                    "Using a separate test Google account is recommended.";
                return;
            }
            ProfileDir = profile;

            bool headless = Environment.GetEnvironmentVariable("GPAC_TEST_HEADLESS") == "1";
            _playwright = await Playwright.CreateAsync();
            try
            {
                _context = await Initlizer.Launch(_playwright, profile, "chrome", headless);
            }
            catch (PlaywrightException chromeError)
            {
                try
                {
                    _context = await Initlizer.Launch(_playwright, profile, channel: null, headless);
                }
                catch (PlaywrightException)
                {
                    SkipReason =
                        $"Live Google Photos test skipped: could not open a browser with the profile {profile}: " +
                        $"{chromeError.Message.Split('\n')[0]} " +
                        "Close the app (and any browser window using that profile) and run the tests again. " +
                        "Google Chrome must be installed.";
                    return;
                }
            }

            Page = _context.Pages.FirstOrDefault() ?? await _context.NewPageAsync();
            Page.SetDefaultTimeout(20_000);

            await Page.GotoAsync(HOME_URL);
            if (!Page.Url.StartsWith("https://photos.google.com") &&
                Environment.GetEnvironmentVariable("GPAC_TEST_SIGN_IN") == "1" && !headless)
            {
                // Lets the owner sign in once in the test's browser window; the profile then stays signed in.
                await Page.GotoAsync("https://photos.google.com/login?hl=" + Const.LOCALE);
                try { await Page.WaitForURLAsync("https://photos.google.com/**", new() { Timeout = 15 * 60_000 }); }
                catch (TimeoutException) { }
            }

            if (!Page.Url.StartsWith("https://photos.google.com"))
            {
                SkipReason =
                    $"Live Google Photos test skipped: the profile {profile} is not signed in (got {Page.Url}). " +
                    "Either run the app once and sign in to Google in the window it opens, or run the tests with " +
                    "GPAC_TEST_SIGN_IN=1 and sign in within 15 minutes in the browser window the tests open.";
                return;
            }

            Directory.CreateDirectory(ScreenshotDir);
        }

        public async Task DisposeAsync()
        {
            if (_context != null) await _context.CloseAsync();
            _playwright?.Dispose();
        }

        /// <summary>
        /// GPAC_TEST_PROFILE_DIR, else the profile the app created next to its build output, else the per-user one.
        /// </summary>
        private static string? FindProfileDir()
        {
            if (Environment.GetEnvironmentVariable("GPAC_TEST_PROFILE_DIR") is { Length: > 0 } custom)
                return IsProfile(custom) ? custom : null;

            for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            {
                string app = Path.Combine(dir.FullName, "GooglePhotosAutoCompressor", "bin");
                if (!Directory.Exists(app)) continue;

                foreach (string config in new[] { "Debug", "Release" })
                {
                    string candidate = Path.Combine(app, config, "net8.0", "chrome-profile");
                    if (IsProfile(candidate)) return candidate;
                }
                break;
            }

            string perUser = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "GooglePhotosAutoCompressor", "chrome-profile");
            return IsProfile(perUser) ? perUser : null;
        }

        private static bool IsProfile(string dir) => Directory.Exists(Path.Combine(dir, "Default"));
    }

    [CollectionDefinition(NAME)]
    public sealed class LivePhotosCollection : ICollectionFixture<LivePhotosFixture>
    {
        public const string NAME = "Live Google Photos";
    }
}
