using System.Text;

namespace GooglePhotosAutoCompressor
{
    /// <summary>
    /// Only one copy of the app may run: two would write the same state file and click in the same
    /// Chrome profile. The lock is an open file, so the OS releases it when the process dies.
    /// </summary>
    internal sealed class AppLock : IDisposable
    {
        public static readonly string DEFAULT_PATH = Path.Combine(Settings.CONFIG_DIR, "app.lock");

        private readonly FileStream _stream;

        private AppLock(FileStream stream)
        {
            _stream = stream;
        }

        /// <summary>The lock, or null when another copy holds it.</summary>
        public static AppLock? TryAcquire(string path)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
                FileStream stream = new(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                if (OperatingSystem.IsWindows() == false)
                {
                    // FileShare.None is advisory on Linux; an explicit lock makes it hold across processes.
                    stream.Lock(0, 1);
                }

                return new AppLock(stream);
            }
            catch (IOException)
            {
                return null;
            }
        }

        public void Dispose() => _stream.Dispose();
    }

    /// <summary>
    /// Console output that also goes to a log file and to listeners (the window's log view).
    /// Every module writes progress with Console.WriteLine, so this captures all of it.
    /// </summary>
    internal sealed class TeeTextWriter : TextWriter
    {
        private readonly TextWriter? _console;
        private readonly TextWriter? _file;
        private readonly StringBuilder _line = new();
        private readonly object _lock = new();

        public TeeTextWriter(TextWriter? console, TextWriter? file)
        {
            _console = console;
            _file = file;
        }

        /// <summary>Raised once per finished line (without the newline).</summary>
        public event Action<string>? LineWritten;

        public override Encoding Encoding => Encoding.UTF8;

        public override void Write(char value)
        {
            string? finished = null;
            lock (_lock)
            {
                _console?.Write(value);
                _file?.Write(value);

                if (value == '\n')
                {
                    finished = _line.ToString().TrimEnd('\r');
                    _line.Clear();
                    _file?.Flush();
                }
                else
                {
                    _line.Append(value);
                }
            }

            if (finished != null)
            {
                LineWritten?.Invoke(finished);
            }
        }

        public override void Write(string? value)
        {
            if (value == null)
            {
                return;
            }

            foreach (char c in value)
            {
                Write(c);
            }
        }

        public override void WriteLine(string? value)
        {
            Write(value);
            Write('\n');
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _file?.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    internal static class RunLog
    {
        public static readonly string LOG_DIR = Path.Combine(Settings.CONFIG_DIR, "logs");
        private const int KEEP_LOGS = 30;

        /// <summary>Sends all console output to a new log file as well. Keeps the last <see cref="KEEP_LOGS"/> files.</summary>
        public static TeeTextWriter Start(TextWriter? console)
        {
            Directory.CreateDirectory(LOG_DIR);
            foreach (FileInfo old in new DirectoryInfo(LOG_DIR).GetFiles("*.log").OrderByDescending(f => f.Name).Skip(KEEP_LOGS - 1))
            {
                try { old.Delete(); } catch (IOException) { }
            }

            CurrentPath = Path.Combine(LOG_DIR, $"{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.log");
            StreamWriter file = new(new FileStream(CurrentPath, FileMode.Create, FileAccess.Write, FileShare.Read), new UTF8Encoding(false));
            TeeTextWriter tee = new(console, file);
            Console.SetOut(tee);
            return tee;
        }

        public static string? CurrentPath { get; private set; }
    }
}
