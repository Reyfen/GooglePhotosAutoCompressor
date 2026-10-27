using System.Runtime.InteropServices;

namespace GooglePhotosAutoCompressor.Tests
{
    public sealed class FfmpegInstallerTests : IDisposable
    {
        private readonly string _dir = Directory.CreateTempSubdirectory("gpac-install-").FullName;

        public void Dispose() => Directory.Delete(_dir, true);

        [Fact]
        public void Asset_name_matches_this_os()
        {
            string? x64 = FfmpegInstaller.AssetName(Architecture.X64);

            if (OperatingSystem.IsWindows())
            {
                Assert.Equal("ffmpeg-master-latest-win64-gpl.zip", x64);
                Assert.Equal("ffmpeg-master-latest-winarm64-gpl.zip", FfmpegInstaller.AssetName(Architecture.Arm64));
            }
            else if (OperatingSystem.IsLinux())
            {
                Assert.Equal("ffmpeg-master-latest-linux64-gpl.tar.xz", x64);
                Assert.Equal("ffmpeg-master-latest-linuxarm64-gpl.tar.xz", FfmpegInstaller.AssetName(Architecture.Arm64));
            }

            Assert.Null(FfmpegInstaller.AssetName(Architecture.X86));
        }

        [Fact]
        public void Finds_checksum_line()
        {
            const string checksums =
                "aaa111  ffmpeg-master-latest-win64-gpl-shared.zip\n" +
                "F4CE1C  ffmpeg-master-latest-win64-gpl.zip\n" +
                "bbb222 *ffmpeg-master-latest-linux64-gpl.tar.xz\r\n";

            Assert.Equal("f4ce1c", FfmpegInstaller.FindChecksum(checksums, "ffmpeg-master-latest-win64-gpl.zip"));
            Assert.Equal("bbb222", FfmpegInstaller.FindChecksum(checksums, "ffmpeg-master-latest-linux64-gpl.tar.xz"));
            Assert.Null(FfmpegInstaller.FindChecksum(checksums, "ffmpeg-master-latest-winarm64-gpl.zip"));
        }

        [Fact]
        public void Installed_file_is_found_and_executable()
        {
            string source = Path.Combine(_dir, "downloaded");
            File.WriteAllText(source, "binary");
            string installDir = Path.Combine(_dir, "tools");

            string installed = FfmpegInstaller.InstallExecutable(source, installDir);

            Assert.Equal(Path.Combine(installDir, Ffmpeg.ExeName), installed);
            Assert.False(File.Exists(installed + ".part"));
            if (OperatingSystem.IsWindows() == false)
            {
                Assert.True(File.GetUnixFileMode(installed).HasFlag(UnixFileMode.UserExecute));
            }
        }

        /// <summary>Real ~100 MB download. Run with GPAC_TEST_FFMPEG_DOWNLOAD=1.</summary>
        [SkippableFact]
        public async Task Downloads_verifies_and_runs_ffmpeg()
        {
            Skip.If(Environment.GetEnvironmentVariable("GPAC_TEST_FFMPEG_DOWNLOAD") != "1", "set GPAC_TEST_FFMPEG_DOWNLOAD=1 to run");
            string? asset = FfmpegInstaller.AssetName(RuntimeInformation.OSArchitecture);
            Skip.If(asset == null, "no build for this system");

            string installed = await FfmpegInstaller.Download(asset!, Path.Combine(_dir, "tools"));

            (int exitCode, _) = new Ffmpeg(installed).Run(new[] { "-hide_banner", "-version" });
            Assert.Equal(0, exitCode);
        }

        [Fact]
        public void Extra_dir_is_searched()
        {
            string installDir = Path.Combine(_dir, "tools");
            Directory.CreateDirectory(installDir);
            File.WriteAllText(Path.Combine(installDir, Ffmpeg.ExeName), "x");

            // The bundled copy (Windows test runs) still wins; otherwise the extra dir is used.
            string? found = Ffmpeg.FindExecutable(installDir);
            string bundled = Path.Combine(AppContext.BaseDirectory, "tools", Ffmpeg.ExeName);
            Assert.Equal(File.Exists(bundled) ? bundled : Path.Combine(installDir, Ffmpeg.ExeName), found);
        }
    }
}
