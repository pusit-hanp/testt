using System;
using System.Collections.Generic;

namespace PS_TpostFromSlip.CostRollup
{
    public sealed class CostRollupException : Exception
    {
        public CostRollupException(string message) : base(message) { }
        public CostRollupException(string message, Exception inner) : base(message, inner) { }
    }

    public sealed class CostRollupOptions
    {
        public bool Enabled { get; private set; }
        public string Message { get; private set; }

        public static CostRollupOptions FromStrings(string flag, string message)
        {
            bool enabled = false;
            if (flag != null && !bool.TryParse(flag.Trim(), out enabled))
                throw new ArgumentException("Check_CostRollUp must be true or false.");
            string matcher = message == null ? "has status Mth End" : message.Trim();
            if (enabled && matcher.Length == 0)
                throw new ArgumentException("Check_CostRollUp_Msg must not be empty when enabled.");
            return new CostRollupOptions { Enabled = enabled, Message = matcher };
        }
    }

    public sealed class CostRollupRecord
    {
        public string CUST_CODE { get; set; }
        public string COSTROLLUP_STATUS { get; set; }
        public string CHANGE_BY { get; set; }
        public string MISLIP_NO_REF { get; set; }
        public DateTime? FROM_DATE { get; set; }
        public DateTime? TO_DATE { get; set; }
        public DateTime? LAST_UPDATE { get; set; }
        // Query metadata, not a column of PULLLIST_COSTROLLUP.
        public DateTime RUN_STARTED_AT { get; set; }
    }

    public sealed class CostRollupSnapshot
    {
        public DateTime RunStartedAt { get; set; }
        public IList<CostRollupRecord> Records { get; set; }
    }

    public interface ICostRollupStore
    {
        CostRollupSnapshot LoadSnapshot();
        void ResetForServiceStart(CostRollupRecord expected);
        void RecordFailure(string customer, string slip);
    }

    public sealed class CostRollupGuard
    {
        private readonly CostRollupOptions options;
        private readonly ICostRollupStore store;
        private readonly Action<string> log;
        // Admin schedules are frozen at run start. Actual SAP errors add a hold
        // immediately. Neither set is refreshed during this run.
        private readonly HashSet<string> snapshotHolds = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> detectedCustomers = new HashSet<string>(StringComparer.Ordinal);
        private bool started;
        private bool faulted;

        public CostRollupGuard(CostRollupOptions options, ICostRollupStore store, Action<string> log)
        {
            if (options == null) throw new ArgumentNullException("options");
            if (options.Enabled && store == null) throw new ArgumentNullException("store");
            this.options = options;
            this.store = store;
            this.log = log ?? delegate { };
        }

        public void Start()
        {
            if (!options.Enabled) { started = true; return; }
            if (faulted) throw new CostRollupException("Cost Rollup is stopped after a previous failure. Restart after correcting the problem.");
            if (started) return;
            try
            {
                var snapshot = store.LoadSnapshot();
                if (snapshot == null || snapshot.Records == null || snapshot.RunStartedAt == DateTime.MinValue)
                    throw new CostRollupException("Repository returned an invalid Cost Rollup snapshot.");
                var rows = new List<CostRollupRecord>();
                var customers = new HashSet<string>(StringComparer.Ordinal);
                // Validate and copy every row before changing any persistent data.
                foreach (var source in snapshot.Records)
                {
                    Validate(source);
                    if (!customers.Add(source.CUST_CODE))
                        throw new CostRollupException("Duplicate Cost Rollup customer: " + source.CUST_CODE);
                    rows.Add(new CostRollupRecord {
                        CUST_CODE = source.CUST_CODE, COSTROLLUP_STATUS = source.COSTROLLUP_STATUS,
                        CHANGE_BY = source.CHANGE_BY, MISLIP_NO_REF = source.MISLIP_NO_REF,
                        FROM_DATE = source.FROM_DATE, TO_DATE = source.TO_DATE, LAST_UPDATE = source.LAST_UPDATE
                    });
                }
                foreach (var row in rows)
                {
                    bool service = row.CHANGE_BY == "SERVICE";
                    bool admin = !string.IsNullOrEmpty(row.CHANGE_BY) && !service;
                    bool expired = row.TO_DATE.HasValue && snapshot.RunStartedAt > row.TO_DATE.Value;
                    bool reset = (service && row.COSTROLLUP_STATUS == "Y") ||
                        (admin && (row.COSTROLLUP_STATUS == "N" || expired));
                    if (reset)
                    {
                        // Conditional write may be a no-op if an admin edited after
                        // this snapshot. Current-run behavior still uses the snapshot.
                        store.ResetForServiceStart(row);
                        continue;
                    }
                    if (row.COSTROLLUP_STATUS == "Y" &&
                        (string.IsNullOrEmpty(row.CHANGE_BY) || !row.FROM_DATE.HasValue ||
                         (row.FROM_DATE.Value <= snapshot.RunStartedAt && snapshot.RunStartedAt <= row.TO_DATE.Value)))
                        snapshotHolds.Add(row.CUST_CODE);
                }
                started = true;
                log("Cost Rollup snapshot started at DB time " + snapshot.RunStartedAt.ToString("yyyy-MM-dd HH:mm:ss") +
                    "; active customer holds=" + snapshotHolds.Count + ". Admin/time changes apply next run.");
            }
            catch (Exception ex)
            {
                faulted = true;
                throw new CostRollupException("Cost Rollup startup failed; posting is stopped.", ex);
            }
        }

        public bool ShouldSkip(string slip)
        {
            if (!options.Enabled) return false;
            EnsureReady();
            string customer;
            if (!TryGetCustomerCode(slip, out customer))
            {
                log("Cost Rollup SKIP invalid slip format: " + slip);
                return true;
            }
            if (detectedCustomers.Contains(customer))
            {
                log("Cost Rollup SKIP customer=" + customer + " slip=" + slip + " (error observed this run)");
                return true;
            }
            if (snapshotHolds.Contains(customer))
            {
                log("Cost Rollup SKIP customer=" + customer + " slip=" + slip + " (run-start hold)");
                return true;
            }
            return false;
        }

        public void RecordFailure(string slip, string message)
        {
            if (!options.Enabled) return;
            EnsureReady();
            if (message == null || message.IndexOf(options.Message, StringComparison.OrdinalIgnoreCase) < 0) return;
            string customer;
            if (!TryGetCustomerCode(slip, out customer))
            {
                log("Cost Rollup cannot record customer for invalid slip: " + slip);
                return;
            }
            if (!detectedCustomers.Add(customer)) return;
            try
            {
                store.RecordFailure(customer, slip);
                log("Cost Rollup detected customer=" + customer + " slip=" + slip + "; later slips held. Existing admin ownership is preserved.");
            }
            catch (Exception ex)
            {
                faulted = true;
                throw new CostRollupException("Cost Rollup recording failed; posting is stopped.", ex);
            }
        }

        private void EnsureReady()
        {
            if (!started || faulted)
                throw new CostRollupException("Cost Rollup is not ready; posting is stopped.");
        }

        private static void Validate(CostRollupRecord row)
        {
            if (row == null || string.IsNullOrWhiteSpace(row.CUST_CODE) || row.CUST_CODE.Length != 2 ||
                row.CUST_CODE != row.CUST_CODE.Trim().ToUpperInvariant())
                throw new CostRollupException("Invalid Cost Rollup customer in snapshot. Expected two uppercase characters.");
            if (row.COSTROLLUP_STATUS != "Y" && row.COSTROLLUP_STATUS != "N")
                throw new CostRollupException("Invalid Cost Rollup status for customer " + row.CUST_CODE + ". Expected Y or N.");
            if (row.FROM_DATE.HasValue != row.TO_DATE.HasValue ||
                (row.FROM_DATE.HasValue && row.FROM_DATE.Value > row.TO_DATE.Value))
                throw new CostRollupException("Invalid From/To schedule for customer " + row.CUST_CODE + ". Supply both dates in order, or neither.");
        }

        public static bool TryGetCustomerCode(string slip, out string customer)
        {
            customer = null;
            if (string.IsNullOrWhiteSpace(slip)) return false;
            string value = slip.Trim();
            int first = value.IndexOf('-');
            if (first <= 0) return false;
            int second = value.IndexOf('-', first + 1);
            if (second < 0 || second == value.Length - 1) return false;
            string segment = value.Substring(first + 1, second - first - 1).Trim();
            if (segment.Length < 2) return false;
            string code = segment.Substring(segment.Length - 2).ToUpperInvariant();
            if (char.IsWhiteSpace(code[0]) || char.IsWhiteSpace(code[1])) return false;
            customer = code;
            return true;
        }
    }
}
