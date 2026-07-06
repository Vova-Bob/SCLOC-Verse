namespace SCLOCVerse.Models.ApplicationUpdate
{
    /// <summary>
    /// Поточний прогрес завантаження оновлення SCLOC-Verse.
    /// </summary>
    public sealed record UpdateDownloadProgress(
        long DownloadedBytes,
        long? TotalBytes,
        double? BytesPerSecond);
}
