using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace StarCitizenUA.Controls
{
    public partial class CacheCleanupDialog : Window
    {
        private CacheCleanupDialog(string title, string message, IReadOnlyList<DialogAction> actions)
        {
            InitializeComponent();

            TitleText.Text = title;
            MessageText.Text = message;

            foreach (var action in actions)
                ButtonPanel.Children.Add(CreateButton(action));
        }

        public MessageBoxResult Result { get; private set; } = MessageBoxResult.Cancel;

        public static MessageBoxResult ShowDialog(Window? owner, string title, string message, MessageBoxButton buttons, MessageBoxImage image)
        {
            var dialog = new CacheCleanupDialog(title, message, BuildActions(buttons, image));
            if (owner != null)
                dialog.Owner = owner;

            dialog.ShowDialog();
            return dialog.Result;
        }

        private Button CreateButton(DialogAction action)
        {
            var button = new Button
            {
                Content = action.Text,
                Width = action.Width,
                Height = 36,
                Margin = new Thickness(8, 0, 0, 0),
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                Background = action.IsDestructive
                    ? new SolidColorBrush(Color.FromRgb(0x9D, 0x4D, 0x52))
                    : new SolidColorBrush(Color.FromRgb(0x30, 0x54, 0x6E)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x8D, 0xD0, 0xFF)),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand
            };

            button.Click += (_, _) =>
            {
                Result = action.Result;
                DialogResult = true;
                Close();
            };

            return button;
        }

        private static IReadOnlyList<DialogAction> BuildActions(MessageBoxButton buttons, MessageBoxImage image)
        {
            return buttons switch
            {
                MessageBoxButton.OK => new[]
                {
                    new DialogAction("Гаразд", MessageBoxResult.OK, false, 100)
                },
                MessageBoxButton.YesNo => new[]
                {
                    new DialogAction("Очистити", MessageBoxResult.Yes, true, 120),
                    new DialogAction("Скасувати", MessageBoxResult.No, false, 120)
                },
                MessageBoxButton.YesNoCancel => new[]
                {
                    new DialogAction("Старі кеші", MessageBoxResult.Yes, true, 120),
                    new DialogAction("Весь кеш", MessageBoxResult.No, true, 110),
                    new DialogAction("Скасувати", MessageBoxResult.Cancel, false, 120)
                },
                MessageBoxButton.OKCancel => new[]
                {
                    new DialogAction("Гаразд", MessageBoxResult.OK, image == MessageBoxImage.Warning, 100),
                    new DialogAction("Скасувати", MessageBoxResult.Cancel, false, 120)
                },
                _ => new[]
                {
                    new DialogAction("Скасувати", MessageBoxResult.Cancel, false, 120)
                }
            };
        }

        private sealed record DialogAction(string Text, MessageBoxResult Result, bool IsDestructive, double Width);
    }
}
