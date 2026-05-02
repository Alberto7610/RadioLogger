namespace RadioLogger.Models
{
    public class StreamingConfig
    {
        public bool IsEnabled { get; set; } = false;
        public string ServerType { get; set; } = "Shoutcast"; // "Shoutcast v1", "Shoutcast v2", "Icecast"
        public string Host { get; set; } = "127.0.0.1";
        public int Port { get; set; } = 8000;
        public string Password { get; set; } = "";
        public string MountPoint { get; set; } = "/live";
        public int Bitrate { get; set; } = 128;

        // Nuevos parámetros (defaults preservan comportamiento anterior)
        public int SampleRate { get; set; } = 44100;       // 22050, 44100, 48000
        public int Channels { get; set; } = 2;              // 1 = Mono, 2 = Stereo
        public string Username { get; set; } = "source";    // Solo Icecast
        public string Genre { get; set; } = "";              // Metadata opcional

        public string GetPublicUrl()
        {
            if (ServerType.Contains("Shoutcast"))
                return $"http://{Host}:{Port}/";

            // Icecast
            var mount = MountPoint.StartsWith("/") ? MountPoint : "/" + MountPoint;
            return $"http://{Host}:{Port}{mount}";
        }
    }
}
