using Pulllist.Service;
using PulllistMaterial.Infrastructure;
using System;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Web.Mvc;

namespace PulllistMaterial.Controllers
{
    [PullListPageAuthorize("serviceMonitoring")]
    [OutputCache(NoStore = true, Duration = 0, VaryByParam = "None")]
    public class ServiceMonitoringController : Controller
    {
        [HttpGet]
        public ActionResult Index()
        {
            ViewBag.BoardKey = PullListPageAccess.BoardKey(Session["auth"]);
            return View();
        }

        [HttpGet]
        public JsonResult GetSummary()
        {
            try
            {
                var data = new ServiceMonitoringService().GetSummary();
                return Json(new { ok = true, data = data, loadedAt = UtcNow() }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Trace.TraceError("ServiceMonitoring summary: {0}", ex);
                return Error(500, "โหลดจำนวน slips ไม่สำเร็จ กรุณาลองใหม่", JsonRequestBehavior.AllowGet);
            }
        }

        // Read-only database query; POST avoids long URLs and keeps saved IDs out of URL/history.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public JsonResult TrackBatch(string[] ids)
        {
            try
            {
                if (!ModelState.IsValid) return Error(400, "ข้อมูลค้นหาไม่ถูกต้อง", JsonRequestBehavior.DenyGet);
                var data = new ServiceMonitoringService().TrackBatch(ids).Select(slip => new
                {
                    slip.MislipNo, slip.Found, slip.Source, slip.Status, slip.PostNo, slip.PullListNo,
                    slip.Assembly, slip.PlannedOrder, slip.PlannedQty, slip.Plant, slip.FromSloc, slip.ToSloc,
                    DocDate = OracleDate(slip.DocDate), PostDate = OracleDate(slip.PostDate),
                    IssueDate = OracleDate(slip.IssueDate), ReceiveDate = OracleDate(slip.ReceiveDate),
                    slip.RequestBy, slip.IssueBy, slip.ReceiveBy, slip.SapDoc, slip.SapDoc1, slip.Remark, slip.Items
                }).ToList();
                var result = Json(new { ok = true, data = data, loadedAt = UtcNow() });
                result.MaxJsonLength = 16 * 1024 * 1024;
                return result;
            }
            catch (ArgumentException ex)
            {
                return Error(400, ex.Message, JsonRequestBehavior.DenyGet);
            }
            catch (Exception ex)
            {
                Trace.TraceError("ServiceMonitoring tracking: {0}", ex);
                return Error(500, "โหลดข้อมูล slip ไม่สำเร็จ กรุณาลองใหม่", JsonRequestBehavior.DenyGet);
            }
        }

        private JsonResult Error(int status, string message, JsonRequestBehavior behavior)
        {
            Response.StatusCode = status;
            Response.TrySkipIisCustomErrors = true;
            return Json(new { ok = false, message = message }, behavior);
        }

        private static string UtcNow()
        {
            return DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
        }

        private static string OracleDate(DateTime? value)
        {
            // Oracle DATE has no timezone. Preserve the database wall time at the JSON boundary.
            return value.HasValue ? value.Value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) : null;
        }
    }
}
