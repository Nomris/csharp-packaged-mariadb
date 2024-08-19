using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Org.Websn.Utility;

namespace Test
{
    public class ManagedTest
    {

        private string _testDirectory;
        private MariaDbPackager.ManagedDatabase _managedDatabase;

        [SetUp]
        public void Setup()
        {
            _testDirectory = Path.Combine(Path.GetDirectoryName(typeof(InstallTests).Assembly.Location), $".test-cache\\{DateTime.UtcNow:yyyyMMdd-HHmmss-fffffff}\\");
            Directory.CreateDirectory(_testDirectory);

            _managedDatabase = new MariaDbPackager.ManagedDatabase(new Version(11, 3, 2), Path.Combine(_testDirectory, "install"), Path.Combine(_testDirectory, "install"), Environment.OSVersion.Platform switch
            {
                PlatformID.Win32NT => "embed-mariadb-test",
                PlatformID.Unix => "/tmp/embed-mariadb-test.sock"
            });
        }

        [Test]
        public async Task RunWithoutQuery()
        {
            Process process = _managedDatabase.StartDatabase();

            await Task.Delay(1000);

            if (process.HasExited) Assert.Fail();

            await _managedDatabase.ShutdownAsync();

            Assert.Pass();
        }

        [Test]
        public async Task RunWithQuery()
        {
            Process process = _managedDatabase.StartDatabase();

            await Task.Delay(1000);

            if (process.HasExited) Assert.Fail();

            using (IDbConnection connection = _managedDatabase.GetConnection())
            {
                connection.Open();

                using (IDbCommand command = connection.CreateCommand())
                {
                    command.CommandText = "CREATE DATABASE `test`";
                    command.ExecuteNonQuery();
                    connection.ChangeDatabase("test");

                    command.CommandText = "CREATE TABLE `test` ( `id` VARCHAR(12) PRIMARY KEY)";
                    command.ExecuteNonQuery();

                    command.CommandText = "INSERT INTO `test` (`id`) VALUES ('12'), ('14'), ('17')";
                    command.ExecuteNonQuery();

                    command.CommandText = "SELECT * FROM `test`";
                    using (IDataReader reader = command.ExecuteReader())
                    {
                        if (!reader.Read()) Assert.Fail();
                        if (reader.GetString(0) != "12") Assert.Fail();
                        if (!reader.Read()) Assert.Fail();
                        if (reader.GetString(0) != "14") Assert.Fail();
                        if (!reader.Read()) Assert.Fail();
                        if (reader.GetString(0) != "17") Assert.Fail();
                    }
                }

                connection.Close();
            }

            await _managedDatabase.ShutdownAsync();

            Assert.Pass();
        }

        [TestCase(5)]
        public async Task Restarts(int cycles)
        {
            while (cycles-- > 0)
            {
                Process process = _managedDatabase.StartDatabase();

                await Task.Delay(1000);

                if (process.HasExited) Assert.Fail(string.Format("Remaing Cycles: {0}", cycles));

                await _managedDatabase.ShutdownAsync();
            }
        }


        [TearDown]
        public void TearDown()
        {
            try
            { _managedDatabase.ShutdownAsync().Wait(); }
            catch
            { }

            Directory.Delete(_testDirectory, true);
        }
    }
}
