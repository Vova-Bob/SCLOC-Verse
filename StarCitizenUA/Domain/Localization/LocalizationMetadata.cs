using System;

namespace StarCitizenUA.Domain.Localization;

/// <summary>
/// Метадані завантаженої локалізації, що зберігаються поза директорією гри.
/// </summary>
internal sealed class LocalizationMetadata
{
    public long AssetId { get; init; }

    public string? ETag { get; init; }

    public string? Sha256 { get; init; }

    public long FileSize { get; init; }

    public DateTimeOffset? LastModified { get; init; }

    /// <summary>
    /// Перевіряє, чи збігається контрольна сума з наявними метаданими.
    /// </summary>
    public bool HasMatchingHash(string candidateHash)
        => !string.IsNullOrWhiteSpace(Sha256)
           && Sha256!.Equals(candidateHash, StringComparison.OrdinalIgnoreCase);
}
