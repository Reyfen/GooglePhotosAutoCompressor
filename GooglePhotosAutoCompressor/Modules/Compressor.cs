using System.Diagnostics;

namespace GooglePhotosAutoCompressor.Modules
{
    internal class Compressor
    {
        private static readonly string FFMPEG_PATH = Path.Combine(AppContext.BaseDirectory, "tools", "ffmpeg.exe");

        public static async Task CompressFiles(FileItemsData filesData)
        {
            string directory = Path.Combine(Const.DOWNLOADS_PATH, "GooglePhotosCompressCache");

            Console.WriteLine("\n===\nCompressFiles\n===\n");

            if (Directory.Exists(directory) == false)
            {
                Directory.CreateDirectory(directory);
            }

            foreach (FileItem file in filesData.Items) 
            {
                if (string.IsNullOrEmpty(file.DownloadPath) || file.CompressUploaded)
                {
                    continue;
                }

                if (HasVideoExtension(file.DownloadPath) == false)
                {
                    continue;
                }

                if (string.IsNullOrEmpty(file.CompressPath) == false && File.Exists(file.CompressPath))
                {
                    continue;
                }

                file.CompressPath = null;
                filesData.Save();

                string path = Path.Combine(directory, Path.GetFileNameWithoutExtension(file.DownloadPath) + "_compressed" + Path.GetExtension(file.DownloadPath));

                if (File.Exists(path))
                {
                    file.CompressPath = path;
                    filesData.Save();
                    long size = new FileInfo(path).Length;
                    Console.WriteLine($"Compressor: alredy compressed {file.CompressPath} \t\t {size / file.Duration.TotalSeconds / 1024 / 1024:F2} MB/s");
                    continue;
                }

                bool success = await Task.Run(() => RunFfmpegHalfRes(file.DownloadPath, path));

                if (success)
                {
                    file.CompressPath = path;
                    filesData.Save();
                    long size = new FileInfo(path).Length;
                    Console.WriteLine($"Compressor: alredy compressed {file.CompressPath} \t\t {size / file.Duration.TotalSeconds / 1024 / 1024:F2} MB/s");
                }
                else
                {
                    Console.WriteLine($"Compressor: failed compressing of {file.DownloadPath}");
                }
            }
        }

        static bool HasVideoExtension(string path)
        {
            var ext = Path.GetExtension(path);
            return ext != null && (ext.Equals(".mp4", StringComparison.OrdinalIgnoreCase)
                                   || ext.Equals(".mov", StringComparison.OrdinalIgnoreCase));
        }

        static string GetHalfScaleFilter() => "scale=iw/2:ih/2";

        static bool RunFfmpegHalfRes(string inputPath, string outputPath)
        {
            // Build args:
            // -y overwrite, -i input, -vf scale(half), re-encode video (H.264) with sane defaults,
            // copy audio as-is.
            string args =
                $"-y -i \"{inputPath}\" -vf {GetHalfScaleFilter()} -c:v libx264 -crf 20 -preset medium -c:a copy \"{outputPath}\"";

            var psi = new ProcessStartInfo
            {
                FileName = FFMPEG_PATH,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi)!;
            proc.OutputDataReceived += (_, e) => { if (e.Data != null) Console.WriteLine(e.Data); };
            proc.ErrorDataReceived += (_, e) => { if (e.Data != null) Console.WriteLine(e.Data); };
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();
            proc.WaitForExit();

            return proc.ExitCode == 0;
        }
    }
}
