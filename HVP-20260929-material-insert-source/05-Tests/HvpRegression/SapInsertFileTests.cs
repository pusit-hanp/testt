using System;
using System.Configuration;
using System.IO;
using System.Linq;
using HvpRegressionSupport;
using Z02JHVPService;

static class SapInsertFileTests
{
    // Reordered headers, two Issue St Loc columns, and several deliberately unmapped fields.
    const string Header = "Special Control Flag\tCustomer PN\tPlant\tMaterial\tMaterial Type\tMaterial Desc.\tMaterial Group\tBase unit of measure\tBasic Material\tPurchasing group\tDefault Storage Location\tMaximum Stock\tIssue St Loc\tMin. Safety Stock\tBOM Usage\tIssue St Loc\tReceive St Loc\tStorage Location\tStorage Bin\tOld material number\tStd Price per Unit";

    static string Row(string material, string type, string plant, string defaultLocation, string issueLocation, string flag = " hvp ")
    {
        return string.Join("\t", new[] { flag, "CUSTOMER-01", plant, material, type, "Description, with spaces", "OC-FNPI", "EA", "AB", "ZZZ",
            defaultLocation, "0", issueLocation, "0", "1", "BAD2", "BAD3", "BAD4", "IGNORED-BIN", "WRONG-OLD-NUMBER", "12345.12345" });
    }

    static string WriteFile(string text)
    {
        string path = Path.Combine(ConfigurationManager.AppSettings["HvpFilePath"], "sap-Z02J-HVP.txt");
        File.WriteAllText(path, text);
        return path;
    }

    public static void Run(Action<string, Action> test, Action<bool, string> assert)
    {
        foreach (string type in new[] { "ROH", "HALB", "FERT" })
        test("SAP insert maps approved fields and correct location for " + type, () => {
            FakeDatabase.MissingMaterials.Add("NEW1");
            WriteFile(Header + "\n" + Row("NEW1", type, " ITH1 ", "RECA", "WIP2") + "\n");
            new HvpFileProcessor().ProcessFiles();
            assert(FakeDatabase.Inserted.Count == 1, "New material was not inserted");
            var inserted = FakeDatabase.Inserted.Single();
            assert((string)inserted["material"] == "NEW1" && (string)inserted["plant"] == "ITH1", "Material/Plant mapping failed");
            assert((string)inserted["storageLocation"] == (type == "ROH" ? "RECA" : "WIP2"), "Wrong SLOC source column");
            assert((string)inserted["materialType"] == type, "Material Type mapping failed");
            assert((string)inserted["oldMaterialNo"] == "CUSTOMER-01", "Customer PN must supply OLD_MATERIAL_NO");
            assert((string)inserted["materialDescription"] == "Description, with spaces", "Description was split/lost");
            assert((string)inserted["materialGroup"] == "OC-FNPI" && (string)inserted["baseUnit"] == "EA", "Group/unit mapping failed");
            assert((string)inserted["basicMaterial"] == "AB" && (string)inserted["purchasingGroup"] == "ZZZ", "Basic material/PGR mapping failed");
            assert((string)inserted["specialControlFlag"] == "HVP", "Flag was not normalized");
        });

        foreach (string type in new[] { "ROH", "HALB", "FERT" })
        test("blank approved SLOC defaults to STR9 for " + type, () => {
            FakeDatabase.MissingMaterials.Add("NEW1");
            WriteFile(Header + "\n" + Row("NEW1", type, "ITH1", " ", " ", "OTHER") + "\n");
            new HvpFileProcessor().ProcessFiles();
            assert(FakeDatabase.Inserted.Count == 1, "Blank SLOC blocked a valid insert");
            var inserted = FakeDatabase.Inserted.Single();
            assert((string)inserted["storageLocation"] == "STR9", "Used an unrelated Location column instead of STR9");
            assert(inserted["specialControlFlag"] == null || (string)inserted["specialControlFlag"] == "", "Non-HVP flag was stored");
        });

        test("missing location header uses STR9 with normalized header names and material type", () => {
            FakeDatabase.MissingMaterials.Add("NEW1");
            WriteFile(" material \t special control flag \t plant \t MATERIAL TYPE \nNEW1\tHVP\tITH1\t halb \n");
            new HvpFileProcessor().ProcessFiles();
            assert(FakeDatabase.Inserted.Count == 1 && (string)FakeDatabase.Inserted[0]["storageLocation"] == "STR9", "Missing location header not handled");
        });

        foreach (string type in new[] { "HALB", "FERT" })
        test("missing first Issue St Loc never borrows the BOM location for " + type, () => {
            FakeDatabase.MissingMaterials.Add("NEW1");
            string header = Header.Replace("Maximum Stock\tIssue St Loc\tMin. Safety Stock", "Maximum Stock\tUnavailable Location\tMin. Safety Stock");
            WriteFile(header + "\n" + Row("NEW1", type, "ITH1", "RECA", "WIP2") + "\n");
            new HvpFileProcessor().ProcessFiles();
            assert(FakeDatabase.Inserted.Count == 1 && (string)FakeDatabase.Inserted[0]["storageLocation"] == "STR9", "BOM location was used when the approved location header was missing");
        });

        test("duplicate new material keeps first inserted fields and applies the last flag", () => {
            FakeDatabase.MissingMaterials.Add("NEW1");
            WriteFile(Header + "\n" + Row("NEW1", "ROH", "ITH1", "RECA", "") + "\n"
                + Row("NEW1", "FERT", "ITH2", "", "WIP2", "OTHER") + "\n");
            new HvpFileProcessor().ProcessFiles();
            assert(FakeDatabase.Inserted.Count == 1, "Repeated Material inserted an extra Plant/Location");
            var inserted = FakeDatabase.Inserted.Single();
            assert((string)inserted["plant"] == "ITH1" && (string)inserted["storageLocation"] == "RECA" && (string)inserted["materialType"] == "ROH", "Later row replaced the first inserted fields");
            assert(FakeDatabase.Writes.SequenceEqual(new[] { "NEW1:HVP", "NEW1:" }), "Last row did not determine the flag");
        });

        test("insert validation logs the missing field without logging database secrets", () => {
            FakeDatabase.MissingMaterials.Add("NEW1");
            ConfigurationManager.ConnectionStrings["Material"].ConnectionString = "Password=never-log-this";
            WriteFile(Header + "\n" + Row("NEW1", "ROH", "", "RECA", "") + "\n");
            new HvpFileProcessor().ProcessFiles();
            string log = string.Join("\n", Directory.GetFiles(ConfigurationManager.AppSettings["HvpStatePath"], "hvp-*.log").Select(File.ReadAllText));
            assert(log.Contains("Plant is required."), "Log did not identify the missing insert field");
            assert(!log.Contains("never-log-this"), "Log exposed a database secret");
        });

        test("a truncated mapped field never partially updates existing materials", () => {
            WriteFile("Material\tSpecial Control Flag\tPlant\nM1\tHVP\tITH1\nM2\tHVP\n");
            new HvpFileProcessor().ProcessFiles();
            assert(FakeDatabase.Writes.Count == 0, "Truncated field was treated as a blank value");
        });

        test("missing Plant for a new material rolls back the file and skips after three attempts", () => {
            FakeDatabase.MissingMaterials.Add("NEW1");
            string path = WriteFile(Header + "\n" + Row("EXISTING", "ROH", "ITH1", "RECA", "") + "\n"
                + Row("NEW1", "ROH", " ", "RECA", "") + "\n");
            byte[] before = File.ReadAllBytes(path);
            var processor = new HvpFileProcessor();
            processor.ProcessFiles(); processor.ProcessFiles(); processor.ProcessFiles(); processor.ProcessFiles();
            assert(FakeDatabase.Writes.Count == 0 && FakeDatabase.Inserted.Count == 0, "Failed file was partly committed");
            string state = Path.Combine(ConfigurationManager.AppSettings["HvpStatePath"], "processed-files.tsv");
            assert(File.ReadLines(state).Last().EndsWith("\t3\tskipped"), "Validation failure retry policy changed");
            assert(File.ReadAllBytes(path).SequenceEqual(before), "Source file was changed/deleted");
        });

        test("blank Material rejects the whole file before DB work", () => {
            WriteFile(Header + "\n" + Row("M1", "ROH", "ITH1", "RECA", "") + "\n"
                + Row(" ", "HALB", "ITH1", "", "WIP2") + "\n");
            new HvpFileProcessor().ProcessFiles();
            assert(FakeDatabase.Commands.Count == 0, "Blank Material reached the database");
        });
    }
}
