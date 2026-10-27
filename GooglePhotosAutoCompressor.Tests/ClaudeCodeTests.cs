namespace GooglePhotosAutoCompressor.Tests
{
    /// <summary>Unit tests for reading Claude Code's reply. No Claude Code needed.</summary>
    public class ClaudeCodeTests
    {
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
