namespace RadioLogger.Models
{
    public class StreamingConfig
    {
        public bool IsEnabled { get; set; } = false;
        public string ServerType { get; set; } = "Shoutcast"; // Shoutcast, Icecast
        public string Host { get; set; } = "127.0.0.1";
        public int Port { get; set; } = 8000;
        public string Password { get; set; } = "changeme";
        public string MountPoint { get; set; } = "/live"; // Only for Icecast
        public int Bitrate { get; set; } = 128;

        public string GetPublicUrl()
        {
            if (ServerType == "Shoutcast")
                return $"http://{Host}:{Port}/;"; // The semicolon is a trick for some Shoutcast versions
            
            // Icecast
            var mount = MountPoint.StartsWith("/") ? MountPoint : "/" + MountPoint;
            return $"http://{Host}:{Port}{mount}";
        }
    }
}
