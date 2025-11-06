using System;

namespace StarCitizenUA.Domain.Localization;

/// <summary>
/// Дані для відображення toast-повідомлення про стан встановлення.
/// </summary>
public sealed class LocalizationNotificationEventArgs : EventArgs
{
    public LocalizationNotificationEventArgs(string environmentName, LocalizationNotificationType type, string message)
    {
        EnvironmentName = environmentName;
        Type = type;
        Message = message;
    }

    public string EnvironmentName { get; }

    public LocalizationNotificationType Type { get; }

    public string Message { get; }
}
