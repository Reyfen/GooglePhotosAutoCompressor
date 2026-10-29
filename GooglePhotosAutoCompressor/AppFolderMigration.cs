namespace GooglePhotosAutoCompressor
{
    /// <summary>
    /// Versions before 2026-10-29 wrote straight into Downloads: GooglePhotosCache.json (+ .bak) and the
    /// GooglePhotosDownloadCache / GooglePhotosCompressCache folders. This moves them into
    /// Downloads/GooglePhotosAutoCompressor once, keeping all progress.
    ///
    /// Crash-safe order: the new state file (with the new paths) is written first, then the folders are
    /// moved, then the old state file is removed. Each part is skipped when already done, so a run that was
    /// cut off finishes the move next time. Nothing is ever deleted except the old state file after the new one exists.
    /// </summary>
    internal static class AppFolderMigration
    {
        public const string OLD_STATE_NAME = "GooglePhotosCache.json";
        public const string OLD_DOWNLOAD_DIR_NAME = "GooglePhotosDownloadCache";
        public const string OLD_COMPRESS_DIR_NAME = "GooglePhotosCompressCache";

        public static void Run() => Run(Const.DOWNLOADS_PATH, Const.APP_DOWNLOADS_PATH, FileItemsData.DEFAULT_PATH,
            moveWorkFolders: string.IsNullOrWhiteSpace(Settings.Current.WorkFolder));

        /// <param name="moveWorkFolders">False when the user chose their own work folder: then only the state file moves.</param>
        public static void Run(string downloads, string appFolder, string newStatePath, bool moveWorkFolders)
        {
            try
            {
                var moves = new List<(string from, string to)>();
                if (moveWorkFolders)
                {
                    moves.Add((Path.Combine(downloads, OLD_DOWNLOAD_DIR_NAME), Path.Combine(appFolder, FilePaths.DOWNLOAD_DIR_NAME)));
                    moves.Add((Path.Combine(downloads, OLD_COMPRESS_DIR_NAME), Path.Combine(appFolder, FilePaths.COMPRESS_DIR_NAME)));
                }

                // A folder is moved only when its new place is free; otherwise paths keep pointing at the old one.
                moves = moves.Where(m => Directory.Exists(m.from) && Directory.Exists(m.to) == false).ToList();

                string oldState = Path.Combine(downloads, OLD_STATE_NAME);
                bool stateToMove = (File.Exists(oldState) || File.Exists(oldState + ".bak")) && File.Exists(newStatePath) == false;

                if (stateToMove)
                {
                    FileItemsData old = new(oldState);
                    foreach (FileItem item in old.Items)
                    {
                        item.DownloadPath = Rebase(item.DownloadPath, moves);
                        item.CompressPath = Rebase(item.CompressPath, moves);
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(newStatePath)!);
                    FileItemsData moved = new(newStatePath);
                    foreach (FileItem item in old.Items)
                    {
                        moved.AddOrGet(item);
                    }

                    moved.Save();
                    Console.WriteLine($"Progress file moved to {newStatePath}");
                }
                // Otherwise the state was moved earlier (perhaps by a start that stopped before the folders),
                // and its paths already point at the new places.

                foreach ((string from, string to) in moves)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                    Directory.Move(from, to);
                    Console.WriteLine($"Moved {from} to {to}");
                }

                if (File.Exists(newStatePath))
                {
                    File.Delete(oldState);
                    File.Delete(oldState + ".bak");
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                // Not fatal: the old files keep working where they are. Tried again on the next start.
                Console.WriteLine($"Could not move the old files from {downloads} into {appFolder}: {e.Message}");
            }
        }

        private static string Rebase(string? path, List<(string from, string to)> moves)
        {
            if (string.IsNullOrEmpty(path))
            {
                return path!;
            }

            foreach ((string from, string to) in moves)
            {
                string prefix = from.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return Path.Combine(to, path[prefix.Length..]);
                }
            }

            return path;
        }
    }
}
