namespace GooglePhotosAutoCompressor.Tests
{
    public class StorageCheckTests
    {
        [Fact]
        public void Reads_the_storage_meter()
        {
            // As the sidebar shows it (owner's account, 2026-11-10).
            (double used, double total) = StorageCheck.ParseMeter("Storage\n12.4 GB of 15 GB used\nUnlock storage discount")!.Value;

            Assert.Equal(12.4e9, used, 3);
            Assert.Equal(15e9, total, 3);
            Assert.Null(StorageCheck.ParseMeter("Photos Updates Albums"));
            Assert.Equal(950e6, StorageCheck.ParseMeter("950 MB of 2 TB used")!.Value.used, 3);
        }

        [Fact]
        public void Stops_only_when_the_copy_clearly_does_not_fit()
        {
            Assert.Null(StorageCheck.WontFit("12.4 GB of 15 GB used", 50_000_000));
            Assert.NotNull(StorageCheck.WontFit("15 GB of 15 GB used", 50_000_000));
            Assert.NotNull(StorageCheck.WontFit("14.99 GB of 15 GB used", 50_000_000));
            Assert.Null(StorageCheck.WontFit("no meter on this page", 50_000_000)); // unknown: the upload itself will tell
        }

        [Theory]
        [InlineData("Your account storage is full", true)]
        [InlineData("You're out of storage. Upgrade to upload", true)]
        [InlineData("Not enough storage to upload 1 item", true)]
        [InlineData("1 item uploaded", false)]
        [InlineData("Storage 12.4 GB of 15 GB used", false)]
        public void Recognises_googles_storage_full_wording(string text, bool full)
        {
            Assert.Equal(full, StorageCheck.SaysFull(text));
        }
    }
}
