using System;
using System.Configuration;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Oracle.ManagedDataAccess.Client;
using HvpRegressionSupport;
using Z02JHVPService;

static class StandaloneDatabaseTests
{
    static List<HvpRecord> Records()
    {
        return new List<HvpRecord> {
            new HvpRecord { Material = "M1", SpecialControlFlag = "HVP" },
            new HvpRecord { Material = "M2", SpecialControlFlag = null }
        };
    }

    static HvpRecord NewRecord()
    {
        return new HvpRecord {
            Material = "NEW1", SpecialControlFlag = " hvp ", Plant = " ITH1 ",
            StorageLocation = "RECA", MaterialType = "ROH", OldMaterialNo = "CUSTOMER1",
            MaterialDescription = "Example material", MaterialGroup = "OC-NPI", BaseUnit = "EA",
            BasicMaterial = "AB", PurchasingGroup = "K08"
        };
    }

    // Inspect the SQL sent to Oracle, not production source text. This catches wrong column/value alignment.
    static Dictionary<string, string> InsertExpressions(string sql)
    {
        int columnsStart = sql.IndexOf('('), columnsEnd = sql.IndexOf(')', columnsStart);
        int valuesStart = sql.IndexOf('(', columnsEnd), valuesEnd = sql.IndexOf(')', valuesStart);
        var columns = sql.Substring(columnsStart + 1, columnsEnd - columnsStart - 1).Split(',');
        var values = sql.Substring(valuesStart + 1, valuesEnd - valuesStart - 1).Split(',');
        if (columns.Length != values.Length) throw new Exception("INSERT column/value counts differ");
        return columns.Select((column, index) => new { Column = column.Trim().ToUpperInvariant(), Value = values[index].Trim() })
            .ToDictionary(pair => pair.Column, pair => pair.Value);
    }

    public static void Run(Action<string, Action> test, Action<bool, string> assert)
    {
        test("direct Material connection opens once and disposes after all records", () => {
            int count = new HvpService().UpdateSpecialControlFlags(Records());
            assert(OracleConnection.LastConnectionString == "TEST-DIRECT-MATERIAL", "Material connectionStrings was not used");
            assert(OracleConnection.OpenCalls == 1 && OracleConnection.DisposeCalls == 1, "Connection lifecycle is incorrect");
            assert(count == 2 && FakeDatabase.Writes.SequenceEqual(new[] { "M1:HVP", "M2:" }), "Record count or writes changed");
        });
        test("missing Material config is rejected without opening Oracle", () => {
            ConfigurationManager.ConnectionStrings["Material"] = null;
            bool threw = false;
            try { new HvpService(); } catch (InvalidOperationException) { threw = true; }
            assert(threw && OracleConnection.OpenCalls == 0, "Missing connection was accepted");
        });
        test("blank Material config is rejected", () => {
            ConfigurationManager.ConnectionStrings["Material"].ConnectionString = "  ";
            bool threw = false;
            try { new HvpService(); } catch (InvalidOperationException) { threw = true; }
            assert(threw, "Blank connection was accepted");
        });
        test("empty work does not open an Oracle connection", () => {
            var service = new HvpService();
            assert(service.UpdateSpecialControlFlags(null) == 0 && service.UpdateSpecialControlFlags(new List<HvpRecord>()) == 0, "Empty work result changed");
            assert(OracleConnection.OpenCalls == 0 && OracleConnection.DisposeCalls == 0, "Empty work opened a connection");
        });
        test("Oracle open failure propagates and disposes without writes", () => {
            OracleConnection.FailOpen = true;
            bool threw = false;
            try { new HvpService().UpdateSpecialControlFlags(Records()); } catch (InvalidOperationException) { threw = true; }
            assert(threw && OracleConnection.DisposeCalls == 1 && FakeDatabase.Writes.Count == 0, "Open failure swallowed or connection leaked");
        });
        test("Oracle command failure stops remaining records and disposes", () => {
            FakeDatabase.FailMaterial = "M1";
            bool threw = false;
            try { new HvpService().UpdateSpecialControlFlags(Records()); } catch (InvalidOperationException) { threw = true; }
            assert(threw && OracleConnection.DisposeCalls == 1 && FakeDatabase.Writes.Count == 0, "Command failure swallowed or connection leaked");
        });
        test("material value remains a parameter and is not inserted into SQL", () => {
            const string material = "M1' OR '1'='1";
            new HvpService().UpdateSpecialControlFlags(new List<HvpRecord> { new HvpRecord { Material = material, SpecialControlFlag = "HVP" } });
            assert(!FakeDatabase.LastSql.Contains(material) && FakeDatabase.Writes.Single() == material + ":HVP", "Material became SQL text");
        });
        test("Oracle open failures retain existing three attempt skip policy", () => {
            string input = Path.Combine(ConfigurationManager.AppSettings["HvpFilePath"], "one-Z02J-HVP.txt");
            File.WriteAllText(input, "Material\tSpecial Control Flag\nM1\tHVP\n");
            var processor = new HvpFileProcessor(); OracleConnection.FailOpen = true;
            processor.ProcessFiles(); processor.ProcessFiles(); processor.ProcessFiles();
            OracleConnection.FailOpen = false; processor.ProcessFiles();
            string state = Path.Combine(ConfigurationManager.AppSettings["HvpStatePath"], "processed-files.tsv");
            assert(OracleConnection.OpenCalls == 3 && File.ReadLines(state).Last().EndsWith("\t3\tskipped"), "DB connection failure retry policy changed");
            assert(FakeDatabase.Writes.Count == 0, "Skipped file was applied");
        });
        test("one file commits all database commands in one transaction", () => {
            new HvpService().UpdateSpecialControlFlags(Records());
            assert(OracleConnection.BeginTransactionCalls == 1 && OracleTransaction.CommitCalls == 1,
                "File commands were not committed together");
            assert(FakeDatabase.Commands.All(c => c.InTransaction) && OracleTransaction.DisposeCalls == 1,
                "A command escaped the transaction or the transaction leaked");
        });
        test("a new material without Plant errors instead of claiming success", () => {
            FakeDatabase.MissingMaterials.Add("M1");
            bool threw = false;
            try { new HvpService().UpdateSpecialControlFlags(new List<HvpRecord> { Records()[0] }); }
            catch (InvalidDataException) { threw = true; }
            assert(threw && FakeDatabase.Writes.Count == 0 && OracleTransaction.CommitCalls == 0,
                "A missing insert Plant was accepted");
        });
        test("failure on the second record rolls back earlier writes in that file", () => {
            FakeDatabase.FailMaterial = "M2";
            bool threw = false;
            try { new HvpService().UpdateSpecialControlFlags(Records()); }
            catch (InvalidOperationException) { threw = true; }
            assert(threw && FakeDatabase.Writes.Count == 0 && OracleTransaction.RollbackCalls == 1,
                "The failed file left a committed first record");
        });
        test("absent material inserts mapped fields with null unmapped values and system creation date", () => {
            FakeDatabase.MissingMaterials.Add("NEW1");
            int count = new HvpService().UpdateSpecialControlFlags(new List<HvpRecord> { NewRecord() });
            assert(FakeDatabase.Inserted.Count == 1, "An absent material was not inserted");
            var values = FakeDatabase.Inserted.Single();
            var sqlValues = InsertExpressions(FakeDatabase.Commands.Last().Sql);
            var expected = new Dictionary<string, string> {
                { "MATERIAL", "NEW1" }, { "PLNT", "ITH1" }, { "SLOC", "RECA" }, { "MTYP", "ROH" },
                { "OLD_MATERIAL_NO", "CUSTOMER1" }, { "MATERIAL_DESCRIPTION", "Example material" },
                { "MATL_GROUP", "OC-NPI" }, { "BUN", "EA" }, { "BASIC_MATERIAL", "AB" },
                { "PGR", "K08" }, { "SPECIAL_CONTROL_FLAG", "HVP" }
            };
            foreach (var pair in expected)
            {
                string parameter = sqlValues[pair.Key].TrimStart(':');
                assert(values.ContainsKey(parameter) && (string)values[parameter] == pair.Value,
                    "Wrong inserted value for " + pair.Key);
            }
            foreach (string column in new[] { "BIN", "STANDARD_PRICE", "SMI2", "SPQ", "MODIFY_DATE" })
                assert(sqlValues[column].Equals("NULL", StringComparison.OrdinalIgnoreCase), column + " must be NULL, not zero or SAP data");
            assert(sqlValues["CREATE_DATE"].Equals("SYSDATE", StringComparison.OrdinalIgnoreCase), "Creation time must come from the database clock");
            assert(count == 1 && FakeDatabase.Writes.SequenceEqual(new[] { "NEW1:HVP" }) && OracleTransaction.CommitCalls == 1,
                "Insert count or commit is incorrect");
        });
        test("an existing material ignores insert-only fields and does not overwrite general data", () => {
            var record = NewRecord(); record.Plant = null; record.StorageLocation = "DIFFERENT"; record.MaterialType = null;
            new HvpService().UpdateSpecialControlFlags(new List<HvpRecord> { record });
            var command = FakeDatabase.Commands.Single();
            string set = command.Sql.Substring(command.Sql.IndexOf("SET", StringComparison.OrdinalIgnoreCase),
                command.Sql.IndexOf("WHERE", StringComparison.OrdinalIgnoreCase) - command.Sql.IndexOf("SET", StringComparison.OrdinalIgnoreCase));
            string where = command.Sql.Substring(command.Sql.IndexOf("WHERE", StringComparison.OrdinalIgnoreCase));
            assert(set.Contains("SPECIAL_CONTROL_FLAG") && set.Contains("MODIFY_DATE") && !set.Contains("PLNT") && !set.Contains("SLOC") && !set.Contains("MTYP"),
                "Existing material general data would be overwritten");
            assert(!where.Contains("PLNT") && !where.Contains("SLOC") && FakeDatabase.Inserted.Count == 0,
                "The service inserted a new location for a material already present elsewhere");
        });
        foreach (string materialType in new[] { "ROH", "HALB", "FERT" })
        test("insert accepts " + materialType + " and defaults missing location to STR9", () => {
            FakeDatabase.MissingMaterials.Add("NEW1");
            var record = NewRecord(); record.MaterialType = materialType; record.StorageLocation = "  ";
            new HvpService().UpdateSpecialControlFlags(new List<HvpRecord> { record });
            assert(FakeDatabase.Inserted.Count == 1 && (string)FakeDatabase.Inserted[0]["storageLocation"] == "STR9",
                "Missing location did not use STR9");
        });
        foreach (string material in new[] { null, "", "  " })
        test("blank Material errors before any SQL: " + (material ?? "null"), () => {
            bool threw = false;
            try { new HvpService().UpdateSpecialControlFlags(new List<HvpRecord> { new HvpRecord { Material = material, SpecialControlFlag = "HVP" } }); }
            catch (InvalidDataException) { threw = true; }
            assert(threw && FakeDatabase.Commands.Count == 0 && OracleTransaction.CommitCalls == 0, "Blank Material was queried or silently accepted");
        });
        test("unsupported insert material type errors without inventing a location", () => {
            FakeDatabase.MissingMaterials.Add("NEW1");
            var record = NewRecord(); record.MaterialType = "XYZ";
            bool threw = false;
            try { new HvpService().UpdateSpecialControlFlags(new List<HvpRecord> { record }); }
            catch (InvalidDataException) { threw = true; }
            assert(threw && FakeDatabase.Inserted.Count == 0 && OracleTransaction.CommitCalls == 0, "Unsupported insert type accepted");
        });
        var tooLongFields = new Dictionary<string, string> {
            { "Plant", "ITH11" }, { "StorageLocation", "STR99" }, { "OldMaterialNo", new string('C', 19) },
            { "MaterialDescription", new string('D', 256) }, { "MaterialGroup", new string('G', 11) },
            { "BaseUnit", "EACH" }, { "BasicMaterial", "ABC" }, { "PurchasingGroup", "K008" }
        };
        foreach (var field in tooLongFields)
        test("oversized insert field errors without truncation: " + field.Key, () => {
            FakeDatabase.MissingMaterials.Add("NEW1"); var record = NewRecord();
            typeof(HvpRecord).GetProperty(field.Key).SetValue(record, field.Value);
            bool threw = false;
            try { new HvpService().UpdateSpecialControlFlags(new List<HvpRecord> { record }); }
            catch (InvalidDataException) { threw = true; }
            assert(threw && FakeDatabase.Inserted.Count == 0, "Oversized " + field.Key + " reached INSERT");
        });
        test("a concurrent insert with ORA-00001 retries the material-only update", () => {
            FakeDatabase.MissingMaterials.Add("NEW1"); FakeDatabase.InsertErrorNumber = 1; FakeDatabase.InsertRaceCreatesMaterial = true;
            int count = new HvpService().UpdateSpecialControlFlags(new List<HvpRecord> { NewRecord() });
            assert(count == 1 && FakeDatabase.Commands.Count == 3 && FakeDatabase.Commands.Last().Sql.TrimStart().StartsWith("UPDATE"),
                "A matching insert race was not reconciled using UPDATE");
            assert(FakeDatabase.Writes.SequenceEqual(new[] { "NEW1:HVP" }) && OracleTransaction.CommitCalls == 1,
                "Raced material did not receive the flag");
        });
        foreach (int errorNumber in new[] { 1, 1400 })
        test("insert Oracle error propagates when no matching material can be updated: " + errorNumber, () => {
            FakeDatabase.MissingMaterials.Add("NEW1"); FakeDatabase.InsertErrorNumber = errorNumber;
            int received = 0;
            try { new HvpService().UpdateSpecialControlFlags(new List<HvpRecord> { NewRecord() }); }
            catch (OracleException error) { received = error.Number; }
            assert(received == errorNumber && OracleTransaction.CommitCalls == 0 && FakeDatabase.Writes.Count == 0,
                "INSERT failure was hidden");
            assert(FakeDatabase.Commands.Count == (errorNumber == 1 ? 3 : 2), "An unrelated Oracle error was treated as a duplicate-key race");
        });
        test("commit failure propagates and cannot leave the file's writes accepted", () => {
            OracleTransaction.FailCommit = true; bool threw = false;
            try { new HvpService().UpdateSpecialControlFlags(Records()); }
            catch (InvalidOperationException) { threw = true; }
            assert(threw && FakeDatabase.Writes.Count == 0 && OracleTransaction.RollbackCalls == 1,
                "A failed commit returned success or retained uncommitted writes");
        });
        test("the database boundary normalizes non-HVP to NULL for both insert and update", () => {
            FakeDatabase.MissingMaterials.Add("NEW1");
            var record = NewRecord(); record.SpecialControlFlag = "OTHER";
            new HvpService().UpdateSpecialControlFlags(new List<HvpRecord> { record, new HvpRecord { Material = "M1", SpecialControlFlag = "OTHER" } });
            assert(FakeDatabase.Inserted.Count == 1 && FakeDatabase.Inserted[0]["specialControlFlag"] == null && FakeDatabase.Commands.Last().Values["specialControlFlag"] == null,
                "A non-HVP flag was stored as text");
        });
    }
}
