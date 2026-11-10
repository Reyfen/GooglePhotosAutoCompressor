using Microsoft.Playwright;

namespace GooglePhotosAutoCompressor.Modules
{
    internal class Initlizer
    {
        public static async Task<(IPlaywright playwright, IPage page)> OpenBrowser()
        {
            Console.WriteLine("\n===\nOpenBrowser\n===\n");

            IPlaywright playwright = await Playwright.CreateAsync();
            string profileDir = GetProfileDir();
            Console.WriteLine($"Browser profile: {profileDir}");

            IBrowserContext context;
            try
            {
                // Installed Google Chrome is preferred: Google sign-in trusts it more than bundled Chromium.
                context = await Launch(playwright, profileDir, "chrome");
            }
            catch (PlaywrightException e)
            {
                Console.WriteLine($"Google Chrome could not be started ({FirstLine(e.Message)}). Using Playwright's Chromium instead.");
                EnsureChromiumInstalled();
                context = await Launch(playwright, profileDir, channel: null);
            }

            IPage page = context.Pages.FirstOrDefault() ?? await context.NewPageAsync();
            return (playwright, page);
        }

        /// <summary>Starts Chrome (or Chromium when <paramref name="channel"/> is null) with the given profile. Also used by the live page tests.</summary>
        internal static Task<IBrowserContext> Launch(IPlaywright playwright, string profileDir, string? channel, bool headless = false)
        {
            return playwright.Chromium.LaunchPersistentContextAsync(
                profileDir,
                new BrowserTypeLaunchPersistentContextOptions()
                {
                    Headless = headless,
                    Channel = channel,
                    // Muted: the app opens videos (date dialog, date step) and their sound would play.
                    Args = new[] { "--disable-blink-features=AutomationControlled", "--mute-audio" },
                    Locale = Const.LOCALE,
                    AcceptDownloads = true,
                });
        }

        /// <summary>
        /// Keeps using an existing profile (older versions created "chrome-profile" in the working
        /// directory) so the user stays signed in; otherwise uses the per-user app data folder.
        /// </summary>
        internal static string GetProfileDir()
        {
            foreach (string legacy in new[] { Path.GetFullPath("chrome-profile"), Path.Combine(AppContext.BaseDirectory, "chrome-profile") })
            {
                if (Directory.Exists(legacy))
                {
                    return legacy;
                }
            }

            return Path.Combine(Settings.CONFIG_DIR, "chrome-profile");
        }

        /// <summary>True when the app's browser profile exists, i.e. someone may be signed in.</summary>
        internal static bool HasProfile(string profileDir) =>
            Directory.Exists(profileDir) && Directory.EnumerateFileSystemEntries(profileDir).Any();

        /// <summary>
        /// Signs the app out of Google by deleting its own browser profile (sign-in, cookies, history). The
        /// user's normal Chrome is a different profile and is not touched; progress and files are not either.
        /// Chrome must not be running with this profile (no run going). Returns null when done, else why not.
        /// </summary>
        internal static string? SignOut(string profileDir)
        {
            if (Directory.Exists(profileDir) == false)
            {
                return null;
            }

            try
            {
                Directory.Delete(profileDir, recursive: true);
                Console.WriteLine($"Signed out: deleted the browser profile {profileDir}");
                return null;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Usually a Chrome window of this profile is still open.
                return $"Could not delete {profileDir}: {e.Message} Close any Chrome window the app opened and try again.";
            }
        }

        private static void EnsureChromiumInstalled()
        {
            int exitCode = Microsoft.Playwright.Program.Main(new[] { "install", "chromium" });
            if (exitCode != 0)
            {
                throw new InvalidOperationException(
                    "Could not install Chromium. Install Google Chrome, or on Linux run: pwsh playwright.ps1 install --with-deps chromium");
            }
        }

        private static string FirstLine(string text) => text.Split('\n')[0].Trim();

        /// <summary>Waits (as long as it takes, until cancelled) for the user to sign in. Calls <paramref name="onWaiting"/> when sign-in is needed.</summary>
        public static async Task Login(IPage page, CancellationToken cancellationToken = default, Action? onWaiting = null)
        {
            Console.WriteLine("\n===\nLogin\n===\n");

            await page.GotoAsync($"https://photos.google.com/?hl={Const.LOCALE}");

            if (page.Url.StartsWith("https://photos.google.com"))
            {
                return;
            }

            Console.WriteLine("Sign in to Google in the browser window. Waiting...");
            onWaiting?.Invoke();
            await page.GotoAsync($"https://photos.google.com/login?hl={Const.LOCALE}");

            // Typing a password and doing 2FA can take minutes. Wait in short steps so Stop works.
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    await page.WaitForURLAsync("https://photos.google.com/**", new() { Timeout = 2000 });
                    return;
                }
                catch (TimeoutException)
                {
                }
            }
        }
    }
}
