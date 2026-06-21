using StarCitizenUA.Controls.Dialogs;
using System.Collections.Generic;
using System.Windows;

namespace StarCitizenUA.Controls
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
                    new DialogButtonInfo("Гаразд", MessageBoxResult.OK, isDefault: true)
                },
                MessageBoxButton.YesNo => new[]
                {
                    new DialogButtonInfo("Очистити", MessageBoxResult.Yes, isDefault: true, isDestructive: true),
                    new DialogButtonInfo("Скасувати", MessageBoxResult.No)
                },
                MessageBoxButton.YesNoCancel => new[]
                {
                    new DialogButtonInfo("Старі кеші", MessageBoxResult.Yes, isDefault: true, isDestructive: true),
                    new DialogButtonInfo("Весь кеш", MessageBoxResult.No, isDestructive: true),
                    new DialogButtonInfo("Скасувати", MessageBoxResult.Cancel)
                },
                MessageBoxButton.OKCancel => new[]
                {
                    new DialogButtonInfo("Гаразд", MessageBoxResult.OK, isDefault: true, isDestructive: image == MessageBoxImage.Warning),
                    new DialogButtonInfo("Скасувати", MessageBoxResult.Cancel)
                },
                _ => new[]
                {
                    new DialogButtonInfo("Скасувати", MessageBoxResult.Cancel)
                }
            };
        }
    }
}
