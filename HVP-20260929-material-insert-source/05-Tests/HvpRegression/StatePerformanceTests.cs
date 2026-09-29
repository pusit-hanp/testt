using System;
using System.Configuration;
using System.Collections.Generic;
using System.Reflection;
using System.IO;
using System.Linq;
using System.Text;
using HvpRegressionSupport;
using Z02JHVPService;

static class StatePerformanceTests
{
    static string Input { get { return ConfigurationManager.AppSettings["HvpFilePath"]; } }
    static string State { get { return Path.Combine(ConfigurationManager.AppSettings["HvpStatePath"], "processed-files.tsv"); } }
    static string AddFile(string name)
    {
        string path = Path.Combine(Input, name + "-Z02J-HVP.txt");
        File.WriteAllText(path, "Material\tSpecial Control Flag\n" + name + "\tHVP\n");
        return path;
    }
    static string Entry(string path, string status = "done")
    {
        var f = new FileInfo(path);
        return path + "\t" + f.CreationTimeUtc.Ticks + "\t" + f.LastWriteTimeUtc.Ticks + "\t" + f.Length + "\t0\t" + status;
    }
    static void Seed(string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(State));
        File.WriteAllText(State, text, new UTF8Encoding(false));
    }
    public static void Run(Action<string, Action> test, Action<bool, string> assert)
    {
        test("row-count compaction preserves the last committed state and bounds duplicate history", () => {
            string path = AddFile("M1");
            Seed(string.Concat(Enumerable.Repeat(Entry(path, "pending") + "\n", 999)));
            var store = new HvpStateStore(); store.Load(State);
            store.Files[path].Attempts = 3; store.Files[path].Status = "skipped";
            store.SaveChanges(new[] { new KeyValuePair<string, HvpFileState>(path, store.Files[path]) });
            assert(store.Compactions == 1 && File.ReadLines(State).Count() == 1, "Duplicate history was not compacted");
            new HvpFileProcessor().ProcessFiles();
            assert(FakeDatabase.Writes.Count == 0, "Compaction lost a persisted skip");
        });
        test("daily compaction keeps pending attempts and does not run for every following append", () => {
            string path = AddFile("M1"); Seed(Entry(path) + "\n");
            var store = new HvpStateStore(); store.Load(State);
            typeof(HvpStateStore).GetField("_lastCompactionDate", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(store, DateTime.UtcNow.Date.AddDays(-1));
            store.Files[path].Attempts = 2; store.Files[path].Status = "pending";
            var change = new[] { new KeyValuePair<string, HvpFileState>(path, store.Files[path]) };
            store.SaveChanges(change);
            assert(store.Compactions == 1 && File.ReadLines(State).Single().EndsWith("\t2\tpending"), "Daily compaction changed pending retry state");
            store.Files[path].Attempts = 3; store.Files[path].Status = "skipped"; store.SaveChanges(change);
            assert(store.Compactions == 1 && File.ReadLines(State).Count() == 2, "Checkpoint was rewritten for the next individual update");
        });
        test("failed append invalidates uncommitted cached success", () => {
            string path = AddFile("M1"); Seed(Entry(path, "pending") + "\n");
            var store = new HvpStateStore(); store.Load(State);
            store.Files[path].Status = "done"; store.Files[path].Attempts = 1;
            bool failed = false;
            using (var held = new FileStream(State, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                try { store.SaveChanges(new[] { new KeyValuePair<string, HvpFileState>(path, store.Files[path]) }); }
                catch (IOException) { failed = true; }
            }
            assert(failed && store.Files == null, "Uncommitted status remained cached after append failure");
            store.Load(State);
            assert(store.Files[path].Status == "pending" && store.Files[path].Attempts == 0, "Reload accepted an uncommitted attempt");
        });
        test("failed compaction leaves the appended result recoverable and ignores stale temp files", () => {
            string path = AddFile("M1"); Seed(string.Concat(Enumerable.Repeat(Entry(path, "pending") + "\n", 999)));
            var store = new HvpStateStore(); store.Load(State);
            store.Files[path].Status = "done"; store.Files[path].Attempts = 1;
            Directory.CreateDirectory(State + ".tmp"); bool failed = false;
            try { store.SaveChanges(new[] { new KeyValuePair<string, HvpFileState>(path, store.Files[path]) }); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { failed = true; }
            assert(failed && store.Files == null, "Failed replacement was silently accepted");
            new HvpFileProcessor().ProcessFiles();
            assert(FakeDatabase.Writes.Count == 0, "Compaction failure lost the committed success");
        });
        test("partial pending tail before a crash is rebuilt before any DB write", () => {
            string a = AddFile("A"), b = AddFile("B"), c = AddFile("C");
            File.SetCreationTimeUtc(a, DateTime.UtcNow.AddMinutes(-30));
            File.SetCreationTimeUtc(b, DateTime.UtcNow.AddMinutes(-20));
            File.SetCreationTimeUtc(c, DateTime.UtcNow.AddMinutes(-10));
            Seed(Entry(a) + "\n" + Entry(b) + "\n" + Entry(c) + "\n"
                + Entry(a, "pending") + "\n" + string.Join("\t", Entry(b, "pending").Split('\t').Take(4)));
            FakeDatabase.BeforeExec = material => {
                if (material != "A") return;
                var records = File.ReadLines(State).GroupBy(line => line.Split('\t')[0]).ToDictionary(g => g.Key, g => g.Last());
                assert(records[b].EndsWith("\t0\tpending") && records[c].EndsWith("\t0\tpending"), "Later replay work was not durable before DB writes");
            };
            new HvpFileProcessor().ProcessFiles();
            assert(FakeDatabase.Writes.SequenceEqual(new[] { "A:HVP", "B:HVP", "C:HVP" }), "Crash recovery lost the replay order");
        });
        test("atomic replacement failure preserves main checkpoint and ignores a completed temp snapshot", () => {
            string path = AddFile("M1"); Seed(string.Concat(Enumerable.Repeat(Entry(path, "pending") + "\n", 999)));
            var store = new HvpStateStore(); store.Load(State);
            store.Files[path].Status = "done"; store.Files[path].Attempts = 1; bool failed = false;
            using (var held = new FileStream(State, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                try { store.SaveChanges(new[] { new KeyValuePair<string, HvpFileState>(path, store.Files[path]) }); }
                catch (IOException) { failed = true; }
            }
            assert(failed && store.Files == null && File.Exists(State + ".tmp"), "Did not reach atomic replacement failure");
            assert(File.ReadLines(State).Count() == 1000 && File.ReadLines(State + ".tmp").Count() == 1, "Main append or complete temp snapshot was lost");
            new HvpFileProcessor().ProcessFiles();
            assert(FakeDatabase.Writes.Count == 0, "Recovery replayed a committed success after replacement failed");
        });
        test("unchanged warm scan does not reopen checkpoint content", () => {
            AddFile("M1");
            var processor = new HvpFileProcessor();
            processor.ProcessFiles();
            using (var held = new FileStream(State, FileMode.Open, FileAccess.Read, FileShare.None))
                processor.ProcessFiles();
            assert(FakeDatabase.Writes.Count == 1, "Idle scan repeated DB writes");
        });
        test("large history receives bounded appended updates for a new file", () => {
            Seed(string.Concat(Enumerable.Range(0, 10000).Select(i => Path.Combine(Input, "history" + i) + "\t1\t1\t0\t1\tdone\n")));
            byte[] before = File.ReadAllBytes(State);
            string path = AddFile("M1");
            new HvpFileProcessor().ProcessFiles();
            byte[] after = File.ReadAllBytes(State);
            assert(after.Take(before.Length).SequenceEqual(before), "Entire existing history was rewritten");
            string[] changes = Encoding.UTF8.GetString(after, before.Length, after.Length - before.Length).Split('\n', StringSplitOptions.RemoveEmptyEntries);
            assert(changes.Length == 2 && changes[0].TrimEnd('\r').EndsWith("\t0\tpending") && changes[1].TrimEnd('\r').EndsWith("\t1\tdone"), "Pending and result were not saved as small durable updates");
            new HvpFileProcessor().ProcessFiles();
            assert(FakeDatabase.Writes.SequenceEqual(new[] { "M1:HVP" }), "Restart lost appended success");
        });
        test("unterminated append resembling a legacy done row cannot hide pending work", () => {
            string done = AddFile("DONE");
            string pending = AddFile("PENDING");
            string truncated = string.Join("\t", Entry(pending).Split('\t').Take(4));
            Seed(Entry(done) + "\n" + truncated);
            new HvpFileProcessor().ProcessFiles();
            assert(FakeDatabase.Writes.SequenceEqual(new[] { "PENDING:HVP" }), "Torn append was accepted as a legacy done row");
            assert(File.ReadAllText(State).EndsWith("\n"), "Torn append was not repaired before the next write");
        });
        test("complete malformed checkpoint record fails before DB writes", () => {
            AddFile("M1"); Seed("broken\n"); bool failed = false;
            try { new HvpFileProcessor().ProcessFiles(); } catch (InvalidDataException) { failed = true; }
            assert(failed && FakeDatabase.Writes.Count == 0, "Complete corruption was silently discarded");
        });
        test("external checkpoint change invalidates a warm cache", () => {
            AddFile("M1"); var processor = new HvpFileProcessor(); processor.ProcessFiles();
            File.WriteAllText(State, "broken\n"); bool failed = false;
            try { processor.ProcessFiles(); } catch (InvalidDataException) { failed = true; }
            assert(failed && FakeDatabase.Writes.Count == 1, "Warm cache concealed a changed checkpoint");
        });
        test("scan summary reports full duration state load and number sorted", () => {
            AddFile("M1"); var processor = new HvpFileProcessor(); processor.ProcessFiles(); processor.ProcessFiles();
            string log = File.ReadAllText(Directory.GetFiles(Path.GetDirectoryName(State), "hvp-*.log").Single());
            string last = log.Split('\n').Last(x => x.Contains("SCAN END"));
            assert(last.Contains("elapsedMs=") && last.Contains("stateCount=1") && last.Contains("stateReloaded=False")
                && last.Contains("stateBytesWritten=0") && last.Contains("sortedFiles=0"), "Missing idle scan performance evidence: " + last);
        });
    }
}
