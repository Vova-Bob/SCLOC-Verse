using System.Threading.Tasks;
using System.Windows;

namespace SCLOCVerse.Interfaces
{
    /// <summary>
    /// РЎРµСЂРІС–СЃ РґР»СЏ РїРѕРєР°Р·Сѓ СѓРЅС–С„С–РєРѕРІР°РЅРёС… РјРѕРґР°Р»СЊРЅРёС… РґС–Р°Р»РѕРіС–РІ.
    /// Р‘РµР·РїРµС‡РЅРёР№ РґР»СЏ РІРёРєР»РёРєСѓ Р· Р±СѓРґСЊ-СЏРєРѕРіРѕ РїРѕС‚РѕРєСѓ Р·Р°РІРґСЏРєРё РІРЅСѓС‚СЂС–С€РЅС–Р№ РґРёСЃРїРµС‚С‡РµСЂРёР·Р°С†С–С—.
    /// </summary>
    public interface IDialogService
    {
        /// <summary>
        /// РџРѕРєР°Р·СѓС” С–РЅС„РѕСЂРјР°С†С–Р№РЅРёР№ РґС–Р°Р»РѕРі.
        /// </summary>
        Task ShowInfoAsync(string message, string? title = null, Window? owner = null);

        /// <summary>
        /// РџРѕРєР°Р·СѓС” РґС–Р°Р»РѕРі РїРѕРјРёР»РєРё.
        /// </summary>
        Task ShowErrorAsync(string message, string? title = null, Window? owner = null);

        /// <summary>
        /// РџРѕРєР°Р·СѓС” РґС–Р°Р»РѕРі РїРѕРїРµСЂРµРґР¶РµРЅРЅСЏ.
        /// </summary>
        Task ShowWarningAsync(string message, string? title = null, Window? owner = null);

        /// <summary>
        /// РџРѕРєР°Р·СѓС” РґС–Р°Р»РѕРі РїС–РґС‚РІРµСЂРґР¶РµРЅРЅСЏ Р· РєРЅРѕРїРєР°РјРё РўР°Рє/РќС–.
        /// </summary>
        Task<bool> ShowConfirmationAsync(string message, string? title = null, Window? owner = null);

        /// <summary>
        /// РџРѕРєР°Р·СѓС” РєРѕРјРїР°РєС‚РЅРёР№ РґС–Р°Р»РѕРі РѕРЅРѕРІР»РµРЅРЅСЏ Р· РєРЅРѕРїРєР°РјРё Р’СЃС‚Р°РЅРѕРІРёС‚Рё/РџС–Р·РЅС–С€Рµ.
        /// </summary>
        Task<bool> ShowUpdateDialogAsync(string availableVersion, Window? owner = null);

        /// <summary>
        /// РџРѕРєР°Р·СѓС” РґС–Р°Р»РѕРі РїС–РґС‚РІРµСЂРґР¶РµРЅРЅСЏ Р· Р·Р°РґР°РЅРёРјРё РєРЅРѕРїРєР°РјРё.
        /// </summary>
        Task<MessageBoxResult> ShowMessageAsync(string message, string? title = null, MessageBoxButton buttons = MessageBoxButton.OK, Window? owner = null);
    }
}
