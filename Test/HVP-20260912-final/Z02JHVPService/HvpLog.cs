using System;
using System.Configuration;
using System.IO;
using System.Text;

namespace Z02JHVPService
{
    internal static class HvpLog
    {
        private static readonly object Sync = new object();

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
            string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + message;
            Console.WriteLine(line);
            lock (Sync)
            {
                File.AppendAllText(Path.Combine(GetStateDirectory(), "hvp-" + DateTime.Now.ToString("yyyyMMdd") + ".log"),
                    line + Environment.NewLine, Encoding.UTF8);
            }
        }
    }
}
