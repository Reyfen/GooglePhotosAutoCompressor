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
                if (File.Exists(candidate))
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

        /// <summary>Runs ffmpeg. Returns exit code and everything it wrote to stderr.</summary>
        public (int exitCode, string stderr) Run(IEnumerable<string> args, bool echo = false)
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
            };
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();
            proc.WaitForExit();

            return (proc.ExitCode, stderr.ToString());
        }

        /// <summary>Reads the container duration, or null when the file is not a readable video.</summary>
        public TimeSpan? GetDuration(string path)
        {
            (_, string stderr) = Run(new[] { "-hide_banner", "-i", path });
            return ParseDuration(stderr);
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
