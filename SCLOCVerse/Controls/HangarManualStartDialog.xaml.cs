using System.Globalization;
using System.Windows;

namespace SCLOCVerse.Controls
{
    /// <summary>
    /// Діалог ручного вводу часу старту циклу Hangar Timer.
    /// </summary>
    public partial class HangarManualStartDialog : Window
    {
        public long ValueMs { get; private set; }

        public HangarManualStartDialog()
        {
            InitializeComponent();
            InputBox.Text = DateTime.Now.ToString("HH:mm:ss");
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            var text = InputBox.Text.Trim();

            if (long.TryParse(text, out var ms))
            {
                ValueMs = ms;
                DialogResult = true;
                return;
            }

            if (DateTime.TryParseExact(
                    text,
                    new[] { "HH:mm:ss", "HH:mm" },
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var t))
            {
                var local = DateTime.Today
                    .AddHours(t.Hour)
                    .AddMinutes(t.Minute)
                    .AddSeconds(t.Second);

                ValueMs = new DateTimeOffset(local).ToUnixTimeMilliseconds();
                DialogResult = true;
                return;
            }

            MessageBox.Show(
                "Невірний формат. Введіть UNIX мс або HH:mm[:ss].",
                "Помилка вводу",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }
}
