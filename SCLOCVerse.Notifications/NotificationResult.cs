namespace SCLOCVerse.Notifications;

/// <summary>
/// Результат відправки повідомлення провайдером.
/// Dispatcher використовує ці поля для аудиту (Стаття 26) та retry-логіки.
/// </summary>
public sealed class NotificationResult
{
    public bool Success { get; init; }
    public int? HttpStatus { get; init; }

    /// <summary>Ідентифікатор повідомлення у зовнішній системі (напр. Discord message id).</summary>
    public string? ProviderMessageId { get; init; }

    /// <summary>Текст помилки, null при успіху. Записується в notification_attempts.error_message.</summary>
    public string? Error { get; init; }

    public static NotificationResult Ok(int httpStatus, string? providerMessageId = null) => new()
    {
        Success = true,
        HttpStatus = httpStatus,
        ProviderMessageId = providerMessageId
    };

    public static NotificationResult Fail(string error, int? httpStatus = null) => new()
    {
        Success = false,
        HttpStatus = httpStatus,
        Error = error
    };
}
