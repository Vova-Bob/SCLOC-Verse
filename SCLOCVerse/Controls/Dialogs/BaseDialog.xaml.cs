using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SCLOCVerse.Helpers;

namespace SCLOCVerse.Controls.Dialogs
{
    public partial class BaseDialog : Window
    {
        private static readonly Brush DefaultAccentBrush = new SolidColorBrush(Color.FromRgb(0x6D, 0xB9, 0xF8));

        private BaseDialog(DialogOptions options)
        {
            InitializeComponent();

            TitleText.Text = options.Title;
            MessageText.Text = options.Message;

            var accentBrush = options.AccentBrush ?? GetDefaultAccentBrush(options.Type);

            // РђРєС†РµРЅС‚ РґР»СЏ СЂРѕР·РґС–Р»СЋРІР°Р»СЊРЅРѕС— Р»С–РЅС–С—.
            if (FindResource("DialogBorder") is SolidColorBrush borderBrush)
            {
                borderBrush.Color = ((SolidColorBrush)accentBrush).Color;
            }

            if (options.Owner != null)
            {
                Owner = options.Owner;
            }

            BuildButtons(options);
        }

        public MessageBoxResult DialogResultValue { get; private set; } = MessageBoxResult.Cancel;

        public static MessageBoxResult Show(DialogOptions options)
        {
            var dialog = new BaseDialog(options);
            dialog.ShowDialog();
            return dialog.DialogResultValue;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            var helper = new WindowHelper();
            helper.ApplyWindowRoundCorners(this);
        }

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                DragMove();
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResultValue = MessageBoxResult.Cancel;
            Close();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);

            if (e.Key == Key.Escape)
            {
                DialogResultValue = MessageBoxResult.Cancel;
                Close();
            }
        }

        private void BuildButtons(DialogOptions options)
        {
            var buttons = options.CustomButtons ?? GetDefaultButtons(options.Type, options.Buttons);

            foreach (var buttonInfo in buttons)
            {
                var button = CreateButton(buttonInfo);

                if (buttonInfo.IsDefault)
                {
                    button.IsDefault = true;
                }

                ButtonPanel.Children.Add(button);
            }
        }

        private Button CreateButton(DialogButtonInfo buttonInfo)
        {
            var button = new Button
            {
                Content = buttonInfo.Text,
                Style = (Style)FindResource("DialogButtonStyle"),
                IsDefault = buttonInfo.IsDefault
            };

            if (buttonInfo.IsDestructive)
            {
                button.Background = new SolidColorBrush(Color.FromRgb(0x9D, 0x4D, 0x52));
            }

            button.Click += (_, _) =>
            {
                DialogResultValue = buttonInfo.Result;
                DialogResult = buttonInfo.Result != MessageBoxResult.Cancel;
                Close();
            };

            return button;
        }

        private static IReadOnlyList<DialogButtonInfo> GetDefaultButtons(DialogType type, MessageBoxButton buttons)
        {
            return buttons switch
            {
                MessageBoxButton.OK => new[]
                {
                    new DialogButtonInfo("Р“Р°СЂР°Р·Рґ", MessageBoxResult.OK, isDefault: true)
                },
                MessageBoxButton.OKCancel => new[]
                {
                    new DialogButtonInfo("Р“Р°СЂР°Р·Рґ", MessageBoxResult.OK, isDefault: true),
                    new DialogButtonInfo("РЎРєР°СЃСѓРІР°С‚Рё", MessageBoxResult.Cancel)
                },
                MessageBoxButton.YesNo => new[]
                {
                    new DialogButtonInfo("РўР°Рє", MessageBoxResult.Yes, isDefault: true),
                    new DialogButtonInfo("РќС–", MessageBoxResult.No)
                },
                MessageBoxButton.YesNoCancel => new[]
                {
                    new DialogButtonInfo("РўР°Рє", MessageBoxResult.Yes, isDefault: true),
                    new DialogButtonInfo("РќС–", MessageBoxResult.No),
                    new DialogButtonInfo("РЎРєР°СЃСѓРІР°С‚Рё", MessageBoxResult.Cancel)
                },
                _ => new[]
                {
                    new DialogButtonInfo("Р“Р°СЂР°Р·Рґ", MessageBoxResult.OK, isDefault: true)
                }
            };
        }

        private static Brush GetDefaultAccentBrush(DialogType type)
        {
            var color = type switch
            {
                DialogType.Success => Color.FromRgb(0x4C, 0xAF, 0x50),
                DialogType.Warning => Color.FromRgb(0xFF, 0x98, 0x00),
                DialogType.Error => Color.FromRgb(0xF4, 0x43, 0x36),
                DialogType.Confirmation => Color.FromRgb(0x21, 0x96, 0xF3),
                _ => Color.FromRgb(0x6D, 0xB9, 0xF8)
            };

            return new SolidColorBrush(color);
        }
    }
}
