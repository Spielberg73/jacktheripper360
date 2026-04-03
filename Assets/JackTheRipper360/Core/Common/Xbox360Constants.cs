namespace JackTheRipper360.Core.Common
{
    /// <summary>
    /// Magic bytes, signatures, and constants for Xbox 360 formats.
    /// </summary>
    public static class Xbox360Constants
    {
        // Container magic bytes
        public static readonly byte[] XEX2_MAGIC = { 0x58, 0x45, 0x58, 0x32 }; // "XEX2"
        public static readonly byte[] XDVDFS_MAGIC = { 0x4D, 0x49, 0x43, 0x52, 0x4F, 0x53, 0x4F, 0x46, 0x54, 0x2A, 0x58, 0x42, 0x4F, 0x58, 0x2A, 0x4D, 0x45, 0x44, 0x49, 0x41 }; // "MICROSOFT*XBOX*MEDIA"
        public const uint STFS_MAGIC_CON = 0x434F4E20;  // "CON "
        public const uint STFS_MAGIC_LIVE = 0x4C495645; // "LIVE"
        public const uint STFS_MAGIC_PIRS = 0x50495253; // "PIRS"

        // Texture magic bytes
        public static readonly byte[] DDS_MAGIC = { 0x44, 0x44, 0x53, 0x20 }; // "DDS "
        public static readonly byte[] XPR0_MAGIC = { 0x58, 0x50, 0x52, 0x30 }; // "XPR0"
        public static readonly byte[] XPR2_MAGIC = { 0x58, 0x50, 0x52, 0x32 }; // "XPR2"
        public static readonly byte[] TX2D_MAGIC = { 0x54, 0x58, 0x32, 0x44 }; // "TX2D"

        // Audio magic bytes
        public static readonly byte[] RIFF_MAGIC = { 0x52, 0x49, 0x46, 0x46 }; // "RIFF"
        public static readonly byte[] WBND_MAGIC = { 0x57, 0x42, 0x4E, 0x44 }; // "WBND" (XWB)
        public static readonly byte[] SDBK_MAGIC = { 0x53, 0x44, 0x42, 0x4B }; // "SDBK" (XSB)
        public static readonly byte[] XMA_MAGIC = { 0x58, 0x4D, 0x41 };        // "XMA"

        // Video magic bytes
        public static readonly byte[] BINK_MAGIC = { 0x42, 0x49, 0x4B }; // "BIK"
        public static readonly byte[] XMV_MAGIC = { 0x30, 0x26, 0xB2, 0x75 }; // ASF header (WMV/XMV)

        // XDVDFS constants
        public const long XDVDFS_SECTOR_SIZE = 2048;
        public const long XDVDFS_ROOT_SECTOR = 32;

        // STFS constants
        public const int STFS_HEADER_SIZE = 0x971A;
        public const int STFS_BLOCK_SIZE = 0x1000;
        public const int STFS_HASH_BLOCK_SIZE = 0x1000;

        // ABadAvatar exploit constants
        public const uint ABADAVATAR_THAW_TITLE_ID = 0x41560855;  // Tony Hawk's American Wasteland
        public const uint ABADAVATAR_RBB_TITLE_ID = 0x45410914;   // Rock Band Blitz
        public const ushort ABADAVATAR_KERNEL_VERSION = 17559;     // Target kernel version
        public const uint ABADAVATAR_STAGE2_LOAD_ADDR = 0x80070000;
        public const uint ABADAVATAR_STAGE3_LOAD_ADDR = 0x90110000;
        public const uint ABADAVATAR_HV_SYSCALL = 0x00000061;     // Hypervisor syscall used in race
        public static readonly byte[] ABADAVATAR_STAGE1_SIGNATURE = { 0x38, 0x60, 0x00, 0x00, 0x7C, 0x63 }; // Stage 1 pivot marker
        public const int ABADAVATAR_SAVE_HEADER_SIZE = 0x80;
        public const int ABADAVATAR_PAYLOAD_MAX_SIZE = 0x10000;    // 64 KB max payload

        // DDS format constants
        public const uint DDS_HEADER_SIZE = 124;
        public const uint DDSD_CAPS = 0x1;
        public const uint DDSD_HEIGHT = 0x2;
        public const uint DDSD_WIDTH = 0x4;
        public const uint DDSD_PIXELFORMAT = 0x1000;
        public const uint DDSD_LINEARSIZE = 0x80000;

        // DDS pixel format flags
        public const uint DDPF_ALPHAPIXELS = 0x1;
        public const uint DDPF_FOURCC = 0x4;
        public const uint DDPF_RGB = 0x40;

        // DDS FourCC codes
        public const uint FOURCC_DXT1 = 0x31545844;
        public const uint FOURCC_DXT3 = 0x33545844;
        public const uint FOURCC_DXT5 = 0x35545844;
        public const uint FOURCC_ATI1 = 0x31495441;
        public const uint FOURCC_ATI2 = 0x32495441;
    }
}
