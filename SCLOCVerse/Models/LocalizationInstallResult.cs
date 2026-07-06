namespace SCLOCVerse.Models
{
    /// <summary>
    /// Результат операції з локалізацією (Install або Check).
    ///
    /// Семантика полів розділена:
    ///   Success   — операція завершена без помилок (Install: встановлення виконано/підтверджено).
    ///   HasUpdate — remote відрізняється від встановленого (Install: == localizationUpdated;
    ///               Check: remote TagName != metadata.InstalledTag).
    /// Router показує Toast лише за HasUpdate, не за Success.
    /// </summary>
    public sealed record LocalizationInstallResult(
         bool Success,
         string EnvironmentName,
         string GlobalIniPath,
         string? UserCfgPath,
         string Message,
         string? Version,
         bool HasUpdate = false);
}
