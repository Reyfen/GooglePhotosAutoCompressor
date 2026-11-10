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

            IBrowserContext context = await LaunchPreferringChrome(playwright, profileDir, headless: false);
            IPage page = context.Pages.FirstOrDefault() ?? await context.NewPageAsync();
            return (playwright, page);
        }

        internal const int CHROME_START_TRIES = 3;

        /// <summary>
        /// Installed Google Chrome is preferred: Google sign-in trusts it more than bundled Chromium. When Chrome is
        /// installed but closes right after starting (seen 2026-11-10: exit code 0x80000003, e.g. while Chrome updates
        /// or another window still holds the profile), it is tried again a few times. It never falls back to Chromium
        /// then: a different browser version can damage the profile Chrome made. Chromium is only used without Chrome.
        /// </summary>
        internal static async Task<IBrowserContext> LaunchPreferringChrome(IPlaywright playwright, string profileDir, bool headless)
        {
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    return await Launch(playwright, profileDir, "chrome", headless);
                }
                catch (PlaywrightException e) when (IsChromeMissing(e.Message))
                {
                    Console.WriteLine("Google Chrome is not installed. Using Playwright's Chromium instead.");
                    EnsureChromiumInstalled();
                    return await Launch(playwright, profileDir, channel: null, headless);
                }
                catch (PlaywrightException e) when (attempt < CHROME_START_TRIES)
                {
                    Console.WriteLine($"Google Chrome closed while starting ({FirstLine(e.Message)}). Trying again ({attempt + 1}/{CHROME_START_TRIES}).");
                    await Task.Delay(TimeSpan.FromSeconds(3 * attempt));
                }
                catch (PlaywrightException e)
                {
                    throw new InvalidOperationException(
                        $"Google Chrome closed right after starting, {CHROME_START_TRIES} times ({FirstLine(e.Message)}). " +
                        "Close any Chrome window the app opened, wait a minute if Chrome is updating, then press Start again.", e);
                }
            }
        }

        /// <summary>Playwright's message when the "chrome" channel is not installed (as opposed to Chrome failing to start).</summary>
        internal static bool IsChromeMissing(string message) =>
            message.Contains("is not found", StringComparison.OrdinalIgnoreCase)
            || message.Contains("Executable doesn't exist", StringComparison.OrdinalIgnoreCase);

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

        /// <summary>
        /// The signed-in account's email, from the account button of Google Photos (read-only). Null when it can't be
        /// read; the run then stops rather than risk mixing two accounts' progress.
        /// </summary>
        internal static async Task<string?> ReadAccountAsync(IPage page)
        {
            ILocator button = page.Locator(Selectors.ACCOUNT_BUTTON).First;
            try
            {
                await button.WaitForAsync(new() { State = WaitForSelectorState.Attached, Timeout = 15_000 });
                return Accounts.ParseEmail(await button.GetAttributeAsync("aria-label"));
            }
            catch (TimeoutException)
            {
                return null;
            }
        }

        /// <summary>
        /// Is the browser still signed in to the run's account? Someone may sign into another account in the Chrome
        /// window during a long run; the next steps would then treat this account's originals as gone and upload its
        /// copies into the other one. Returns null when it is the same account, else why the run must stop.
        /// </summary>
        internal static async Task<string?> CheckAccountAsync(IPage page, string expected)
        {
            await GotoEnglishAsync(page, $"https://photos.google.com/?hl={Const.LOCALE}");
            string? now = await ReadAccountAsync(page);
            return Accounts.Mismatch(expected, now);
        }

        /// <summary>
        /// Opens a Google Photos page in English (<see cref="Const.LOCALE"/>): selectors use English labels. The hl in
        /// the address is not enough: some pages forward to another address without it ("Recently added" does) and then
        /// show in the account's own language (seen 2026-11-10 with a Ukrainian account: the date step found no
        /// "Open info" button). Such a page is opened once more with hl added.
        /// </summary>
        internal static async Task GotoEnglishAsync(IPage page, string url)
        {
            await page.GotoAsync(url);
            await KeepEnglishAsync(page);
        }

        /// <summary>Reloads the current Google Photos page with hl added when it is shown in another language.</summary>
        internal static async Task KeepEnglishAsync(IPage page)
        {
            if (IsInGooglePhotos(page.Url) == false)
            {
                return;
            }

            string lang = await page.EvaluateAsync<string>("() => document.documentElement.lang || ''");
            if (IsAppLanguage(lang))
            {
                return;
            }

            Console.WriteLine($"Google Photos opened in '{lang}'; opening it again in {Const.LOCALE}.");
            await page.GotoAsync(Selectors.WithLocale(page.Url));
        }

        /// <summary>True when the page language (html lang) is the app's; an unknown (empty) one is not trusted.</summary>
        internal static bool IsAppLanguage(string? lang) => string.Equals(lang, Const.LOCALE, StringComparison.OrdinalIgnoreCase);

        /// <summary>Where Google sent the browser instead of Google Photos, when a person has to act.</summary>
        internal enum GoogleWants
        {
            /// <summary>Signed out: Google Photos shows its "about" page (seen live 2026-11-10).</summary>
            SignIn,
            /// <summary>A Google accounts page: sign-in, "Verify it's you", 2-step challenge.</summary>
            Confirm,
            /// <summary>"Our systems have detected unusual traffic" (google.com/sorry).</summary>
            UnusualTraffic,
        }

        /// <summary>What Google wants from a person on this page, or null when it is a normal page.</summary>
        internal static GoogleWants? WhatGoogleWants(string? url)
        {
            if (string.IsNullOrEmpty(url))
            {
                return null;
            }

            if (url.StartsWith("https://www.google.com/sorry", StringComparison.OrdinalIgnoreCase))
            {
                return GoogleWants.UnusualTraffic;
            }

            if (url.StartsWith("https://accounts.google.com", StringComparison.OrdinalIgnoreCase))
            {
                return GoogleWants.Confirm;
            }

            if (url.StartsWith("https://www.google.com/photos/about", StringComparison.OrdinalIgnoreCase)
                || (url.StartsWith("https://photos.google.com", StringComparison.OrdinalIgnoreCase) && IsInGooglePhotos(url) == false))
            {
                return GoogleWants.SignIn;
            }

            return null;
        }

        /// <summary>
        /// When Google needs a person (signed out, "verify it's you", unusual traffic), waits — as long as it takes,
        /// until stopped — until Google Photos is back. Returns false at once when the page is a normal one.
        /// </summary>
        internal static async Task<bool> WaitForPersonAsync(IPage page, CancellationToken cancellationToken, Action<string> onWaiting)
        {
            GoogleWants? wants = WhatGoogleWants(page.Url);
            if (wants == null)
            {
                return false;
            }

            if (wants == GoogleWants.SignIn)
            {
                // Same wait as at the start of a run.
                await Login(page, cancellationToken, () => onWaiting("Google signed the app out. Sign in again in the Chrome window. The app waits for you."));
                return true;
            }

            onWaiting(wants == GoogleWants.UnusualTraffic
                ? "Google asks to confirm you are not a robot. Do that in the Chrome window. The app waits for you."
                : "Google asks you to sign in or confirm it's you in the Chrome window. The app waits for you.");
            Console.WriteLine($"Waiting for a person: {wants} ({page.Url.Split('?')[0]})");
            while (WhatGoogleWants(page.Url) != null)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Delay(2000, cancellationToken);
            }

            return true;
        }

        /// <summary>True when the app's browser profile exists, i.e. someone may be signed in.</summary>
        internal static bool HasProfile(string profileDir) =>
            Directory.Exists(profileDir) && Directory.EnumerateFileSystemEntries(profileDir).Any();

        /// <summary>
        /// Signs the app's browser out of Google the way Google's own "Sign out" does: the profile is kept, so the
        /// accounts stay listed as "signed out" in Google's account chooser and one can sign back in by picking it.
        /// Opens the app's profile without a window, visits Google's sign-out page, then checks that Google Photos
        /// asks for a sign-in. Chrome must not be running with this profile (no run going).
        /// Returns null when signed out, else what went wrong.
        /// </summary>
        internal static async Task<string?> SignOutAsync(string profileDir)
        {
            if (HasProfile(profileDir) == false)
            {
                return null;
            }

            using IPlaywright playwright = await Playwright.CreateAsync();
            IBrowserContext context;
            try
            {
                context = await LaunchPreferringChrome(playwright, profileDir, headless: true);
            }
            catch (Exception e) when (e is PlaywrightException or InvalidOperationException)
            {
                return $"Could not open the app's browser: {FirstLine(e.Message)}";
            }

            try
            {
                IPage page = context.Pages.FirstOrDefault() ?? await context.NewPageAsync();
                await page.GotoAsync(Selectors.SIGN_OUT_URL);
                await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 20_000 });

                // Signed out when Google Photos sends us to the sign-in page instead of the library.
                await page.GotoAsync($"https://photos.google.com/?hl={Const.LOCALE}");
                if (IsInGooglePhotos(page.Url))
                {
                    return "Google still shows this browser as signed in. Try again, or sign out in the Chrome window during a run.";
                }

                Console.WriteLine("Signed out of Google (accounts stay listed for signing in again).");
                return null;
            }
            catch (Exception e) when (e is PlaywrightException or TimeoutException)
            {
                return $"Could not sign out: {FirstLine(e.Message)}. Check the internet connection and try again.";
            }
            finally
            {
                await context.CloseAsync();
            }
        }

        /// <summary>
        /// "Delete all login data": deletes the app's own browser profile, so every account in its list, cookies and
        /// history are gone for good. The user's normal Chrome is a different profile and is not touched; progress
        /// and files are not either. Chrome must not be running with this profile. Returns null when done, else why not.
        /// </summary>
        internal static string? DeleteLoginData(string profileDir)
        {
            if (Directory.Exists(profileDir) == false)
            {
                return null;
            }

            try
            {
                Directory.Delete(profileDir, recursive: true);
                Console.WriteLine($"Deleted all login data: the browser profile {profileDir}");
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

            if (IsInGooglePhotos(page.Url))
            {
                await KeepEnglishAsync(page);
                return;
            }

            Console.WriteLine("Sign in to Google in the browser window. Waiting...");
            onWaiting?.Invoke();
            await page.GotoAsync($"https://photos.google.com/login?hl={Const.LOCALE}");

            // Typing a password and doing 2FA can take minutes. Wait in short steps so Stop works.
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                // Not WaitForURL("photos.google.com/**"): it also matches the sign-in address photos.google.com/login
                // itself, so the wait ended before anyone signed in (seen 2026-11-10 after Sign out).
                if (IsInGooglePhotos(page.Url))
                {
                    // After a sign-in Google lands on its own address, in the account's language.
                    await KeepEnglishAsync(page);
                    return;
                }

                await Task.Delay(2000, cancellationToken);
            }
        }

        /// <summary>
        /// True on a Google Photos page of a signed-in account: on photos.google.com, but not its sign-in address
        /// (/login, which only forwards to Google's sign-in) and not a page where Google wants a person.
        /// </summary>
        internal static bool IsInGooglePhotos(string? url)
        {
            if (string.IsNullOrEmpty(url) || url.StartsWith("https://photos.google.com", StringComparison.OrdinalIgnoreCase) == false)
            {
                return false;
            }

            string path = new Uri(url).AbsolutePath.TrimEnd('/');
            return path.Equals("/login", StringComparison.OrdinalIgnoreCase) == false;
        }
    }
}
