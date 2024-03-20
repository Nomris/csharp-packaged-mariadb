using Newtonsoft.Json;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System;
using System.Data;

namespace Org.Websn.Utility
{
    public static partial class MariaDbPackager
    {
        public sealed class ManagedDatabase
        {
            private readonly Version _targetVersion;
            private readonly string _binaryDirectory;
            private readonly string _dataDirectory;
            private readonly string _socketPathOrPipeName;
            private readonly RuntimeConfig _config;

            /// <summary>
            /// The Version that is to be used by the Database
            /// </summary>
            public Version TargetVersion => _targetVersion;

            /// <summary>
            /// The Directory where the MariaDb Binaries are to be installed
            /// </summary>
            public string BinaryDirectory => _binaryDirectory;

            /// <summary>
            /// The Directory where the MariaDb Database files are stored
            /// </summary>
            public string DataDirectory => _dataDirectory;

            /// <summary>
            /// The name of the pipe on WindowsNT or the path to socket file on Unix
            /// </summary>
            public string SocketPathOrPipeName => _socketPathOrPipeName;

            /// <summary>
            /// The connection stirng for the Database
            /// </summary>
            public string ConnectionString => _config.ConnectionString;

            public ManagedDatabase(Version targetVersion, string binaryDirectory, string dataDirectory, string socketPathOrPipeName)
            {
                if (targetVersion == null) throw new ArgumentNullException(nameof(targetVersion));
                if (string.IsNullOrEmpty(binaryDirectory)) throw new ArgumentNullException(nameof(binaryDirectory));
                if (string.IsNullOrEmpty(dataDirectory)) throw new ArgumentNullException(nameof(dataDirectory));
                if (string.IsNullOrEmpty(socketPathOrPipeName)) throw new ArgumentNullException(nameof(socketPathOrPipeName));

                _targetVersion = targetVersion;
                _binaryDirectory = binaryDirectory.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                _dataDirectory = dataDirectory.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                _socketPathOrPipeName = socketPathOrPipeName;

                // Get Currently Installed Version
                InstalledFile currentInstall = null;
                if (File.Exists(binaryDirectory + Path.DirectorySeparatorChar + CurrentInstallFileName))
                    currentInstall = JsonConvert.DeserializeObject<InstalledFile>(File.ReadAllText(binaryDirectory + Path.DirectorySeparatorChar + CurrentInstallFileName));

                if (currentInstall == null || currentInstall.Version != targetVersion || currentInstall.PlatformID != Environment.OSVersion.Platform) // Check for Current and Target Version mismatch
                    InstallServerAsync(binaryDirectory, targetVersion.Major, targetVersion.Minor, targetVersion.Build, Environment.OSVersion.Platform).Wait(); // Install traget Version

                // Create Connection String
                StringBuilder sb = new StringBuilder();
                sb.Append("UID=root;Port=3306;Protocol=");
                switch (Environment.OSVersion.Platform)
                {
                    case PlatformID.Win32NT:
                        sb.Append("TCP;Host=127.254.231.2");
                        break;

                    case PlatformID.Unix:
                        sb.Append("Unix;Host=");
                        sb.Append(_socketPathOrPipeName);
                        break;
                }
                string defaultsFileDirectory = InitializeDatabaseDirectoryAsync(binaryDirectory, dataDirectory, _socketPathOrPipeName, Environment.OSVersion.Platform).Result;

                _config = new RuntimeConfig()
                {
                    ConnectionString = sb.ToString(),
                    ServiceStartInfo = new ProcessStartInfo() // Create StartInfo for starting the Database
                    {
                        FileName = $"{binaryDirectory}{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}mariadbd{(Environment.OSVersion.Platform == PlatformID.Win32NT ? ".exe" : "")}",
                        Arguments = $"--defaults-file=\"{defaultsFileDirectory}\" --skip-grant-tables",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        RedirectStandardInput = true,
                        CreateNoWindow = true
                    },
                    ShutdownStartInfo = new ProcessStartInfo() // Create StartInfo for stopping the Database
                    {
                        FileName = $"{binaryDirectory}{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}mariadb{(Environment.OSVersion.Platform == PlatformID.Win32NT ? ".exe" : "")}",
                        Arguments = $"--defaults-file=\"{defaultsFileDirectory}\" -e SHUTDOWN",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        RedirectStandardInput = true,
                        CreateNoWindow = true
                    }
                };
            }

            /// <summary>
            /// Create a <see cref="IDbConnection"/> for the ManagedDatabase
            /// </summary>
            public IDbConnection GetConnection() => _config.GetConnection();

            public Process StartDatabase()
            {
                return _config.Start();
            }

            public async Task ShutdownAsync() 
            {
                await _config.StopAsync();
            }
        }
    }
}
