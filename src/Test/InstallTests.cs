using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Org.Websn.Utility;

namespace Test
{
    public class InstallTests
    {
        private string _testDirectory;

        [SetUp]
        public void Setup()
        {
            _testDirectory = Path.Combine(Path.GetDirectoryName(typeof(InstallTests).Assembly.Location), $".test-cache\\{DateTime.UtcNow:yyyyMMdd-HHmmss-fffffff}");
            Directory.CreateDirectory(_testDirectory);
        }

        [TearDown]
        public void TearDown()
        {
            Directory.Delete(_testDirectory, true);
        }

        [TestCase(PlatformID.Win32NT)]
        [TestCase(PlatformID.Unix)]
        public async Task InstallLatest(PlatformID platform)
        {
             await MariaDbPackager.InstallLatestServerAsync(Path.Combine(_testDirectory, "install"), platform: platform);
        }


        [TestCase(PlatformID.Win32NT, 11, 3, 2)]
        [TestCase(PlatformID.Unix, 11, 3, 2)]
        [TestCase(PlatformID.Win32NT, 11, 2, 3)]
        [TestCase(PlatformID.Unix, 11, 2, 3)]
        public async Task InstallTarget(PlatformID platform, int major, int minor, int build)
        {
            await MariaDbPackager.InstallServerAsync(Path.Combine(_testDirectory, "install"), major, minor, build, platform: platform);
        }

    }
}
