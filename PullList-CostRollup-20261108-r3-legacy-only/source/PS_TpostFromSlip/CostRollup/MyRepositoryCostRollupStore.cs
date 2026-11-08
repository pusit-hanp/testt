using System;
using System.Collections.Generic;

namespace PS_TpostFromSlip.CostRollup
{
    // Uses only the Invoke surface already used by Form1. Validate binding/commit
    // with the company's actual MyRepository assembly before enabling in PROD.
    public sealed class MyRepositoryCostRollupStore : ICostRollupStore
    {
        private readonly string connectionString;

        public MyRepositoryCostRollupStore(string connectionString)
        {
            this.connectionString = connectionString;
        }

        public CostRollupSnapshot LoadSnapshot()
        {
            var connection = new MyRepository.Invoke(connectionString);
            connection.parameters.Clear();
            // One statement supplies both DB wall-clock time and a consistent set
            // of rows. DUAL keeps the clock available when the table is empty.
            var rows = connection.Get<CostRollupRecord>(@"SELECT c.CUST_CODE, c.COSTROLLUP_STATUS,
    c.MISLIP_NO_REF, c.CHANGE_BY, c.FROM_DATE, c.TO_DATE, c.LAST_UPDATE,
    SYSDATE AS RUN_STARTED_AT
FROM DUAL LEFT JOIN PULLLIST_COSTROLLUP c ON 1 = 1");
            if (rows == null || rows.Count == 0 || rows[0] == null)
                throw new CostRollupException("Repository returned no Cost Rollup snapshot/DB clock.");
            var result = new CostRollupSnapshot { RunStartedAt = rows[0].RUN_STARTED_AT, Records = rows };
            if (rows.Count == 1 && rows[0].CUST_CODE == null)
                result.Records = new List<CostRollupRecord>();
            return result;
        }

        public void ResetForServiceStart(CostRollupRecord expected)
        {
            var connection = new MyRepository.Invoke(connectionString);
            connection.parameters.Clear();
            Bind(connection, "crCustCode", expected.CUST_CODE);
            Bind(connection, "crExpectedStatus", expected.COSTROLLUP_STATUS);
            Bind(connection, "crExpectedOwner", expected.CHANGE_BY);
            Bind(connection, "crExpectedRef", expected.MISLIP_NO_REF);
            Bind(connection, "crExpectedFrom", expected.FROM_DATE);
            Bind(connection, "crExpectedTo", expected.TO_DATE);
            Bind(connection, "crExpectedUpdate", expected.LAST_UPDATE);
            // Compare every snapshot field: DATE precision alone cannot protect
            // two edits in the same second. A later admin change wins in the DB.
            connection.ExecuteNow(@"UPDATE PULLLIST_COSTROLLUP
SET COSTROLLUP_STATUS = 'N',
    CHANGE_BY = CASE WHEN CHANGE_BY = 'SERVICE' THEN CHANGE_BY ELSE NULL END,
    LAST_UPDATE = SYSDATE
WHERE CUST_CODE = crCustCode AND COSTROLLUP_STATUS = crExpectedStatus
  AND (CHANGE_BY = crExpectedOwner OR (CHANGE_BY IS NULL AND crExpectedOwner IS NULL))
  AND (MISLIP_NO_REF = crExpectedRef OR (MISLIP_NO_REF IS NULL AND crExpectedRef IS NULL))
  AND (FROM_DATE = crExpectedFrom OR (FROM_DATE IS NULL AND crExpectedFrom IS NULL))
  AND (TO_DATE = crExpectedTo OR (TO_DATE IS NULL AND crExpectedTo IS NULL))
  AND (LAST_UPDATE = crExpectedUpdate OR (LAST_UPDATE IS NULL AND crExpectedUpdate IS NULL))");
        }

        private static void Bind(MyRepository.Invoke connection, string name, object value)
        {
            connection.parameters.Add(new KeyValuePair<string, dynamic>(name, value ?? DBNull.Value));
        }

        public void RecordFailure(string customer, string slip)
        {
            var connection = new MyRepository.Invoke(connectionString);
            connection.parameters.Clear();
            connection.parameters.Add(new KeyValuePair<string, dynamic>("crCustCode", customer));
            connection.parameters.Add(new KeyValuePair<string, dynamic>("crSlipNo", slip));
            // Conditional update protects an admin edit made after the precheck.
            // Existing Y rows retain the first triggering slip and timestamp.
            connection.ExecuteNow(@"MERGE INTO PULLLIST_COSTROLLUP target
USING (SELECT crCustCode AS CUST_CODE, crSlipNo AS MISLIP_NO_REF FROM DUAL) source
ON (target.CUST_CODE = source.CUST_CODE)
WHEN MATCHED THEN UPDATE SET
    target.COSTROLLUP_STATUS = 'Y',
    target.MISLIP_NO_REF = source.MISLIP_NO_REF,
    target.CHANGE_BY = 'SERVICE',
    target.FROM_DATE = NULL,
    target.TO_DATE = NULL,
    target.LAST_UPDATE = SYSDATE
WHERE (target.CHANGE_BY = 'SERVICE' OR target.CHANGE_BY IS NULL)
  AND target.COSTROLLUP_STATUS = 'N'
WHEN NOT MATCHED THEN INSERT
    (CUST_CODE, COSTROLLUP_STATUS, MISLIP_NO_REF, CHANGE_BY, FROM_DATE, TO_DATE, LAST_UPDATE)
VALUES
    (source.CUST_CODE, 'Y', source.MISLIP_NO_REF, 'SERVICE', NULL, NULL, SYSDATE)");
        }
    }
}
