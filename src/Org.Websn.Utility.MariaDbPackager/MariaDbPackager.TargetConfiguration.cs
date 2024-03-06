using System;

using Newtonsoft.Json;

namespace Org.Websn.Utility
{
    public static partial class MariaDbPackager
    {
        [Obsolete]
        internal sealed partial class TargetConfiguration
        {
            [JsonProperty("version")]
            public Version Version { get; set; } = null;

            [JsonProperty("support")]
            public SupportClass? SupportType { get; set; } = null;

            [JsonProperty("channel")]
            public ReleaseChannel? ReleaseChannel { get; set; } = null;

            [JsonProperty("mode")]
            public InstallationMode Mode { get; set; } = InstallationMode.Automatic;

            [JsonProperty("connection_string")]
            public string ConnectionString { get; set; } = null;
        }
    }
}
