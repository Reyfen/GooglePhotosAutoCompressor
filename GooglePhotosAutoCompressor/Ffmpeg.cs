using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

namespace GooglePhotosAutoCompressor
{
    /// <summary>Runs the ffmpeg executable.</summary>
    internal class Ffmpeg
    {
        public string ExePath { get; }

        public Ffmpeg(string exePath)
        {
            ExePath = exePath;
        }

        public static string ExeName => OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";

        /// <summary>Bundled copy next to the app first, then <paramref name="extraDirs"/>, then PATH. Null when not found.</summary>
        public static string? FindExecutable(params string[] extraDirs)
        {
            foreach (string dir in extraDirs.Prepend(Path.Combine(AppContext.BaseDirectory, "tools")))
            {
                string candidate = Path.Combine(dir, ExeName);
                if (IsRealBinary(candidate))
                {
                    return candidate;
                }
            }

            string? pathVariable = Environment.GetEnvironmentVariable("PATH");
            if (string.IsNullOrEmpty(pathVariable))
            {
                return null;
            }

            foreach (string dir in pathVariable.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                string candidate = Path.Combine(dir.Trim('"'), ExeName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return null;
        }

        /// <summary>
        /// A clone without Git LFS has a ~130 byte text pointer instead of the real ffmpeg.exe.
        /// </summary>
        internal static bool IsRealBinary(string path)
        {
            return File.Exists(path) && new FileInfo(path).Length > 1024 * 1024;
        }

        /// <summary>Runs ffmpeg. Returns exit code and everything it wrote to stderr.</summary>
        /// <param name="onLine">Called with every stderr line (ffmpeg's "-stats" progress lines included).</param>
        /// <param name="cancellationToken">Kills ffmpeg. The caller deletes its half-written output.</param>
        /// <param name="timeLimit">Kills ffmpeg when it runs longer (a broken file can make it hang); exit code is then <see cref="TIMED_OUT"/>.</param>
        public (int exitCode, string stderr) Run(IEnumerable<string> args, bool echo = false, Action<string>? onLine = null,
            CancellationToken cancellationToken = default, TimeSpan? timeLimit = null)
        {
            var psi = new ProcessStartInfo
            {
                FileName = ExePath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            // ArgumentList quotes each argument correctly on Windows and Linux.
            foreach (string arg in args)
            {
                psi.ArgumentList.Add(arg);
            }

            var stderr = new System.Text.StringBuilder();
            using Process proc = Process.Start(psi)!;
            proc.OutputDataReceived += (_, _) => { };
            proc.ErrorDataReceived += (_, e) =>
            {
                if (e.Data == null)
                {
                    return;
                }

                lock (stderr)
                {
                    stderr.AppendLine(e.Data);
                }

                if (echo)
                {
                    Console.WriteLine(e.Data);
                }

                onLine?.Invoke(e.Data);
            };
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();

            using (cancellationToken.Register(() =>
            {
                try { proc.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { } // already exited
            }))
            {
                if (timeLimit is TimeSpan limit && proc.WaitForExit(limit) == false)
                {
                    try { proc.Kill(entireProcessTree: true); }
                    catch (InvalidOperationException) { } // exited just now
                    proc.WaitForExit();
                    cancellationToken.ThrowIfCancellationRequested();
                    string message = $"ffmpeg ran longer than {limit.TotalMinutes:0.#} minutes and was stopped.";
                    Console.WriteLine(message);
                    return (TIMED_OUT, stderr + message);
                }

                proc.WaitForExit();
            }

            cancellationToken.ThrowIfCancellationRequested();

            return (proc.ExitCode, stderr.ToString());
        }

        /// <summary>Exit code <see cref="Run"/> returns when ffmpeg was stopped for running too long.</summary>
        public const int TIMED_OUT = -2;

        /// <summary>
        /// How long encoding a video may take: 10× its length plus 5 minutes (normal encodes take about 1×; slow PCs
        /// and 4K maybe 3–4×). Beyond that ffmpeg is assumed stuck on a broken file.
        /// </summary>
        public static TimeSpan EncodeTimeLimit(TimeSpan videoLength) =>
            TimeSpan.FromMinutes(5) + TimeSpan.FromTicks(Math.Max(0, videoLength.Ticks) * 10);

        /// <summary>Reads the container duration, or null when the file is not a readable video.</summary>
        public TimeSpan? GetDuration(string path)
        {
            (_, string stderr) = Run(new[] { "-hide_banner", "-i", path });
            return ParseDuration(stderr);
        }

        /// <summary>Reads "time=00:01:02.50" from an ffmpeg progress line, or null.</summary>
        public static TimeSpan? ParseProgressTime(string line)
        {
            Match match = Regex.Match(line, @"time=\s*(\d+):(\d{2}):(\d{2}(?:\.\d+)?)");
            if (match.Success == false)
            {
                return null;
            }

            return TimeSpan.FromHours(int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture))
                   + TimeSpan.FromMinutes(int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture))
                   + TimeSpan.FromSeconds(double.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture));
        }

        public static TimeSpan? ParseDuration(string ffmpegOutput)
        {
            Match match = Regex.Match(ffmpegOutput, @"Duration:\s*(\d+):(\d{2}):(\d{2}(?:\.\d+)?)");
            if (match.Success == false)
            {
                return null;
            }

            int hours = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            int minutes = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
            double seconds = double.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);

            return TimeSpan.FromHours(hours) + TimeSpan.FromMinutes(minutes) + TimeSpan.FromSeconds(seconds);
        }
    }
}
