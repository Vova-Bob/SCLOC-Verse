using System;

namespace StarCitizenUA.Domain.Localization;

/// <summary>
/// Дані про оновлення прогресу встановлення локалізації.
/// </summary>
public sealed class LocalizationProgressEventArgs : EventArgs
{
    public LocalizationProgressEventArgs(string environmentName, LocalizationProgressStage stage, double? percent = null, string? details = null)
    {
        EnvironmentName = environmentName;
        Stage = stage;
        Percent = percent;
        Details = details;
    }

    public string EnvironmentName { get; }

    public LocalizationProgressStage Stage { get; }

    public double? Percent { get; }

    public string? Details { get; }
}
