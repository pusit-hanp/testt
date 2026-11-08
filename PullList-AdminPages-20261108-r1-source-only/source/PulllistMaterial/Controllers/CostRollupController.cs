using System;
using System.Diagnostics;
using System.Linq;
using System.Web.Mvc;
using Pulllist.DAL.CostRollup;
using Pulllist.Service;
using PulllistMaterial.Infrastructure;

namespace PulllistMaterial.Controllers
{
    [PullListPageAuthorize("costRollup")]
    [OutputCache(NoStore = true, Duration = 0, VaryByParam = "*")]
    public class CostRollupController : Controller
    {
        [HttpGet]
        public ActionResult Index() { return View(); }

        [HttpGet]
        public ActionResult Search(string customer, string status)
        {
            try
            {
                if (!ModelState.IsValid) throw new ArgumentException("ข้อมูลค้นหาไม่ถูกต้อง");
                var rows = new CostRollupService().Search(customer, status);
                return Json(new { status = true, rows = rows.Select(CostRollupSnapshot.FromRow).ToList() }, JsonRequestBehavior.AllowGet);
            }
            catch (ArgumentException ex) { return Failure("VALIDATION", ex.Message, 400, true); }
            catch (Exception ex) { return DatabaseFailure(ex, true); }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Add(CostRollupWriteRequest request)
        {
            return Mutate(delegate(string actor) { return new CostRollupService().Add(request, actor); });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Update(CostRollupWriteRequest request)
        {
            return Mutate(delegate(string actor) { return new CostRollupService().Update(request, actor); });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Delete(CostRollupSnapshot original)
        {
            return Mutate(delegate(string actor) { return new CostRollupService().Delete(original); });
        }

        private ActionResult Mutate(Func<string, CostRollupMutationResult> action)
        {
            try
            {
                if (!ModelState.IsValid) throw new ArgumentException("ข้อมูลที่ส่งมาไม่ถูกต้อง");
                string actor = PullListPageAccess.Actor(Session["auth"]);
                var result = action(actor);
                if (!result.Success)
                    return Failure(result.Outcome, result.Message, result.Outcome == "NOT_FOUND" ? 404 : 409, false);
                return Json(new { status = true, outcome = result.Outcome, message = result.Message });
            }
            catch (ArgumentException ex) { return Failure("VALIDATION", ex.Message, 400, false); }
            catch (Exception ex) { return DatabaseFailure(ex, false); }
        }

        private ActionResult DatabaseFailure(Exception exception, bool allowGet)
        {
            Trace.TraceError("CostRollup request failed: {0}", exception);
            return Failure("ERROR", "ทำรายการไม่สำเร็จ กรุณาโหลดข้อมูลใหม่เพื่อตรวจสอบก่อนลองอีกครั้ง", 500, allowGet);
        }

        private ActionResult Failure(string outcome, string message, int statusCode, bool allowGet)
        {
            Response.StatusCode = statusCode;
            Response.TrySkipIisCustomErrors = true;
            return Json(new { status = false, outcome = outcome, message = message }, allowGet ? JsonRequestBehavior.AllowGet : JsonRequestBehavior.DenyGet);
        }
    }
}
