using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Z02JHVPService
{
    public class HvpFileProcessor
    {
        private readonly HvpStateStore _state = new HvpStateStore();

        public void ProcessFiles()
        {
            // Also exclude a console debug process while the Windows Service scans.
            using (var jobLock = new FileStream(Path.Combine(HvpLog.GetStateDirectory(), "job.lock"),
                FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
            {
                try { ProcessFilesCore(); }
                catch { _state.Invalidate(); throw; }
            }
        }

        private void ProcessFilesCore()
        {
            var timer = Stopwatch.StartNew();
            int completed = 0, totalFiles = 0, sortedFiles = 0;
            long stateLoadMs = 0, enumerateMs = 0;
            bool loaded = false;
            try
            {
                string folderPath = ConfigurationManager.AppSettings["HvpFilePath"];
                if (string.IsNullOrWhiteSpace(folderPath))
                    throw new InvalidOperationException("Set HvpFilePath in Z02JHVPService.exe.config.");
                if (!Directory.Exists(folderPath))
                {
                    HvpLog.Write("SOURCE UNAVAILABLE folder=" + folderPath);
                    throw new DirectoryNotFoundException("Cannot access HvpFilePath: " + folderPath);
                }

                string statePath = Path.Combine(HvpLog.GetStateDirectory(), "processed-files.tsv");
                long phaseStart = timer.ElapsedMilliseconds;
                _state.Load(statePath);
                loaded = true;
                stateLoadMs = timer.ElapsedMilliseconds - phaseStart;
                var processedFiles = _state.Files;
                int maxAttempts;
                if (!int.TryParse(ConfigurationManager.AppSettings["MaxFileAttempts"], out maxAttempts) || maxAttempts <= 0)
                    maxAttempts = 3;
                phaseStart = timer.ElapsedMilliseconds;
                var files = new DirectoryInfo(folderPath).EnumerateFiles("*Z02J-HVP.txt")
                    .Where(f => f.Name.EndsWith("Z02J-HVP.txt", StringComparison.OrdinalIgnoreCase))
                    .ToList();
                totalFiles = files.Count;
                enumerateMs = timer.ElapsedMilliseconds - phaseStart;

                HvpLog.Write("SCAN files=" + files.Count + " folder=" + folderPath);
                FileInfo firstPending = null;
                foreach (var file in files)
                {
                    HvpFileState previous;
                    bool pending = !processedFiles.TryGetValue(file.FullName, out previous) || previous.Version != GetVersion(file)
                        || previous.Status == "pending";
                    if (pending && (firstPending == null || CompareFiles(file, firstPending) < 0)) firstPending = file;
                }
                if (firstPending == null) return;
                // Checking metadata still covers old replacements; only the replay tail needs sorting.
                var queue = files.Where(file => CompareFiles(file, firstPending) >= 0)
                    .OrderBy(file => file.CreationTimeUtc)
                    .ThenBy(file => file.Name, StringComparer.OrdinalIgnoreCase).ToList();
                sortedFiles = queue.Count;

                // A changed older file must not leave an already-completed newer value overwritten.
                // Persist the pending tail BEFORE writing DB, so a failed replay survives restart.
                var changes = new List<KeyValuePair<string, HvpFileState>>();
                foreach (var file in queue)
                {
                    string version = GetVersion(file);
                    HvpFileState previous;
                    // Preserve failures and skips for the SAME file version, including during tail replay.
                    if (!processedFiles.TryGetValue(file.FullName, out previous) || previous.Version != version || previous.Status == "done")
                    {
                        var pendingState = new HvpFileState { Version = version, Attempts = 0, Status = "pending" };
                        processedFiles[file.FullName] = pendingState;
                        changes.Add(new KeyValuePair<string, HvpFileState>(file.FullName, pendingState));
                    }
                }
                _state.SaveChanges(changes);
                foreach (var file in queue)
                {
                    HvpFileState state = processedFiles[file.FullName];
                    if (state.Status == "skipped") continue;
                    if (state.Attempts >= maxAttempts)
                    {
                        state.Status = "skipped";
                        _state.SaveChanges(new[] { new KeyValuePair<string, HvpFileState>(file.FullName, state) });
                        HvpLog.Write("SKIP file=" + file.Name + " attempts=" + state.Attempts + "/" + maxAttempts);
                        continue;
                    }
                    Exception failure = null;
                    try
                    {
                        file.Refresh();
                        string version = GetVersion(file);
                        if (version != state.Version)
                        {
                            // Keep the queued version pending; the next scan must sort the new metadata.
                            HvpLog.Write("DEFER file=" + file.Name + "; changed since scan; reorder next scan.");
                            break;
                        }
                        HvpLog.Write("START file=" + file.Name + " creationUtc=" + file.CreationTimeUtc.ToString("O")
                            + " lastWriteUtc=" + file.LastWriteTimeUtc.ToString("O") + " attempt=" + (state.Attempts + 1) + "/" + maxAttempts);
                        ProcessSingleFile(file);

                        // Do not record a file that was replaced while this job ran.
                        file.Refresh();
                        if (!file.Exists || GetVersion(file) != version)
                            throw new IOException("File changed during processing; retry next scan.");

                    }
                    catch (Exception ex)
                    {
                        failure = ex;
                    }
                    state.Attempts++;
                    state.Status = failure == null ? "done" : state.Attempts >= maxAttempts ? "skipped" : "pending";
                    // State-write errors must stop this scan; they are not failed input-file attempts.
                    _state.SaveChanges(new[] { new KeyValuePair<string, HvpFileState>(file.FullName, state) });
                    if (failure == null)
                    {
                        completed++;
                        HvpLog.Write("DONE file=" + file.Name);
                        continue;
                    }
                    HvpLog.Write("FAILED file=" + file.Name + " attempt=" + state.Attempts + "/" + maxAttempts
                        + " type=" + failure.GetType().FullName + " hresult=" + failure.HResult);
                    if (failure is InvalidDataException) HvpLog.Write(failure.Message);
                    if (state.Status == "skipped")
                    {
                        HvpLog.Write("SKIP file=" + file.Name + "; attempt limit reached; input unchanged.");
                        continue;
                    }
                    // Below the limit, retry in the next timer scan before advancing.
                    break;
                }
            }
            finally
            {
                HvpLog.Write("SCAN END completedFiles=" + completed + " elapsedMs=" + timer.ElapsedMilliseconds
                    + " files=" + totalFiles + " stateCount=" + (loaded && _state.Files != null ? _state.Files.Count : 0)
                    + " stateReloaded=" + (loaded && _state.Reloaded) + " stateLoadMs=" + stateLoadMs
                    + " enumerateMs=" + enumerateMs + " sortedFiles=" + sortedFiles
                    + " stateBytesWritten=" + (loaded ? _state.BytesWritten : 0)
                    + " stateCompactions=" + (loaded ? _state.Compactions : 0));
            }
        }

        private static int CompareFiles(FileInfo left, FileInfo right)
        {
            int order = left.CreationTimeUtc.CompareTo(right.CreationTimeUtc);
            return order != 0 ? order : StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name);
        }

        private void ProcessSingleFile(FileInfo file)
        {
            var records = new List<HvpRecord>();
            // Read-only sharing prevents reading a file held open for writing.
            using (var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var reader = new StreamReader(stream, Encoding.UTF8, true))
            {
                string headerLine = reader.ReadLine();
                if (string.IsNullOrWhiteSpace(headerLine))
                    throw new InvalidDataException("Empty header.");

                string[] headers = headerLine.Split('\t');
                int matIndex = Array.FindIndex(headers, h => string.Equals(h.Trim(), "Material", StringComparison.OrdinalIgnoreCase));
                int flagIndex = Array.FindIndex(headers, h => string.Equals(h.Trim(), "Special Control Flag", StringComparison.OrdinalIgnoreCase));
                if (matIndex < 0 || flagIndex < 0)
                {
                    HvpLog.Write("INVALID HEADER file=" + file.Name + "; required: Material and Special Control Flag, tab-separated.");
                    throw new InvalidDataException("Required SAP columns not found.");
                }

                int lineNumber = 1;
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    lineNumber++;
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    string[] cols = line.Split('\t');
                    if (cols.Length <= Math.Max(matIndex, flagIndex) || string.IsNullOrWhiteSpace(cols[matIndex]))
                    {
                        HvpLog.Write("INVALID ROW file=" + file.Name + " line=" + lineNumber);
                        throw new InvalidDataException("Incomplete SAP row; no rows from this file have been applied.");
                    }
                    records.Add(new HvpRecord
                    {
                        Material = cols[matIndex].Trim(),
                        SpecialControlFlag = string.Equals(cols[flagIndex].Trim(), "HVP", StringComparison.OrdinalIgnoreCase) ? "HVP" : ""
                    });
                }
            }

            if (records.Count == 0)
                throw new InvalidDataException("No data rows; retry on next scan.");
            // Create here so a temporary connection failure does not prevent the timer starting.
            var service = new HvpService();
            int processedRecords = service.UpdateSpecialControlFlags(records);
            HvpLog.Write("DB COMMANDS completedRecords=" + processedRecords + " file=" + file.Name
                + "; this is not an affected-row count.");
        }

        private static string GetVersion(FileInfo file)
        {
            return file.CreationTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture) + "\t"
                + file.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture) + "\t"
                + file.Length.ToString(CultureInfo.InvariantCulture);
        }

    }
}
