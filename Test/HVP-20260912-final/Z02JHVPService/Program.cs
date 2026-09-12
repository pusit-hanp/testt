using System;
using System.ServiceProcess;
using System.Threading;

namespace Z02JHVPService
{
    static class Program
    {
        private static readonly ManualResetEvent _exitEvent = new ManualResetEvent(false);

        static void Main()
        {
            if (Environment.UserInteractive)
            {
                Console.WriteLine(" Starting Z02JHVPService in Console Debug Mode... ");

                HvpWinService service = new HvpWinService();
                service.StartService();

                Console.WriteLine("\n[INFO] Service is running in background.");
                Console.WriteLine("[INFO] Press [Enter] or [Ctrl+C] to Stop & Exit...\n");

                Console.CancelKeyPress += (sender, e) =>
                {
                    e.Cancel = true;
                    _exitEvent.Set();
                };

                if (!Console.IsInputRedirected)
                {
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

                Console.WriteLine("[INFO] Stopping Z02JHVPService...");
                service.StopService();
                Console.WriteLine("[INFO] Service stopped successfully.");
            }
            else
            {
                ServiceBase[] ServicesToRun = new ServiceBase[]
                {
                    new HvpWinService()
                };
                ServiceBase.Run(ServicesToRun);
            }
        }
    }
}
