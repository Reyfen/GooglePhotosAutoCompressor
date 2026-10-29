namespace GooglePhotosAutoCompressor.Tests
{
    /// <summary>Unit tests for reading Claude Code's reply. No Claude Code needed.</summary>
    public class ClaudeCodeTests
    {
        [Fact]
        public void Never_passes_api_keys_to_claude_code()
        {
            // The AI page tests must use the Claude Code sign-in, not paid API tokens.
            var env = new Dictionary<string, string?>
            {
                ["ANTHROPIC_API_KEY"] = "x", ["ANTHROPIC_AUTH_TOKEN"] = "x", ["CLAUDE_CODE_USE_BEDROCK"] = "1",
                ["CLAUDE_CODE_USE_VERTEX"] = "1", ["PATH"] = "keep",
            };

            ClaudeCode.UseSignInNotApiKeys(env);

            Assert.Equal(new[] { "PATH" }, env.Keys);
        }

        [Fact]
        public void Parses_plain_json_reply()
        {
            ScreenVerdict v = ClaudeCode.ParseVerdict("""{"ok": true, "reason": "Large files page with a grid of videos."}""");
            Assert.Equal(new ScreenVerdict(true, "Large files page with a grid of videos."), v);
        }

        [Fact]
        public void Parses_json_inside_extra_text()
        {
            ScreenVerdict v = ClaudeCode.ParseVerdict("""
                Here is my answer:
                ```json
                {"ok": false, "reason": "A cookie banner covers the page."}
                ```
                """);
            Assert.False(v.Ok);
            Assert.Equal("A cookie banner covers the page.", v.Reason);
        }

        [Fact]
        public void Reply_without_verdict_throws()
        {
            Assert.Throws<FormatException>(() => ClaudeCode.ParseVerdict("I could not open the file."));
        }

        [Fact]
        public void Missing_message_says_what_to_install()
        {
            Assert.Contains("Claude Code", ClaudeCode.MISSING_MESSAGE);
            Assert.Contains("sign in", ClaudeCode.MISSING_MESSAGE);
            Assert.Contains("GPAC_TEST_CLAUDE_PATH", ClaudeCode.MISSING_MESSAGE);
        }
    }
}
