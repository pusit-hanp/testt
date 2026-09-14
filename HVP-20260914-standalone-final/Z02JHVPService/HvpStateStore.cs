using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Z02JHVPService
{
    internal sealed class HvpFileState
    {
        public string Version;
        public int Attempts;
        public string Status;
    }

    // Call only while holding the existing cross-process job.lock.
    internal sealed class HvpStateStore
    {
        private static readonly Encoding Utf8 = new UTF8Encoding(false, true);
        private string _path;
        private long _length = -1;
        private DateTime _creationUtc;
        private DateTime _lastWriteUtc;
        private long _physicalRows;
        private DateTime _lastCompactionDate;

        public Dictionary<string, HvpFileState> Files { get; private set; }
        public bool Reloaded { get; private set; }
        public long BytesWritten { get; private set; }
        public int Compactions { get; private set; }

        public void Load(string path)
        {
            Reloaded = false;
            BytesWritten = 0;
            Compactions = 0;
            try
            {
                string fullPath = Path.GetFullPath(path);
                var info = new FileInfo(fullPath);
                info.Refresh();
                if (Files != null && string.Equals(_path, fullPath, StringComparison.OrdinalIgnoreCase)
                    && Matches(info)) return;

                var files = new Dictionary<string, HvpFileState>(StringComparer.OrdinalIgnoreCase);
                long physicalRows = 0;
                long discardedBytes = 0;
                if (info.Exists)
                {
                    discardedBytes = RecoverUnterminatedTail(fullPath);
                    foreach (string line in File.ReadLines(fullPath, Utf8))
                    {
                        string[] parts = line.Split('\t');
                        long creation, modified, length;
                        if ((parts.Length != 4 && parts.Length != 6)
                            || !long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out creation)
                            || !long.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out modified)
                            || !long.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out length))
                            throw new InvalidDataException("Invalid HVP checkpoint row.");

                        int attempts = 0;
                        string status = "done"; // Existing four-column checkpoints mean success.
                        if (parts.Length == 6)
                        {
                            status = parts[5];
                            if (!int.TryParse(parts[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out attempts)
                                || attempts < 0 || (status != "done" && status != "pending" && status != "skipped"))
                                throw new InvalidDataException("Invalid HVP checkpoint row.");
                        }
                        // Updates are appended in commit order; the latest complete row wins.
                        files[parts[0]] = new HvpFileState
                        {
                            Version = parts[1] + "\t" + parts[2] + "\t" + parts[3],
                            Attempts = attempts,
                            Status = status
                        };
                        physicalRows++;
                    }
                }

                _path = fullPath;
                Files = files;
                _physicalRows = physicalRows;
                _lastCompactionDate = DateTime.UtcNow.Date;
                RememberSignature();
                Reloaded = true;
                if (discardedBytes > 0)
                    HvpLog.Write("STATE RECOVERED discardedUnterminatedBytes="
                        + discardedBytes.ToString(CultureInfo.InvariantCulture));
            }
            catch
            {
                Invalidate();
                throw;
            }
        }

        public void SaveChanges(IEnumerable<KeyValuePair<string, HvpFileState>> changes)
        {
            try
            {
                if (Files == null || _path == null)
                    throw new InvalidOperationException("Load the HVP checkpoint before saving changes.");
                using (var iterator = changes.GetEnumerator())
                {
                    if (!iterator.MoveNext()) return;
                    long appendedRows = 0;
                    using (var stream = new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.Read))
                    {
                        long originalLength = stream.Length;
                        using (var writer = new StreamWriter(stream, Utf8, 4096, true))
                        {
                            do
                            {
                                WriteRow(writer, iterator.Current);
                                appendedRows++;
                            }
                            while (iterator.MoveNext());
                            writer.Flush();
                            // The complete pending tail must be durable before the first DB command.
                            stream.Flush(true);
                            BytesWritten += stream.Length - originalLength;
                        }
                    }
                    _physicalRows += appendedRows;
                }

                DateTime today = DateTime.UtcNow.Date;
                if (_physicalRows >= Math.Max(1000L, 2L * Files.Count)
                    || (today > _lastCompactionDate && _physicalRows > Files.Count))
                    Compact(today);
                RememberSignature();
            }
            catch
            {
                // Callers mutate FileState before saving. Never reuse uncommitted in-memory state.
                Invalidate();
                throw;
            }
        }

        public void Invalidate()
        {
            Files = null;
            _path = null;
            _length = -1;
            _creationUtc = default(DateTime);
            _lastWriteUtc = default(DateTime);
            _physicalRows = 0;
            _lastCompactionDate = default(DateTime);
        }

        private bool Matches(FileInfo info)
        {
            return info.Exists
                ? _length == info.Length && _creationUtc == info.CreationTimeUtc && _lastWriteUtc == info.LastWriteTimeUtc
                : _length == -1;
        }

        private void RememberSignature()
        {
            var info = new FileInfo(_path);
            info.Refresh();
            _length = info.Exists ? info.Length : -1;
            _creationUtc = info.Exists ? info.CreationTimeUtc : default(DateTime);
            _lastWriteUtc = info.Exists ? info.LastWriteTimeUtc : default(DateTime);
        }

        private static void WriteRow(StreamWriter writer, KeyValuePair<string, HvpFileState> file)
        {
            writer.WriteLine(file.Key + "\t" + file.Value.Version + "\t"
                + file.Value.Attempts.ToString(CultureInfo.InvariantCulture) + "\t" + file.Value.Status);
        }

        private void Compact(DateTime today)
        {
            string temporaryPath = _path + ".tmp";
            using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream, Utf8, 4096, true))
            {
                foreach (var file in Files) WriteRow(writer, file);
                writer.Flush();
                stream.Flush(true);
                BytesWritten += stream.Length;
            }
            // The old checkpoint stays intact until its complete replacement is ready.
            File.Replace(temporaryPath, _path, null);
            _physicalRows = Files.Count;
            _lastCompactionDate = today;
            Compactions++;
        }

        private static long RecoverUnterminatedTail(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read))
            {
                long originalLength = stream.Length;
                if (originalLength == 0) return 0;
                stream.Position = originalLength - 1;
                if (stream.ReadByte() == '\n') return 0;

                var buffer = new byte[4096];
                long end = originalLength;
                long committedLength = 0;
                bool found = false;
                while (end > 0 && !found)
                {
                    long start = Math.Max(0, end - buffer.Length);
                    int count = (int)(end - start);
                    stream.Position = start;
                    int read = 0;
                    while (read < count)
                    {
                        int n = stream.Read(buffer, read, count - read);
                        if (n == 0) throw new EndOfStreamException("Cannot read the HVP checkpoint tail.");
                        read += n;
                    }
                    for (int i = count - 1; i >= 0; i--)
                    {
                        if (buffer[i] != '\n') continue;
                        committedLength = start + i + 1;
                        found = true;
                        break;
                    }
                    end = start;
                }
                // A torn six-column update can look like a valid legacy four-column success.
                // Only newline-terminated records are committed, including during recovery.
                stream.SetLength(committedLength);
                stream.Flush(true);
                return originalLength - committedLength;
            }
        }
    }
}
