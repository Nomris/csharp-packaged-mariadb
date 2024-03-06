using System;
using System.Data;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

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

            public async Task StopAsync()
            {
                if (!_process.HasExited)
                {

                    Process shutdownProcess = await Task.Run(() => Process.Start(ShutdownStartInfo));

#if NET5_0_OR_GREATER
                    await shutdownProcess.WaitForExitAsync();
#else
                    shutdownProcess.WaitForExit();
#endif
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

#if NET5_0_OR_GREATER
                    await _process.WaitForExitAsync();
#else
                    _process.WaitForExit();
#endif
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

            public void Stop() => StopAsync().Wait();

            public IDbConnection GetConnection()
            {
                return new MySqlConnector.MySqlConnection(ConnectionString);
            }
        }
    }
}
