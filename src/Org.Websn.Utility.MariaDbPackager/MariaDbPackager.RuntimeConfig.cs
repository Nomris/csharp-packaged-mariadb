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

            /// <summary>
            /// The Connection-String needed to connect to the Database
            /// </summary>
            public string ConnectionString { get; internal set; }

            /// <summary>
            /// The <see cref="ProcessStartInfo"/> for starting the MariaDB server, use <see cref="Start"/> insted
            /// </summary>
            public ProcessStartInfo ServiceStartInfo { get; internal set; }

            /// <summary>
            /// The <see cref="ProcessStartInfo"/> for stopping the MariaDB server, use <see cref="Stop"/> or <see cref="StopAsync"/> insted
            /// </summary>
            public ProcessStartInfo ShutdownStartInfo { get; internal set; }

            /// <summary>
            /// Starts the MariaDB server and returns the <see cref="Process"/> representing the MariaDB server
            /// </summary>
            /// <returns>The refrence to the MariaDB server</returns>
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

            /// <summary>
            /// Stops the MariaDB server
            /// </summary>
            /// <exception cref="Exception">Will be raised if the MariaDB server didn't stop corectly</exception>
            public async Task<int> StopAsync()
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

                        return -1;
                    }

#if NET5_0_OR_GREATER
                    await _process.WaitForExitAsync();
#else
                    _process.WaitForExit();
#endif
                }

                try
                {
                    return _process.ExitCode;                }
                finally
                {
                    _process.Dispose();
                    _process = null;
                }
            }

            /// <summary>
            /// Stops the MariaDB server
            /// </summary>
            /// <exception cref="Exception">Will be raised if the MariaDB server didn't stop corectly</exception>
            public void Stop() => StopAsync().Wait();

            /// <summary>
            /// Get's a <see cref="IDbConnection"/> capable of connecting to the MariaDB server
            /// </summary>
            public IDbConnection GetConnection()
            {
                return new MySqlConnector.MySqlConnection(ConnectionString);
            }
        }
    }
}
