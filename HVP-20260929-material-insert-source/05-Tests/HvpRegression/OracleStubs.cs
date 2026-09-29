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
        public static int BeginTransactionCalls;
        public bool IsOpen;
        public OracleConnection(string connectionString) { LastConnectionString = connectionString; }
        public void Open()
        {
            OpenCalls++;
            if (FailOpen) throw new InvalidOperationException("SIMULATED OPEN FAILURE");
            IsOpen = true;
        }
        public void Dispose() { IsOpen = false; DisposeCalls++; }
        public OracleTransaction BeginTransaction()
        {
            if (!IsOpen) throw new InvalidOperationException("Connection must be open before beginning a transaction.");
            BeginTransactionCalls++;
            return new OracleTransaction(this);
        }
        public static void Reset()
        {
            LastConnectionString = null; OpenCalls = 0; DisposeCalls = 0; FailOpen = false;
            BeginTransactionCalls = 0;
            OracleTransaction.Reset();
            HvpRegressionSupport.FakeDatabase.Reset();
        }
    }

    public class OracleException : Exception
    {
        public int Number { get; private set; }
        public OracleException(int number) : base("SIMULATED ORACLE ERROR") { Number = number; }
    }

    public class OracleTransaction : IDbTransaction
    {
        public static int CommitCalls;
        public static int RollbackCalls;
        public static int DisposeCalls;
        public static bool FailCommit;
        internal readonly OracleConnection Owner;
        private readonly int _writeCount;
        private readonly int _insertCount;
        private readonly System.Collections.Generic.HashSet<string> _missing;
        private bool _completed;
        public OracleTransaction(OracleConnection connection)
        {
            Owner = connection;
            _writeCount = HvpRegressionSupport.FakeDatabase.Writes.Count;
            _insertCount = HvpRegressionSupport.FakeDatabase.Inserted.Count;
            _missing = new System.Collections.Generic.HashSet<string>(HvpRegressionSupport.FakeDatabase.MissingMaterials);
        }
        public IDbConnection Connection { get { return null; } }
        public IsolationLevel IsolationLevel { get { return IsolationLevel.ReadCommitted; } }
        public void Commit()
        {
            if (FailCommit) throw new InvalidOperationException("SIMULATED COMMIT FAILURE");
            CommitCalls++; _completed = true;
        }
        public void Rollback()
        {
            if (_completed) return;
            RollbackCalls++;
            var dbWrites = HvpRegressionSupport.FakeDatabase.Writes;
            dbWrites.RemoveRange(_writeCount, dbWrites.Count - _writeCount);
            var dbInserted = HvpRegressionSupport.FakeDatabase.Inserted;
            dbInserted.RemoveRange(_insertCount, dbInserted.Count - _insertCount);
            HvpRegressionSupport.FakeDatabase.MissingMaterials = new System.Collections.Generic.HashSet<string>(_missing);
            _completed = true;
        }
        public void Dispose() { if (!_completed) Rollback(); DisposeCalls++; }
        internal static void Reset() { CommitCalls = 0; RollbackCalls = 0; DisposeCalls = 0; FailCommit = false; }
    }
}

namespace Dapper
{
    public static class SqlMapper
    {
        public static int Execute(this Oracle.ManagedDataAccess.Client.OracleConnection connection,
            string sql, DynamicParameters parameters, IDbTransaction transaction = null)
        {
            if (!connection.IsOpen) throw new InvalidOperationException("Connection must be opened before execution.");
            if (transaction != null && ((Oracle.ManagedDataAccess.Client.OracleTransaction)transaction).Owner != connection)
                throw new InvalidOperationException("Transaction belongs to another connection.");
            return HvpRegressionSupport.FakeDatabase.Exec(sql, parameters, transaction != null);
        }
    }
}
