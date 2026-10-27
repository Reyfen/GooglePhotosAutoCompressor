namespace GooglePhotosAutoCompressor.Tests
{
    /// <summary>Unit tests for reading Claude Code's reply. No Claude Code needed.</summary>
    public class ClaudeCodeTests
    {
        [Fact]
        public void Parses_plain_json_reply()
        {
            FrameVerdict v = ClaudeCode.ParseVerdict("""{"same_scene": true, "corrupted": false, "reason": "Same test card."}""");
            Assert.Equal(new FrameVerdict(true, false, "Same test card."), v);
        }

        [Fact]
        public void Parses_json_inside_extra_text()
        {
            FrameVerdict v = ClaudeCode.ParseVerdict("""
                Here is my answer:
                ```json
                {"same_scene": false, "corrupted": true, "reason": "Green block top left."}
                ```
                """);
            Assert.False(v.SameScene);
            Assert.True(v.Corrupted);
        }

        [Fact]
        public void Reply_without_verdict_throws()
        {
            Assert.Throws<FormatException>(() => ClaudeCode.ParseVerdict("I could not open the files."));
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
