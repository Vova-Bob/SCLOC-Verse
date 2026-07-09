using System.Windows;
using System.Windows.Controls;

namespace SCLOCVerse.Controls.SettingsHub
{
    /// <summary>
    /// Узагальнена панель-плейсхолдер для зарезервованих категорій Settings Hub.
    /// Показує, що категорія існує, але контент з'явиться пізніше (правило:
    /// контент лише для реалізованого функціоналу).
    /// </summary>
    public partial class SettingsPlaceholderPane : UserControl
    {
        public SettingsPlaceholderPane()
        {
            InitializeComponent();
        }

        /// <summary>Заголовок категорії.</summary>
        public string Title { get => TitleText.Text; set => TitleText.Text = value; }

        /// <summary>Пояснювальний текст плейсхолдера.</summary>
        public string Message { get => MessageText.Text; set => MessageText.Text = value; }

        /// <summary>Символ-іконка (Segoe MDL2/емодзі).</summary>
        public string Icon { get => IconGlyph.Text; set => IconGlyph.Text = value; }
    }
}
