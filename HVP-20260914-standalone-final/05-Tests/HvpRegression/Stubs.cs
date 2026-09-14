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
        public static void Exec(string sql, Dapper.DynamicParameters parameters)
        {
            LastSql = sql;
            string material = (string)parameters.Values["material"];
            if (BeforeExec != null) BeforeExec(material);
            if (material == FailMaterial) throw new InvalidOperationException("SIMULATED DB FAILURE");
            Writes.Add(material + ":" + (parameters.Values["specialControlFlag"] ?? ""));
        }
    }
}
