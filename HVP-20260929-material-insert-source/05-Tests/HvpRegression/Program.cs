using System;
using System.IO;
using System.Linq;
using System.Configuration;
using Z02JHVPService;
using HvpRegressionSupport;

class Regression
{
    static string root;
    static int failures;
    static void Assert(bool ok, string message) { if (!ok) throw new Exception(message); }
    static void Test(string name, Action body)
    {
        root = Path.Combine(Path.GetTempPath(), "hvp-regression-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        ConfigurationManager.AppSettings.Clear();
        ConfigurationManager.AppSettings["HvpFilePath"] = root;
        ConfigurationManager.AppSettings["HvpStatePath"] = Path.Combine(root, "state");
        FakeDatabase.Writes.Clear(); FakeDatabase.FailMaterial = null; FakeDatabase.BeforeExec = null;

        ConfigurationManager.ConnectionStrings.Clear();
        ConfigurationManager.ConnectionStrings["Material"] = new ConnectionStringSettings { ConnectionString = "TEST-DIRECT-MATERIAL" };
        Oracle.ManagedDataAccess.Client.OracleConnection.Reset();
        try { body(); Console.WriteLine("PASS " + name); }
        catch (Exception ex) { failures++; Console.WriteLine("FAIL " + name + " => " + ex.Message); }
        // Retain isolated temp fixtures for inspection. Never operate on SAP folders.
    }
    static string FileAt(string name, string body, int minutesAgo)
    {
        string path = Path.Combine(root, name + "-Z02J-HVP.txt");
        File.WriteAllText(path, body);
        File.SetCreationTimeUtc(path, DateTime.UtcNow.AddMinutes(-minutesAgo));
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(-minutesAgo));
        return path;
    }
    static string Row(string material, string flag) { return "Material\tSpecial Control Flag\n" + material + "\t" + flag + "\n"; }
    static int Main()
    {
        Test("older creation time with recently updated time is still processed", () => {
            string path = FileAt("old", Row("M1", "HVP"), 30);
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(-5));
            new HvpFileProcessor().ProcessFiles();
            Assert(FakeDatabase.Writes.SequenceEqual(new[] { "M1:HVP" }), "Old CreationTime excluded the file");
        });
        Test("all pending files run in creation order with stable tie order", () => {
            FileAt("z", Row("M3", "OTHER"), 10);
            FileAt("a", Row("M1", "HVP"), 30);
            FileAt("b", Row("M2", ""), 20);
            new HvpFileProcessor().ProcessFiles();
            Assert(FakeDatabase.Writes.SequenceEqual(new[] { "M1:HVP", "M2:", "M3:" }), "Wrong order/missing files");
        });
        Test("success survives processor restart without replay; source remains intact", () => {
            string path = FileAt("one", Row("M1", "HVP"), 1);
            byte[] before = File.ReadAllBytes(path);
            new HvpFileProcessor().ProcessFiles(); new HvpFileProcessor().ProcessFiles();
            Assert(FakeDatabase.Writes.Count == 1, "Successful file was repeated");
            Assert(File.Exists(path) && before.SequenceEqual(File.ReadAllBytes(path)), "Input moved/removed/changed");
        });
        Test("same pathname with changed time/content is processed again", () => {
            string path = FileAt("same", Row("M1", "HVP"), 20);
            var processor = new HvpFileProcessor(); processor.ProcessFiles();
            File.WriteAllText(path, Row("M1", "OTHER"));
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(-5));
            processor.ProcessFiles();
            Assert(FakeDatabase.Writes.SequenceEqual(new[] { "M1:HVP", "M1:" }), "Replacement was missed");
        });
        Test("header padding and case don't lose a valid SAP export", () => {
            FileAt("header", " Material \t special control flag \nM1\t hvp \nM2\tNA\n", 1);
            new HvpFileProcessor().ProcessFiles();
            Assert(FakeDatabase.Writes.SequenceEqual(new[] { "M1:HVP", "M2:" }), "Header/value normalization failed");
        });
        Test("truncated row doesn't clear a flag or partly apply the file", () => {
            FileAt("short", "Material\tSpecial Control Flag\nM1\tHVP\nM2\n", 1);
            new HvpFileProcessor().ProcessFiles();
            Assert(FakeDatabase.Writes.Count == 0, "Incomplete row was treated as blank");
        });
        Test("failed oldest file blocks later writes until retry succeeds", () => {
            FileAt("first", Row("M1", "HVP"), 20); FileAt("second", Row("M2", ""), 10);
            var processor = new HvpFileProcessor(); FakeDatabase.FailMaterial = "M1";
            processor.ProcessFiles(); Assert(FakeDatabase.Writes.Count == 0, "Later file bypassed failed oldest");
            FakeDatabase.FailMaterial = null; processor.ProcessFiles();
            Assert(FakeDatabase.Writes.SequenceEqual(new[] { "M1:HVP", "M2:" }), "Failed file not retried in order");
        });
        Test("unavailable source folder has a visible failure", () => {
            ConfigurationManager.AppSettings["HvpFilePath"] = Path.Combine(root, "missing");
            bool threw = false;
            try { new HvpFileProcessor().ProcessFiles(); } catch (DirectoryNotFoundException) { threw = true; }
            Assert(threw, "Missing folder returned silently");
        });
        Test("missing direct connection cannot claim successful initialization", () => {
            ConfigurationManager.ConnectionStrings["Material"] = null;
            bool threw = false; try { new HvpService(); } catch (InvalidOperationException) { threw = true; }
            Assert(threw, "Null connection accepted");
        });
        Test("changed older file replays later files, including a failed replay after restart", () => {
            string old = FileAt("old", Row("M1", "HVP"), 30);
            FileAt("new", Row("M2", ""), 20);
            new HvpFileProcessor().ProcessFiles(); FakeDatabase.Writes.Clear();
            File.WriteAllText(old, Row("M1", "OTHER"));
            File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddSeconds(-5));
            FakeDatabase.FailMaterial = "M2";
            new HvpFileProcessor().ProcessFiles(); FakeDatabase.FailMaterial = null;
            new HvpFileProcessor().ProcessFiles();
            Assert(FakeDatabase.Writes.SequenceEqual(new[] { "M1:", "M2:" }), "Later checkpoint hid a required replay");
        });
        Test("concurrent process cannot enter the same checkpoint job", () => {
            FileAt("one", Row("M1", "HVP"), 1);
            string state = ConfigurationManager.AppSettings["HvpStatePath"];
            Directory.CreateDirectory(state);
            using (var held = new FileStream(Path.Combine(state, "job.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
            {
                try { new HvpFileProcessor().ProcessFiles(); } catch (IOException) { }
                Assert(FakeDatabase.Writes.Count == 0, "Concurrent job bypassed exclusive lock");
            }
        });
        Test("SQL applies by material only, never plant/sloc", () => {
            new HvpService().UpdateSpecialControlFlags(new System.Collections.Generic.List<HvpRecord> {
                new HvpRecord { Material="M1", SpecialControlFlag="HVP" } });
            string where = FakeDatabase.LastSql.Substring(FakeDatabase.LastSql.IndexOf("WHERE", StringComparison.OrdinalIgnoreCase)).ToUpperInvariant();
            Assert(where.Contains(":MATERIAL") && !where.Contains("PLNT") && !where.Contains("SLOC"), "Wrong matching key");
        });
        Test("third failure skips persistently and continues to the next file", () => {
            string bad = FileAt("bad", Row("M1", "HVP"), 20);
            FileAt("next", Row("M2", ""), 10);
            FakeDatabase.FailMaterial = "M1";
            new HvpFileProcessor().ProcessFiles();
            new HvpFileProcessor().ProcessFiles();
            Assert(FakeDatabase.Writes.Count == 0, "Skipped before three attempts");
            new HvpFileProcessor().ProcessFiles();
            Assert(FakeDatabase.Writes.SequenceEqual(new[] { "M2:" }), "Third failure still blocks queue");
            FakeDatabase.FailMaterial = null;
            new HvpFileProcessor().ProcessFiles();
            Assert(FakeDatabase.Writes.Count == 1 && File.Exists(bad), "Skipped file retried or removed");
            File.WriteAllText(bad, Row("M1", "NA"));
            File.SetLastWriteTimeUtc(bad, DateTime.UtcNow.AddSeconds(-2));
            new HvpFileProcessor().ProcessFiles();
            Assert(FakeDatabase.Writes.SequenceEqual(new[] { "M2:", "M1:", "M2:" }), "Changed version didn't reset skip/replay");
        });
        foreach (string limit in new[] { "1", "2", "bad", "0", "-1" })
        Test("configured attempt limit " + limit, () => {
            ConfigurationManager.AppSettings["MaxFileAttempts"] = limit;
            FileAt("bad", Row("M1", "HVP"), 20); FileAt("next", Row("M2", ""), 10);
            FakeDatabase.FailMaterial = "M1";
            int expected = limit == "1" ? 1 : limit == "2" ? 2 : 3;
            for (int i = 1; i < expected; i++) {
                new HvpFileProcessor().ProcessFiles();
                Assert(FakeDatabase.Writes.Count == 0, "Skipped too soon");
            }
            new HvpFileProcessor().ProcessFiles();
            Assert(FakeDatabase.Writes.SequenceEqual(new[] { "M2:" }), "Configured/default limit ignored");
        });
        Test("legacy four-column checkpoint remains compatible", () => {
            string file = FileAt("done", Row("M1", "HVP"), 20);
            var info = new FileInfo(file);
            string state = ConfigurationManager.AppSettings["HvpStatePath"];
            Directory.CreateDirectory(state);
            File.WriteAllText(Path.Combine(state, "processed-files.tsv"), file + "\t" + info.CreationTimeUtc.Ticks
                + "\t" + info.LastWriteTimeUtc.Ticks + "\t" + info.Length + "\n");
            new HvpFileProcessor().ProcessFiles();
            Assert(FakeDatabase.Writes.Count == 0, "Legacy success checkpoint replayed");
        });
        foreach (bool becomesNewest in new[] { true, false })
        Test("changed queued file is deferred and reordered " + (becomesNewest ? "newer" : "older"), () => {
            FileAt("a", Row("TRIGGER", "HVP"), 30);
            string changed = FileAt("b", Row("M1", "HVP"), 20);
            FileAt("c", Row("M1", "OTHER"), 10);
            string queuedVersion = File.GetCreationTimeUtc(changed).Ticks + "\t"
                + File.GetLastWriteTimeUtc(changed).Ticks + "\t" + new FileInfo(changed).Length;
            FakeDatabase.BeforeExec = material => {
                if (material != "TRIGGER") return;
                FakeDatabase.BeforeExec = null;
                File.SetCreationTimeUtc(changed, DateTime.UtcNow.AddMinutes(becomesNewest ? 0 : -40));
                File.SetLastWriteTimeUtc(changed, DateTime.UtcNow);
            };
            new HvpFileProcessor().ProcessFiles();
            Assert(FakeDatabase.Writes.SequenceEqual(new[] { "TRIGGER:HVP" }), "Changed file/tail ran in stale queue order");
            string checkpoint = File.ReadAllLines(Path.Combine(ConfigurationManager.AppSettings["HvpStatePath"], "processed-files.tsv"))
                .Last(line => line.StartsWith(changed + "\t", StringComparison.OrdinalIgnoreCase));
            Assert(checkpoint == changed + "\t" + queuedVersion + "\t0\tpending", "Deferral consumed an attempt or accepted the new version");
            FakeDatabase.Writes.Clear();
            new HvpFileProcessor().ProcessFiles();
            string[] expected = becomesNewest ? new[] { "M1:", "M1:HVP" } : new[] { "M1:HVP", "TRIGGER:HVP", "M1:" };
            Assert(FakeDatabase.Writes.SequenceEqual(expected), "Restart did not process the current creation order");
            new HvpFileProcessor().ProcessFiles();
            Assert(FakeDatabase.Writes.SequenceEqual(expected), "Completed reordered files were repeated");
        });
        LogRetentionTests.Run(Test, Assert);
        StatePerformanceTests.Run(Test, Assert);
        StandaloneDatabaseTests.Run(Test, Assert);
        FileSelectionTests.Run(Test, Assert);
        SapInsertFileTests.Run(Test, Assert);
        Console.WriteLine("Failures: " + failures);
        return failures == 0 ? 0 : 1;
    }
}
