using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using Dapper;
using Oracle.ManagedDataAccess.Client;

namespace Z02JHVPService
{
    public class HvpRecord
    {
        public string Material { get; set; }
        public string SpecialControlFlag { get; set; }
    }

    public class HvpService
    {
        private readonly string _connectionString;

        public HvpService()
        {
            var settings = ConfigurationManager.ConnectionStrings["Material"];
            _connectionString = settings == null ? null : settings.ConnectionString;
            if (string.IsNullOrWhiteSpace(_connectionString))
            {
                HvpLog.Write("DB CONFIG FAILED: set connectionStrings/Material in Z02JHVPService.exe.config.");
                throw new InvalidOperationException("The Material connection string is missing or empty.");
            }
        }

        public int UpdateSpecialControlFlags(List<HvpRecord> records)
        {
            if (records == null || records.Count == 0) return 0;

            int processedRecords = 0;
            using (var connection = new OracleConnection(_connectionString))
            {
                connection.Open();
                string sql = @"UPDATE MAT_MASTER
                               SET SPECIAL_CONTROL_FLAG = :specialControlFlag,
                                   MODIFY_DATE = SYSDATE
                               WHERE TRIM(MATERIAL) = TRIM(:material)";
                foreach (var item in records)
                {
                    DynamicParameters param = new DynamicParameters();
                    // Parameter order also follows SQL order for positional Oracle providers.
                    param.Add("specialControlFlag", item.SpecialControlFlag, DbType.String);
                    param.Add("material", item.Material, DbType.String);
                    connection.Execute(sql, param);
                    processedRecords++;
                }
            }
            // Preserve the existing return meaning: input records, not affected DB rows.
            return processedRecords;
        }
    }
}
