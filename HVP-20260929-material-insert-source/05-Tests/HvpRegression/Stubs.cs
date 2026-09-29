// Test-only substitutes: no real Oracle server is used.
// File parsing, ordering, checkpoint and SQL construction use the real source.
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Data;
namespace System.Configuration
{
    public static class ConfigurationManager
    {
        public static NameValueCollection AppSettings = new NameValueCollection();
        public static Dictionary<string, ConnectionStringSettings> ConnectionStrings = new Dictionary<string, ConnectionStringSettings>();
    }
    public class ConnectionStringSettings
    {
        public string ConnectionString { get; set; }
    }
}
namespace Dapper
{
    public class DynamicParameters
    {
        public Dictionary<string, object> Values = new Dictionary<string, object>();
        public void Add(string name, object value, DbType? dbType = null) { Values[name] = value; }
    }
}
namespace HvpRegressionSupport
{
    public static class FakeDatabase
    {
        public static List<string> Writes = new List<string>();
        public static string FailMaterial;
        public static string LastSql;
        public static Action<string> BeforeExec;
        public static HashSet<string> MissingMaterials = new HashSet<string>();
        public static List<Dictionary<string, object>> Inserted = new List<Dictionary<string, object>>();
        public static List<DatabaseCommand> Commands = new List<DatabaseCommand>();
        public static int InsertErrorNumber;
        public static bool InsertRaceCreatesMaterial;
        public static void Reset()
        {
            Writes.Clear(); FailMaterial = null; LastSql = null; BeforeExec = null;
            MissingMaterials.Clear(); Inserted.Clear(); Commands.Clear();
            InsertErrorNumber = 0; InsertRaceCreatesMaterial = false;
        }
        public static int Exec(string sql, Dapper.DynamicParameters parameters, bool inTransaction)
        {
            LastSql = sql;
            string material = (string)parameters.Values["material"];
            Commands.Add(new DatabaseCommand { Sql = sql,
                Values = new Dictionary<string, object>(parameters.Values), InTransaction = inTransaction });
            if (BeforeExec != null) BeforeExec(material);
            if (material == FailMaterial) throw new InvalidOperationException("SIMULATED DB FAILURE");
            if (sql.TrimStart().StartsWith("INSERT", StringComparison.OrdinalIgnoreCase))
            {
                if (InsertErrorNumber != 0)
                {
                    if (InsertRaceCreatesMaterial) MissingMaterials.Remove(material);
                    throw new Oracle.ManagedDataAccess.Client.OracleException(InsertErrorNumber);
                }
                Inserted.Add(new Dictionary<string, object>(parameters.Values));
                MissingMaterials.Remove(material);
            }
            else if (MissingMaterials.Contains(material)) return 0;
            Writes.Add(material + ":" + (parameters.Values["specialControlFlag"] ?? ""));
            // A material may update several rows. The public result is still input-record count.
            return sql.TrimStart().StartsWith("INSERT", StringComparison.OrdinalIgnoreCase) ? 1 : 3;
        }
    }
    public class DatabaseCommand
    {
        public string Sql;
        public Dictionary<string, object> Values;
        public bool InTransaction;
    }
}
