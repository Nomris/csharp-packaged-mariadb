using System;

namespace Org.Websn.Utility
{
    public static partial class MariaDbPackager
    {
        internal sealed class InstalledFile
        {
            public Version Version { get; set; }

            public PlatformID PlatformID { get; set; }
        }
    }
}
