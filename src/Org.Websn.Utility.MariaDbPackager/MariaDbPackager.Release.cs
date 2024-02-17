using System;
using System.Collections.Generic;

using Newtonsoft.Json;

namespace Org.Websn.Utility
{
    public static partial class MariaDbPackager
    {
        internal sealed class Release
        {
            [JsonProperty("release_id")]
            public Version Version { get; set; }

            [JsonProperty("release_name")]
            public string Name { get; set; }

            [JsonProperty("release_status")]
            public string Status { get; set; }

            [JsonProperty("release_support_type")]
            public string SupportType { get; set; }

            [JsonProperty("files")]
            public List<FileObject> Files { get; set; } = new List<FileObject>();

            internal sealed class FileObject
            {
                [JsonProperty("file_id")]
                public int Id { get; set; }

                [JsonProperty("file_name")]
                public string Name { get; set; }

                [JsonProperty("package_type")]
                public string PackageType { get; set; }

                [JsonProperty("os")]
                public string Platform { get; set; }
            }
        }
    }
}
