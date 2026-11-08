using System;
using System.Configuration;
using System.Web;
using System.Web.Mvc;
namespace PulllistMaterial.Infrastructure
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
    public sealed class PullListPageAuthorizeAttribute : AuthorizeAttribute
    {
        private readonly string configKey;
        public PullListPageAuthorizeAttribute(string configKey) { this.configKey = configKey; }

        public override void OnAuthorization(AuthorizationContext context)
        {
            if (context == null) throw new ArgumentNullException("context");
            var response = context.HttpContext.Response;
            response.Cache.SetCacheability(HttpCacheability.NoCache);
            response.Cache.SetNoStore();
            var session = context.HttpContext.Session;
            var actor = session == null ? null : session["auth"];
            var permissions = session == null ? null : session["auth_item"];
            if (PullListPageAccess.HasAccess(actor, permissions, ConfigurationManager.AppSettings[configKey])) return;

            bool loggedIn = actor != null && !String.IsNullOrWhiteSpace(Convert.ToString(actor));
            if (!loggedIn && String.Equals(context.ActionDescriptor.ActionName, "Index", StringComparison.OrdinalIgnoreCase))
            {
                context.Result = new RedirectResult(VirtualPathUtility.ToAbsolute("~/Login/Index", context.HttpContext.Request.ApplicationPath));
                return;
            }
            response.StatusCode = loggedIn ? 403 : 401;
            response.SuppressFormsAuthenticationRedirect = true;
            response.TrySkipIisCustomErrors = true;
            context.Result = new JsonResult {
                Data = new { success = false, error = loggedIn ? "Access denied. Check page permissions." : "Session expired. Sign in again." },
                JsonRequestBehavior = JsonRequestBehavior.AllowGet
            };
        }
    }
}
