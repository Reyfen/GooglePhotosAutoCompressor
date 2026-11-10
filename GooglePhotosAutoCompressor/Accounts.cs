using System.Text.RegularExpressions;

namespace GooglePhotosAutoCompressor
{
    /// <summary>
    /// Progress is kept per Google account: media keys belong to one account, and mixing two would be unsafe (a run
    /// for account B would not find account A's originals in its list, treat them as binned and upload A's copies
    /// into B). Files: Downloads/GooglePhotosAutoCompressor/accounts/&lt;email as file name&gt;.json.
    /// The shared progress.json of versions up to 1.0.1 is adopted by the first account a run sees.
    /// </summary>
    internal static class Accounts
    {
        public const string DIR_NAME = "accounts";

        /// <summary>The email in the account button's label: "Google Account: Name\n(name@gmail.com)". Lower case.</summary>
        public static string? ParseEmail(string? accountLabel)
        {
            if (string.IsNullOrEmpty(accountLabel))
            {
                return null;
            }

            Match match = Regex.Match(accountLabel, @"\(\s*([^\s()@]+@[^\s()@]+\.[^\s()@]+)\s*\)\s*$");
            return match.Success ? match.Groups[1].Value.ToLowerInvariant() : null;
        }

        /// <summary>A file name made from the email ("name@gmail.com" → "name_gmail.com"), safe on every system.</summary>
        public static string FileName(string email) =>
            Regex.Replace(email.Trim().ToLowerInvariant(), @"[^a-z0-9._-]", "_") + ".json";

        public static string Dir(string root) => Path.Combine(root, DIR_NAME);

        public static string ProgressPath(string email, string? root = null) =>
            Path.Combine(Dir(root ?? Const.APP_DOWNLOADS_PATH), FileName(email));

        /// <summary>
        /// The shared progress file of older versions goes to the first account a run sees (it was that user's only
        /// account). Moved with its backup; only when the account has no progress of its own yet. Returns true when moved.
        /// </summary>
        public static bool AdoptSharedProgress(string sharedPath, string accountPath)
        {
            bool main = File.Exists(sharedPath), backup = File.Exists(sharedPath + ".bak");
            if (File.Exists(accountPath) || File.Exists(accountPath + ".bak") || (main == false && backup == false))
            {
                return false;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(accountPath)!);
            // Newest file first: if the move stops half-way, only the older backup is left behind.
            if (main)
            {
                File.Move(sharedPath, accountPath);
            }

            if (backup)
            {
                File.Move(sharedPath + ".bak", accountPath + ".bak");
            }

            Console.WriteLine($"Progress of earlier runs now belongs to this account: {accountPath}");
            return true;
        }

        /// <summary>Every progress file: one per account, plus the shared one of older versions if still there.</summary>
        public static IReadOnlyList<string> AllProgressFiles(string? root = null, string? sharedPath = null)
        {
            string dir = Dir(root ?? Const.APP_DOWNLOADS_PATH);
            List<string> files = Directory.Exists(dir) ? Directory.GetFiles(dir, "*.json").ToList() : new List<string>();
            string shared = sharedPath ?? FileItemsData.DEFAULT_PATH;
            if (File.Exists(shared) || File.Exists(shared + ".bak"))
            {
                files.Add(shared);
            }

            return files;
        }

        /// <summary>"Delete all login data": every account's progress and stats go too (owner's choice, 2026-11-10).</summary>
        public static int DeleteAllProgress(string? root = null, string? sharedPath = null)
        {
            int deleted = 0;
            foreach (string file in AllProgressFiles(root, sharedPath))
            {
                foreach (string path in new[] { file, file + ".bak" }.Where(File.Exists))
                {
                    File.Delete(path);
                    deleted++;
                }
            }

            string dir = Dir(root ?? Const.APP_DOWNLOADS_PATH);
            if (Directory.Exists(dir) && Directory.EnumerateFileSystemEntries(dir).Any() == false)
            {
                Directory.Delete(dir);
            }

            return deleted;
        }
    }
}
