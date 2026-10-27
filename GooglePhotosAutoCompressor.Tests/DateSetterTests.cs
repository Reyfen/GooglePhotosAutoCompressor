using GooglePhotosAutoCompressor.Modules;

namespace GooglePhotosAutoCompressor.Tests
{
    public class DateSetterTests
    {
        [Theory]
        [InlineData("https://photos.google.com/photo/AF1QipN-abc_123?hl=en-GB", "AF1QipN-abc_123")]
        [InlineData("https://photos.google.com/u/1/photo/AF1Qip9", "AF1Qip9")]
        [InlineData("https://photos.google.com/?hl=en-GB", null)]
        public void ParseMediaKeyFromUrl(string url, string? expected)
        {
            Assert.Equal(expected, DateSetter.ParseMediaKeyFromUrl(url));
        }

        [Fact]
        public void Date_is_set_only_after_original_is_binned()
        {
            var item = new FileItem { MediaKey = "k", CompressPath = "x_compressed_1234abcd.mp4", Verified = true };
            Assert.False(ItemRules.NeedsDateSet(item));

            item.OriginalDeleted = true;
            Assert.True(ItemRules.NeedsDateSet(item)); // even if the upload was not confirmed

            item.DateSet = true;
            Assert.False(ItemRules.NeedsDateSet(item));
        }
    }
}
