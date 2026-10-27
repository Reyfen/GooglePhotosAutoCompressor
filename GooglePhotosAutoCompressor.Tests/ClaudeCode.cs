using System.Diagnostics;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace GooglePhotosAutoCompressor.Tests
{
    /// <summary>What Claude Code said about two pictures.</summary>
    public sealed record FrameVerdict(bool SameScene, bool Corrupted, string Reason);

    /// <summary>
    /// Runs the local Claude Code CLI (<c>claude -p</c>) to look at images.
    /// Tests that use it skip with <see cref="MISSING_MESSAGE"/> when it is not installed.
    /// </summary>
    public static class ClaudeCode
    {
        public const string MISSING_MESSAGE =
            "AI image test skipped: Claude Code CLI ('claude') was not found " +
            "(searched PATH, ~/.local/bin and the npm folder, or GPAC_TEST_CLAUDE_PATH if set). " +
            "To run it: 1) install Claude Code (https://claude.com/claude-code), for example " +
            "'npm install -g @anthropic-ai/claude-code' (needs Node.js 18+); " +
            "2) run 'claude' once in a terminal and sign in; " +
            "3) check that 'claude --version' works in the same terminal you run 'dotnet test' from. " +
            "Or set GPAC_TEST_CLAUDE_PATH to the full path of the claude executable.";

        /// <summary>Model alias for the tests. Override with GPAC_TEST_CLAUDE_MODEL.</summary>
        public static string Model => Environment.GetEnvironmentVariable("GPAC_TEST_CLAUDE_MODEL") is { Length: > 0 } m ? m : "sonnet";

        private static readonly Lazy<string?> _path = new(Find);
        public static string? ExecutablePath => _path.Value;

        private static string? Find()
        {
            if (Environment.GetEnvironmentVariable("GPAC_TEST_CLAUDE_PATH") is { Length: > 0 } custom)
                return File.Exists(custom) ? custom : null;

            string[] names = OperatingSystem.IsWindows()
                ? new[] { "claude.exe", "claude.cmd" }
                : new[] { "claude" };

            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            IEnumerable<string> dirs = (Environment.GetEnvironmentVariable("PATH") ?? "")
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                .Append(Path.Combine(home, ".local", "bin"))            // native installer
                .Append(Path.Combine(home, ".claude", "local"));         // older local install
            if (OperatingSystem.IsWindows())
                dirs = dirs.Append(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm"));

            foreach (string dir in dirs)
                foreach (string name in names)
                {
                    string candidate = Path.Combine(dir.Trim('"'), name);
                    if (File.Exists(candidate)) return candidate;
                }
            return null;
        }

        /// <summary>
        /// Asks Claude Code to compare <paramref name="original"/> and <paramref name="candidate"/>.
        /// Both images are copied into a fresh folder, which is the only place Claude Code may read.
        /// </summary>
        public static FrameVerdict CompareFrames(string original, string candidate)
        {
            string exe = ExecutablePath ?? throw new InvalidOperationException(MISSING_MESSAGE);
            string dir = Directory.CreateTempSubdirectory("gpac-claude-").FullName;
            try
            {
                File.Copy(original, Path.Combine(dir, "original.png"));
                File.Copy(candidate, Path.Combine(dir, "candidate.png"));
                string reply = Ask(exe, dir, PROMPT);
                return ParseVerdict(reply);
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch (IOException) { }
            }
        }

        private const string PROMPT = """
            Use the Read tool to look at two images in the current folder: original.png and candidate.png.
            original.png is a frame from an original video. candidate.png should be the same frame taken from a
            re-encoded copy of that video at half resolution.

            Decide:
            - same_scene: true if candidate.png shows the same picture as original.png (same content, layout and orientation).
            - corrupted: true if candidate.png has damage that a viewer would notice: coloured or grey blocks, smearing,
              missing areas, a black or blank frame, wrong colours, or a picture that is rotated, mirrored or stretched
              compared to original.png. Lower resolution and slightly softer detail are expected and are NOT corruption.

            Reply with only one line of JSON and nothing else:
            {"same_scene": true or false, "corrupted": true or false, "reason": "one short sentence"}
            """;

        private static string Ask(string exe, string workDir, string prompt)
        {
            var psi = new ProcessStartInfo(exe)
            {
                WorkingDirectory = workDir,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            foreach (string arg in new[]
            {
                "-p", "--output-format", "json",
                "--model", Model,
                "--tools", "Read", "--allowedTools", "Read",
                "--strict-mcp-config", "--no-session-persistence",
            })
                psi.ArgumentList.Add(arg);
            // A test run started from inside a Claude Code session must not look like a nested one.
            psi.Environment.Remove("CLAUDECODE");

            using Process p = Process.Start(psi) ?? throw new InvalidOperationException($"Could not start {exe}");
            Task<string> stdout = p.StandardOutput.ReadToEndAsync();
            Task<string> stderr = p.StandardError.ReadToEndAsync();
            p.StandardInput.Write(prompt);
            p.StandardInput.Close();

            if (!p.WaitForExit(TimeSpan.FromMinutes(3)))
            {
                try { p.Kill(true); } catch (InvalidOperationException) { }
                throw new TimeoutException("Claude Code did not answer within 3 minutes.");
            }

            string output = stdout.Result;
            JObject? json = null;
            try { json = JObject.Parse(output); } catch (Newtonsoft.Json.JsonException) { }

            if (p.ExitCode != 0 || json == null || json.Value<bool?>("is_error") == true)
            {
                string detail = json?.Value<string>("result") ?? (output + stderr.Result).Trim();
                throw new InvalidOperationException(
                    $"Claude Code is installed ({exe}) but the call failed (exit {p.ExitCode}): {detail}\n" +
                    "If you are not signed in, run 'claude' once in a terminal and sign in. " +
                    $"If the model '{Model}' is not available to you, set GPAC_TEST_CLAUDE_MODEL to one that is.");
            }
            return json.Value<string>("result") ?? "";
        }

        /// <summary>Pulls the verdict JSON out of Claude's reply, even if it added text around it.</summary>
        public static FrameVerdict ParseVerdict(string reply)
        {
            Match m = Regex.Match(reply, @"\{[^{}]*""same_scene""[^{}]*\}", RegexOptions.Singleline);
            if (!m.Success)
                throw new FormatException($"Claude Code reply had no verdict JSON: {reply}");
            JObject o = JObject.Parse(m.Value);
            return new FrameVerdict(
                o.Value<bool>("same_scene"),
                o.Value<bool>("corrupted"),
                o.Value<string>("reason") ?? "");
        }
    }
}
