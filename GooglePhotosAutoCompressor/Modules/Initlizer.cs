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

        private static Task<IBrowserContext> Launch(IPlaywright playwright, string profileDir, string? channel)
        {
            return playwright.Chromium.LaunchPersistentContextAsync(
                profileDir,
                new BrowserTypeLaunchPersistentContextOptions()
                {
                    Headless = false,
                    Channel = channel,
                    Args = new[] { "--disable-blink-features=AutomationControlled" },
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

            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GooglePhotosAutoCompressor", "chrome-profile");
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

        public static async Task Login(IPage page)
        {
            Console.WriteLine("\n===\nLogin\n===\n");

            await page.GotoAsync($"https://photos.google.com/?hl={Const.LOCALE}");

            bool redirected = page.Url.StartsWith("https://www.google.com/photos/about");

            if (redirected)
            {
                Console.WriteLine("Sign in to Google in the browser window. Waiting...");
                await page.GotoAsync($"https://photos.google.com/login?hl={Const.LOCALE}");

                // No timeout: typing a password and doing 2FA takes longer than Playwright's 30 s default.
                await page.WaitForURLAsync("https://photos.google.com/**", new() { Timeout = 0 });
            }
        }
    }
}
