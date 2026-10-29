using System.Diagnostics;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace GooglePhotosAutoCompressor.Tests
{
    /// <summary>Claude Code's answer to a yes/no question about a screenshot.</summary>
    public sealed record ScreenVerdict(bool Ok, string Reason);

    /// <summary>
    /// Runs the local Claude Code CLI (<c>claude -p</c>) to look at images.
    /// Tests that use it skip with <see cref="MISSING_MESSAGE"/> when it is not installed.
    /// It always uses the Claude Code sign-in (subscription), never API tokens: see <see cref="API_KEY_VARIABLES"/>.
    /// </summary>
    public static class ClaudeCode
    {
        public const string MISSING_MESSAGE =
            "AI page test skipped: Claude Code CLI ('claude') was not found " +
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

        /// <summary>Asks a yes/no question about a screenshot of the Google Photos website.</summary>
        public static ScreenVerdict CheckScreenshot(string screenshot, string question)
        {
            string reply = AskAboutScreenshot(screenshot, $$"""
                Use the Read tool to look at screenshot.png in the current folder. It is a screenshot of the
                Google Photos website (photos.google.com, UI language English UK), taken by an automated test
                of a tool that clicks through that website.

                Question: {{question}}

                Judge only what is visible. Reply with only one line of JSON and nothing else:
                {"ok": true or false, "reason": "one short sentence about what you see"}
                """);
            return ParseVerdict(reply);
        }

        /// <summary>
        /// Used when a selector or click failed: asks Claude Code what the page shows instead,
        /// so the test failure says what changed on Google's side.
        /// </summary>
        public static string DescribeProblem(string screenshot, string expected)
        {
            return AskAboutScreenshot(screenshot, $"""
                Use the Read tool to look at screenshot.png in the current folder. It is a screenshot of the
                Google Photos website (photos.google.com, UI language English UK), taken by an automated test.
                The test expected this, but could not find it or the click did not work: {expected}

                In at most 4 short sentences: what does the page show? Is the expected control visible at all,
                and if so where is it and what is its visible label or icon? Is anything (a dialog, a banner,
                a sign-in page, an error) in the way?
                """).Trim();
        }

        private static string AskAboutScreenshot(string screenshot, string prompt)
        {
            string exe = ExecutablePath ?? throw new InvalidOperationException(MISSING_MESSAGE);
            string dir = Directory.CreateTempSubdirectory("gpac-claude-").FullName;
            try
            {
                // Claude Code only gets this folder, holding only the screenshot.
                File.Copy(screenshot, Path.Combine(dir, "screenshot.png"));
                return Ask(exe, dir, prompt);
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch (IOException) { }
            }
        }

        /// <summary>
        /// Removed from the environment of every <c>claude</c> call: with one of these set, Claude Code would bill
        /// API tokens (or another provider) instead of using the signed-in Claude Code account.
        /// </summary>
        internal static readonly string[] API_KEY_VARIABLES =
        {
            "ANTHROPIC_API_KEY", "ANTHROPIC_AUTH_TOKEN", "CLAUDE_CODE_USE_BEDROCK", "CLAUDE_CODE_USE_VERTEX",
        };

        internal static void UseSignInNotApiKeys(IDictionary<string, string?> environment)
        {
            foreach (string name in API_KEY_VARIABLES)
            {
                environment.Remove(name);
            }
        }

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
            UseSignInNotApiKeys(psi.Environment);

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
        public static ScreenVerdict ParseVerdict(string reply)
        {
            Match m = Regex.Match(reply, @"\{[^{}]*""ok""[^{}]*\}", RegexOptions.Singleline);
            if (!m.Success)
                throw new FormatException($"Claude Code reply had no verdict JSON: {reply}");
            JObject o = JObject.Parse(m.Value);
            return new ScreenVerdict(o.Value<bool>("ok"), o.Value<string>("reason") ?? "");
        }
    }
}
