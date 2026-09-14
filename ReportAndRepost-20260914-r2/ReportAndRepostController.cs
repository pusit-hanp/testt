using System;
using System.Collections.Generic;
using System.Configuration;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Web;
using System.Web.Mvc;
using Pulllist.DAL.PULLLIST;
using Pulllist.Service;

namespace PulllistMaterial.Controllers
{
    public class ReportAndRepostController : BaseController
    {
        private static readonly HttpClient _httpClient = new HttpClient();

        private bool HasReportAccess()
        {
            // Use the same permission as CTN Report; no new role system.
            string tcode = ConfigurationManager.AppSettings["ctnReport"];
            return Session["auth"] != null && !String.IsNullOrWhiteSpace(tcode) &&
                Convert.ToString(Session["auth_item"]).Contains(tcode);
        }

        [HttpGet]
        public ActionResult Index()
        {
            if (Session["auth"] == null) return RedirectToAction("index", "Login");
            if (!HasReportAccess()) return Unauthorize();
            return View();
        }

        [HttpGet]
        public ActionResult Search(ReportAndRepostCriteria criteria, int draw = 0, int start = 0, int length = 25)
        {
            if (!HasReportAccess())
            {
                Response.StatusCode = 403;
                return Json(new { error = "Unauthorized" }, JsonRequestBehavior.AllowGet);
            }
            DateTime from, to;
            if (criteria == null ||
                !DateTime.TryParseExact(criteria.DATE_FROM, "MM/dd/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out from) ||
                !DateTime.TryParseExact(criteria.DATE_TO, "MM/dd/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out to) ||
                from > to || start < 0 || length < 1 || length > 25)
            {
                Response.StatusCode = 400;
                return Json(new { error = "Check date range and page size." }, JsonRequestBehavior.AllowGet);
            }
            try
            {
                PulllistService service = new PulllistService();
                return Json(service.GetReportAndRepost(criteria, draw, start, length), JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Response.StatusCode = 500;
                return Json(new { error = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Repost(string rowId)
        {
            if (!HasReportAccess())
            {
                Response.StatusCode = 403;
                return Json(new { Success = false, Outcome = "SKIPPED", Message = "Unauthorized" });
            }
            if (String.IsNullOrWhiteSpace(rowId) || rowId.Length != 18)
            {
                Response.StatusCode = 400;
                return Json(new { Success = false, Outcome = "SKIPPED", Message = "Invalid row. Search again." });
            }
            Uri uri;
            string url = ConfigurationManager.AppSettings["CONFIRM_CTN"];
            if (!Uri.TryCreate(url, UriKind.Absolute, out uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                Response.StatusCode = 500;
                return Json(new { Success = false, Outcome = "SKIPPED", Message = "SmartWH configuration is missing or invalid." });
            }
            try
            {
                PulllistService service = new PulllistService();
                ReportAndRepostResult result = service.RepostReportCtn(rowId,
                    row => SendToSmart(row, url));
                return Json(result);
            }
            catch (Exception ex)
            {
                Response.StatusCode = 500;
                return Json(new { Success = false, Outcome = "FAILED", Message = ex.Message });
            }
        }

        private ReportAndRepostSmartReply SendToSmart(ReportAndRepostRow row, string url)
        {
            try
            {
                // PS_TpostFromSlip.Form1.TranferToSmartAsync: POST query + empty text/plain body.
                // Preserve endpoint parameters, replacing only the three transaction values.
                var builder = new UriBuilder(url);
                var query = HttpUtility.ParseQueryString(builder.Query);
                query["ctn"] = row.CTN ?? "";
                query["pl_no"] = row.MI_SLIPNUMBER ?? "";
                query["user"] = row.ISSUE_BY ?? "";
                // Explicit UTF-8 encoding avoids HttpValueCollection's %uXXXX on .NET Framework.
                var queryParts = new List<string>();
                foreach (string key in query.AllKeys)
                {
                    foreach (string value in query.GetValues(key))
                    {
                        string name = key == null ? "" : HttpUtility.UrlEncode(key, Encoding.UTF8) + "=";
                        queryParts.Add(name + HttpUtility.UrlEncode(value, Encoding.UTF8));
                    }
                }
                builder.Query = String.Join("&", queryParts);
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;

                using (var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
                using (var content = new StringContent("", Encoding.UTF8, "text/plain"))
                using (var response = _httpClient.PostAsync(builder.Uri, content, cancellation.Token)
                    .GetAwaiter().GetResult())
                {
                    string body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                    string savedStatus = ReportAndRepostStatus.GetSuccessStatus(
                        (int)response.StatusCode, true, body);
                    bool uncertain = response.IsSuccessStatusCode && savedStatus == null &&
                        ReportAndRepostStatus.GetKind(body) != "FAIL";
                    string detail = !String.IsNullOrWhiteSpace(body) ? body : response.ReasonPhrase;
                    if (String.IsNullOrWhiteSpace(detail)) detail = "CAN NOT RECEIVE DATA FROM SMART";
                    return new ReportAndRepostSmartReply {
                        Success = savedStatus != null,
                        IsUncertain = uncertain,
                        Detail = "HTTP " + (int)response.StatusCode + ": " + detail
                    };
                }
            }
            catch (OperationCanceledException)
            {
                return new ReportAndRepostSmartReply { Success = false, IsUncertain = true, Detail = "Timeout" };
            }
            catch (Exception ex)
            {
                return new ReportAndRepostSmartReply { Success = false, IsUncertain = true, Detail = "Exception: " + ex.Message };
            }
        }
    }
}
