namespace GooglePhotosAutoCompressor.Tests
{
    public class TakenTimeTests
    {
        [Fact]
        public void Reads_the_date_dialog_fields()
        {
            // Values as the dialog showed them in a live check.
            Assert.Equal(new DateTime(2025, 7, 4, 17, 57, 0), TakenTime.FromFields("2025", "07", "04", "17", "57"));
            Assert.Equal(new DateTime(2025, 7, 4, 0, 5, 0), TakenTime.FromFields("2025", "7", "4", "0", "05"));
        }

        [Theory]
        [InlineData("", "07", "04", "17", "57")]
        [InlineData("2025", "13", "04", "17", "57")]
        [InlineData("2025", "02", "30", "17", "57")]
        [InlineData("2025", "07", "04", "24", "00")]
        [InlineData("2025", "07", "04", "17", "60")]
        [InlineData("2025", "07", "04", "5 PM", "00")]
        [InlineData("2025", "07", "04", null, "00")]
        public void Rejects_missing_or_impossible_fields(string? y, string? mo, string? d, string? h, string? mi)
        {
            Assert.Null(TakenTime.FromFields(y, mo, d, h, mi));
        }

        [Theory]
        [InlineData("GMT+01:00 British Summer Time", 3_600_000)]
        [InlineData("GMT+01:00", 3_600_000)]
        [InlineData("GMT-11:00 American Samoa Standard Time", -39_600_000)]
        [InlineData("GMT+05:30 India Standard Time", 19_800_000)]
        [InlineData("GMT+00:00 Greenwich Mean Time", 0)]
        [InlineData("GMT", 0)]
        [InlineData("Time zone\nGMT+14:00", 50_400_000)]
        public void Reads_time_zone_labels_as_googles_millisecond_values(string label, int expected)
        {
            Assert.Equal(expected, TakenTime.ParseOffsetMs(label));
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        [InlineData("Central European Time")]
        public void Unknown_time_zone_label_is_null(string? label)
        {
            Assert.Null(TakenTime.ParseOffsetMs(label));
        }

        [Theory]
        [InlineData(3_600_000, "GMT+01:00")]
        [InlineData(-12_600_000, "GMT-03:30")]
        [InlineData(0, "GMT+00:00")]
        public void Formats_offsets(int ms, string expected)
        {
            Assert.Equal(expected, TakenTime.FormatOffset(ms));
            Assert.Equal(ms, TakenTime.ParseOffsetMs(expected));
        }

        [Fact]
        public void Photo_keeps_time_when_its_exif_matches_to_the_minute()
        {
            DateTime taken = new(2025, 7, 4, 17, 57, 0);

            Assert.True(TakenTime.PhotoKeepsTime(taken, "2025:07:04 17:57:42"));
            Assert.False(TakenTime.PhotoKeepsTime(taken, "2025:07:04 18:57:42")); // edited in Google Photos
            Assert.False(TakenTime.PhotoKeepsTime(taken, null));
            Assert.False(TakenTime.PhotoKeepsTime(taken, "garbage"));
        }

        [Fact]
        public void Photo_with_unknown_original_time_trusts_its_exif()
        {
            Assert.True(TakenTime.PhotoKeepsTime(null, "2025:07:04 17:57:42"));
        }
    }
}
