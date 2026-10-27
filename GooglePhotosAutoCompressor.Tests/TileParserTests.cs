namespace GooglePhotosAutoCompressor.Tests
{
    public class TileParserTests
    {
        [Theory]
        [InlineData("12.5 MB", (long)(12.5 * 1024 * 1024))]
        [InlineData("1.2 GB", (long)(1.2 * 1024 * 1024 * 1024))]
        [InlineData("900 KB", 900L * 1024)]
        [InlineData("1,024 MB", 1024L * 1024 * 1024)]
        [InlineData("  3 mb ", 3L * 1024 * 1024)]
        public void ParseSizeToBytes_reads_sizes(string text, long expected)
        {
            Assert.Equal(expected, TileParser.ParseSizeToBytes(text));
        }

        [Theory]
        [InlineData("")]
        [InlineData("MB")]
        [InlineData("abc MB")]
        [InlineData("12 TB")]
        [InlineData("1 2 MB")]
        public void ParseSizeToBytes_returns_minus_one_for_bad_text(string text)
        {
            Assert.Equal(-1, TileParser.ParseSizeToBytes(text));
        }

        [Fact]
        public void Parse_reads_video_tile()
        {
            FileItem? item = TileParser.Parse("AF1Qkey", new[] { "0:09\n24 MB\n31 Aug 2025" });

            Assert.NotNull(item);
            Assert.Equal("AF1Qkey", item!.MediaKey);
            Assert.Equal(new DateOnly(2025, 8, 31), item.Date);
            Assert.Equal(TimeSpan.FromSeconds(9), item.Duration);
            Assert.Equal(24L * 1024 * 1024, item.Size);
        }

        [Fact]
        public void Parse_reads_long_video_and_single_digit_day()
        {
            FileItem? item = TileParser.Parse("k", new[] { "1:02:03", "1.5 GB", "4 Oct 2024" });

            Assert.NotNull(item);
            Assert.Equal(new TimeSpan(1, 2, 3), item!.Duration);
            Assert.Equal(new DateOnly(2024, 10, 4), item.Date);
        }

        [Theory]
        [InlineData("4 Sept 2024")]
        [InlineData("4 Sep 2024")]
        public void Parse_reads_september_in_en_GB_and_short_form(string date)
        {
            FileItem? item = TileParser.Parse("k", new[] { "0:09", "8 MB", date });

            Assert.Equal(new DateOnly(2024, 9, 4), item!.Date);
        }

        [Fact]
        public void Parse_reads_tile_without_duration_as_photo()
        {
            FileItem? item = TileParser.Parse("k", new[] { "8 MB\n4 Oct 2024" });

            Assert.NotNull(item);
            Assert.Equal(MediaType.Image, item!.MediaType);
            Assert.Equal(8L * 1024 * 1024, item.Size);
            Assert.Equal(new DateOnly(2024, 10, 4), item.Date);
            Assert.Equal(TimeSpan.Zero, item.Duration);
        }

        [Fact]
        public void Parse_reads_tile_with_duration_as_video()
        {
            Assert.Equal(MediaType.Video, TileParser.Parse("k", new[] { "0:09\n8 MB\n4 Oct 2024" })!.MediaType);
        }

        [Fact]
        public void Parse_skips_photo_tile_without_date()
        {
            Assert.Null(TileParser.Parse("k", new[] { "8 MB" }));
        }

        [Fact]
        public void Parse_skips_zero_length_video()
        {
            Assert.Null(TileParser.Parse("k", new[] { "0:00\n8 MB\n4 Oct 2024" }));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void Parse_skips_tile_without_media_key(string? key)
        {
            Assert.Null(TileParser.Parse(key, new[] { "0:09\n24 MB\n31 Aug 2025" }));
        }
    }
}
