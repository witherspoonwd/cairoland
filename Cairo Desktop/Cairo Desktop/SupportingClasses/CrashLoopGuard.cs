using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;

namespace CairoDesktop.SupportingClasses
{
    /// <summary>
    /// Shell Launcher restarts Cairo whenever it crashes. If it crashes straight away every time, that loop would
    /// leave the user with no way in, so after <see cref="MaxStarts"/> starts within <see cref="Window"/> Cairo
    /// opens Task Manager instead and exits with the DoNothing code.
    /// </summary>
    internal static class CrashLoopGuard
    {
        private const int MaxStarts = 3;
        private static readonly TimeSpan Window = TimeSpan.FromSeconds(60);

        private static string StartsFile => Path.Combine(CairoApplication.CairoApplicationDataFolder, "starts.txt");

        /// <summary>
        /// Records this start. Returns true when Cairo is in a crash loop and should not start.
        /// </summary>
        public static bool RecordStart()
        {
            try
            {
                DateTime now = DateTime.UtcNow;
                List<DateTime> starts = ReadStarts().Where(start => now - start < Window && start <= now).ToList();
                starts.Add(now);

                Directory.CreateDirectory(Path.GetDirectoryName(StartsFile));
                File.WriteAllLines(StartsFile, starts.Select(start => start.ToString("o", CultureInfo.InvariantCulture)));

                return starts.Count >= MaxStarts;
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"CrashLoopGuard: Unable to record start: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Forgets earlier starts, so a restart the user asked for doesn't count towards a crash loop.
        /// </summary>
        public static void Reset()
        {
            try
            {
                File.Delete(StartsFile);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"CrashLoopGuard: Unable to reset: {ex.Message}");
            }
        }

        private static IEnumerable<DateTime> ReadStarts()
        {
            if (!File.Exists(StartsFile))
            {
                yield break;
            }

            foreach (string line in File.ReadAllLines(StartsFile))
            {
                if (DateTime.TryParse(line, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime start))
                {
                    yield return start.ToUniversalTime();
                }
            }
        }
    }
}
