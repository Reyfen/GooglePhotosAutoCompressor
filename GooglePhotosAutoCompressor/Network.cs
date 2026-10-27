namespace GooglePhotosAutoCompressor
{
    /// <summary>
    /// Tells "the internet is down" apart from "this item is broken". Without it, a Wi-Fi drop during
    /// downloads would count as a failure for every item, and after a few drops they would be abandoned.
    /// </summary>
    internal static class Network
    {
        private static readonly HttpClient HTTP = new() { Timeout = TimeSpan.FromSeconds(5) };
        private static long _lastOfflineTicks;

        /// <summary>Replaceable for tests.</summary>
        public static Func<CancellationToken, Task<bool>> Check { get; set; } = DefaultCheck;

        /// <summary>Last time a check failed (UTC), or null.</summary>
        public static DateTime? LastOfflineAt
        {
            get
            {
                long ticks = Interlocked.Read(ref _lastOfflineTicks);
                return ticks == 0 ? null : new DateTime(ticks, DateTimeKind.Utc);
            }
        }

        public static async Task<bool> IsOnlineAsync(CancellationToken cancellationToken = default)
        {
            bool online = await Check(cancellationToken);
            if (online == false)
            {
                Interlocked.Exchange(ref _lastOfflineTicks, DateTime.UtcNow.Ticks);
            }

            return online;
        }

        /// <summary>True when the connection was down at any check since <paramref name="sinceUtc"/>.</summary>
        public static bool WasOfflineSince(DateTime sinceUtc) => LastOfflineAt >= sinceUtc;

        /// <summary>Returns once the connection is back. Calls <paramref name="onWaiting"/> once when it has to wait.</summary>
        public static async Task WaitUntilOnlineAsync(CancellationToken cancellationToken, Action? onWaiting = null)
        {
            bool told = false;
            while (await IsOnlineAsync(cancellationToken) == false)
            {
                if (told == false)
                {
                    onWaiting?.Invoke();
                    told = true;
                }

                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            }
        }

        /// <summary>Checks every few seconds while a run is going, so short drops are noticed (see <see cref="WasOfflineSince"/>).</summary>
        public static async Task MonitorAsync(CancellationToken cancellationToken)
        {
            try
            {
                while (cancellationToken.IsCancellationRequested == false)
                {
                    await IsOnlineAsync(cancellationToken);
                    await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
            }
        }

        private static async Task<bool> DefaultCheck(CancellationToken cancellationToken)
        {
            try
            {
                using HttpResponseMessage response = await HTTP.GetAsync("https://www.google.com/generate_204", cancellationToken);
                return response.IsSuccessStatusCode;
            }
            catch (Exception e) when (e is HttpRequestException || (e is TaskCanceledException && cancellationToken.IsCancellationRequested == false))
            {
                return false;
            }
        }
    }

    internal static class DiskSpace
    {
        /// <summary>
        /// Room for the download plus a copy of up to the same size, keeping <paramref name="minFreeBytes"/> free.
        /// </summary>
        public static bool HasRoomFor(long fileSize, long freeBytes, long minFreeBytes)
        {
            return freeBytes - 2 * fileSize >= minFreeBytes;
        }

        /// <summary>Free bytes on the drive holding <paramref name="dir"/>; very large when it cannot be read.</summary>
        public static long FreeBytes(string dir)
        {
            try
            {
                string full = Path.GetFullPath(dir);
                // Linux: pick the deepest mount point that contains the folder (e.g. /home on its own partition).
                DriveInfo? best = DriveInfo.GetDrives()
                    .Where(d => d.IsReady && full.StartsWith(d.RootDirectory.FullName, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                    .OrderByDescending(d => d.RootDirectory.FullName.Length)
                    .FirstOrDefault();
                return best?.AvailableFreeSpace ?? long.MaxValue;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
            {
                return long.MaxValue;
            }
        }
    }
}
