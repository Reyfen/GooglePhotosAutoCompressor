using ImageMagick;
using Microsoft.Playwright;

namespace GooglePhotosAutoCompressor
{
    internal sealed record CheckResult(string Name, bool Ok, string Detail);

    /// <summary>
    /// Checks that everything the app needs works on this PC, without touching Google Photos:
    /// ffmpeg, photo compression, a browser Playwright can start, writable folders and the internet.
    /// Used by the "Check setup" button and by "--check" (also to test installed packages).
    /// </summary>
    internal static class SetupCheck
    {
        public static async Task<IReadOnlyList<CheckResult>> RunAsync(bool includeBrowser = true)
        {
            List<CheckResult> results = new()
            {
                CheckFfmpeg(),
                CheckPhotos(),
                CheckFolder("Settings folder", Settings.CONFIG_DIR),
                CheckFolder("Work folder", Settings.Current.WorkFolderOrDefault),
            };

            if (includeBrowser)
            {
                results.Add(await CheckBrowser());
            }

            results.Add(await Network.IsOnlineAsync()
                ? new CheckResult("Internet", true, "google.com reachable")
                : new CheckResult("Internet", false, "google.com not reachable"));
            return results;
        }

        private static CheckResult CheckFfmpeg()
        {
            string? path = Ffmpeg.FindExecutable(FfmpegInstaller.INSTALL_DIR);
            if (path == null)
            {
                return new CheckResult("ffmpeg", false, "not found; the app offers to download it when you press Start");
            }

            try
            {
                (int exit, string _) = new Ffmpeg(path).Run(new[] { "-hide_banner", "-version" });
                return new CheckResult("ffmpeg", exit == 0, path);
            }
            catch (Exception e) when (e is System.ComponentModel.Win32Exception or IOException)
            {
                return new CheckResult("ffmpeg", false, $"{path}: {e.Message}");
            }
        }

        private static CheckResult CheckPhotos()
        {
            try
            {
                using MagickImage image = new(MagickColors.SkyBlue, 64, 48);
                image.Quality = 85;
                byte[] jpeg = image.ToByteArray(MagickFormat.Jpeg);
                using MagickImage back = new(jpeg);
                return new CheckResult("Photo compression", back.Width == 64, $"Magick.NET {typeof(MagickImage).Assembly.GetName().Version}");
            }
            catch (Exception e)
            {
                return new CheckResult("Photo compression", false, e.Message);
            }
        }

        private static CheckResult CheckFolder(string name, string dir)
        {
            try
            {
                Directory.CreateDirectory(dir);
                string probe = Path.Combine(dir, $".gpac-write-test-{Environment.ProcessId}");
                File.WriteAllText(probe, "ok");
                File.Delete(probe);
                return new CheckResult(name, true, $"{dir} ({FreeText(dir)} free)");
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return new CheckResult(name, false, $"{dir}: {e.Message}");
            }
        }

        private static string FreeText(string dir)
        {
            long free = DiskSpace.FreeBytes(dir);
            return free == long.MaxValue ? "?" : $"{free / 1024.0 / 1024 / 1024:F1} GB";
        }

        /// <summary>Starts the browser the app would use, headless with a throwaway profile, and opens a blank page.</summary>
        private static async Task<CheckResult> CheckBrowser()
        {
            string profile = Path.Combine(Path.GetTempPath(), $"gpac-check-{Environment.ProcessId}");
            try
            {
                using IPlaywright playwright = await Playwright.CreateAsync();
                foreach (string? channel in new[] { "chrome", null })
                {
                    try
                    {
                        await using IBrowserContext context = await Modules.Initlizer.Launch(playwright, profile, channel, headless: true);
                        IPage page = context.Pages.FirstOrDefault() ?? await context.NewPageAsync();
                        await page.GotoAsync("about:blank");
                        return new CheckResult("Browser", true, channel == null ? "Playwright Chromium" : "Google Chrome");
                    }
                    catch (PlaywrightException) when (channel != null)
                    {
                        // No Chrome: try Playwright's Chromium.
                    }
                }

                return new CheckResult("Browser", false, "no browser");
            }
            catch (Exception e)
            {
                string first = e.Message.Split('\n')[0];
                return new CheckResult("Browser", false,
                    $"Google Chrome not found and Chromium not installed ({first}). Install Google Chrome; otherwise the app installs Chromium on Start.");
            }
            finally
            {
                try { Directory.Delete(profile, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }

        public static string Format(IEnumerable<CheckResult> results) =>
            string.Join(Environment.NewLine, results.Select(r => $"{(r.Ok ? "OK  " : "FAIL")}  {r.Name}: {r.Detail}"));
    }
}
