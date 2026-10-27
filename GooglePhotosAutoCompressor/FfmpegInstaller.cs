using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace GooglePhotosAutoCompressor
{
    /// <summary>
    /// Finds ffmpeg, or (after asking) downloads a static build from github.com/BtbN/FFmpeg-Builds,
    /// checks its SHA-256 against the release's checksums file and unpacks it to the user's app data folder.
    /// </summary>
    internal static class FfmpegInstaller
    {
        public const string RELEASE_URL = "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/";

        public static readonly string INSTALL_DIR = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GooglePhotosAutoCompressor", "tools");

        /// <summary>Returns a usable ffmpeg path, asking the user to download one when none is found. Null if declined or failed.</summary>
        public static async Task<string?> FindOrOfferDownload(Func<string, Task<bool>> askYesNo)
        {
            string? found = Ffmpeg.FindExecutable(INSTALL_DIR);
            if (found != null)
            {
                return found;
            }

            string? asset = AssetName(RuntimeInformation.OSArchitecture);
            if (asset == null)
            {
                Console.WriteLine($"{Ffmpeg.ExeName} not found and no download is available for this system. Install ffmpeg and add it to PATH.");
                return null;
            }

            if (await askYesNo($"{Ffmpeg.ExeName} was not found. Download it now (about 100 MB, from {RELEASE_URL}{asset})?") == false)
            {
                Console.WriteLine("Install ffmpeg (e.g. 'sudo apt install ffmpeg' or 'winget install ffmpeg') and run again.");
                return null;
            }

            try
            {
                return await Download(asset, INSTALL_DIR);
            }
            catch (Exception e) when (e is HttpRequestException or IOException or InvalidDataException or InvalidOperationException)
            {
                Console.WriteLine($"ffmpeg download failed: {e.Message}");
                return null;
            }
        }

        /// <summary>Console prompt. Without an interactive console the answer is no.</summary>
        public static Task<bool> AskOnConsole(string question) => Task.FromResult(AskOnConsoleSync(question));

        private static bool AskOnConsoleSync(string question)
        {
            if (Console.IsInputRedirected)
            {
                Console.WriteLine(question + " (no console input, skipping)");
                return false;
            }

            Console.Write(question + " [Y/n] ");
            string? answer = Console.ReadLine()?.Trim().ToLowerInvariant();
            return string.IsNullOrEmpty(answer) || answer is "y" or "yes";
        }

        /// <summary>Release file for this OS and CPU, or null when there is none.</summary>
        internal static string? AssetName(Architecture arch)
        {
            string? platform = (OperatingSystem.IsWindows(), OperatingSystem.IsLinux(), arch) switch
            {
                (true, _, Architecture.X64) => "win64",
                (true, _, Architecture.Arm64) => "winarm64",
                (_, true, Architecture.X64) => "linux64",
                (_, true, Architecture.Arm64) => "linuxarm64",
                _ => null,
            };

            if (platform == null)
            {
                return null;
            }

            return $"ffmpeg-master-latest-{platform}-gpl" + (OperatingSystem.IsWindows() ? ".zip" : ".tar.xz");
        }

        /// <summary>Finds the hash for <paramref name="fileName"/> in a "hash  name" checksums file.</summary>
        internal static string? FindChecksum(string checksums, string fileName)
        {
            foreach (string line in checksums.Split('\n'))
            {
                string[] parts = line.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2 && parts[1].Trim().TrimStart('*') == fileName)
                {
                    return parts[0].ToLowerInvariant();
                }
            }

            return null;
        }

        internal static async Task<string> Download(string asset, string installDir)
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("GooglePhotosAutoCompressor");

            string checksums = await http.GetStringAsync(RELEASE_URL + "checksums.sha256");
            string expectedHash = FindChecksum(checksums, asset)
                                  ?? throw new InvalidDataException($"{asset} is not listed in checksums.sha256");

            string workDir = Path.Combine(Path.GetTempPath(), "gpac-ffmpeg-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(workDir);

            try
            {
                string archive = Path.Combine(workDir, asset);
                await DownloadWithProgress(http, RELEASE_URL + asset, archive);

                string actualHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(archive))).ToLowerInvariant();
                if (actualHash != expectedHash)
                {
                    throw new InvalidDataException($"checksum mismatch for {asset}");
                }

                string extracted = Extract(archive, Path.Combine(workDir, "x"));
                return InstallExecutable(extracted, installDir);
            }
            finally
            {
                Directory.Delete(workDir, true);
            }
        }

        private static async Task DownloadWithProgress(HttpClient http, string url, string target)
        {
            using HttpResponseMessage response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            long? total = response.Content.Headers.ContentLength;
            await using Stream source = await response.Content.ReadAsStreamAsync();
            await using FileStream file = File.Create(target);

            byte[] buffer = new byte[1 << 16];
            long done = 0;
            int lastPercent = -1;
            int read;
            while ((read = await source.ReadAsync(buffer)) > 0)
            {
                await file.WriteAsync(buffer.AsMemory(0, read));
                done += read;

                int percent = total > 0 ? (int)(done * 100 / total.Value) : -1;
                if (percent != lastPercent && percent % 10 == 0)
                {
                    Console.WriteLine($"ffmpeg download: {percent}%");
                    lastPercent = percent;
                }
            }
        }

        /// <summary>Unpacks the archive and returns the path of the ffmpeg binary inside it.</summary>
        private static string Extract(string archive, string targetDir)
        {
            Directory.CreateDirectory(targetDir);

            if (archive.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                ZipFile.ExtractToDirectory(archive, targetDir);
            }
            else
            {
                var psi = new ProcessStartInfo("tar") { UseShellExecute = false };
                foreach (string arg in new[] { "-xJf", archive, "-C", targetDir })
                {
                    psi.ArgumentList.Add(arg);
                }

                using Process tar = Process.Start(psi) ?? throw new InvalidOperationException("could not start tar");
                tar.WaitForExit();
                if (tar.ExitCode != 0)
                {
                    throw new InvalidOperationException("tar failed to unpack ffmpeg (is xz-utils installed?)");
                }
            }

            return Directory.EnumerateFiles(targetDir, Ffmpeg.ExeName, SearchOption.AllDirectories)
                       .FirstOrDefault(p => Path.GetFileName(Path.GetDirectoryName(p)) == "bin")
                   ?? throw new InvalidDataException($"{Ffmpeg.ExeName} not found in the downloaded archive");
        }

        /// <summary>Copies to a temp name, marks it executable and renames, so a half copy is never picked up.</summary>
        internal static string InstallExecutable(string source, string installDir)
        {
            Directory.CreateDirectory(installDir);
            string target = Path.Combine(installDir, Ffmpeg.ExeName);
            string temp = target + ".part";

            File.Copy(source, temp, true);
            if (OperatingSystem.IsWindows() == false)
            {
                File.SetUnixFileMode(temp, File.GetUnixFileMode(temp) | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
            }

            File.Move(temp, target, true);
            Console.WriteLine($"ffmpeg installed to {target}");
            return target;
        }
    }
}
