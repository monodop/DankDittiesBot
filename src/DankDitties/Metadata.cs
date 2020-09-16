using System;
using System.Collections.Generic;
using System.Text;

namespace DankDitties
{
    public class Metadata
    {
        public Dictionary<string, PostMetadata> Posts { get; set; } = new Dictionary<string, PostMetadata>();
    }

    public class PostMetadata
    {
        public string Id { get; set; }
        public string Url { get; set; }
        public string Permalink { get; set; }
        public string Domain { get; set; }
        public string Title { get; set; }
        public bool IsUserRequested { get; set; }
        public bool IsReviewed { get; set; }
        public bool IsApproved { get; set; }
        public bool DownloadFailed { get; set; }
        public string DownloadCacheFilename { get; set; }

        public bool IsReady => IsApproved && DownloadCacheFilename != null;
    }
}
