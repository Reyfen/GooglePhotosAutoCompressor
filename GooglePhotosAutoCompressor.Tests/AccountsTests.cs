namespace GooglePhotosAutoCompressor.Tests
{
    public sealed class AccountsTests : IDisposable
    {
        private readonly string _root = Directory.CreateTempSubdirectory("gpac-accounts-").FullName;
        private string Shared => Path.Combine(_root, "progress.json");

        public void Dispose() => Directory.Delete(_root, true);

        [Theory]
        // As seen live (2026-11-10): name, then the address in brackets on a second line.
        [InlineData("Google Account: Ella Kagner - formerly: Mike Bobko  \n(Someone.Name@gmail.com)", "someone.name@gmail.com")]
        [InlineData("Google Account: Work (team)\n(me@company.co.uk)", "me@company.co.uk")]
        // Storage almost full: Google adds a note after the email (seen 2026-11-10).
        [InlineData("Google Account: Evgenia Bobko  \n(Someone@gmail.com), Storage usage alert", "someone@gmail.com")]
        [InlineData("Google Account: Name only", null)]
        [InlineData("", null)]
        [InlineData(null, null)]
        public void Reads_the_email_from_the_account_button(string? label, string? expected)
        {
            Assert.Equal(expected, Accounts.ParseEmail(label));
        }

        [Fact]
        public void A_run_stops_when_the_window_switched_to_another_account()
        {
            Assert.Null(Accounts.Mismatch("me@gmail.com", "Me@Gmail.com"));            // same account
            Assert.Contains("other@gmail.com", Accounts.Mismatch("me@gmail.com", "other@gmail.com"));
            Assert.NotNull(Accounts.Mismatch("me@gmail.com", null));                   // can't confirm: stop too
        }

        [Fact]
        public void Each_account_has_its_own_safe_file_name()
        {
            Assert.Equal("someone.name_gmail.com.json", Accounts.FileName("Someone.Name@gmail.com"));
            Assert.NotEqual(Accounts.ProgressPath("a@gmail.com", _root), Accounts.ProgressPath("b@gmail.com", _root));
            Assert.StartsWith(Path.Combine(_root, "accounts"), Accounts.ProgressPath("a@gmail.com", _root));
        }

        [Fact]
        public void The_first_account_takes_over_the_shared_progress_of_older_versions()
        {
            var shared = new FileItemsData(Shared);
            shared.AddOrGet(new FileItem { MediaKey = "k", DateSet = true, CompressUploaded = true });
            shared.Save();
            shared.Save(); // also writes the .bak
            string mine = Accounts.ProgressPath("me@gmail.com", _root);

            Assert.True(Accounts.AdoptSharedProgress(Shared, mine));

            Assert.False(File.Exists(Shared));
            Assert.Single(new FileItemsData(mine).Items);
            // A second account starts empty: the shared file is gone and never copied twice.
            string other = Accounts.ProgressPath("other@gmail.com", _root);
            Assert.False(Accounts.AdoptSharedProgress(Shared, other));
            Assert.Empty(new FileItemsData(other).Items);
        }

        [Fact]
        public void An_account_with_its_own_progress_keeps_it()
        {
            string mine = Accounts.ProgressPath("me@gmail.com", _root);
            var own = new FileItemsData(mine);
            own.AddOrGet(new FileItem { MediaKey = "own" });
            own.Save();
            new FileItemsData(Shared).Save();

            Assert.False(Accounts.AdoptSharedProgress(Shared, mine));
            Assert.Equal("own", new FileItemsData(mine).Items.Single().MediaKey);
            Assert.True(File.Exists(Shared));
        }

        [Fact]
        public void Delete_all_progress_removes_every_account_and_the_shared_file()
        {
            new FileItemsData(Accounts.ProgressPath("a@gmail.com", _root)).Save();
            new FileItemsData(Accounts.ProgressPath("b@gmail.com", _root)).Save();
            new FileItemsData(Shared).Save();
            Assert.Equal(3, Accounts.AllProgressFiles(_root, Shared).Count);

            Assert.True(Accounts.DeleteAllProgress(_root, Shared) >= 3);

            Assert.Empty(Accounts.AllProgressFiles(_root, Shared));
            Assert.False(Directory.Exists(Accounts.Dir(_root)));
        }
    }
}
