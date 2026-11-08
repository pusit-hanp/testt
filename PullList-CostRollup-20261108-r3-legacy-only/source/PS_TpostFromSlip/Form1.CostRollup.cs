using System;
using System.Configuration;
using PS_TpostFromSlip.CostRollup;

namespace PS_TpostFromSlip
{
    public partial class Form1
    {
        private CostRollupGuard costRollupGuard;
        private bool processRunActive;

        private void InitializeCostRollup()
        {
            var options = CostRollupOptions.FromStrings(
                ConfigurationManager.AppSettings["Check_CostRollUp"],
                ConfigurationManager.AppSettings["Check_CostRollUp_Msg"]);
            ICostRollupStore store = options.Enabled ? new MyRepositoryCostRollupStore(ConnectionString) : null;
            costRollupGuard = new CostRollupGuard(options, store, message => log.Info(message));
            costRollupGuard.Start();
        }

        private bool ShouldSkipForCostRollup(string slip)
        {
            if (costRollupGuard == null)
                throw new CostRollupException("Cost Rollup configuration was not initialized; posting is stopped.");
            return costRollupGuard.ShouldSkip(slip);
        }

        private void RecordCostRollupFailure(string slip, string message)
        {
            if (costRollupGuard == null)
                throw new CostRollupException("Cost Rollup configuration was not initialized; posting is stopped.");
            costRollupGuard.RecordFailure(slip, message);
        }
    }
}
