using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SCLOCVerse.Services.InputSystem;

namespace SCLOCVerse.Controls.SettingsHub
{
    /// <summary>
    /// Панель «Гарячі клавіші» Settings Hub — інтерактивний редактор (Phase 0.5).
    /// Клік на комбінацію → capture → Rebind (з conflict-діалогом); reset per-item/per-category.
    /// </summary>
    public partial class HotkeysSettingsPane : UserControl
    {
        private IHotkeyService? _hotkeyService;
        private readonly Dictionary<string, HotkeyDefinition> _definitionsById = new();

        // Поточний рядок у режимі capture (null = не в режимі).
        private CaptureRow? _capture;

        public HotkeysSettingsPane()
        {
            InitializeComponent();
        }

        /// <summary>
        /// З'єднує панель із HotkeyService і будує read-only список.
        /// Викликається з MainWindow після ініціалізації Hub.
        /// </summary>
        public void Populate(IHotkeyService hotkeyService)
        {
            _hotkeyService = hotkeyService;
            RebuildList();
        }

        // ============ Побудова списку ============

        private void RebuildList()
        {
            HotkeyList.Children.Clear();
            _definitionsById.Clear();
            var list = _hotkeyService?.GetDefinitions() ?? Enumerable.Empty<HotkeyDefinition>().ToList();
            CountText.Text = $"{list.Count} дій · {list.Count(d => d.CurrentGesture.HasValue)} змінено";

            foreach (var group in list.GroupBy(d => ResolveGroup(d.Id.Value))
                                       .OrderBy(g => GroupOrder(g.Key)))
            {
                HotkeyList.Children.Add(MakeGroupHeader(group.Key));
                foreach (var def in group)
                {
                    _definitionsById[def.Id.Value] = def;
                    HotkeyList.Children.Add(MakeRow(def));
                }
            }
        }

        // ============ Захоплення (capture) ============

        private void GestureButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not string id)
                return;

            if (!_definitionsById.TryGetValue(id, out var def))
                return;

            // Вийти з попереднього режиму capture.
            if (_capture is not null)
                ExitCaptureMode(restore: true);

            EnterCaptureMode(btn, def);
        }

        private void EnterCaptureMode(Button button, HotkeyDefinition def)
        {
            button.Background = MakeCaptureBrush();
            button.Content = "Натисніть клавіші…";
            button.ToolTip = "Esc або Backspace — скасувати";
            _capture = new CaptureRow(button, def);

            // Підписуємось на клавіатуру вікна (PreviewKeyDown ловить до будь-якого контролу).
            var window = Window.GetWindow(this);
            if (window != null)
                window.PreviewKeyDown += Capture_PreviewKeyDown;
        }

        private void Capture_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (_capture is null)
                return;

            var key = e.Key;

            // Esc/Backspace → скасувати capture (не зберігати).
            if (key == Key.Escape || key == Key.Back)
            {
                ExitCaptureMode(restore: true);
                e.Handled = true;
                return;
            }

            // Модифікатори самі по собі — не завершують capture.
            if (HotkeyCaptureMapper.IsModifierKey(key))
                return;

            // Мапити WPF Key → HotkeyGesture.
            var modifiers = Keyboard.Modifiers;
            var gesture = HotkeyCaptureMapper.TryMap(key, modifiers);

            if (gesture is null)
            {
                // Клавіша поза відомим набором — не завершує capture, ігноруємо.
                return;
            }

            e.Handled = true;
            AttemptRebind(_capture.Def, gesture.Value);
            ExitCaptureMode(restore: false);
        }

        private void ExitCaptureMode(bool restore)
        {
            if (_capture is null)
                return;

            var window = Window.GetWindow(this);
            if (window != null)
                window.PreviewKeyDown -= Capture_PreviewKeyDown;

            if (restore)
            {
                // Відновити відображення попереднього жеста.
                _capture.Button.Background = MakeGestureBrush(_capture.Def);
                _capture.Button.Content = FormatGesture(_capture.Def.EffectiveGesture);
                _capture.Button.ToolTip = "Клікніть, щоб змінити комбінацію";
            }

            _capture = null;
        }

        // ============ Rebind + conflict ============

        private void AttemptRebind(HotkeyDefinition def, HotkeyGesture gesture)
        {
            if (_hotkeyService is null)
                return;

            var result = _hotkeyService.Rebind(def.Id, gesture, HotkeyConflictPolicy.Reject, out var conflictingId);

            switch (result)
            {
                case RebindResult.Success:
                    RebuildList();
                    break;

                case RebindResult.Unchanged:
                    // Жест не змінився — нічого не робимо.
                    break;

                case RebindResult.Conflict:
                    // Знайти конфліктуючу дію.
                    var conflictDef = _definitionsById.TryGetValue(conflictingId.Value, out var c) ? c : null;
                    var conflictName = conflictDef?.Description ?? conflictingId.Value;
                    var msg = $"Комбінація вже використовується:\n\n«{conflictName}»\n\nПеревизначити? (Та комбінація буде скинута.)";
                    var confirm = MessageBox.Show(msg, "Конфлікт комбінацій", MessageBoxButton.YesNo, MessageBoxImage.Question);
                    if (confirm == MessageBoxResult.Yes)
                    {
                        var r2 = _hotkeyService.Rebind(def.Id, gesture, HotkeyConflictPolicy.Replace, out _);
                        if (r2 == RebindResult.Success)
                            RebuildList();
                        else if (r2 == RebindResult.RegistrationFailed)
                            MessageBox.Show("Не вдалося зареєструвати комбінацію.\nМожливо, вона зайнята іншим застосунком.",
                                "Помилка", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                    break;

                case RebindResult.InvalidGesture:
                    // Capture-валідація не пропустила — не показуємо (capture ігнорує невідомі клавіші).
                    break;

                case RebindResult.RegistrationFailed:
                    MessageBox.Show("Не вдалося зареєструвати комбінацію.\nМожливо, вона зайнята іншим застосунком.",
                        "Помилка", MessageBoxButton.OK, MessageBoxImage.Warning);
                    break;
            }
        }

        // ============ Reset ============

        private void ResetItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not string id)
                return;
            if (!_definitionsById.TryGetValue(id, out var def))
                return;

            if (!def.CurrentGesture.HasValue)
                return; // вже default

            // Reset = Rebind до DefaultGesture (CurrentGesture → null, видаляється з JSON).
            if (def.DefaultGesture == def.EffectiveGesture)
                return;

            var result = _hotkeyService?.Rebind(def.Id, def.DefaultGesture, HotkeyConflictPolicy.Reject, out _);
            if (result == RebindResult.Success)
                RebuildList();
        }

        private void CategoryReset_Click(object sender, RoutedEventArgs e)
        {
            if (_hotkeyService is null)
                return;

            var modified = _definitionsById.Values.Where(d => d.CurrentGesture.HasValue).ToList();
            if (modified.Count == 0)
                return;

            var confirm = MessageBox.Show($"Скинути {modified.Count} змінених комбінацій до типових?",
                "Скидання гарячих клавіш", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes)
                return;

            foreach (var def in modified)
            {
                if (def.DefaultGesture != def.EffectiveGesture)
                    _hotkeyService.Rebind(def.Id, def.DefaultGesture, HotkeyConflictPolicy.Reject, out _);
            }

            RebuildList();
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
            return string.Join("+", parts);
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
                Margin = new Thickness(2, 14, 0, 8),
                Opacity = 0.95
            };
        }

        private UIElement MakeRow(HotkeyDefinition def)
        {
            var grid = new Grid { Margin = new Thickness(0, 2, 0, 2), MinHeight = 40 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Опис дії.
            var desc = new TextBlock
            {
                Text = def.Description ?? def.Id.Value,
                FontFamily = new FontFamily("Segoe UI Semibold"),
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromRgb(0xEA, 0xF4, 0xFF)),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            Grid.SetColumn(desc, 0);
            grid.Children.Add(desc);

            // Типово (описовий рядок під назвою).
            var defaultText = new TextBlock
            {
                Text = "Типово: " + FormatGesture(def.DefaultGesture),
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(0x6F, 0x8C, 0xA8)),
                Margin = new Thickness(0, 18, 0, 0)
            };
            Grid.SetColumn(defaultText, 0);
            grid.Children.Add(defaultText);

            // Кнопка жеста (клікабельна → capture).
            bool isModified = def.CurrentGesture.HasValue && def.CurrentGesture.Value != def.DefaultGesture;
            var gestureBtn = new Button
            {
                Tag = def.Id.Value,
                Content = FormatGesture(def.EffectiveGesture),
                Background = MakeGestureBrush(def),
                BorderBrush = new SolidColorBrush(isModified
                    ? Color.FromRgb(0x4A, 0xA3, 0xD8)   // змінена — блакитна рамка
                    : Color.FromRgb(0x2A, 0x5A, 0x78)),  // default — нейтральна
                Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xF3, 0xFF)),
                FontFamily = new FontFamily("Segoe UI Semibold"),
                FontSize = 12.5,
                Cursor = Cursors.Hand,
                Margin = new Thickness(10, 0, 0, 0),
                Padding = new Thickness(12, 5, 12, 5),
                ToolTip = "Клікніть, щоб змінити комбінацію",
                Template = MakeGestureButtonTemplate()
            };
            gestureBtn.Click += GestureButton_Click;
            Grid.SetColumn(gestureBtn, 1);
            grid.Children.Add(gestureBtn);

            // Reset (↺) — лише для змінених.
            if (def.CurrentGesture.HasValue && def.CurrentGesture.Value != def.DefaultGesture)
            {
                var resetBtn = new Button
                {
                    Tag = def.Id.Value,
                    Content = "↺",
                    Style = (Style)FindResource("HubResetGlyph"),
                    Margin = new Thickness(4, 0, 0, 0),
                    ToolTip = "Скинути до типової",
                    Cursor = Cursors.Hand
                };
                resetBtn.Click += ResetItem_Click;
                Grid.SetColumn(resetBtn, 2);
                grid.Children.Add(resetBtn);
            }

            return grid;
        }

        private static Brush MakeGestureBrush(HotkeyDefinition def)
        {
            // Змінені — блакитна рамка; default — нейтральна. Фон однаковий (темний).
            bool modified = def.CurrentGesture.HasValue && def.CurrentGesture.Value != def.DefaultGesture;
            return new SolidColorBrush(Color.FromRgb(0x0A, 0x1D, 0x29));
        }

        private static Brush MakeCaptureBrush()
        {
            return new SolidColorBrush(Color.FromRgb(0x1A, 0x3D, 0x58));
        }

        private static ControlTemplate MakeGestureButtonTemplate()
        {
            // Плоский шаблон: Border (фон + рамка) + ContentPresenter.
            var template = new ControlTemplate(typeof(Button));
            var factory = new FrameworkElementFactory(typeof(Border));
            factory.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent), Path = new System.Windows.PropertyPath(Button.BackgroundProperty) });
            factory.SetBinding(Border.BorderBrushProperty, new System.Windows.Data.Binding { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent), Path = new System.Windows.PropertyPath(Button.BorderBrushProperty) });
            factory.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            factory.SetValue(Border.CornerRadiusProperty, new CornerRadius(5));
            factory.SetValue(Border.PaddingProperty, new Thickness(12, 5, 12, 5));
            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            factory.AppendChild(cp);
            template.VisualTree = factory;
            return template;
        }

        // ============ Стан capture ============

        private sealed class CaptureRow(Button button, HotkeyDefinition def)
        {
            public Button Button { get; } = button;
            public HotkeyDefinition Def { get; } = def;
        }
    }
}