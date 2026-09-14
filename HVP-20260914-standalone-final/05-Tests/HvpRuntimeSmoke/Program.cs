// Offline integration with the actual .NET Framework / Oracle / Dapper assemblies.
// No connection.Open(), network access or database writes.
using System;
using System.Configuration;
using System.Data;
using System.Formats.Asn1;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Dapper;
using Oracle.ManagedDataAccess.Client;
using Z02JHVPService;

class RuntimeSmoke
{
    static int failures;
    static void Test(string name, Action body)
    {
        try { body(); Console.WriteLine("PASS " + name); }
        catch (Exception e) { failures++; Console.WriteLine("FAIL " + name + " type=" + e.GetType().FullName); }
    }
    static void Check(bool value) { if (!value) throw new InvalidOperationException("Assertion failed"); }
    static int Main()
    {
        const string descriptor = "(DESCRIPTION=(ADDRESS=(PROTOCOL=TCP)(HOST=xxx)(PORT=1521))(CONNECT_DATA=(SID=xxx)))";
        Test("actual Oracle driver accepts the supplied SID descriptor without connecting", () => {
            var builder = new OracleConnectionStringBuilder("User Id=MATERIAL;Password=xxx;Data Source=" + descriptor + ";");
            Check(builder.UserID == "MATERIAL" && builder.DataSource == descriptor);
            using (var connection = new OracleConnection(builder.ConnectionString)) Check(connection.State == ConnectionState.Closed);
        });
        Test("actual Dapper binds Oracle values in SQL order and maps null to DBNull", () => {
            using (var connection = new OracleConnection("User Id=MATERIAL;Password=xxx;Data Source=" + descriptor + ";"))
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "UPDATE MAT_MASTER SET SPECIAL_CONTROL_FLAG = :specialControlFlag WHERE TRIM(MATERIAL) = TRIM(:material)";
                var parameters = new DynamicParameters();
                parameters.Add("specialControlFlag", null, DbType.String);
                parameters.Add("material", "M1' OR '1'='1", DbType.String);
                // Execute normally supplies this cache identity before binding parameters.
                // Construct it offline so the real bind path runs without opening Oracle.
                var constructor = typeof(SqlMapper.Identity).GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .Single(c => c.GetParameters().Length == 6);
                var identity = (SqlMapper.Identity)constructor.Invoke(new object[] {
                    command.CommandText, (CommandType?)CommandType.Text, connection,
                    typeof(int), typeof(DynamicParameters), null
                });
                ((SqlMapper.IDynamicParameters)parameters).AddParameters(command, identity);
                Check(command.Parameters.Count == 2);
                Check(command.Parameters[0].ParameterName == "specialControlFlag" && command.Parameters[0].Value == DBNull.Value);
                Check(command.Parameters[1].ParameterName == "material" && (string)command.Parameters[1].Value == "M1' OR '1'='1");
                Check(command.Parameters[1].OracleDbType == OracleDbType.Varchar2);
            }
        });
        Test("actual ASN1 dependency decodes under net48", () => {
            var reader = new AsnReader(new byte[] { 2, 1, 42 }, AsnEncodingRules.DER);
            Check(reader.ReadInteger() == 42 && !reader.HasData);
        });
        Test("actual JSON dependency round trips under net48", () => {
            string json = JsonSerializer.Serialize(new[] { "HVP", "", "MATERIAL" });
            Check(JsonSerializer.Deserialize<string[]>(json).SequenceEqual(new[] { "HVP", "", "MATERIAL" }));
        });
        Test("standalone HvpService accepts Material config and empty work without DB", () => {
            Check(ConfigurationManager.ConnectionStrings["Material"] != null);
            Check(new HvpService().UpdateSpecialControlFlags(null) == 0);
        });
        Test("production EXE assembly references contain no Pulllist assemblies", () => {
            var names = typeof(HvpService).Assembly.GetReferencedAssemblies().Select(a => a.Name);
            Check(!names.Any(n => n.IndexOf("Pulllist", StringComparison.OrdinalIgnoreCase) >= 0));
            Console.WriteLine("ASSEMBLY REFERENCES " + string.Join(", ", names));
        });
        Console.WriteLine("Failures: " + failures);
        return failures == 0 ? 0 : 1;
    }
}
