using System.Security.Cryptography;
using System.Text;

namespace GooglePhotosAutoCompressor
{
    /// <summary>
    /// Where files go on disk. Every path includes a short id derived from the media key,
    /// because different videos often share a file name (IMG_0001.MOV from two phones).
    /// </summary>
    internal static class FilePaths
    {
        public static string DOWNLOAD_DIR => Path.Combine(Settings.Current.WorkFolderOrDefault, "GooglePhotosDownloadCache");
        public static string COMPRESS_DIR => Path.Combine(Settings.Current.WorkFolderOrDefault, "GooglePhotosCompressCache");

        /// <summary>
        /// Half-written files a crash leaves behind: downloads ("x.mov.part") and copies being encoded
        /// ("x_compressed_1a2b3c4d.part.mov"). Finished files never match.
        /// </summary>
        public static bool IsPartialFile(string fileName)
        {
            return fileName.EndsWith(".part", StringComparison.OrdinalIgnoreCase)
                   || System.Text.RegularExpressions.Regex.IsMatch(fileName, @"_compressed_[0-9a-f]{8}\.part\.[A-Za-z0-9]+$");
        }

        /// <summary>Deletes half-written files from earlier runs that were killed. Returns how many.</summary>
        public static int DeletePartialFiles(params string[] dirs)
        {
            int deleted = 0;
            foreach (string dir in dirs.Where(Directory.Exists))
            {
                foreach (string file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                {
                    if (IsPartialFile(Path.GetFileName(file)))
                    {
                        try
                        {
                            File.Delete(file);
                            deleted++;
                        }
                        catch (IOException)
                        {
                            // Still in use (another copy of the app?). Leave it.
                        }
                    }
                }
            }

            return deleted;
        }

        /// <summary>Stable 8-char id for a media key, safe for file names.</summary>
        public static string ShortId(string mediaKey)
        {
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(mediaKey));
            return Convert.ToHexString(hash, 0, 4).ToLowerInvariant();
        }

        /// <summary>Download target: one folder per item, original file name kept.</summary>
        public static string DownloadPath(string downloadDir, string mediaKey, string suggestedFileName)
        {
            return Path.Combine(downloadDir, ShortId(mediaKey), Path.GetFileName(suggestedFileName));
        }

        /// <summary>
        /// Compressed file name. This name is what Google Photos shows after upload,
        /// and the date setter finds items by it, so it must be unique per media key.
        /// </summary>
        /// <summary>True for files this app produced (old "x_compressed.mp4" and new "x_compressed_1a2b3c4d.mp4").</summary>
        public static bool IsCompressedName(string fileName)
        {
            return System.Text.RegularExpressions.Regex.IsMatch(
                Path.GetFileNameWithoutExtension(fileName), "_compressed(_[0-9a-f]{8})?$");
        }

        public static string CompressPath(string compressDir, string mediaKey, string downloadPath)
        {
            string name = Path.GetFileNameWithoutExtension(downloadPath);
            return Path.Combine(compressDir, $"{name}_compressed_{ShortId(mediaKey)}{Path.GetExtension(downloadPath)}");
        }
    }
}
