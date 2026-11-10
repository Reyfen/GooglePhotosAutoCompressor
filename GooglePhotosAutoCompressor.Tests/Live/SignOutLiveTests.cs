using GooglePhotosAutoCompressor.Modules;
using Microsoft.Playwright;

namespace GooglePhotosAutoCompressor.Tests.Live
{
    /// <summary>
    /// The Sign out button on the real Google pages, without touching any account: it runs on a brand-new, empty
    /// browser profile in a temp folder, never on the app's signed-in one. Checks that Google's sign-out page still
    /// answers and that the app sees "signed out" (Google Photos sends it to the sign-in page).
    /// Needs Google Chrome (or Playwright's Chromium) and the internet; no Claude Code.
    /// </summary>
    [Trait("Category", "Live")]
    public sealed class SignOutLiveTests : IDisposable
    {
        private readonly string _profile = Directory.CreateTempSubdirectory("gpac-signout-profile-").FullName;

        public void Dispose()
        {
            try { Directory.Delete(_profile, true); } catch (IOException) { }
        }

        private static void RequireLive() =>
            Skip.If(Environment.GetEnvironmentVariable("GPAC_TEST_LIVE") == "0", "Live tests are switched off (GPAC_TEST_LIVE=0).");

        [SkippableFact]
        public async Task Googles_sign_out_page_answers()
        {
            RequireLive();
            using IPlaywright playwright = await Playwright.CreateAsync();
            IBrowserContext context = await Initlizer.Launch(playwright, _profile, "chrome", headless: true);
            try
            {
                IPage page = context.Pages.FirstOrDefault() ?? await context.NewPageAsync();
                IResponse? response = await page.GotoAsync(Selectors.SIGN_OUT_URL);

                Assert.NotNull(response);
                Assert.True(response!.Status < 400, $"{Selectors.SIGN_OUT_URL} answered {response.Status}");
                Assert.StartsWith("https://accounts.google.com", page.Url);
            }
            finally
            {
                await context.CloseAsync();
            }
        }

        [SkippableFact]
        public async Task A_signed_out_browser_lands_where_the_app_waits_for_a_person()
        {
            RequireLive();
            // A run that gets signed out half way must pause and ask for a sign-in, not fail its steps.
            using IPlaywright playwright = await Playwright.CreateAsync();
            IBrowserContext context = await Initlizer.Launch(playwright, _profile, "chrome", headless: true);
            try
            {
                IPage page = context.Pages.FirstOrDefault() ?? await context.NewPageAsync();
                await page.GotoAsync($"https://photos.google.com/quotamanagement/large?hl={Const.LOCALE}");

                Assert.NotNull(Initlizer.WhatGoogleWants(page.Url));
            }
            finally
            {
                await context.CloseAsync();
            }
        }

        [SkippableFact]
        public async Task The_sign_in_page_does_not_count_as_signed_in()
        {
            RequireLive();
            // The sign-in wait once ended on photos.google.com/login itself, before anyone signed in.
            using IPlaywright playwright = await Playwright.CreateAsync();
            IBrowserContext context = await Initlizer.Launch(playwright, _profile, "chrome", headless: true);
            try
            {
                IPage page = context.Pages.FirstOrDefault() ?? await context.NewPageAsync();
                await page.GotoAsync($"https://photos.google.com/login?hl={Const.LOCALE}");
                await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

                Assert.False(Initlizer.IsInGooglePhotos(page.Url), page.Url);
                Assert.NotNull(Initlizer.WhatGoogleWants(page.Url));
            }
            finally
            {
                await context.CloseAsync();
            }
        }

        [SkippableFact]
        public async Task Sign_out_reports_signed_out_for_a_profile_without_a_sign_in()
        {
            RequireLive();
            // A fresh profile must hold something for the app to treat it as "maybe signed in".
            File.WriteAllText(Path.Combine(_profile, "placeholder"), "");

            string? error = await Initlizer.SignOutAsync(_profile);

            Assert.Null(error);
            Assert.True(Directory.Exists(_profile)); // signing out never deletes the profile
        }
    }
}
