using System;
using System.Data;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Org.Websn.Utility
{
    public static partial class MariaDbPackager
    {
        public sealed class RuntimeConfig
        {
            [DllImport("libc", SetLastError = true)]
            private static extern int kill(int pid, int sig);

            private Process _process;

            public string ConnectionString { get; internal set; }

            public ProcessStartInfo ServiceStartInfo { get; internal set; }

            public ProcessStartInfo ShutdownStartInfo { get; internal set; }

            public Process Start()
            {
                if (_process != null)
                {
                    if (!_process.HasExited) return _process;
                    _process.Dispose();
                }
                _process = new Process();
                _process.StartInfo = ServiceStartInfo;
                _process.Start();

                return _process;
            }

            public void Stop()
            {
                if (!_process.HasExited)
                {

                    Process shutdownProcess = Process.Start(ShutdownStartInfo);

                    shutdownProcess.WaitForExit();

                    if (shutdownProcess.ExitCode != 0)
                    {
                        switch (Environment.OSVersion.Platform)
                        {
                            case PlatformID.Unix:
                                kill(_process.Id, 15);
                                break;

                            case PlatformID.Win32NT:
                                _process.Kill();
                                break;
                        }

                        return;
                    }

                    _process.WaitForExit();
                }

                try
                {
                    if (_process.ExitCode != 0) throw new Exception("MariaDB Server exit code none-zero (" + _process.ExitCode + ")");
                }
                finally
                {
                    _process.Dispose();
                    _process = null;
                }
            }

            public IDbConnection GetConnection()
            {
                return new MySqlConnector.MySqlConnection(ConnectionString);
            }
        }
    }
}
