using Pulllist.DAL.ServiceMonitoring;
using System.Collections.Generic;
using System.Linq;

namespace Pulllist.Service
{
    public class ServiceMonitoringService : BaseService<ServiceMonitoringDAO, MonitoringSlip>
    {
        public List<MonitoringSummaryRow> GetSummary()
        {
            DAO.SetConnection(base.GetConnection("MATERIAL", "MATERIAL"));
            var counts = DAO.GetCounts();
            var result = new List<MonitoringSummaryRow>();
            foreach (string prefix in new[] { "GT-", "MU-", "PK-" })
            {
                foreach (int movement in new[] { 311, 313, 315 })
                {
                    int waiting = movement == 311 ? 5 : movement == 313 ? 10 : 14;
                    int posting = movement == 311 ? 8 : movement == 313 ? 11 : 15;
                    bool applicable = prefix != "PK-" || movement != 311;
                    result.Add(new MonitoringSummaryRow
                    {
                        Prefix = prefix, Movement = movement, Applicable = applicable,
                        WaitingStatus = waiting, PostingStatus = posting,
                        Waiting = applicable ? (long?)counts.Where(x => x.Prefix == prefix && x.Status == waiting).Sum(x => x.Total) : null,
                        Posting = applicable ? (long?)counts.Where(x => x.Prefix == prefix && x.Status == posting).Sum(x => x.Total) : null
                    });
                }
            }
            return result;
        }

        public List<MonitoringSlip> TrackBatch(IEnumerable<string> ids)
        {
            var validated = ServiceMonitoringDAO.ValidateIds(ids);
            DAO.SetConnection(base.GetConnection("MATERIAL", "MATERIAL"));
            return DAO.TrackBatch(validated);
        }
    }
}
