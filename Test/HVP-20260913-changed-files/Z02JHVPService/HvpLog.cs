using System;
using System.Collections.Generic;
using System.Configuration;
using System.Globalization;
using System.IO;
using System.Text;

namespace Z02JHVPService
{
    internal static class HvpLog
    {
        private static readonly object Sync = new object();
        private static readonly Dictionary<string, DateTime> LastCleanupDates =
            new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

        internal static string GetStateDirectory()
        {
            string path = ConfigurationManager.AppSettings["HvpStatePath"];
            if (string.IsNullOrWhiteSpace(path))
                path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Z02JHVPService");
            Directory.CreateDirectory(path);
            return path;
        }

        internal static void Write(string message)
        {
            DateTime now = DateTime.Now;
            string timestamp = now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            string line = timestamp + " " + message;
            Console.WriteLine(line);
            lock (Sync)
            {
                string state = GetStateDirectory();
                string output = line + Environment.NewLine;
                if (CleanupOldLogs(state, now.Date))
                {
                    // Append with this write, without recursively calling the logger or exposing exception details.
                    string warning = timestamp + " LOG CLEANUP FAILED; check State log permissions.";
                    Console.WriteLine(warning);
                    output = warning + Environment.NewLine + output;
                }
                File.AppendAllText(Path.Combine(state, "hvp-" + now.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".log"),
                    output, Encoding.UTF8);
            }
        }

        private static bool CleanupOldLogs(string state, DateTime today)
        {
            bool failed = false;
            try
            {
                string key = Path.GetFullPath(state).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                DateTime previous;
                if (LastCleanupDates.TryGetValue(key, out previous) && previous == today) return false;
                // One attempt per local calendar day and directory in this service process, including failures.
                LastCleanupDates[key] = today;

                int days;
                if (!int.TryParse(ConfigurationManager.AppSettings["LogRetentionDays"], out days) || days < 0)
                    days = 30;
                if (days == 0) return false;

                string source = ConfigurationManager.AppSettings["HvpFilePath"];
                if (!string.IsNullOrWhiteSpace(source) && string.Equals(key,
                    Path.GetFullPath(source).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase)) return false;

                // A junction in any ancestor also redirects State, even when State itself is an ordinary directory.
                for (DirectoryInfo directory = new DirectoryInfo(state); directory != null; directory = directory.Parent)
                    if ((directory.Attributes & FileAttributes.ReparsePoint) != 0) return false;

                // Keep filename dates >= today - days. Very large values retain all dates without overflowing.
                DateTime cutoff = days > today.Ticks / TimeSpan.TicksPerDay ? DateTime.MinValue : today.AddDays(-days);
                foreach (string path in Directory.EnumerateFiles(state, "hvp-*.log", SearchOption.TopDirectoryOnly))
                {
                    string name = Path.GetFileName(path);
                    DateTime date;
                    if (name.Length != 16 || !name.StartsWith("hvp-", StringComparison.Ordinal)
                        || !name.EndsWith(".log", StringComparison.Ordinal)
                        || !DateTime.TryParseExact(name.Substring(4, 8), "yyyyMMdd", CultureInfo.InvariantCulture,
                            DateTimeStyles.None, out date) || date >= cutoff) continue;
                    try
                    {
                        if ((File.GetAttributes(path) & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0) continue;
                        File.Delete(path);
                    }
                    catch { failed = true; }
                }
            }
            catch { failed = true; }
            return failed;
        }
    }
}
