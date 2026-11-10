using GooglePhotosAutoCompressor.Modules;

namespace GooglePhotosAutoCompressor.Tests
{
    public class GoogleWantsTests
    {
        [Theory]
        // Signed out: Google Photos sends the browser here (seen live 2026-11-10).
        [InlineData("https://www.google.com/photos/about/?hl=en-GB", "SignIn")]
        [InlineData("https://accounts.google.com/v3/signin/identifier?continue=https%3A%2F%2Fphotos.google.com", "Confirm")]
        [InlineData("https://accounts.google.com/v3/signin/challenge/pwd?TL=abc", "Confirm")]
        [InlineData("https://photos.google.com/login?hl=en-GB", "SignIn")]
        [InlineData("https://www.google.com/sorry/index?continue=https://photos.google.com/", "UnusualTraffic")]
        public void Recognises_pages_where_google_wants_a_person(string url, string expected)
        {
            Assert.Equal(Enum.Parse<Initlizer.GoogleWants>(expected), Initlizer.WhatGoogleWants(url));
        }

        [Theory]
        [InlineData("https://photos.google.com/?hl=en-GB")]
        [InlineData("https://photos.google.com/quotamanagement/large?hl=en-GB")]
        [InlineData("https://photos.google.com/photo/AF1Qip123")]
        [InlineData("")]
        [InlineData(null)]
        public void Normal_pages_need_nobody(string? url)
        {
            Assert.Null(Initlizer.WhatGoogleWants(url));
        }

        [Theory]
        [InlineData("https://photos.google.com/?hl=en-GB", true)]
        [InlineData("https://photos.google.com/quotamanagement/large?hl=en-GB", true)]
        // The sign-in address: the sign-in wait used to stop here before anyone signed in (2026-11-10).
        [InlineData("https://photos.google.com/login?hl=en-GB", false)]
        [InlineData("https://photos.google.com/login/", false)]
        [InlineData("https://accounts.google.com/v3/signin/identifier?continue=https%3A%2F%2Fphotos.google.com", false)]
        [InlineData("https://www.google.com/photos/about/?hl=en-GB", false)]
        [InlineData("about:blank", false)]
        [InlineData(null, false)]
        public void Only_a_signed_in_google_photos_page_ends_the_sign_in_wait(string? url, bool expected)
        {
            Assert.Equal(expected, Initlizer.IsInGooglePhotos(url));
        }
    }
}
