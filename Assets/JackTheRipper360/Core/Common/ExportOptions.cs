namespace JackTheRipper360.Core.Common
{
    public class ExportOptions
    {
        public string OutputDirectory { get; set; }
        public string PreferredFormat { get; set; }
        public bool OverwriteExisting { get; set; } = true;
        public bool PreserveDirectoryStructure { get; set; } = true;
        public int TextureMaxResolution { get; set; } = 0; // 0 = no limit
        public int AudioSampleRate { get; set; } = 0; // 0 = keep original
    }
}
