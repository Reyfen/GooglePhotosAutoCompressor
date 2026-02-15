using GooglePhotosAutoCompressor.Modules;
using Microsoft.Playwright;

namespace GooglePhotosAutoCompressor
{
    internal class Program
    {
        private static async Task Main(string[] args)
        {
            (IPlaywright playwright, IPage page) = await Initlizer.OpenBrowser();

            try
            {
                await Initlizer.Login(page);
                Console.ReadLine();
                FileItemsData files = await Downloader.DownloadUncommresedFiles(page);
                await Compressor.CompressFiles(files);
                await Deletor.DeleteCompressedFiles(page, files);
                await Uploader.UploadFiles(page, files);
                await DateSetter.SetCompressedFilesDates(page, files);

                Console.WriteLine("\n===\nDone\n===\n");
                Console.ReadLine();
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
                Console.ReadLine();
            }
            finally
            {
                playwright.Dispose();
            }
        }
    }
}
