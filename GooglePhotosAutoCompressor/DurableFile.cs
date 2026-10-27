using System.Text;

namespace GooglePhotosAutoCompressor
{
    /// <summary>
    /// Writes a file so that a crash, a killed process or a power cut leaves either the old or the new
    /// content, never a half-written or empty file.
    /// </summary>
    internal static class DurableFile
    {
        /// <summary>
        /// Writes to "path.tmp", flushes it to the disk (not just the OS cache), then swaps it in.
        /// Without the flush, a power cut right after the rename can leave an empty file.
        /// </summary>
        public static void WriteAllText(string path, string content, string? backupPath = null)
        {
            string fullPath = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            string tempPath = fullPath + ".tmp";

            using (FileStream stream = new(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                byte[] bytes = new UTF8Encoding(false).GetBytes(content);
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(flushToDisk: true);
            }

            if (backupPath != null && File.Exists(fullPath))
            {
                File.Replace(tempPath, fullPath, backupPath);
            }
            else
            {
                File.Move(tempPath, fullPath, overwrite: true);
            }
        }
    }
}
