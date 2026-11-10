using GooglePhotosAutoCompressor.Modules;

namespace GooglePhotosAutoCompressor.Tests
{
    /// <summary>Pages must be in English for the selectors; "Recently added" forwards to an address without hl (2026-11-10).</summary>
    public class PageLanguageTests
    {
        [Theory]
        [InlineData("https://photos.google.com/search/CgAiCgoAEgYKBHICCgAojY%2FCtZw0", "https://photos.google.com/search/CgAiCgoAEgYKBHICCgAojY%2FCtZw0?hl=en-GB")]
        [InlineData("https://photos.google.com/search/abc/photo/AF1Qip?hl=uk", "https://photos.google.com/search/abc/photo/AF1Qip?hl=en-GB")]
        [InlineData("https://photos.google.com/?pli=1&hl=uk&x=2", "https://photos.google.com/?pli=1&x=2&hl=en-GB")]
        [InlineData("https://photos.google.com/photo/AF1Qip#frag", "https://photos.google.com/photo/AF1Qip?hl=en-GB#frag")]
        public void WithLocale_sets_hl_once(string url, string expected)
        {
            Assert.Equal(expected, Selectors.WithLocale(url));
        }

        [Theory]
        [InlineData("en-GB", true)]
        [InlineData("en-gb", true)]
        [InlineData("uk", false)]
        [InlineData("en", false)] // en-US dates read differently
        [InlineData("", false)]
        [InlineData(null, false)]
        public void Only_the_apps_language_counts(string? lang, bool expected)
        {
            Assert.Equal(expected, Initlizer.IsAppLanguage(lang));
        }
    }
}
