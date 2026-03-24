namespace JackTheRipper360.Core.Common
{
    public class ExportResult
    {
        public bool Success { get; set; }
        public string OutputPath { get; set; }
        public string ErrorMessage { get; set; }
        public long BytesWritten { get; set; }

        public static ExportResult Succeeded(string outputPath, long bytesWritten = 0) =>
            new ExportResult { Success = true, OutputPath = outputPath, BytesWritten = bytesWritten };

        public static ExportResult Failed(string error) =>
            new ExportResult { Success = false, ErrorMessage = error };
    }
}
