namespace JackTheRipper360.Core.Models
{
    /// <summary>
    /// Material data extracted from Xbox 360 model formats.
    /// </summary>
    public class MaterialData
    {
        public string Name { get; set; }
        public string DiffuseTexture { get; set; }
        public string NormalTexture { get; set; }
        public string SpecularTexture { get; set; }

        public float[] DiffuseColor { get; set; } = { 1f, 1f, 1f, 1f };
        public float[] SpecularColor { get; set; } = { 0f, 0f, 0f, 1f };
        public float Shininess { get; set; }
        public float Opacity { get; set; } = 1f;
    }
}
