using System;
using System.Collections.Generic;

namespace Pulllist.DAL.PULLLIST
{
    public class ReportAndRepostCriteria
    {
        public string CTN { get; set; }
        public string PN { get; set; }
        public string BATCH { get; set; }
        public string MI_SLIPNUMBER { get; set; }
        public string DATE_FROM { get; set; }
        public string DATE_TO { get; set; }
        public string FROM_LOCATION { get; set; }
        public string TO_LOCATION { get; set; }
        public string STATUS_MODE { get; set; }
        public string STATUS_VALUE { get; set; }
    }

    public class ReportAndRepostRow
    {
        public string ROW_ID { get; set; }
        public string MI_SLIPNUMBER { get; set; }
        public string FROM_LOCATION { get; set; }
        public string TO_LOCATION { get; set; }
        public string CTN { get; set; }
        public string PN { get; set; }
        public string BATCH { get; set; }
        public string QTY { get; set; }
        public string REASON { get; set; }
        public string ACT_BIN { get; set; }
        public string RFID_TAG { get; set; }
        public string RFID_CART { get; set; }
        public DateTime? APPROVE_DATE { get; set; }
        public string STATUS_TRANFER { get; set; }
        public string SAP_DOC1 { get; set; }
        public string ISSUE_BY { get; set; }
        public int HEADER_COUNT { get; set; }
        public int TRANSACTION_ROWS { get; set; }
        public bool CAN_REPOST { get; set; }
        public string BLOCK_REASON { get; set; }
    }

    public class ReportAndRepostStatusCount
    {
        public string STATUS_TRANFER { get; set; }
        public int TOTAL { get; set; }
    }

    public class ReportAndRepostPage
    {
        public int draw { get; set; }
        public int recordsTotal { get; set; }
        public int recordsFiltered { get; set; }
        public List<ReportAndRepostRow> data { get; set; }
        public List<ReportAndRepostStatusCount> statuses { get; set; }
    }

    public class ReportAndRepostSmartReply
    {
        public bool Success { get; set; }
        public bool IsUncertain { get; set; }
        public string Detail { get; set; }
    }

    public class ReportAndRepostResult
    {
        public string ROW_ID { get; set; }
        public string MI_SLIPNUMBER { get; set; }
        public string CTN { get; set; }
        public bool Success { get; set; }
        public string Outcome { get; set; }
        public string Message { get; set; }
    }
}
