using System;
using System.Collections.Generic;
using System.Data;
using Dapper;
using Pulllist.DAL;
using Pulllist.DAL.Mat_Master;
using Pulllist.Service;

namespace Z02JHVPService
{
    public class HvpRecord
    {
        public string Material { get; set; }
        public string SpecialControlFlag { get; set; }
    }

    public class HvpService : BaseService<MatMasterDAO, MatMaster>
    {
        private readonly string _connectionString;

        public HvpService()
        {
            // BaseService reads <exe directory>\patch\patch.xml and THIS process's appSettings.
            _connectionString = base.GetConnection("MATERIAL", "MATERIAL");
            if (string.IsNullOrWhiteSpace(_connectionString))
            {
                HvpLog.Write("DB CONFIG FAILED: check patch\\patch.xml, MATERIAL node, DecryptCTN/Encs_T appSettings and service account permissions.");
                throw new InvalidOperationException("BaseService.GetConnection returned an empty connection string.");
            }
        }

        public int UpdateSpecialControlFlags(List<HvpRecord> records)
        {
            if (records == null || records.Count == 0) return 0;

            int processedRecords = 0;
            using (var session = new SessionFactory(_connectionString))
            {
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
                    session.Exec(sql, param);
                    processedRecords++;
                }
            }
            // Legacy SessionFactory.Exec returns void; do not call this a DB row count.
            return processedRecords;
        }
    }
}
