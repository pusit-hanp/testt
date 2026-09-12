using System;
using System.Collections.Generic;
using System.Configuration;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Z02JHVPService
{
    public class HvpFileProcessor
    {
        private class FileState
        {
            public string Version;
            public int Attempts;
            public string Status;
        }

        public void ProcessFiles()
        {
            // Also exclude a console debug process while the Windows Service scans.
            using (var jobLock = new FileStream(Path.Combine(HvpLog.GetStateDirectory(), "job.lock"),
                FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
            {
                ProcessFilesCore();
            }
        }

        private void ProcessFilesCore()
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
            var processedFiles = LoadProcessedFiles(statePath);
            int maxAttempts;
            if (!int.TryParse(ConfigurationManager.AppSettings["MaxFileAttempts"], out maxAttempts) || maxAttempts <= 0)
                maxAttempts = 3;
            var files = new DirectoryInfo(folderPath).GetFiles("*Z02J-HVP.txt")
                .Where(f => f.Name.EndsWith("Z02J-HVP.txt", StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f.CreationTimeUtc)
                .ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            int completed = 0;
            HvpLog.Write("SCAN files=" + files.Count + " folder=" + folderPath);
            int firstPending = files.FindIndex(file =>
            {
                FileState previous;
                return !processedFiles.TryGetValue(file.FullName, out previous) || previous.Version != GetVersion(file)
                    || previous.Status == "pending";
            });
            if (firstPending < 0) { HvpLog.Write("SCAN END completedFiles=0"); return; }

            // A changed older file must not leave an already-completed newer value overwritten.
            // Persist the pending tail BEFORE writing DB, so a failed replay survives restart.
            foreach (var file in files.Skip(firstPending))
            {
                string version = GetVersion(file);
                FileState previous;
                // Preserve failures and skips for the SAME file version, including during tail replay.
                if (!processedFiles.TryGetValue(file.FullName, out previous) || previous.Version != version || previous.Status == "done")
                    processedFiles[file.FullName] = new FileState { Version = version, Attempts = 0, Status = "pending" };
            }
            SaveProcessedFiles(statePath, processedFiles);
            foreach (var file in files.Skip(firstPending))
            {
                FileState state = processedFiles[file.FullName];
                if (state.Status == "skipped") continue;
                if (state.Attempts >= maxAttempts)
                {
                    state.Status = "skipped";
                    SaveProcessedFiles(statePath, processedFiles);
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
                SaveProcessedFiles(statePath, processedFiles);
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
            HvpLog.Write("SCAN END completedFiles=" + completed);
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

        private static Dictionary<string, FileState> LoadProcessedFiles(string path)
        {
            var result = new Dictionary<string, FileState>(StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(path)) return result;
            foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
            {
                string[] parts = line.Split('\t');
                long creation, modified, length;
                if ((parts.Length != 4 && parts.Length != 6) || !long.TryParse(parts[1], out creation)
                    || !long.TryParse(parts[2], out modified) || !long.TryParse(parts[3], out length))
                    throw new InvalidDataException("Invalid checkpoint: " + path);
                int attempts = 0;
                string status = "done"; // Existing four-column checkpoints mean success.
                if (parts.Length == 6)
                {
                    status = parts[5];
                    if (!int.TryParse(parts[4], out attempts) || attempts < 0
                        || (status != "done" && status != "pending" && status != "skipped"))
                        throw new InvalidDataException("Invalid checkpoint: " + path);
                }
                result[parts[0]] = new FileState { Version = string.Join("\t", parts.Skip(1).Take(3)), Attempts = attempts, Status = status };
            }
            return result;
        }

        private static void SaveProcessedFiles(string path, Dictionary<string, FileState> files)
        {
            string temporaryPath = path + ".tmp";
            File.WriteAllLines(temporaryPath, files.Select(f => f.Key + "\t" + f.Value.Version + "\t"
                + f.Value.Attempts.ToString(CultureInfo.InvariantCulture) + "\t" + f.Value.Status), Encoding.UTF8);
            // Only the service checkpoint is replaced. SAP input files remain untouched.
            if (File.Exists(path)) File.Replace(temporaryPath, path, null);
            else File.Move(temporaryPath, path);
        }
    }
}
