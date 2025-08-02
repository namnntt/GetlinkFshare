using PuppeteerSharp;

namespace GetlinkFshare.Models
{
    public class DownloadInfo
    {
        public required string DirectLink { get; set; }
        public required CookieParam[] Cookies { get; set; }
        public long? FileSize { get; set; }
        public required string UserAgent { get; set; }
        public required string RefererUrl { get; set; }
    }
}
