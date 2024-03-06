using System;
using System.Threading.Tasks;

using Org.Websn.Utility;

namespace Test
{
    public class PassiveTests
    {
        [SetUp]
        public void Setup()
        { }

        [Test]
        public async Task GetLatestVersion()
        {
            Version version = await MariaDbPackager.GetLatestServerVersionAsync();
            if (version == null)
                Assert.Fail();
            else
                Assert.Pass("Latest stable Version is " + version);
        }
    }
}