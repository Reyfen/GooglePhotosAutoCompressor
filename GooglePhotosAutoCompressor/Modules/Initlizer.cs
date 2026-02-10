using Microsoft.Playwright;

namespace GooglePhotosAutoCompressor.Modules
{
    internal class Initlizer
    {
        public static async Task<(IPlaywright playwright, IPage page)> OpenBrowser()
        {
            Console.WriteLine("\n===\nOpenBrowser\n===\n");

            IPlaywright playwright = await Playwright.CreateAsync();

            IBrowserContext context = await playwright.Chromium.LaunchPersistentContextAsync(
                userDataDir: "chrome-profile",
                new BrowserTypeLaunchPersistentContextOptions()
                {
                    Headless = false,
                    Channel = "chrome",
                    Args = new[] {
                        "--disable-blink-features=AutomationControlled"
                    },
                    Locale = Const.LOCALE,
                });

            IPage page = await context.NewPageAsync();
            return (playwright, page);
        }

        public static async Task Login(IPage page)
        {
            Console.WriteLine("\n===\nLoginAndOpenLargeFilesPage\n===\n");

            await page.GotoAsync($"https://photos.google.com/?hl={Const.LOCALE}");

            bool redirected = page.Url.StartsWith("https://www.google.com/photos/about");

            if (redirected)
            {
                await page.GotoAsync($"https://photos.google.com/login?hl={Const.LOCALE}");

                await page.WaitForURLAsync("https://photos.google.com/**");
            }
        }
    }
}
