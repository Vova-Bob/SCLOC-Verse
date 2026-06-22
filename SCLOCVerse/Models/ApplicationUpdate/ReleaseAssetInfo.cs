namespace SCLOCVerse.Models.ApplicationUpdate
{
    public class ReleaseAssetInfo
    {
        public string Name { get; set; } = string.Empty;
        public string DownloadUrl { get; set; } = string.Empty;
        public long Size { get; set; }
        public string? Checksum { get; set; }
    }
}
