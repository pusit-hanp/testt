using System;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Pulllist.DAL.PULLLIST
{
    public static class ReportAndRepostStatus
    {
        public const string SuccessStatus = "{\"result\":\"Success\",\"msg\":\"success\"}";

        public static string GetKind(string status)
        {
            if (String.IsNullOrWhiteSpace(status)) return "EMPTY";
            string value = status.Trim();
            if (String.Equals(value, "success", StringComparison.OrdinalIgnoreCase)) return "SUCCESS";

            try
            {
                JToken token = JToken.Parse(value);
                if (token.Type == JTokenType.String &&
                    String.Equals((string)token, "success", StringComparison.OrdinalIgnoreCase)) return "SUCCESS";
                JObject obj = token as JObject;
                if (obj != null)
                {
                    string result = Convert.ToString(obj.GetValue("result", StringComparison.OrdinalIgnoreCase));
                    if (String.Equals(result, "Success", StringComparison.OrdinalIgnoreCase)) return "SUCCESS";
                    if (String.Equals(result, "Fail", StringComparison.OrdinalIgnoreCase)) return "FAIL";
                    string code = Convert.ToString(obj.GetValue("code", StringComparison.OrdinalIgnoreCase));
                    string level = Convert.ToString(obj.GetValue("level", StringComparison.OrdinalIgnoreCase));
                    if (String.Equals(code, "SomethingWrong", StringComparison.OrdinalIgnoreCase) &&
                        String.Equals(level, "ERROR", StringComparison.OrdinalIgnoreCase)) return "FAIL";
                }
            }
            catch (JsonReaderException) { }

            if (value.StartsWith("Exception:", StringComparison.OrdinalIgnoreCase) ||
                // Non-success HTTP responses and timeouts stored by PS_TpostFromSlip.
                Regex.IsMatch(value, @"^Error [3-5][0-9]{2}:", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) ||
                String.Equals(value, "Timeout", StringComparison.OrdinalIgnoreCase) ||
                String.Equals(value, "CTN NOT FOUND", StringComparison.OrdinalIgnoreCase) ||
                String.Equals(value, "CAN NOT RECEIVE DATA FROM SMART", StringComparison.OrdinalIgnoreCase)) return "FAIL";
            return "UNKNOWN";
        }

        public static bool CanRepost(string status, string sapDoc)
        {
            return GetKind(status) == "FAIL" && !String.IsNullOrWhiteSpace(sapDoc);
        }

        public static string GetSuccessStatus(int httpStatus, bool transportCompleted, string body)
        {
            return transportCompleted && httpStatus >= 200 && httpStatus < 300 && GetKind(body) == "SUCCESS"
                ? SuccessStatus : null;
        }

        public static string GetBlockReason(ReportAndRepostRow row)
        {
            if (GetKind(row.STATUS_TRANFER) != "FAIL") return "Status is not a recognized Fail/Error.";
            if (row.HEADER_COUNT != 1) return "Slip header is missing or duplicated.";
            if (!CanRepost(row.STATUS_TRANFER, row.SAP_DOC1)) return "SAP_DOC1 is empty.";
            if (String.IsNullOrWhiteSpace(row.CTN) || String.IsNullOrWhiteSpace(row.MI_SLIPNUMBER)) return "CTN or Slip is empty.";
            if (String.IsNullOrWhiteSpace(row.ISSUE_BY)) return "Issue by is empty.";
            if (row.TRANSACTION_ROWS != 1) return "CTN/Slip has multiple log rows; check before sending.";
            return "";
        }
    }
}
