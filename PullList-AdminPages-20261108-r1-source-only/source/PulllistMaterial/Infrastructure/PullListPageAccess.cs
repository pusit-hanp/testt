using System;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
namespace PulllistMaterial.Infrastructure
{
    public static class PullListPageAccess
    {
        public static bool HasAccess(object auth, object permissions, string tcode)
        {
            try { Actor(auth); } catch (ArgumentException) { return false; }
            if (String.IsNullOrWhiteSpace(tcode)) return false;
            tcode = tcode.Trim();
            if (String.Equals(tcode, "XX", StringComparison.OrdinalIgnoreCase) ||
                !Regex.IsMatch(tcode, @"\A[A-Za-z0-9_.:/-]{1,64}\z")) return false;
            var items = Convert.ToString(permissions, CultureInfo.InvariantCulture);
            if (String.IsNullOrEmpty(items)) return false;
            // Keep legacy auth_item source, but require a whole permission token.
            // Confirm the company's session delimiter format before enabling a real TCODE.
            return Regex.IsMatch(items, @"(?<![A-Za-z0-9_.:/-])" + Regex.Escape(tcode) + @"(?![A-Za-z0-9_.:/-])");
        }

        public static string Actor(object auth)
        {
            var actor = Convert.ToString(auth, CultureInfo.InvariantCulture).Trim();
            if (actor.Length == 0 || Encoding.UTF8.GetByteCount(actor) > 50 || actor.Any(c => Char.IsControl(c) || Char.IsWhiteSpace(c)) ||
                String.Equals(actor, "SERVICE", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("A valid employee login is required.");
            return actor;
        }

        public static string BoardKey(object auth)
        {
            using (var sha = SHA256.Create())
            {
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes("PullList.ServiceMonitoring.v1|" + Actor(auth)));
                return "pulllist.monitor.v1." + BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
            }
        }
    }
}
