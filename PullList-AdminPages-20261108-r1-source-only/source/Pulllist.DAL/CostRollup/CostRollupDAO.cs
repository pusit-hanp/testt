using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Dapper;

namespace Pulllist.DAL.CostRollup
{
    public class CostRollupDAO : BaseDAO<CostRollupRow>, IDAO<CostRollupRow>
    {
        private const string Columns = "CUST_CODE, COSTROLLUP_STATUS, MISLIP_NO_REF, CHANGE_BY, FROM_DATE, TO_DATE, LAST_UPDATE";
        private const string OriginalWhere = @"
            WHERE CUST_CODE = :CUST_CODE
              AND COSTROLLUP_STATUS = :OLD_COSTROLLUP_STATUS
              AND (MISLIP_NO_REF = :OLD_MISLIP_NO_REF OR (MISLIP_NO_REF IS NULL AND :OLD_MISLIP_NO_REF IS NULL))
              AND (CHANGE_BY = :OLD_CHANGE_BY OR (CHANGE_BY IS NULL AND :OLD_CHANGE_BY IS NULL))
              AND (FROM_DATE = :OLD_FROM_DATE OR (FROM_DATE IS NULL AND :OLD_FROM_DATE IS NULL))
              AND (TO_DATE = :OLD_TO_DATE OR (TO_DATE IS NULL AND :OLD_TO_DATE IS NULL))
              AND (LAST_UPDATE = :OLD_LAST_UPDATE OR (LAST_UPDATE IS NULL AND :OLD_LAST_UPDATE IS NULL))";

        public List<CostRollupRow> Search(string customer, string status)
        {
            var parameters = new DynamicParameters();
            string sql = "SELECT " + Columns + " FROM PULLLIST_COSTROLLUP WHERE 1 = 1";
            if (!String.IsNullOrWhiteSpace(customer))
            {
                parameters.Add("CUST_CODE", CostRollupValidation.Customer(customer), DbType.String);
                sql += " AND CUST_CODE = :CUST_CODE";
            }
            if (!String.IsNullOrWhiteSpace(status))
            {
                parameters.Add("COSTROLLUP_STATUS", CostRollupValidation.Status(status), DbType.String);
                sql += " AND COSTROLLUP_STATUS = :COSTROLLUP_STATUS";
            }
            using (var session = new SessionFactory(base.ConnectionString))
                return session.Exec<CostRollupRow>(sql + " ORDER BY CUST_CODE", parameters).ToList();
        }

        public CostRollupMutationResult Add(CostRollupWriteRequest request, string actor)
        {
            var row = CostRollupValidation.Write(request, actor, true);
            var parameters = WriteParameters(row);
            return Execute(@"BEGIN
                INSERT INTO PULLLIST_COSTROLLUP
                    (CUST_CODE, COSTROLLUP_STATUS, MISLIP_NO_REF, CHANGE_BY, FROM_DATE, TO_DATE, LAST_UPDATE)
                VALUES (:CUST_CODE, :COSTROLLUP_STATUS, NULL, :CHANGE_BY, :FROM_DATE, :TO_DATE, SYSDATE);
                :OUTCOME := 1;
            EXCEPTION WHEN DUP_VAL_ON_INDEX THEN :OUTCOME := 2;
            END;", parameters);
        }

        public CostRollupMutationResult Update(CostRollupWriteRequest request, string actor)
        {
            var row = CostRollupValidation.Write(request, actor, false);
            var original = CostRollupValidation.Original(request.Original);
            if (!String.Equals(row.CUST_CODE, original.CUST_CODE, StringComparison.Ordinal))
                throw new ArgumentException("เปลี่ยน Customer ของรายการเดิมไม่ได้");
            var parameters = WriteParameters(row);
            AddOriginal(parameters, original);
            return Execute(ConditionalMutation(@"UPDATE PULLLIST_COSTROLLUP
                SET COSTROLLUP_STATUS = :COSTROLLUP_STATUS, MISLIP_NO_REF = NULL,
                    CHANGE_BY = :CHANGE_BY, FROM_DATE = :FROM_DATE, TO_DATE = :TO_DATE,
                    LAST_UPDATE = SYSDATE"), parameters);
        }

        public CostRollupMutationResult Delete(CostRollupSnapshot snapshot)
        {
            var original = CostRollupValidation.Original(snapshot);
            var parameters = new DynamicParameters();
            parameters.Add("CUST_CODE", original.CUST_CODE, DbType.String);
            AddOriginal(parameters, original);
            return Execute(ConditionalMutation("DELETE FROM PULLLIST_COSTROLLUP"), parameters);
        }

        private static string ConditionalMutation(string statement)
        {
            // One conditional DML statement handles service/admin races, including changes in
            // the same second. A zero-row result is classified without retrying the mutation.
            return @"DECLARE V_EXISTS NUMBER;
            BEGIN " + statement + OriginalWhere + @";
                IF SQL%ROWCOUNT = 1 THEN :OUTCOME := 1;
                ELSE
                    SELECT COUNT(*) INTO V_EXISTS FROM PULLLIST_COSTROLLUP WHERE CUST_CODE = :CUST_CODE;
                    IF V_EXISTS = 0 THEN :OUTCOME := 4; ELSE :OUTCOME := 3; END IF;
                END IF;
            END;";
        }

        private CostRollupMutationResult Execute(string sql, DynamicParameters parameters)
        {
            // SessionFactory.Exec returns void in the existing application. Use an OUT bind
            // rather than relying on a nonexistent affected-row API or a second blind write.
            parameters.Add("OUTCOME", dbType: DbType.Int32, direction: ParameterDirection.Output);
            using (var session = new SessionFactory(base.ConnectionString))
                session.Exec(sql, parameters);
            return CostRollupMutationResult.FromDatabase(parameters.Get<int>("OUTCOME"));
        }

        private static DynamicParameters WriteParameters(CostRollupRow row)
        {
            var parameters = new DynamicParameters();
            parameters.Add("CUST_CODE", row.CUST_CODE, DbType.String);
            parameters.Add("COSTROLLUP_STATUS", row.COSTROLLUP_STATUS, DbType.String);
            parameters.Add("CHANGE_BY", row.CHANGE_BY, DbType.String);
            parameters.Add("FROM_DATE", row.FROM_DATE, DbType.DateTime);
            parameters.Add("TO_DATE", row.TO_DATE, DbType.DateTime);
            return parameters;
        }

        private static void AddOriginal(DynamicParameters parameters, CostRollupRow row)
        {
            parameters.Add("OLD_COSTROLLUP_STATUS", row.COSTROLLUP_STATUS, DbType.String);
            parameters.Add("OLD_MISLIP_NO_REF", row.MISLIP_NO_REF, DbType.String);
            parameters.Add("OLD_CHANGE_BY", row.CHANGE_BY, DbType.String);
            parameters.Add("OLD_FROM_DATE", row.FROM_DATE, DbType.DateTime);
            parameters.Add("OLD_TO_DATE", row.TO_DATE, DbType.DateTime);
            parameters.Add("OLD_LAST_UPDATE", row.LAST_UPDATE, DbType.DateTime);
        }
    }
}
