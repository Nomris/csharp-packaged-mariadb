﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Resources;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

using Newtonsoft.Json;

using SharpCompress.Common.Rar;
using SharpCompress.Common.Tar;
using SharpCompress.Readers;
using SharpCompress.Readers.GZip;
using SharpCompress.Readers.Tar;
using SharpCompress.Readers.Zip;

namespace Org.Websn.Utility
{
    public static partial class MariaDbPackager
    {
        private const string RestBase = "https://downloads.mariadb.org/rest-api";
        private static HttpClient _client = new HttpClient(new HttpClientHandler()
        {
            AllowAutoRedirect = true,
        })
        {
            Timeout = TimeSpan.FromHours(24),
            DefaultRequestHeaders =
            {
                { "User-Agent", "httpclient" }
            }
        };

        public static async Task<RuntimeConfig> EnsureServerAsync(string managedDirectory, string targetConfigFile = null, string overwriteInstallationDirectory = null, string overwriteDataDirectory = null, string overwriteSocketFileOrPipeName = null)
        {
            if (string.IsNullOrEmpty(targetConfigFile))
            {
                if (!Directory.Exists(managedDirectory)) throw new DirectoryNotFoundException(managedDirectory);
                targetConfigFile = managedDirectory + Path.DirectorySeparatorChar + "target_installation.json";
            }

            if (!File.Exists(targetConfigFile)) throw new FileNotFoundException(targetConfigFile);
            TargetConfiguration configuration = JsonConvert.DeserializeObject<TargetConfiguration>(File.ReadAllText(targetConfigFile));

            if (configuration.Mode == InstallationMode.Manuel)
            {
                if (string.IsNullOrEmpty(configuration.ConnectionString)) throw new InvalidDataException("No connection string (connection_string) was provided while MODE=MANUEL");

                return new RuntimeConfig()
                {
                    ConnectionString = configuration.ConnectionString
                };
            }

            string installationDirectory = managedDirectory + Path.DirectorySeparatorChar + "bin";
            string dataDirectory = managedDirectory + Path.DirectorySeparatorChar + "data";
            string socketFileOrPipeName = null;

            switch (Environment.OSVersion.Platform)
            {
                case PlatformID.Win32NT:
                    socketFileOrPipeName = "embedded-maria-db";
                    break;

                case PlatformID.Unix:
                    socketFileOrPipeName = managedDirectory + Path.DirectorySeparatorChar + "db.sock";
                    break;
            }

            if (!string.IsNullOrEmpty(overwriteInstallationDirectory)) installationDirectory = overwriteInstallationDirectory;
            if (!string.IsNullOrEmpty(overwriteDataDirectory)) dataDirectory = overwriteDataDirectory;
            if (!string.IsNullOrEmpty(overwriteSocketFileOrPipeName)) socketFileOrPipeName = overwriteSocketFileOrPipeName;

            InstalledFile installedVersion = null;
            if (File.Exists(installationDirectory + Path.DirectorySeparatorChar + "current_install.json"))
                installedVersion = JsonConvert.DeserializeObject<InstalledFile>(File.ReadAllText(installationDirectory + Path.DirectorySeparatorChar + "current_install.json"));
            
            Version targetVersion = configuration.Version;
            if (targetVersion == null)
                targetVersion = await GetLatestServerVersionAsync(configuration.ReleaseChannel.HasValue ? configuration.ReleaseChannel.Value : ReleaseChannel.Stable, configuration.SupportType);

            if (installedVersion == null || installedVersion.PlatformID != Environment.OSVersion.Platform || installedVersion.Version != targetVersion)
                await InstallServerAsync(installationDirectory, targetVersion.Major, targetVersion.Minor, targetVersion.Build, Environment.OSVersion.Platform);

            StringBuilder sb = new StringBuilder();
            sb.Append("UID=root;Protocol=");
            switch (Environment.OSVersion.Platform)
            {
                case PlatformID.Win32NT:
                    sb.Append("Pipe;Host=.;Pipe=");
                    break;

                case PlatformID.Unix:
                    sb.Append("Unix;Port=3306;Host=");
                    break;
            }
            sb.Append(socketFileOrPipeName);

            return new RuntimeConfig()
            {
                ConnectionString = sb.ToString(),
                ServiceStartInfo = new ProcessStartInfo()
                {
                    FileName = $"{installationDirectory}{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}mariadbd{(Environment.OSVersion.Platform == PlatformID.Win32NT ? ".exe" : "")}",
                    Arguments = $"--defaults-file=\"{await InitializeDatabaseDirectoryAsync(installationDirectory, dataDirectory, socketFileOrPipeName, Environment.OSVersion.Platform)}\" --skip-grant-tables",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    RedirectStandardInput = true,
                    CreateNoWindow = true
                },
                ShutdownStartInfo = new ProcessStartInfo()
                {
                    FileName = $"{installationDirectory}{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}mariadb{(Environment.OSVersion.Platform == PlatformID.Win32NT ? ".exe" : "")}",
                    Arguments = $"--defaults-file=\"{await InitializeDatabaseDirectoryAsync(installationDirectory, dataDirectory, socketFileOrPipeName, Environment.OSVersion.Platform)}\" -e SHUTDOWN",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    RedirectStandardInput = true,
                    CreateNoWindow = true
                }
            };
        }

        private static async Task<string> InitializeDatabaseDirectoryAsync(string installationDirectory, string dataDirectory, string socketFileOrNamePipe, PlatformID? platform = null)
        {
            if (Environment.OSVersion.Platform != PlatformID.Unix && Environment.OSVersion.Platform != PlatformID.Win32NT)
                throw new PlatformNotSupportedException($"{Environment.OSVersion.Platform} is not supported");
            if (platform == null) platform = Environment.OSVersion.Platform;

            string dbConfig = dataDirectory + Path.DirectorySeparatorChar + "db-mariadb.cnf";
            dataDirectory += Path.DirectorySeparatorChar + "db";

            if (!Directory.Exists(Path.GetDirectoryName(dbConfig))) Directory.CreateDirectory(Path.GetDirectoryName(dbConfig));

            using (FileStream fs = new FileStream(dbConfig, FileMode.Create, FileAccess.Write, FileShare.None))
            using (StreamWriter writer = new StreamWriter(fs))
            {
                await writer.WriteLineAsync("[mysqld]");
                await writer.WriteLineAsync("skip-networking=true");
                await writer.WriteLineAsync("user=root");
                await writer.WriteLineAsync("port=3306");
                await writer.WriteAsync("datadir='");
                await writer.WriteAsync(platform.Value == PlatformID.Win32NT ? dataDirectory.Replace('\\', '/') : dataDirectory);
                await writer.WriteLineAsync("'");
                switch (platform)
                {
                    case PlatformID.Win32NT:
                        await writer.WriteLineAsync("named_pipe=true");
                        await writer.WriteLineAsync("plugin_load_add=auth_named_pipe");
                        break;
                    case PlatformID.Unix:
                        await writer.WriteLineAsync("plugin_load_add=auth_unix_socket");
                        break;
                }
                

                await writer.WriteAsync("socket=");
                if (platform == PlatformID.Unix) await writer.WriteAsync('\'');
                await writer.WriteAsync(socketFileOrNamePipe);
                if (platform == PlatformID.Unix) await writer.WriteAsync('\'');
                await writer.WriteLineAsync();

                await writer.WriteLineAsync();

                await writer.WriteLineAsync("[client]");
                await writer.WriteLineAsync("port=3306");
                await writer.WriteLineAsync("user=root");
                if (platform == PlatformID.Win32NT)
                    await writer.WriteLineAsync("pipe");

                await writer.WriteAsync("socket=");
                if (platform == PlatformID.Unix) await writer.WriteAsync('\'');
                await writer.WriteAsync(socketFileOrNamePipe);
                if (platform == PlatformID.Unix) await writer.WriteAsync('\'');
                await writer.WriteLineAsync();

                await writer.FlushAsync();
            }

            if (!Directory.Exists(dataDirectory))
            {
                Process process = null;
                try
                {
                    switch (platform)
                    {
                        case PlatformID.Win32NT:
                            process = Process.Start(new ProcessStartInfo()
                            {
                                FileName = installationDirectory + Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar + "mysql_install_db.exe",
                                Arguments = $"--config={dbConfig}",
                                UseShellExecute = false,
                                RedirectStandardOutput = true,
                                RedirectStandardError = true,
                                RedirectStandardInput = true,
                            });
                            break;

                        case PlatformID.Unix:
                            process = Process.Start(new ProcessStartInfo()
                            {
                                FileName = "/bin/sh",
                                Arguments = installationDirectory + Path.DirectorySeparatorChar + "scripts" + Path.DirectorySeparatorChar + $"mariadb-install-db --defaults-file={dbConfig}",
                                UseShellExecute = false,
                                RedirectStandardOutput = true,
                                RedirectStandardError = true,
                                RedirectStandardInput = true,
                            });
                            break;
                    }

#if NET5_0_OR_GREATER
                    await process.WaitForExitAsync();
#else
                    process.WaitForExit();
#endif

                    if (process.ExitCode != 0) throw new Exception("Data Directory Initialization faild (" + process.ExitCode + ")");
                }
                catch (Exception exc)
                {
                    Directory.Delete(dataDirectory, true);

                    throw new Exception(null, exc);
                }
            }

            return dbConfig;
        }

        public static async Task InstallLatestServerAsync(string installationDirectory, ReleaseChannel channel = ReleaseChannel.Stable, SupportClass? supportClass = null, PlatformID? platform = null)
        {
            if (Environment.OSVersion.Platform != PlatformID.Unix && Environment.OSVersion.Platform != PlatformID.Win32NT)
                throw new PlatformNotSupportedException($"{Environment.OSVersion.Platform} is not supported");
            if (platform == null) platform = Environment.OSVersion.Platform;

            Version latestVersion = await GetLatestServerVersionAsync(channel, supportClass);
            InstalledFile installedVersion = JsonConvert.DeserializeObject<InstalledFile>(File.ReadAllText(installationDirectory + Path.DirectorySeparatorChar + "current_install.json"));
            if (latestVersion == installedVersion.Version && platform.Value == installedVersion.PlatformID) return;

            await InstallServerAsync(installationDirectory, latestVersion.Major, latestVersion.Minor, latestVersion.Build, platform);
        }

        public static async Task<Version> GetLatestServerVersionAsync(ReleaseChannel channel = ReleaseChannel.Stable, SupportClass? supportClass = null)
        {
            HttpResponseMessage response = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Get, $"{RestBase}/mariadb/"));
            response.EnsureSuccessStatusCode();

            Release release = JsonConvert.DeserializeObject<Dictionary<string, List<Release>>>(await response.Content.ReadAsStringAsync())["major_releases"].Where(e =>
            {
                switch (channel)
                {
                    case ReleaseChannel.Stable:
                        return e.Status.ToLower() == "stable";
                    case ReleaseChannel.Canidate:
                        return e.Status.ToLower() == "rc" || e.Status.ToLower() == "stable";
                    default:
                    case ReleaseChannel.Alpha:
                        return true;
                }
            }).Where(e =>
            {
                switch (supportClass)
                {
                    case SupportClass.LongTermSupport:
                        return e.SupportType.ToLower() == "long term support";

                    case SupportClass.ShortTermSupport:
                        return e.SupportType.ToLower() == "short term support" || e.SupportType.ToLower() == "long term support";

                    default:
                        return true;
                }
            }).OrderBy(e => e.Version).Reverse().FirstOrDefault();
            if (release == null) throw new Exception("Faild to get available version");


            response = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Get, $"{RestBase}/mariadb/{release.Version.Major}.{release.Version.Minor}"));
            response.EnsureSuccessStatusCode();

            return JsonConvert.DeserializeObject<Dictionary<string, Dictionary<Version, Release>>>(await response.Content.ReadAsStringAsync())["releases"].Where(e =>
            {
                switch (channel)
                {
                    case ReleaseChannel.Stable:
                        return !e.Value.Name.ToLower().EndsWith("rc") && !e.Value.Name.ToLower().EndsWith("alpha");
                    case ReleaseChannel.Canidate:
                        return !e.Value.Name.ToLower().EndsWith("alpha");
                    default:
                    case ReleaseChannel.Alpha:
                        return true;
                }
            }).OrderBy(e => e.Key).Reverse().FirstOrDefault().Key;
        }

        public static async Task InstallServerAsync(string installationDirectory, int major, int minor, int? build = null, PlatformID? platform = null)
        {
            if (Environment.OSVersion.Platform != PlatformID.Unix && Environment.OSVersion.Platform != PlatformID.Win32NT)
                throw new PlatformNotSupportedException($"{Environment.OSVersion.Platform} is not supported");
            if (platform == null) platform = Environment.OSVersion.Platform;

            installationDirectory = installationDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string safeInstallDirectory = installationDirectory + $".new{Path.DirectorySeparatorChar}";


            if (!Directory.Exists(safeInstallDirectory)) 
                Directory.CreateDirectory(safeInstallDirectory);
            else
                foreach (string fsItem in Directory.EnumerateFileSystemEntries(safeInstallDirectory))
                {
                    if (Directory.Exists(fsItem)) Directory.Delete(fsItem, true);
                    if (File.Exists(fsItem)) File.Delete(fsItem);
                }

            string queryBase = $"{RestBase}/mariadb/{major}.{minor}";
            if (build.HasValue) queryBase += $".{build}";

            HttpResponseMessage response = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Get, queryBase));
            response.EnsureSuccessStatusCode();

            Release release = JsonConvert.DeserializeObject<Dictionary<string, Dictionary<Version, Release>>>(await response.Content.ReadAsStringAsync())[build.HasValue ? "release_data" : "releases"].OrderBy(e => e.Key).Reverse().ToDictionary(keySelector: e => e.Key, elementSelector: e => e.Value).First().Value;
            IEnumerable<Release.FileObject> validFiles = release.Files
                .Where(e => e.PackageType != null)
                .Where(e => e.PackageType.ToLower() == "zip file" || e.PackageType.ToLower() == "gzipped tar file");

            File.WriteAllText(safeInstallDirectory + Path.DirectorySeparatorChar + "current_install.json", JsonConvert.SerializeObject(new InstalledFile()
            {
                Version = release.Version,
                PlatformID = platform.Value
            }));

            switch (platform)
            {
                case PlatformID.Unix:
                    validFiles = validFiles.Where(e => e.Platform.ToLower() == "linux");
                    break;

                case PlatformID.Win32NT:
                    validFiles = validFiles.Where(e => e.Platform.ToLower() == "windows");
                    break;
            }

            validFiles = validFiles.OrderBy(e => e.Name.ToLower().Contains("debug") ? 1 : -1);

            Release.FileObject file = validFiles.FirstOrDefault();

            if (file == null) throw new FileNotFoundException("Faild to get any server files");

            async Task ExtractArchiveAsync(IReader reader)
            {
                string path;
                while (reader.MoveToNextEntry())
                {
                    path = reader.Entry.Key;
                    path = path.Substring(path.IndexOf('/')).Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
                    path = safeInstallDirectory + Path.DirectorySeparatorChar + path.TrimStart(Path.DirectorySeparatorChar);

                    if (reader.Entry.IsDirectory)
                    {
                        Directory.CreateDirectory(path);
                        continue;
                    }

                    using (Stream entryStream = reader.OpenEntryStream())
                    using (FileStream fs = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
                        await entryStream.CopyToAsync(fs);

                    if (reader.Entry is TarEntry tarEntry && Environment.OSVersion.Platform == PlatformID.Unix)
                        Process.Start("/bin/chmod", $"{tarEntry.Mode >> 6 & 0b111}{tarEntry.Mode >> 3 & 0b111}{tarEntry.Mode & 0b111} \"{path}\"").WaitForExit();
                }
            }

            switch (file.PackageType.ToLower()) 
            {
                case "zip file":
                    using (FileStream fs = new FileStream(await DownloadFileAsync(file.Id, "zip"), FileMode.Open, FileAccess.Read, FileShare.Read))
                    using (IReader archiveReader = ZipReader.Open(fs))
                        await ExtractArchiveAsync(archiveReader);
                    break;

                case "gzipped tar file":
                    using (FileStream fs = new FileStream(await DownloadFileAsync(file.Id, "tar.gz"), FileMode.Open, FileAccess.Read, FileShare.Read))
                    using (GZipReader gzReader = GZipReader.Open(fs))
                    {
                        if (!gzReader.MoveToNextEntry()) throw new InvalidDataException("Faild to open .gz file");
                        using (Stream entryStream = gzReader.OpenEntryStream())
                        using (IReader archiveReader = TarReader.Open(entryStream))
                            await ExtractArchiveAsync(archiveReader);
                    }
                    break;
            }

            if (Directory.Exists(installationDirectory + ".old")) Directory.Delete(installationDirectory + ".old", true);
            if (Directory.Exists(installationDirectory)) Directory.Move(installationDirectory, installationDirectory + ".old");
            Directory.Move(safeInstallDirectory, installationDirectory);
        }

        private static async Task<string> DownloadFileAsync(int id, string extension = null)
        {
            if (Environment.OSVersion.Platform != PlatformID.Unix && Environment.OSVersion.Platform != PlatformID.Win32NT)
                throw new PlatformNotSupportedException($"{Environment.OSVersion.Platform} is not supported");

            if (string.IsNullOrEmpty(extension)) extension = "bin.tmp";

            string fileBaseUrl = $"{RestBase}/{id}";

            HttpResponseMessage response = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Get, fileBaseUrl));
            response.EnsureSuccessStatusCode();

            HttpResponseMessage checksumResponse = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Get, $"{fileBaseUrl}/checksum"));
            checksumResponse.EnsureSuccessStatusCode();

            string str512hash = JsonConvert.DeserializeObject<Dictionary<string, Dictionary<string, Dictionary<string, string>>>>(await checksumResponse.Content.ReadAsStringAsync())["response"]["checksum"]["sha512sum"];
            if (str512hash.Length % 2 != 0) str512hash = $"0{str512hash}";

            int i = -1, strHalfLength = str512hash.Length / 2;
            byte[] _512hash = new byte[strHalfLength];
            while (++i < strHalfLength) _512hash[i] = Convert.ToByte(str512hash.Substring(i * 2, 2), fromBase: 16);


            Stream stream = await response.Content.ReadAsStreamAsync();
#if NET5_0_OR_GREATER
            byte[] computedHash = await SHA512.Create().ComputeHashAsync(stream);
#else
            byte[] computedHash = SHA512.Create().ComputeHash(stream);
#endif
            if (!computedHash.SequenceEqual(_512hash)) throw new InvalidDataException("The Hashes didn't match");
            stream.Seek(0, SeekOrigin.Begin);
            
            Random rand;
            unchecked { rand = new Random((int)DateTime.UtcNow.Ticks); }
            string downloadFile = $"mariadb_{id}_{new int[3].Aggregate("", (acum, i2) => acum + rand.Next('a', 'z').ToString("x2"))}.{extension}";
            switch (Environment.OSVersion.Platform)
            {
                case PlatformID.Unix:
                    downloadFile = $"/tmp/{downloadFile}";
                    break;

                case PlatformID.Win32NT:
                    downloadFile = $"{Environment.GetEnvironmentVariable("TMP")}\\{downloadFile}";
                    break;
            }

            using (FileStream tmpFile = new FileStream(downloadFile, FileMode.CreateNew, FileAccess.Write))
                 await stream.CopyToAsync(tmpFile);

            switch (Environment.OSVersion.Platform)
            {
                case PlatformID.Win32NT:
                    File.SetAttributes(downloadFile, FileAttributes.Temporary);
                    break;
                case PlatformID.Unix:
                    Process.Start("/bin/chmod", $"644 \"{downloadFile}\"").WaitForExit();
                    break;
            }
            return downloadFile;
        }
    }
}