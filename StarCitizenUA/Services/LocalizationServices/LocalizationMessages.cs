using System.Net;

namespace StarCitizenUA.Services.LocalizationServices
{
    internal static class LocalizationMessages
    {
        internal static string ReleaseNotFound(string environmentName)
            => $"Не вдалося знайти реліз з файлом локалізації для {environmentName}.";

        internal static string AssetMissing(string? releaseTag, string fileName)
            => $"Реліз {releaseTag ?? "без назви"} не містить файлу {fileName}.";

        internal static string InstallCompleted(string environmentName, string? releaseTag, bool userCfgCreated, bool updated)
        {
            if (updated)
            {
                return userCfgCreated
                    ? $"Локалізацію для {environmentName} встановлено з релізу {releaseTag ?? "невідомого"}. Файл user.cfg створено."
                    : $"Локалізацію для {environmentName} оновлено з релізу {releaseTag ?? "невідомого"}.";
            }

            return userCfgCreated
                ? $"Локалізація для {environmentName} вже відповідала релізу {releaseTag ?? "невідомому"}. Файл user.cfg створено."
                : $"Локалізація для {environmentName} вже відповідала релізу {releaseTag ?? "невідомому"}.";
        }

        internal static string DeleteAll(string environmentName)
            => $"Файли локалізації для {environmentName} видалено.";

        internal static string DeleteUserCfg(string environmentName)
            => $"Файл user.cfg для {environmentName} видалено.";

        internal static string DeleteGlobalIni(string environmentName)
            => $"Файл global.ini для {environmentName} видалено.";

        internal static string DeleteMissing(string environmentName)
            => $"Файли локалізації для {environmentName} не знайдено.";

        internal static string HttpError(HttpStatusCode statusCode)
            => $"Помилка запиту до GitHub: {(int)statusCode} {statusCode}.";

        internal static string InvalidContentType(string mediaType)
            => $"Отримано непідтримуваний тип вмісту: {mediaType}.";

        internal static string ReleaseParseError()
            => "Не вдалося обробити відповідь GitHub із релізами.";

        internal static string FileLockedFailure()
            => "Не вдалося завершити оновлення через тимчасове блокування файлу. Спробуйте ще раз.";
    }
}
