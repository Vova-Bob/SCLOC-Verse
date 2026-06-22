using SCLOCVerse.Controls.Dialogs;
using System.Collections.Generic;
using System.Windows;

namespace SCLOCVerse.Controls
{
    public partial class CacheCleanupDialog : Window
    {
        private CacheCleanupDialog()
        {
            InitializeComponent();
        }

        public static MessageBoxResult ShowDialog(Window? owner, string title, string message, MessageBoxButton buttons, MessageBoxImage image)
        {
            var options = new DialogOptions
            {
                Owner = owner,
                Title = title,
                Message = message,
                Type = ResolveDialogType(image),
                Buttons = buttons,
                CustomButtons = BuildCustomButtons(buttons, image)
            };

            return BaseDialog.Show(options);
        }

        private static DialogType ResolveDialogType(MessageBoxImage image)
        {
            return image switch
            {
                MessageBoxImage.Warning => DialogType.Warning,
                MessageBoxImage.Error => DialogType.Error,
                MessageBoxImage.Question => DialogType.Confirmation,
                _ => DialogType.Info
            };
        }

        private static IReadOnlyList<DialogButtonInfo> BuildCustomButtons(MessageBoxButton buttons, MessageBoxImage image)
        {
            return buttons switch
            {
                MessageBoxButton.OK => new[]
                {
                    new DialogButtonInfo("Р“Р°СЂР°Р·Рґ", MessageBoxResult.OK, isDefault: true)
                },
                MessageBoxButton.YesNo => new[]
                {
                    new DialogButtonInfo("РћС‡РёСЃС‚РёС‚Рё", MessageBoxResult.Yes, isDefault: true, isDestructive: true),
                    new DialogButtonInfo("РЎРєР°СЃСѓРІР°С‚Рё", MessageBoxResult.No)
                },
                MessageBoxButton.YesNoCancel => new[]
                {
                    new DialogButtonInfo("РЎС‚Р°СЂС– РєРµС€С–", MessageBoxResult.Yes, isDefault: true, isDestructive: true),
                    new DialogButtonInfo("Р’РµСЃСЊ РєРµС€", MessageBoxResult.No, isDestructive: true),
                    new DialogButtonInfo("РЎРєР°СЃСѓРІР°С‚Рё", MessageBoxResult.Cancel)
                },
                MessageBoxButton.OKCancel => new[]
                {
                    new DialogButtonInfo("Р“Р°СЂР°Р·Рґ", MessageBoxResult.OK, isDefault: true, isDestructive: image == MessageBoxImage.Warning),
                    new DialogButtonInfo("РЎРєР°СЃСѓРІР°С‚Рё", MessageBoxResult.Cancel)
                },
                _ => new[]
                {
                    new DialogButtonInfo("РЎРєР°СЃСѓРІР°С‚Рё", MessageBoxResult.Cancel)
                }
            };
        }
    }
}
