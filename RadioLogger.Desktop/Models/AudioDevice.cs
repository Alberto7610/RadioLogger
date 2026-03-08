namespace RadioLogger.Models
{
    public class AudioDevice
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Driver { get; set; } = string.Empty;
        public bool IsInput { get; set; }
        public bool IsEnabled { get; set; } // For UI selection
        
        public override string ToString() => Name;
    }
}
