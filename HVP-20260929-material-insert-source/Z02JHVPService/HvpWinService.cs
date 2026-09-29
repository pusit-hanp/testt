using System;
using System.Configuration;
using System.ServiceProcess;
using System.Threading;
using System.Timers;
using Timer = System.Timers.Timer;

namespace Z02JHVPService
{
    public partial class HvpWinService : ServiceBase
    {
        private Timer _timer;
        private readonly HvpFileProcessor _processor;
        private readonly object _jobLock = new object();
        private volatile bool _stopping;

        public HvpWinService()
        {
            this.ServiceName = "Z02JHVPService";
            _processor = new HvpFileProcessor();
        }

        protected override void OnStart(string[] args) { StartService(); }

        public void StartService()
        {
            int minutes;
            if (!int.TryParse(ConfigurationManager.AppSettings["TimerIntervalMinutes"], out minutes) || minutes <= 0)
                minutes = 5;
            // Fail startup visibly if the account cannot write the state/log directory.
            HvpLog.Write("SERVICE START intervalMinutes=" + minutes);
            _stopping = false;
            _timer = new Timer(minutes * 60d * 1000d);
            _timer.Elapsed += OnTimerElapsed;
            _timer.AutoReset = true;
            _timer.Start();
            // Return promptly to the Windows Service Control Manager.
            ThreadPool.QueueUserWorkItem(_ => ExecuteJob());
        }

        private void OnTimerElapsed(object sender, ElapsedEventArgs e) { ExecuteJob(); }

        private void ExecuteJob()
        {
            if (!Monitor.TryEnter(_jobLock)) return;
            try
            {
                if (_stopping) return;
                _processor.ProcessFiles();
            }
            catch (Exception ex)
            {
                // Do not log connection strings or raw exception text containing credentials.
                try { HvpLog.Write("JOB FAILED type=" + ex.GetType().Name + "; check source/state access and earlier log entries."); }
                catch { Console.Error.WriteLine("Cannot write HVP log; check service account permissions."); }
            }
            finally { Monitor.Exit(_jobLock); }
        }

        public void StopService()
        {
            _stopping = true;
            if (_timer != null) { _timer.Stop(); _timer.Dispose(); _timer = null; }
            lock (_jobLock) { }
        }

        protected override void OnStop() { StopService(); }
    }
}
