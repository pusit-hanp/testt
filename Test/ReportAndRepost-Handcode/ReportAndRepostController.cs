using System;
using System.Configuration;
using System.Globalization;
using System.Threading;
using System.Web.Mvc;
using Pulllist.DAL.PULLLIST;
using Pulllist.Service;
using RestSharp;

namespace PulllistMaterial.Controllers
{
    public class ReportAndRepostController : BaseController
    {
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
            if (!HasReportAccess()) return Unauthorized();
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
            string authorization = ConfigurationManager.AppSettings["CONFIRM_CTN_AUTHORIZATION"];
            if (!Uri.TryCreate(url, UriKind.Absolute, out uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
                String.IsNullOrWhiteSpace(authorization))
            {
                Response.StatusCode = 500;
                return Json(new { Success = false, Outcome = "SKIPPED", Message = "SmartWH configuration is missing or invalid." });
            }
            try
            {
                PulllistService service = new PulllistService();
                ReportAndRepostResult result = service.RepostReportCtn(rowId,
                    row => SendToSmart(row, url, authorization));
                return Json(result);
            }
            catch (Exception ex)
            {
                Response.StatusCode = 500;
                return Json(new { Success = false, Outcome = "FAILED", Message = ex.Message });
            }
        }

        private ReportAndRepostSmartReply SendToSmart(ReportAndRepostRow row, string url, string authorization)
        {
            try
            {
                using (var client = new RestClient(url))
                using (var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
                {
                    var request = new RestRequest();
                    request.Method = Method.Post;
                    request.AddHeader("Authorization", authorization);
                    request.AddHeader("X-Requested-With", "X");
                    request.AddHeader("Accept", "application/json");
                    request.AddHeader("Content-Type", "application/json");
                    request.AddQueryParameter("ctn", row.CTN);
                    request.AddQueryParameter("pl_no", row.MI_SLIPNUMBER);
                    request.AddQueryParameter("user", row.ISSUE_BY);

                    // Same POST/query contract as the uncommented CTNReportController call.
                    RestResponse response = client.ExecuteAsync(request, cancellation.Token).GetAwaiter().GetResult();
                    string savedStatus = ReportAndRepostStatus.GetSuccessStatus(
                        (int)response.StatusCode, response.ResponseStatus == ResponseStatus.Completed, response.Content);
                    bool uncertain = response.ResponseStatus != ResponseStatus.Completed || (int)response.StatusCode == 0 ||
                        ((int)response.StatusCode >= 200 && (int)response.StatusCode < 300 && savedStatus == null &&
                         ReportAndRepostStatus.GetKind(response.Content) != "FAIL");
                    string detail = !String.IsNullOrWhiteSpace(response.Content) ? response.Content : response.ErrorMessage;
                    if (String.IsNullOrWhiteSpace(detail)) detail = "CAN NOT RECEIVE DATA FROM SMART";
                    return new ReportAndRepostSmartReply {
                        Success = savedStatus != null,
                        IsUncertain = uncertain,
                        Detail = "HTTP " + (int)response.StatusCode + ": " + detail
                    };
                }
            }
            catch (Exception ex)
            {
                return new ReportAndRepostSmartReply { Success = false, IsUncertain = true, Detail = "Exception: " + ex.Message };
            }
        }
    }
}
