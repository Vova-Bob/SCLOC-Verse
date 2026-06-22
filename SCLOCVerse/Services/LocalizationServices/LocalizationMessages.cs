using System.Net;

namespace SCLOCVerse.Services.LocalizationServices
{
    internal static class LocalizationMessages
    {
        internal static string ReleaseNotFound(string environmentName)
            => $"РќРµ РІРґР°Р»РѕСЃСЏ Р·РЅР°Р№С‚Рё СЂРµР»С–Р· Р· С„Р°Р№Р»РѕРј Р»РѕРєР°Р»С–Р·Р°С†С–С— РґР»СЏ {environmentName}.";

        internal static string AssetMissing(string? releaseTag, string fileName)
            => $"Р РµР»С–Р· {releaseTag ?? "Р±РµР· РЅР°Р·РІРё"} РЅРµ РјС–СЃС‚РёС‚СЊ С„Р°Р№Р»Сѓ {fileName}.";

        internal static string InstallCompleted(string environmentName, string? releaseTag, bool userCfgCreated, bool updated)
        {
            if (updated)
            {
                return userCfgCreated
                    ? $"Р›РѕРєР°Р»С–Р·Р°С†С–СЋ РґР»СЏ {environmentName} РІСЃС‚Р°РЅРѕРІР»РµРЅРѕ Р· СЂРµР»С–Р·Сѓ {releaseTag ?? "РЅРµРІС–РґРѕРјРѕРіРѕ"}. Р¤Р°Р№Р» user.cfg СЃС‚РІРѕСЂРµРЅРѕ."
                    : $"Р›РѕРєР°Р»С–Р·Р°С†С–СЋ РґР»СЏ {environmentName} РѕРЅРѕРІР»РµРЅРѕ Р· СЂРµР»С–Р·Сѓ {releaseTag ?? "РЅРµРІС–РґРѕРјРѕРіРѕ"}.";
            }

            return userCfgCreated
                ? $"Р›РѕРєР°Р»С–Р·Р°С†С–СЏ РґР»СЏ {environmentName} РІР¶Рµ РІС–РґРїРѕРІС–РґР°Р»Р° СЂРµР»С–Р·Сѓ {releaseTag ?? "РЅРµРІС–РґРѕРјРѕРјСѓ"}. Р¤Р°Р№Р» user.cfg СЃС‚РІРѕСЂРµРЅРѕ."
                : $"Р›РѕРєР°Р»С–Р·Р°С†С–СЏ РґР»СЏ {environmentName} РІР¶Рµ РІС–РґРїРѕРІС–РґР°Р»Р° СЂРµР»С–Р·Сѓ {releaseTag ?? "РЅРµРІС–РґРѕРјРѕРјСѓ"}.";
        }

        internal static string DeleteAll(string environmentName)
            => $"Р¤Р°Р№Р»Рё Р»РѕРєР°Р»С–Р·Р°С†С–С— РґР»СЏ {environmentName} РІРёРґР°Р»РµРЅРѕ.";

        internal static string DeleteUserCfg(string environmentName)
            => $"Р¤Р°Р№Р» user.cfg РґР»СЏ {environmentName} РІРёРґР°Р»РµРЅРѕ.";

        internal static string DeleteGlobalIni(string environmentName)
            => $"Р¤Р°Р№Р» global.ini РґР»СЏ {environmentName} РІРёРґР°Р»РµРЅРѕ.";

        internal static string DeleteMissing(string environmentName)
            => $"Р¤Р°Р№Р»Рё Р»РѕРєР°Р»С–Р·Р°С†С–С— РґР»СЏ {environmentName} РЅРµ Р·РЅР°Р№РґРµРЅРѕ.";

        internal static string HttpError(HttpStatusCode statusCode)
            => $"РџРѕРјРёР»РєР° Р·Р°РїРёС‚Сѓ РґРѕ GitHub: {(int)statusCode} {statusCode}.";

        internal static string InvalidContentType(string mediaType)
            => $"РћС‚СЂРёРјР°РЅРѕ РЅРµРїС–РґС‚СЂРёРјСѓРІР°РЅРёР№ С‚РёРї РІРјС–СЃС‚Сѓ: {mediaType}.";

        internal static string ReleaseParseError()
            => "РќРµ РІРґР°Р»РѕСЃСЏ РѕР±СЂРѕР±РёС‚Рё РІС–РґРїРѕРІС–РґСЊ GitHub С–Р· СЂРµР»С–Р·Р°РјРё.";

        internal static string FileLockedFailure()
            => "РќРµ РІРґР°Р»РѕСЃСЏ Р·Р°РІРµСЂС€РёС‚Рё РѕРЅРѕРІР»РµРЅРЅСЏ С‡РµСЂРµР· С‚РёРјС‡Р°СЃРѕРІРµ Р±Р»РѕРєСѓРІР°РЅРЅСЏ С„Р°Р№Р»Сѓ. РЎРїСЂРѕР±СѓР№С‚Рµ С‰Рµ СЂР°Р·.";

        internal static string InstallInProgress()
            => "РћРЅРѕРІР»РµРЅРЅСЏ Р»РѕРєР°Р»С–Р·Р°С†С–С— РІР¶Рµ РІРёРєРѕРЅСѓС”С‚СЊСЃСЏ. Р”РѕС‡РµРєР°Р№С‚РµСЃСЏ Р·Р°РІРµСЂС€РµРЅРЅСЏ РїРѕС‚РѕС‡РЅРѕРіРѕ РїСЂРѕС†РµСЃСѓ.";
    }
}
