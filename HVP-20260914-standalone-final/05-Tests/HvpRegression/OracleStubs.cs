// Test-only Oracle boundary: never deploy this file.
using System;
using System.Data;

namespace Oracle.ManagedDataAccess.Client
{
    public class OracleConnection : IDisposable
    {
        public static string LastConnectionString;
        public static int OpenCalls;
        public static int DisposeCalls;
        public static bool FailOpen;
        public bool IsOpen;
        public OracleConnection(string connectionString) { LastConnectionString = connectionString; }
        public void Open()
        {
            OpenCalls++;
            if (FailOpen) throw new InvalidOperationException("SIMULATED OPEN FAILURE");
            IsOpen = true;
        }
        public void Dispose() { IsOpen = false; DisposeCalls++; }
        public static void Reset() { LastConnectionString = null; OpenCalls = 0; DisposeCalls = 0; FailOpen = false; }
    }
}

namespace Dapper
{
    public static class SqlMapper
    {
        public static int Execute(this Oracle.ManagedDataAccess.Client.OracleConnection connection,
            string sql, DynamicParameters parameters)
        {
            if (!connection.IsOpen) throw new InvalidOperationException("Connection must be opened before execution.");
            HvpRegressionSupport.FakeDatabase.Exec(sql, parameters);
            // A material may update several DB rows. The public result is still record count.
            return 3;
        }
    }
}
