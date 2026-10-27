using GooglePhotosAutoCompressor.Modules;
using Microsoft.Playwright;

namespace GooglePhotosAutoCompressor
{
    internal class Program
    {
        private static async Task Main(string[] args)
        {
            string? ffmpegPath = Ffmpeg.FindExecutable();
            if (ffmpegPath == null)
            {
                Console.WriteLine($"{Ffmpeg.ExeName} not found next to the app or on PATH.");
                return;
            }

            Ffmpeg ffmpeg = new(ffmpegPath);
            (IPlaywright playwright, IPage page) = await Initlizer.OpenBrowser();

            try
            {
                await Initlizer.Login(page);
                //Console.WriteLine("\n===\nPress any key to continue\n===\n");
                //Console.ReadKey();
                FileItemsData files = await Downloader.DownloadUncommresedFiles(page);
                await Compressor.CompressFiles(files, ffmpeg);
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
