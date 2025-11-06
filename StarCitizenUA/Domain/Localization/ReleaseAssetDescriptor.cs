namespace StarCitizenUA.Domain.Localization;

/// <summary>
/// Опис релізного asset-у з GitHub, який містить файл локалізації.
/// </summary>
internal sealed record ReleaseAssetDescriptor(
    long AssetId,
    string Name,
    string DownloadUrl,
    string? ContentType,
    long? Size,
    string? ReleaseTag,
    bool IsPrerelease);
