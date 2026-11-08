using System;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Pulllist.DAL.CostRollup
{
    public class CostRollupRow
    {
        public string CUST_CODE { get; set; }
        public string COSTROLLUP_STATUS { get; set; }
        public string MISLIP_NO_REF { get; set; }
        public string CHANGE_BY { get; set; }
        public DateTime? FROM_DATE { get; set; }
        public DateTime? TO_DATE { get; set; }
        public DateTime? LAST_UPDATE { get; set; }
    }

    // Oracle DATE is a wall-clock value, not UTC. JSON dates are invariant text.
    public class CostRollupSnapshot
    {
        public string CUST_CODE { get; set; }
        public string COSTROLLUP_STATUS { get; set; }
        public string MISLIP_NO_REF { get; set; }
        public string CHANGE_BY { get; set; }
        public string FROM_DATE { get; set; }
        public string TO_DATE { get; set; }
        public string LAST_UPDATE { get; set; }

        public static CostRollupSnapshot FromRow(CostRollupRow row)
        {
            if (row == null) throw new ArgumentNullException("row");
            return new CostRollupSnapshot {
                CUST_CODE = row.CUST_CODE, COSTROLLUP_STATUS = row.COSTROLLUP_STATUS,
                MISLIP_NO_REF = row.MISLIP_NO_REF, CHANGE_BY = row.CHANGE_BY,
                FROM_DATE = Format(row.FROM_DATE), TO_DATE = Format(row.TO_DATE),
                LAST_UPDATE = Format(row.LAST_UPDATE)
            };
        }

        private static string Format(DateTime? value)
        {
            return value.HasValue ? value.Value.ToString(CostRollupValidation.DateFormat, CultureInfo.InvariantCulture) : null;
        }
    }

    public class CostRollupWriteRequest
    {
        public string CUST_CODE { get; set; }
        public string COSTROLLUP_STATUS { get; set; }
        public string FROM_DATE { get; set; }
        public string TO_DATE { get; set; }
        public CostRollupSnapshot Original { get; set; }
    }

    public class CostRollupMutationResult
    {
        public bool Success { get; set; }
        public string Outcome { get; set; }
        public string Message { get; set; }

        public static CostRollupMutationResult FromDatabase(int outcome)
        {
            switch (outcome)
            {
                case 1: return new CostRollupMutationResult { Success = true, Outcome = "SUCCESS", Message = "บันทึกแล้ว การเปลี่ยนแปลงมีผลใน service รอบถัดไป" };
                case 2: return new CostRollupMutationResult { Outcome = "DUPLICATE", Message = "Customer นี้มีอยู่แล้ว กรุณาโหลดรายการล่าสุด" };
                case 3: return new CostRollupMutationResult { Outcome = "CONFLICT", Message = "ข้อมูลถูกเปลี่ยนโดย service หรือผู้ใช้อื่น กรุณาโหลดรายการล่าสุดก่อนแก้ไข" };
                case 4: return new CostRollupMutationResult { Outcome = "NOT_FOUND", Message = "ไม่พบข้อมูลนี้แล้ว กรุณาโหลดรายการล่าสุด" };
                default: throw new InvalidOperationException("Unexpected Cost Rollup database outcome.");
            }
        }
    }

    public static class CostRollupValidation
    {
        public const string DateFormat = "yyyy-MM-dd'T'HH:mm:ss";

        public static string Customer(string value)
        {
            string normalized = (value ?? "").Trim().ToUpperInvariant();
            if (!Regex.IsMatch(normalized, @"\A[A-Z0-9]{2}\z"))
                throw new ArgumentException("Customer ต้องเป็นตัวอักษรหรือตัวเลข 2 ตัว เช่น 0C");
            return normalized;
        }

        public static string Status(string value)
        {
            if (value != "Y" && value != "N") throw new ArgumentException("Status ต้องเป็น Y หรือ N");
            return value;
        }

        public static CostRollupRow Write(CostRollupWriteRequest request, string actor, bool adding)
        {
            if (request == null) throw new ArgumentException("ไม่พบข้อมูลที่ต้องการบันทึก");
            // Ownership controls service behavior. SERVICE is never a manual actor.
            string user = (actor ?? "").Trim();
            if (user.Length == 0 || Encoding.UTF8.GetByteCount(user) > 50 || user.Any(c => Char.IsControl(c) || Char.IsWhiteSpace(c)) ||
                String.Equals(user, "SERVICE", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("ไม่พบรหัสผู้ใช้ที่ใช้บันทึกได้ กรุณาเข้าสู่ระบบใหม่");
            var result = new CostRollupRow {
                CUST_CODE = Customer(request.CUST_CODE), COSTROLLUP_STATUS = Status(request.COSTROLLUP_STATUS),
                CHANGE_BY = user, FROM_DATE = Date(request.FROM_DATE), TO_DATE = Date(request.TO_DATE)
            };
            if (adding && result.COSTROLLUP_STATUS != "Y") throw new ArgumentException("เพิ่มรายการใหม่ได้เฉพาะ Status Y");
            if (result.FROM_DATE.HasValue != result.TO_DATE.HasValue)
                throw new ArgumentException("กรอก From และ To ให้ครบ หรือเว้นว่างทั้งคู่");
            if (result.FROM_DATE.HasValue && result.FROM_DATE.Value > result.TO_DATE.Value)
                throw new ArgumentException("From ต้องไม่เกิน To");
            return result;
        }

        public static CostRollupRow Original(CostRollupSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentException("กรุณาโหลดรายการล่าสุดก่อนแก้ไขหรือลบ");
            // Do not trim original nullable text: it must compare exactly with the DB snapshot.
            if ((snapshot.CHANGE_BY != null && snapshot.CHANGE_BY.Length > 50) ||
                (snapshot.MISLIP_NO_REF != null && snapshot.MISLIP_NO_REF.Length > 50))
                throw new ArgumentException("ข้อมูลต้นฉบับไม่ถูกต้อง กรุณาโหลดรายการล่าสุด");
            return new CostRollupRow {
                CUST_CODE = Customer(snapshot.CUST_CODE), COSTROLLUP_STATUS = Status(snapshot.COSTROLLUP_STATUS),
                CHANGE_BY = snapshot.CHANGE_BY, MISLIP_NO_REF = snapshot.MISLIP_NO_REF,
                FROM_DATE = Date(snapshot.FROM_DATE), TO_DATE = Date(snapshot.TO_DATE), LAST_UPDATE = Date(snapshot.LAST_UPDATE)
            };
        }

        private static DateTime? Date(string value)
        {
            if (String.IsNullOrWhiteSpace(value)) return null;
            DateTime date;
            if (!DateTime.TryParseExact(value, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
                throw new ArgumentException("วันที่ต้องเป็น yyyy-MM-ddTHH:mm:ss และเป็นวันที่จริง");
            return DateTime.SpecifyKind(date, DateTimeKind.Unspecified);
        }
    }
}
