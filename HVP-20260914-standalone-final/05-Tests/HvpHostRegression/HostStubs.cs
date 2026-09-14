// Boundary doubles only. Program.cs is compiled unchanged into each child process.
using System;
using System.IO;
using System.Reflection;
using System.Threading;

namespace HostRegression
{
    internal static class Trace
    {
        private static readonly object Sync = new object();
        internal static bool Flag(string name) { return System.Environment.GetEnvironmentVariable(name) == "1"; }
        internal static void Write(string message)
        {
            lock (Sync) File.AppendAllText(System.Environment.GetEnvironmentVariable("HVP_HOST_TRACE"), message + "\n");
        }
    }

    internal static class Entry
    {
        // Reflection accommodates the original Main() while running its actual body.
        private static int Main(string[] args)
        {
            try
            {
                MethodInfo main = typeof(Z02JHVPService.Program).GetMethod("Main", BindingFlags.Static | BindingFlags.NonPublic);
                object result = main.Invoke(null, main.GetParameters().Length == 0 ? null : new object[] { args });
                return result == null ? 0 : (int)result;
            }
            catch (TargetInvocationException ex)
            {
                // A fatal error that escapes Program would otherwise crash/WER.
                // The marker makes that escape observable without launching a dialog.
                Trace.Write("UNHANDLED " + ex.InnerException.Message);
                return 101;
            }
        }
    }
}

namespace System.ServiceProcess
{
    public class ServiceBase : IDisposable
    {
        public static void Run(ServiceBase[] services)
        {
            HostRegression.Trace.Write("SERVICE_RUN count=" + services.Length);
            if (HostRegression.Trace.Flag("HVP_HOST_SERVICE_FAIL"))
                throw new InvalidOperationException("Password=host-secret-service");
        }
        public void Dispose() { HostRegression.Trace.Write("DISPOSE"); }
    }
}

namespace Z02JHVPService
{
    // These wrappers select host boundaries that a CI child cannot otherwise control.
    // Console I/O itself still uses real redirected OS streams.
    internal static class Environment
    {
        internal static bool UserInteractive { get { return HostRegression.Trace.Flag("HVP_HOST_INTERACTIVE"); } }
    }

    internal static class Console
    {
        internal static TextWriter Error { get { return System.Console.Error; } }
        internal static bool IsInputRedirected { get { return !HostRegression.Trace.Flag("HVP_HOST_CONSOLE_INPUT"); } }
        internal static event ConsoleCancelEventHandler CancelKeyPress
        {
            add { System.Console.CancelKeyPress += value; }
            remove { System.Console.CancelKeyPress -= value; }
        }
        internal static void WriteLine(string value) { System.Console.WriteLine(value); }
        internal static string ReadLine()
        {
            HostRegression.Trace.Write("READ_STDIN");
            return System.Console.ReadLine();
        }
    }

    public class HvpWinService : System.ServiceProcess.ServiceBase
    {
        private Timer timer;
        public HvpWinService()
        {
            HostRegression.Trace.Write("CONSTRUCT");
            if (HostRegression.Trace.Flag("HVP_HOST_CONSTRUCTOR_FAIL"))
                throw new InvalidOperationException("Password=host-secret-constructor");
        }
        public void StartService()
        {
            HostRegression.Trace.Write("START");
            timer = new Timer(_ => HostRegression.Trace.Write("TICK"), null, 20, 20);
            if (HostRegression.Trace.Flag("HVP_HOST_START_FAIL"))
                throw new InvalidOperationException("Password=host-secret-start");
        }
        public void StopService()
        {
            if (timer != null) { timer.Dispose(); timer = null; }
            HostRegression.Trace.Write("STOP");
            if (HostRegression.Trace.Flag("HVP_HOST_STOP_FAIL"))
                throw new InvalidOperationException("Password=host-secret-stop");
        }
    }

    internal static class HvpLog
    {
        internal static void Write(string message)
        {
            if (HostRegression.Trace.Flag("HVP_HOST_LOG_FAIL"))
                throw new IOException("Password=host-secret-log");
            HostRegression.Trace.Write("LOG " + message);
        }
    }
}
