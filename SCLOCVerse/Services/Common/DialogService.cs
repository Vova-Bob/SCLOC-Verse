using SCLOCVerse.Controls.Dialogs;
using SCLOCVerse.Interfaces;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace SCLOCVerse.Services.Common
{
    /// <summary>
    /// Р РµР°Р»С–Р·Р°С†С–СЏ СЃРµСЂРІС–СЃСѓ СѓРЅС–С„С–РєРѕРІР°РЅРёС… РјРѕРґР°Р»СЊРЅРёС… РґС–Р°Р»РѕРіС–РІ.
    /// Р’СЃС– РјРµС‚РѕРґРё Р±РµР·РїРµС‡РЅС– РґР»СЏ РІРёРєР»РёРєСѓ Р· С„РѕРЅРѕРІРёС… РїРѕС‚РѕРєС–РІ.
    /// </summary>
    public sealed class DialogService : IDialogService
    {
        private readonly Dispatcher _dispatcher;

        public DialogService(Dispatcher? dispatcher = null)
        {
            _dispatcher = dispatcher ?? Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        }

        public Task ShowInfoAsync(string message, string? title = null, Window? owner = null)
        {
            return ShowOnUiThreadAsync(() => BaseDialog.Show(new DialogOptions
            {
                Type = DialogType.Info,
                Title = title ?? "Р†РЅС„РѕСЂРјР°С†С–СЏ",
                Message = message,
                Buttons = MessageBoxButton.OK,
                Owner = owner ?? ResolveOwner()
            }));
        }

        public Task ShowErrorAsync(string message, string? title = null, Window? owner = null)
        {
            return ShowOnUiThreadAsync(() => BaseDialog.Show(new DialogOptions
            {
                Type = DialogType.Error,
                Title = title ?? "РџРѕРјРёР»РєР°",
                Message = message,
                Buttons = MessageBoxButton.OK,
                Owner = owner ?? ResolveOwner()
            }));
        }

        public Task ShowWarningAsync(string message, string? title = null, Window? owner = null)
        {
            return ShowOnUiThreadAsync(() => BaseDialog.Show(new DialogOptions
            {
                Type = DialogType.Warning,
                Title = title ?? "РџРѕРїРµСЂРµРґР¶РµРЅРЅСЏ",
                Message = message,
                Buttons = MessageBoxButton.OKCancel,
                Owner = owner ?? ResolveOwner()
            }));
        }

        public Task<bool> ShowConfirmationAsync(string message, string? title = null, Window? owner = null)
        {
            return ShowOnUiThreadAsync(() =>
            {
                var result = BaseDialog.Show(new DialogOptions
                {
                    Type = DialogType.Confirmation,
                    Title = title ?? "РџС–РґС‚РІРµСЂРґР¶РµРЅРЅСЏ",
                    Message = message,
                    Buttons = MessageBoxButton.YesNo,
                    Owner = owner ?? ResolveOwner()
                });

                return result == MessageBoxResult.Yes;
            });
        }

        public Task<bool> ShowUpdateDialogAsync(string availableVersion, Window? owner = null)
        {
            return ShowOnUiThreadAsync(() => UpdateDialog.Show(owner ?? ResolveOwner(), availableVersion));
        }

        public Task<MessageBoxResult> ShowMessageAsync(string message, string? title = null, MessageBoxButton buttons = MessageBoxButton.OK, Window? owner = null)
        {
            return ShowOnUiThreadAsync(() => BaseDialog.Show(new DialogOptions
            {
                Type = ResolveTypeFromButtons(buttons),
                Title = title ?? GetDefaultTitle(buttons),
                Message = message,
                Buttons = buttons,
                Owner = owner ?? ResolveOwner()
            }));
        }

        private static Window? ResolveOwner()
        {
            return Application.Current?.MainWindow;
        }

        private static string GetDefaultTitle(MessageBoxButton buttons)
        {
            return buttons switch
            {
                MessageBoxButton.YesNo or MessageBoxButton.YesNoCancel => "РџС–РґС‚РІРµСЂРґР¶РµРЅРЅСЏ",
                _ => "РџРѕРІС–РґРѕРјР»РµРЅРЅСЏ"
            };
        }

        private static DialogType ResolveTypeFromButtons(MessageBoxButton buttons)
        {
            return buttons switch
            {
                MessageBoxButton.YesNo or MessageBoxButton.YesNoCancel => DialogType.Confirmation,
                MessageBoxButton.OKCancel => DialogType.Warning,
                _ => DialogType.Info
            };
        }

        private Task<T> ShowOnUiThreadAsync<T>(Func<T> showDialog)
        {
            if (_dispatcher.CheckAccess())
            {
                return Task.FromResult(showDialog());
            }

            return _dispatcher.InvokeAsync(() => showDialog()).Task;
        }

        private Task<MessageBoxResult> ShowOnUiThreadAsync(Func<MessageBoxResult> showDialog)
        {
            if (_dispatcher.CheckAccess())
            {
                return Task.FromResult(showDialog());
            }

            return _dispatcher.InvokeAsync(() => showDialog()).Task;
        }
    }
}
