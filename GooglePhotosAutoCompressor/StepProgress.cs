namespace GooglePhotosAutoCompressor
{
    /// <summary>
    /// How far the running step is (0 to 1), for the window's step bar. Each step reports "done of total"
    /// as it goes; <see cref="Pipeline"/> resets it when a step starts. Steps without a measure (sign-in)
    /// simply don't report.
    /// </summary>
    internal static class StepProgress
    {
        public static event Action<double>? Changed;

        public static void Report(double done, double total)
        {
            if (total > 0)
            {
                Report(done / total);
            }
        }

        public static void Report(double fraction) => Changed?.Invoke(Math.Clamp(double.IsNaN(fraction) ? 0 : fraction, 0, 1));
    }
}
