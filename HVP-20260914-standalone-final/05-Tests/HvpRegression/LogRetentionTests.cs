using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using Z02JHVPService;

internal static class LogRetentionTests
{
    internal static void Run(Action<string, Action> test, Action<bool, string> assert)
    {
        test("retention deletes expired filename dates and keeps boundary/current/future logs", () => {
            string state = State();
            string expired = Daily(state, -31);
            string boundary = Daily(state, -30);
            string current = Daily(state, 0);
            string future = Daily(state, 1);
            File.SetLastWriteTimeUtc(expired, DateTime.UtcNow);
            File.SetLastWriteTimeUtc(boundary, DateTime.UtcNow.AddYears(-1));
            HvpLog.Write("RETENTION TEST");
            assert(!File.Exists(expired), "Expired daily log survived first write");
            assert(File.Exists(boundary) && File.Exists(current) && File.Exists(future), "Retention deleted an in-range filename date");
            assert(File.ReadAllText(current).Contains("RETENTION TEST"), "Cleanup prevented the current log write");
        });

        test("retention only deletes exact daily log filenames in State", () => {
            string state = State();
            string expired = Daily(state, -31);
            string[] names = { "processed-files.tsv", "processed-files.tsv.tmp", "job.lock", "SAP-Z02J-HVP.txt",
                "hvp-20000101.log.bak", "other-20000101.log", "hvp-20001301.log", "hvp-2000011.log", "HVP-20000101.log", "hvp-20010101.LOG" };
            foreach (string name in names) File.WriteAllText(Path.Combine(state, name), "untouched");
            string nested = Path.Combine(state, "archive");
            Directory.CreateDirectory(nested);
            string nestedLog = Daily(nested, -31);
            string namedDirectory = Path.Combine(state, "hvp-20020101.log");
            Directory.CreateDirectory(namedDirectory);
            HvpLog.Write("RETENTION SCOPE TEST");
            assert(!File.Exists(expired), "Expired exact filename was not deleted");
            foreach (string name in names) assert(File.ReadAllText(Path.Combine(state, name)) == "untouched", "Unrelated file changed: " + name);
            assert(File.Exists(nestedLog) && Directory.Exists(namedDirectory), "Retention entered or removed a subdirectory");
        });

        test("retention skips cleanup when State is the SAP source directory", () => {
            string state = State();
            ConfigurationManager.AppSettings["HvpFilePath"] = Path.Combine(state, ".");
            string expired = Daily(state, -31);
            string source = Path.Combine(state, "20000101-Z02J-HVP.txt");
            File.WriteAllText(source, "SAP fixture");
            HvpLog.Write("RETENTION SOURCE DIRECTORY TEST");
            assert(File.Exists(expired) && File.ReadAllText(source) == "SAP fixture", "Cleanup touched the configured SAP source directory");
        });

        foreach (string value in new[] { null, "bad", "-1", "2147483648" })
        {
            string setting = value;
            test("retention uses 30-day default for " + (setting ?? "missing config"), () => {
                if (setting != null) ConfigurationManager.AppSettings["LogRetentionDays"] = setting;
                string state = State();
                string expired = Daily(state, -31);
                string boundary = Daily(state, -30);
                HvpLog.Write("RETENTION DEFAULT TEST");
                assert(!File.Exists(expired) && File.Exists(boundary), "Invalid/missing setting did not use 30 days");
            });
        }

        test("retention honors a configured positive number of days", () => {
            ConfigurationManager.AppSettings["LogRetentionDays"] = "2";
            string state = State();
            string expired = Daily(state, -3);
            string boundary = Daily(state, -2);
            HvpLog.Write("RETENTION CONFIG TEST");
            assert(!File.Exists(expired) && File.Exists(boundary), "Configured retention boundary was ignored");
        });

        test("retention zero disables cleanup while logging continues", () => {
            ConfigurationManager.AppSettings["LogRetentionDays"] = "0";
            string state = State();
            string old = Daily(state, -365);
            HvpLog.Write("RETENTION DISABLED TEST");
            assert(File.Exists(old), "Disabled cleanup deleted a log");
            assert(File.ReadAllText(DailyPath(state, 0)).Contains("RETENTION DISABLED TEST"), "Disabled cleanup also disabled logging");
        });

        test("retention very large positive setting does not overflow the cutoff", () => {
            ConfigurationManager.AppSettings["LogRetentionDays"] = "2147483647";
            string state = State();
            string old = Daily(state, -365);
            HvpLog.Write("RETENTION LARGE CONFIG TEST");
            assert(File.Exists(old), "Large positive retention incorrectly fell back to 30 days");
            assert(!File.ReadAllText(DailyPath(state, 0)).Contains("CLEANUP FAILED"), "Large retention overflowed cleanup");
        });

        test("retention runs once per day for each State directory", () => {
            string first = State();
            string firstExpired = Daily(first, -31);
            HvpLog.Write("RETENTION FIRST DIRECTORY");
            assert(!File.Exists(firstExpired), "Initial directory cleanup did not run");
            Daily(first, -31);
            HvpLog.Write("RETENTION SAME DAY");
            assert(File.Exists(firstExpired), "Repeated write rescanned the directory on the same day");
            string second = Path.Combine(Path.GetDirectoryName(first), "second-state");
            Directory.CreateDirectory(second);
            ConfigurationManager.AppSettings["HvpStatePath"] = second;
            string secondExpired = Daily(second, -31);
            HvpLog.Write("RETENTION SECOND DIRECTORY");
            assert(!File.Exists(secondExpired), "Changing State directory skipped its cleanup");
            ConfigurationManager.AppSettings["HvpStatePath"] = Path.Combine(first, ".");
            HvpLog.Write("RETENTION RETURN TO FIRST");
            assert(File.Exists(firstExpired), "Returning to the same directory ran cleanup again");
        });

        test("retention retries cleanup on the next local calendar day", () => {
            string state = State();
            HvpLog.Write("RETENTION PREVIOUS DAY");
            string expired = Daily(state, -31);
            // Advance the prior-run state without changing the machine clock or production API.
            FieldInfo field = typeof(HvpLog).GetField("LastCleanupDates", BindingFlags.Static | BindingFlags.NonPublic);
            assert(field != null, "Retention did not keep per-directory cleanup dates");
            var dates = (Dictionary<string, DateTime>)field.GetValue(null);
            dates[Path.GetFullPath(state)] = DateTime.Today.AddDays(-1);
            HvpLog.Write("RETENTION NEXT DAY");
            assert(!File.Exists(expired), "A previous day's cleanup suppressed today's cleanup");
        });

        test("retention cleanup failure is sanitized and does not prevent writes or other deletions", () => {
            string state = State();
            string locked = Daily(state, -32);
            string expired = Daily(state, -31);
            using (var held = new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                HvpLog.Write("RETENTION CONTINUES AFTER FAILURE");
                assert(!File.Exists(expired), "One deletion failure stopped cleanup of the remaining logs");
                string log = File.ReadAllText(DailyPath(state, 0));
                assert(log.Contains("RETENTION CONTINUES AFTER FAILURE"), "Cleanup failure prevented the caller log write");
                assert(log.Contains("LOG CLEANUP FAILED"), "Cleanup failure was silent");
                assert(!log.Contains(locked) && !log.Contains(state) && !log.Contains("Exception") && !log.Contains("StackTrace"), "Cleanup exposed an exception or raw path");
            }
            HvpLog.Write("RETENTION NO SAME DAY RETRY");
            assert(File.Exists(locked), "Cleanup retried a failed deletion on the same day");
        });

        test("retention skips reparse directories including State and its ancestors", () => {
            string state = State();
            string target = Path.Combine(Path.GetDirectoryName(state), "link-target");
            Directory.CreateDirectory(target);
            string oldTarget = Daily(target, -31);
            string junction = Path.Combine(state, "hvp-20000101.log");
            Junction(junction, target);
            string expired = Daily(state, -31);
            HvpLog.Write("RETENTION REPARSE CHILD TEST");
            assert(!File.Exists(expired), "Cleanup skipped a normal expired log beside a reparse child");
            assert(File.Exists(oldTarget) && Directory.Exists(junction), "Cleanup touched a reparse child");
            ConfigurationManager.AppSettings["HvpStatePath"] = junction;
            HvpLog.Write("RETENTION REPARSE STATE TEST");
            assert(File.Exists(oldTarget), "Cleanup followed a reparse State directory");
            string nestedTarget = Path.Combine(target, "nested");
            Directory.CreateDirectory(nestedTarget);
            string nestedOld = Daily(nestedTarget, -31);
            ConfigurationManager.AppSettings["HvpStatePath"] = Path.Combine(junction, "nested");
            HvpLog.Write("RETENTION REPARSE ANCESTOR TEST");
            assert(File.Exists(nestedOld), "Cleanup followed a reparse ancestor of State");
        });

        test("retention uses Gregorian daily filenames regardless of process culture", () => {
            string state = State();
            string expired = Daily(state, -31);
            CultureInfo original = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("th-TH");
                HvpLog.Write("RETENTION CULTURE TEST");
            }
            finally { CultureInfo.CurrentCulture = original; }
            assert(!File.Exists(expired), "Culture changed the parsed retention cutoff");
            assert(File.Exists(DailyPath(state, 0)), "Current log filename used a different calendar from retention");
        });
    }

    private static string State()
    {
        string state = ConfigurationManager.AppSettings["HvpStatePath"];
        Directory.CreateDirectory(state);
        return state;
    }

    private static string DailyPath(string state, int days)
    {
        return Path.Combine(state, "hvp-" + DateTime.Today.AddDays(days).ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".log");
    }

    private static string Daily(string state, int days)
    {
        string path = DailyPath(state, days);
        File.WriteAllText(path, "fixture" + Environment.NewLine);
        return path;
    }

    private static void Junction(string path, string target)
    {
        // These are generated temporary fixture paths. Creation needs no administrator rights.
        var start = new ProcessStartInfo("cmd.exe", "/c mklink /J \"" + path + "\" \"" + target + "\"") {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        using (var process = Process.Start(start))
        {
            process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0) throw new Exception("Could not create junction fixture: " + error);
        }
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0)
            throw new Exception("Junction fixture is not a reparse point");
    }
}
