using System;
using System.Configuration;
using System.IO;
using System.Linq;
using HvpRegressionSupport;
using Z02JHVPService;

static class FileSelectionTests
{
    public static void Run(Action<string, Action> test, Action<bool, string> assert)
    {
        test("configured suffix selects mixed-case filenames and excludes the old suffix", () => {
            ConfigurationManager.AppSettings["HvpFileSuffix"] = " custom.txt ";
            string folder = ConfigurationManager.AppSettings["HvpFilePath"];
            File.WriteAllText(Path.Combine(folder, "sap-CuStOm.TXT"), "Material\tSpecial Control Flag\nCUSTOM\thvp\n");
            File.WriteAllText(Path.Combine(folder, "old-Z02J-HVP.txt"), "Material\tSpecial Control Flag\nOLD\tHVP\n");
            new HvpFileProcessor().ProcessFiles();
            assert(FakeDatabase.Writes.SequenceEqual(new[] { "CUSTOM:HVP" }), "Configured suffix ignored or case-sensitive");
        });
        foreach (string suffix in new[] { "", "   ", "*", "*.txt", "../file.txt" })
        test("invalid suffix cannot select arbitrary source files: " + suffix, () => {
            ConfigurationManager.AppSettings["HvpFileSuffix"] = suffix;
            string folder = ConfigurationManager.AppSettings["HvpFilePath"];
            File.WriteAllText(Path.Combine(folder, "one-Z02J-HVP.txt"), "Material\tSpecial Control Flag\nM1\tHVP\n");
            bool threw = false;
            try { new HvpFileProcessor().ProcessFiles(); } catch (InvalidOperationException) { threw = true; }
            assert(threw && FakeDatabase.Writes.Count == 0, "Invalid suffix was accepted");
        });
    }
}
