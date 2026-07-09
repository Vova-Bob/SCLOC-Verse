using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SCLOCVerse.Services.InputSystem;

namespace SCLOCVerse.Controls.SettingsHub
{
    /// <summary>
    /// Read-only панель «Гарячі клавіші» Settings Hub.
    /// Відображає реальні зареєстровані комбінації з HotkeyService без редагування.
    /// Інтерактивний редактор — цільова модель наступної фази (див. KB §17.7).
    /// </summary>
    public partial class HotkeysSettingsPane : UserControl
    {
        public HotkeysSettingsPane()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Будує read-only список із реальних визначень HotkeyService.
        /// </summary>
        public void Populate(IEnumerable<HotkeyDefinition> definitions)
        {
            HotkeyList.Children.Clear();
            var list = (definitions ?? Enumerable.Empty<HotkeyDefinition>()).ToList();
            CountText.Text = $"{list.Count} дій";

            if (list.Count == 0)
            {
                HotkeyList.Children.Add(MakeEmptyNote("Жодна гаряча клавіша ще не зареєстрована."));
                return;
            }

            // Групування за функцією (виведене з Id).
            foreach (var group in list.GroupBy(d => ResolveGroup(d.Id.Value))
                                       .OrderBy(g => GroupOrder(g.Key)))
            {
                HotkeyList.Children.Add(MakeGroupHeader(group.Key));
                foreach (var def in group)
                    HotkeyList.Children.Add(MakeRow(def.Description ?? def.Id.Value, FormatGesture(def.EffectiveGesture)));
            }
        }

        // ============ Форматування жесту ============

        private static string FormatGesture(HotkeyGesture gesture)
        {
            var parts = new List<string>();

            if ((gesture.Modifiers & HotkeyModifiers.Control) != 0) parts.Add("Ctrl");
            if ((gesture.Modifiers & HotkeyModifiers.Alt) != 0) parts.Add("Alt");
            if ((gesture.Modifiers & HotkeyModifiers.Shift) != 0) parts.Add("Shift");
            if ((gesture.Modifiers & HotkeyModifiers.Win) != 0) parts.Add("Win");

            parts.Add(FormatKey(gesture.Key));
            return string.Join(" + ", parts);
        }

        private static string FormatKey(HotkeyKey key) => key switch
        {
            HotkeyKey.Escape => "Esc",
            HotkeyKey.D0 => "0",
            HotkeyKey.OemMinus => "−",
            HotkeyKey.OemPlus => "+",
            _ => key.ToString()
        };

        // ============ Групування ============

        private static string ResolveGroup(string id)
        {
            if (id.Contains("Scale")) return "Overlay · масштаб";
            if (id.Contains("Opacity")) return "Overlay · прозорість";
            if (id.Contains("ToggleOverlay") || id.Contains("ToggleClickThrough") || id.Contains("BeginTemporaryDrag"))
                return "Hangar Timer · overlay";
            return "Hangar Timer · цикл";
        }

        private static int GroupOrder(string group) => group switch
        {
            "Hangar Timer · overlay" => 0,
            "Hangar Timer · цикл" => 1,
            "Overlay · масштаб" => 2,
            "Overlay · прозорість" => 3,
            _ => 9
        };

        // ============ Побудова елементів UI ============

        private static UIElement MakeGroupHeader(string title)
        {
            return new TextBlock
            {
                Text = title.ToUpperInvariant(),
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 10.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x4A, 0xA3, 0xD8)),
                Margin = new Thickness(2, 0, 0, 8),
                Opacity = 0.95
            };
        }

        private static UIElement MakeRow(string description, string gesture)
        {
            var grid = new Grid { Margin = new Thickness(0, 2, 0, 2), MinHeight = 40 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var desc = new TextBlock
            {
                Text = description,
                FontFamily = new FontFamily("Segoe UI Semibold"),
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromRgb(0xEA, 0xF4, 0xFF)),
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.NoWrap,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            Grid.SetColumn(desc, 0);
            grid.Children.Add(desc);

            var badge = new Border
            {
                Style = MakeDefaultBadgeStyle(),
                Child = new TextBlock
                {
                    Text = gesture,
                    FontFamily = new FontFamily("Segoe UI Semibold"),
                    FontSize = 12.5,
                    Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xF3, 0xFF)),
                    HorizontalAlignment = HorizontalAlignment.Center
                },
                Margin = new Thickness(16, 4, 0, 4),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(badge, 1);
            grid.Children.Add(badge);

            return grid;
        }

        private static Style MakeDefaultBadgeStyle()
        {
            var style = new Style(typeof(Border));
            style.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(0x0A, 0x1D, 0x29))));
            style.Setters.Add(new Setter(Border.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(0x2A, 0x5A, 0x78))));
            style.Setters.Add(new Setter(Border.BorderThicknessProperty, new Thickness(1)));
            style.Setters.Add(new Setter(Border.CornerRadiusProperty, new CornerRadius(6)));
            style.Setters.Add(new Setter(Border.PaddingProperty, new Thickness(12, 5, 12, 5)));
            return style;
        }

        private static UIElement MakeEmptyNote(string message)
        {
            return new TextBlock
            {
                Text = message,
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromRgb(0x6F, 0x8C, 0xA8)),
                Margin = new Thickness(0, 20, 0, 0),
                TextWrapping = TextWrapping.Wrap
            };
        }
    }
}
