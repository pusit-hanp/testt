using Dapper;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text.RegularExpressions;

namespace Pulllist.DAL.ServiceMonitoring
{
    public class ServiceMonitoringDAO : BaseDAO<MonitoringSlip>, IDAO<MonitoringSlip>
    {
        public const int MaximumSlips = 50;
        private static readonly Regex SlipPattern = new Regex(@"\A[A-Za-z0-9][A-Za-z0-9._/-]{0,49}\z", RegexOptions.CultureInvariant);

        public List<MonitoringCount> GetCounts()
        {
            // Deliberately count TPHEADER only, without service eligibility filters.
            const string sql = @"
SELECT SUBSTR(MISLIP_NO, 1, 3) AS Prefix, STATUS AS Status, COUNT(*) AS Total
FROM TPHEADER
WHERE ((MISLIP_NO LIKE 'GT-%' OR MISLIP_NO LIKE 'MU-%') AND STATUS IN (5, 8))
   OR ((MISLIP_NO LIKE 'GT-%' OR MISLIP_NO LIKE 'MU-%' OR MISLIP_NO LIKE 'PK-%')
       AND STATUS IN (10, 11, 14, 15))
GROUP BY SUBSTR(MISLIP_NO, 1, 3), STATUS";
            using (var session = new SessionFactory(base.ConnectionString))
            {
                return session.Exec<MonitoringCount>(sql, new DynamicParameters()).ToList();
            }
        }

        public static List<string> ValidateIds(IEnumerable<string> ids)
        {
            if (ids == null) throw new ArgumentException("ระบุ MISLIP_NO อย่างน้อย 1 รายการ");
            var input = ids.Take(MaximumSlips + 1).ToList();
            if (input.Count == 0 || input.Count > MaximumSlips)
                throw new ArgumentException("ค้นหาได้ครั้งละ 1 ถึง 50 slips");
            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var raw in input)
            {
                if (raw == null || raw.Any(Char.IsControl))
                    throw new ArgumentException("MISLIP_NO ไม่ถูกต้อง");
                string id = raw.Trim();
                if (!SlipPattern.IsMatch(id))
                    throw new ArgumentException("MISLIP_NO ต้องยาว 1 ถึง 50 ตัว ใช้ตัวอักษร ตัวเลข จุด ขีดกลาง ขีดล่าง หรือ /");
                if (seen.Add(id)) result.Add(id);
            }
            return result;
        }

        public List<MonitoringSlip> TrackBatch(IEnumerable<string> ids)
        {
            var requested = ValidateIds(ids);
            var parameters = new DynamicParameters();
            var selects = new List<string>();
            for (int i = 0; i < requested.Count; i++)
            {
                string name = "slip" + i;
                selects.Add("SELECT :" + name + " AS MISLIP_NO FROM DUAL");
                parameters.Add(name, requested[i], DbType.String, size: 50);
            }

            // Single Oracle statement gives a consistent TP/WIP choice, including TP with zero items.
            // Each user value is bound once in the CTE. Generated SQL contains indexes only.
            string sql = "WITH requested AS (" + String.Join(" UNION ALL ", selects) + @")
SELECT h.MISLIP_NO AS MislipNo, 'TP' AS Source, h.STATUS AS Status, h.POST_NO AS PostNo,
       h.PULLLIST_NO AS PullListNo, h.ASSEMBLY AS Assembly, h.PLANNED_ORDER AS PlannedOrder,
       h.PLANNED_QTY AS PlannedQty, h.PLANT AS Plant, h.FROM_SLOC AS FromSloc, h.TO_SLOC AS ToSloc,
       h.DOC_DATE AS DocDate, h.POST_DATE AS PostDate, h.ISSUE_DATE AS IssueDate, h.RECEIVE_DATE AS ReceiveDate,
       h.REQUEST_BY AS RequestBy, h.ISSUE_BY AS IssueBy, h.RECEIVE_BY AS ReceiveBy,
       h.SAP_DOC AS SapDoc, h.SAP_DOC1 AS SapDoc1, h.REMARK AS Remark,
       i.MATERIAL AS Material, i.DESCRIPTION AS Description, i.UOM AS Uom,
       i.PULL_QTY AS PullQty, i.ISSUE_QTY AS IssueQty, i.ITEM_REMARK AS ItemRemark,
       i.TCO_STS AS TcoSts, i.TCO_BATCH AS TcoBatch
FROM TPHEADER h
JOIN requested r ON r.MISLIP_NO = h.MISLIP_NO
LEFT JOIN TPITEMS i ON i.MISLIP_NO = h.MISLIP_NO
UNION ALL
SELECT h.MISLIP_NO AS MislipNo, 'WIP' AS Source, CAST(NULL AS NUMBER) AS Status, CAST(NULL AS NUMBER) AS PostNo,
       h.PULLLIST_NO AS PullListNo, h.ASSEMBLY AS Assembly, h.PLANNED_ORDER AS PlannedOrder,
       h.PLANNED_QTY AS PlannedQty, h.PLANT AS Plant, h.FROM_SLOC AS FromSloc, h.TO_SLOC AS ToSloc,
       h.DOC_DATE AS DocDate, h.POST_DATE AS PostDate, h.ISSUE_DATE AS IssueDate, h.RECEIVE_DATE AS ReceiveDate,
       h.REQUEST_BY AS RequestBy, h.ISSUE_BY AS IssueBy, h.RECEIVE_BY AS ReceiveBy,
       h.SAP_DOC AS SapDoc, h.SAP_DOC1 AS SapDoc1, h.REMARK AS Remark,
       i.MATERIAL AS Material, i.DESCRIPTION AS Description, i.UOM AS Uom,
       i.PULL_QTY AS PullQty, i.ISSUE_QTY AS IssueQty, i.ITEM_REMARK AS ItemRemark,
       CAST(NULL AS VARCHAR2(3)) AS TcoSts, CAST(NULL AS VARCHAR2(10)) AS TcoBatch
FROM WIPHEADER h
JOIN requested r ON r.MISLIP_NO = h.MISLIP_NO
LEFT JOIN WIPITEMS i ON i.MISLIP_NO = h.MISLIP_NO
WHERE NOT EXISTS (SELECT 1 FROM TPHEADER tp WHERE tp.MISLIP_NO = h.MISLIP_NO)
ORDER BY MislipNo, Material";

            List<MonitoringTrackingRow> rows;
            using (var session = new SessionFactory(base.ConnectionString))
            {
                rows = session.Exec<MonitoringTrackingRow>(sql, parameters).ToList();
            }
            var groups = rows.ToLookup(x => x.MislipNo, StringComparer.Ordinal);
            return requested.Select(id => ToSlip(id, groups[id].ToList())).ToList();
        }

        private static MonitoringSlip ToSlip(string id, List<MonitoringTrackingRow> rows)
        {
            if (rows.Count == 0) return new MonitoringSlip { MislipNo = id, Found = false };
            var h = rows[0];
            return new MonitoringSlip
            {
                MislipNo = id, Found = true, Source = h.Source, Status = h.Status, PostNo = h.PostNo,
                PullListNo = h.PullListNo, Assembly = h.Assembly, PlannedOrder = h.PlannedOrder,
                PlannedQty = h.PlannedQty, Plant = h.Plant, FromSloc = h.FromSloc, ToSloc = h.ToSloc,
                DocDate = h.DocDate, PostDate = h.PostDate, IssueDate = h.IssueDate, ReceiveDate = h.ReceiveDate,
                RequestBy = h.RequestBy, IssueBy = h.IssueBy, ReceiveBy = h.ReceiveBy,
                SapDoc = h.SapDoc, SapDoc1 = h.SapDoc1, Remark = h.Remark,
                Items = rows.Where(x => x.Material != null).Select(x => new MonitoringItem
                {
                    Material = x.Material, Description = x.Description, Uom = x.Uom,
                    PullQty = x.PullQty, IssueQty = x.IssueQty, ItemRemark = x.ItemRemark,
                    TcoSts = x.TcoSts, TcoBatch = x.TcoBatch
                }).ToList()
            };
        }
    }
}
