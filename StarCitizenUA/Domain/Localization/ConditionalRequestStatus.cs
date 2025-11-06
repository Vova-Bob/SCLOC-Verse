namespace StarCitizenUA.Domain.Localization;

/// <summary>
/// Статус умовного HTTP-запиту для asset-у локалізації.
/// </summary>
internal enum ConditionalRequestStatus
{
    Success,
    NotModified,
    PreconditionFailed,
    RateLimited,
    Forbidden
}
