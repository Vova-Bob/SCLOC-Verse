namespace StarCitizenUA.Application.Localization;

/// <summary>
/// Текстові повідомлення для користувача, що відображаються через ToastService.
/// </summary>
internal static class LocalizationMessages
{
    public static string LocalizationUpToDate(string environmentName, string? releaseTag)
        => releaseTag is null
            ? $"Локалізація для {environmentName} вже актуальна."
            : $"Локалізація для {environmentName} вже відповідає релізу {releaseTag}.";

    public static string LocalizationUpdated(string environmentName, string? releaseTag)
        => releaseTag is null
            ? $"Локалізацію для {environmentName} оновлено."
            : $"Локалізацію для {environmentName} оновлено до релізу {releaseTag}.";

    public static string UserCfgCreated(string environmentName)
        => $"Для {environmentName} створено файл user.cfg.";

    public static string InstallResult(bool updated, string environmentName, string? releaseTag, bool userCfgCreated)
    {
        var baseMessage = updated
            ? LocalizationUpdated(environmentName, releaseTag)
            : LocalizationUpToDate(environmentName, releaseTag);

        if (userCfgCreated)
        {
            baseMessage += " Файл user.cfg створено.";
        }

        return baseMessage;
    }

    public static string RateLimited(TimeSpan? delay)
    {
        var seconds = delay?.TotalSeconds > 0 ? Math.Ceiling(delay.Value.TotalSeconds) : 5;
        return $"GitHub обмежив частоту запитів. Повторіть спробу через приблизно {seconds} с.";
    }

    public static string Forbidden(string? error)
        => string.IsNullOrWhiteSpace(error)
            ? "Сервер GitHub повернув 403 Forbidden. Перевірте підключення або обмеження API."
            : $"GitHub відмовив у доступі: {error}";
}
