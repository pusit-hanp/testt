using System;
using System.Collections.Generic;

namespace Pulllist.DAL.ServiceMonitoring
{
    public class MonitoringCount
    {
        public string Prefix { get; set; }
        public int Status { get; set; }
        public long Total { get; set; }
    }

    public class MonitoringSummaryRow
    {
        public string Prefix { get; set; }
        public int Movement { get; set; }
        public bool Applicable { get; set; }
        public int WaitingStatus { get; set; }
        public int PostingStatus { get; set; }
        public long? Waiting { get; set; }
        public long? Posting { get; set; }
    }

    public class MonitoringHeader
    {
        public string MislipNo { get; set; }
        public string Source { get; set; }
        public int? Status { get; set; }
        public int? PostNo { get; set; }
        public string PullListNo { get; set; }
        public string Assembly { get; set; }
        public string PlannedOrder { get; set; }
        public decimal? PlannedQty { get; set; }
        public string Plant { get; set; }
        public string FromSloc { get; set; }
        public string ToSloc { get; set; }
        public DateTime? DocDate { get; set; }
        public DateTime? PostDate { get; set; }
        public DateTime? IssueDate { get; set; }
        public DateTime? ReceiveDate { get; set; }
        public string RequestBy { get; set; }
        public string IssueBy { get; set; }
        public string ReceiveBy { get; set; }
        public string SapDoc { get; set; }
        public string SapDoc1 { get; set; }
        public string Remark { get; set; }
    }

    public class MonitoringItem
    {
        public string Material { get; set; }
        public string Description { get; set; }
        public string Uom { get; set; }
        public decimal? PullQty { get; set; }
        public decimal? IssueQty { get; set; }
        public string ItemRemark { get; set; }
        public string TcoSts { get; set; }
        public string TcoBatch { get; set; }
    }

    // Flat projection of one SQL statement; header existence determines the source.
    public class MonitoringTrackingRow : MonitoringHeader
    {
        public string Material { get; set; }
        public string Description { get; set; }
        public string Uom { get; set; }
        public decimal? PullQty { get; set; }
        public decimal? IssueQty { get; set; }
        public string ItemRemark { get; set; }
        public string TcoSts { get; set; }
        public string TcoBatch { get; set; }
    }

    public class MonitoringSlip : MonitoringHeader
    {
        public bool Found { get; set; }
        public List<MonitoringItem> Items { get; set; }

        public MonitoringSlip()
        {
            Items = new List<MonitoringItem>();
        }
    }
}
