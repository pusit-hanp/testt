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
    }
}
