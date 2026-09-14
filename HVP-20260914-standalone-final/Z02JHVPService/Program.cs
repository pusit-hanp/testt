using System;
using System.ServiceProcess;
using System.Threading;

namespace Z02JHVPService
{
    static class Program
    {
        private static readonly ManualResetEvent _exitEvent = new ManualResetEvent(false);

        static int Main(string[] args)
        {
            bool background = args.Length == 1 && args[0] == "--background";
            if (args.Length != 0 && !background)
            {
                Console.Error.WriteLine("Invalid arguments. Usage: Z02JHVPService.exe [--background]");
                return 2;
            }

            try
            {
                using (HvpWinService service = new HvpWinService())
                {
                    if (background || Environment.UserInteractive)
                        RunStandalone(service, background);
                    else
                        ServiceBase.Run(new ServiceBase[] { service });
                }
                return 0;
            }
            catch (Exception ex)
            {
                // Exception messages and arguments may contain connection credentials.
                string message = "HOST FAILED type=" + ex.GetType().Name + "; check configuration, state/log access and earlier log entries.";
                try { HvpLog.Write(message); }
                catch { Console.Error.WriteLine(message); }
                return 1;
            }
        }

        private static void RunStandalone(HvpWinService service, bool background)
        {
            ConsoleCancelEventHandler cancelHandler = (sender, e) =>
            {
                e.Cancel = true;
                _exitEvent.Set();
            };
            Console.CancelKeyPress += cancelHandler;
            try
            {
                Console.WriteLine("Starting Z02JHVPService" + (background ? " in background mode..." : " in console mode..."));
                service.StartService();

                Console.WriteLine("[INFO] Service is running.");
                // Task Scheduler must never read stdin, even when a console is available.
                if (!background && !Console.IsInputRedirected)
                {
                    Console.WriteLine("[INFO] Press [Enter] or [Ctrl+C] to Stop & Exit...");
                    ThreadPool.QueueUserWorkItem(_ =>
                    {
                        try
                        {
                            Console.ReadLine();
                        }
                        catch { }
                        _exitEvent.Set();
                    });
                }

                _exitEvent.WaitOne();
            }
            finally
            {
                // Also stop a timer created by a partially failed StartService call.
                try { service.StopService(); }
                finally { Console.CancelKeyPress -= cancelHandler; }
            }
            Console.WriteLine("[INFO] Service stopped successfully.");
        }
    }
}
