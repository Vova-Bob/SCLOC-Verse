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
    /// Панель «Гарячі клавіші» Settings Hub — інтерактивний редактор.
    /// Головний принцип: НУЛЬОВИЙ LAYOUT-SHIFT. Геометрія рядка фіксована завжди.
    /// Змінюється лише вміст фіксованих зон (keycap Content, icon Content/Opacity).
    /// </summary>
    public partial class HotkeysSettingsPane : UserControl
    {
        private IHotkeyService? _hotkeyService;
        private readonly Dictionary<string, HotkeyDefinition> _definitionsById = new();

        // Стани кожного рядка (для оновлення без перебудови Grid).
        private readonly Dictionary<string, RowElements> _rows = new();

        // Поточний рядок у режимі capture (null = не в режимі).
        private RowElements? _capture;

        // Контекст для conflict-діалогу.
        private HotkeyDefinition? _conflictDef;
        private HotkeyGesture _conflictGesture;
        private List<HotkeyDefinition>? _pendingCategoryReset;

        public HotkeysSettingsPane()
        {
            InitializeComponent();
        }

        /// <summary>З'єднує панель із HotkeyService і будує список.</summary>
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
            _rows.Clear();
            var list = _hotkeyService?.GetDefinitions() ?? Enumerable.Empty<HotkeyDefinition>().ToList();
            var modifiedCount = list.Count(d =>
                (d.CurrentGesture.HasValue && d.CurrentGesture.Value != d.DefaultGesture) || d.IsUnassigned);
            CountText.Text = $"{list.Count()} дій" + (modifiedCount > 0 ? $" · {modifiedCount} змінено" : "");

            foreach (var group in list.GroupBy(d => ResolveGroup(d.Id.Value))
                                       .OrderBy(g => GroupOrder(g.Key)))
            {
                HotkeyList.Children.Add(MakeGroupHeader(group.Key));
                foreach (var def in group)
                {
                    _definitionsById[def.Id.Value] = def;
                    var row = MakeRow(def);
                    HotkeyList.Children.Add(row.Grid);
                }
            }
        }

        // ============ Рядок: фіксована геометрія (Col0=*, Col1=130, Col2=30) ============

        private RowElements MakeRow(HotkeyDefinition def)
        {
            bool isModified = def.CurrentGesture.HasValue && def.CurrentGesture.Value != def.DefaultGesture;
            bool isUnassigned = def.IsUnassigned;
            bool isChanged = isModified || isUnassigned;

            var grid = new Grid { Margin = new Thickness(0, 2, 0, 2), MinHeight = 42 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) }); // keycap — фіксований
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });   // icon — фіксований

            // Колонка 0: опис + «Типово:» (тільки для modified).
            var textPanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };

            var desc = new TextBlock
            {
                Text = def.Description ?? def.Id.Value,
                FontFamily = new FontFamily("Segoe UI Semibold"),
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromRgb(0xEA, 0xF4, 0xFF)),
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            textPanel.Children.Add(desc);

            // «Типово:» — лише для modified або unassigned.
            bool showDefault = isModified || def.IsUnassigned;
            var defaultLabel = new TextBlock
            {
                Text = "Типово: " + FormatGesture(def.DefaultGesture),
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(0x6F, 0x8C, 0xA8)),
                Opacity = showDefault ? 0.6 : 0
            };
            textPanel.Children.Add(defaultLabel);

            Grid.SetColumn(textPanel, 0);
            grid.Children.Add(textPanel);

            // Колонка 1: keycap (фіксована ширина 130px).
            string keycapText = isUnassigned ? "Не призначено" : FormatGesture(def.EffectiveGesture);
            Color keycapBorder = isUnassigned
                ? Color.FromRgb(0x3A, 0x4A, 0x56)  // unassigned — сіра, приглушена
                : isModified
                    ? Color.FromRgb(0x4A, 0xA3, 0xD8)  // modified — блакитна
                    : Color.FromRgb(0x2A, 0x5A, 0x78); // default — нейтральна
            Color keycapText2 = isUnassigned
                ? Color.FromRgb(0x6F, 0x8C, 0xA8)  // unassigned — приглушений
                : Color.FromRgb(0xE8, 0xF3, 0xFF); // нормальний

            var keycap = new Button
            {
                Tag = def.Id.Value,
                Content = keycapText,
                Background = new SolidColorBrush(Color.FromRgb(0x0A, 0x1D, 0x29)),
                BorderBrush = new SolidColorBrush(keycapBorder),
                Foreground = new SolidColorBrush(keycapText2),
                FontFamily = new FontFamily(isUnassigned ? "Segoe UI" : "Consolas"),
                FontSize = isUnassigned ? 11 : 12,
                Cursor = Cursors.Hand,
                Margin = new Thickness(10, 0, 0, 0),
                Padding = new Thickness(8, 4, 8, 4),
                HorizontalContentAlignment = HorizontalAlignment.Center,
                ToolTip = "Клікніть, щоб змінити",
                Template = MakeKeycapTemplate()
            };
            keycap.Click += Keycap_Click;
            Grid.SetColumn(keycap, 1);
            grid.Children.Add(keycap);

            // Колонка 2: icon (↺ або ✕) — ЗАВЖДИ зарезервований, Opacity керує.
            var icon = new Button
            {
                Tag = def.Id.Value,
                Content = "↺",
                Background = Brushes.Transparent,
                BorderBrush = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Foreground = new SolidColorBrush(Color.FromRgb(0x6F, 0x8C, 0xA8)),
                FontSize = 15,
                Cursor = Cursors.Hand,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                Opacity = isChanged ? 1 : 0,
                IsHitTestVisible = isChanged,
                ToolTip = "Скинути до типової"
            };
            icon.Click += ResetIcon_Click;

            // Hover: icon opacity 0→0.4 для unmodified.
            grid.MouseEnter += (_, _) => { if (icon.Opacity == 0) icon.Opacity = 0.35; };
            grid.MouseLeave += (_, _) => { if (!_rows.GetValueOrDefault(def.Id.Value)?.IsModified ?? false && icon.Content is "↺") icon.Opacity = 0; };

            Grid.SetColumn(icon, 2);
            grid.Children.Add(icon);

            var elements = new RowElements(grid, keycap, icon, desc, defaultLabel, def)
            {
                IsModified = isModified
            };
            _rows[def.Id.Value] = elements;
            return elements;
        }

        // ============ Стани рядка: змінюють лише Content + Opacity ============

        private void SetCaptureState(RowElements row, string keycapText)
        {
            // Keycap → capture текст; Icon → ✕ (cancel), повна видимість.
            row.Keycap.Content = keycapText;
            row.Keycap.BorderBrush = new SolidColorBrush(Color.FromRgb(0xE3, 0x6D, 0x3A)); // помаранчевий
            row.Keycap.Foreground = new SolidColorBrush(Color.FromRgb(0xE3, 0x6D, 0x3A));
            row.Icon.Content = "✕";
            row.Icon.Opacity = 1;
            row.Icon.IsHitTestVisible = true;
            row.Icon.ToolTip = "Скасувати";
            row.DefaultLabel.Opacity = 0;
        }

        private void SetNormalState(RowElements row)
        {
            var def = row.Def;
            bool isModified = def.CurrentGesture.HasValue && def.CurrentGesture.Value != def.DefaultGesture;
            bool isUnassigned = def.IsUnassigned;
            bool isChanged = isModified || isUnassigned;

            // Keycap.
            row.Keycap.Content = isUnassigned ? "Не призначено" : FormatGesture(def.EffectiveGesture);
            row.Keycap.BorderBrush = new SolidColorBrush(
                isUnassigned ? Color.FromRgb(0x3A, 0x4A, 0x56)
                : isModified ? Color.FromRgb(0x4A, 0xA3, 0xD8)
                : Color.FromRgb(0x2A, 0x5A, 0x78));
            row.Keycap.Foreground = new SolidColorBrush(
                isUnassigned ? Color.FromRgb(0x6F, 0x8C, 0xA8)
                : Color.FromRgb(0xE8, 0xF3, 0xFF));
            row.Keycap.FontFamily = new FontFamily(isUnassigned ? "Segoe UI" : "Consolas");
            row.Keycap.FontSize = isUnassigned ? 11 : 12;

            // Icon.
            row.Icon.Content = "↺";
            row.Icon.Opacity = isChanged ? 1 : 0;
            row.Icon.IsHitTestVisible = isChanged;
            row.Icon.ToolTip = "Скинути до типової";

            // «Типово:».
            row.DefaultLabel.Opacity = isChanged ? 0.6 : 0;
            row.IsModified = isChanged;
        }

        // ============ Click handlers ============

        private void Keycap_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not string id)
                return;
            if (!_rows.TryGetValue(id, out var row))
                return;

            // Вийти з попереднього capture.
            if (_capture is not null && _capture != row)
                SetNormalState(_capture);

            // Увійти в capture.
            _capture = row;
            SetCaptureState(row, "● Очікування…");

            var window = Window.GetWindow(this);
            if (window != null)
                window.PreviewKeyDown += Capture_PreviewKeyDown;
        }

        private void ResetIcon_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not string id)
                return;

            // Якщо зараз capture (icon = ✕) → скасувати.
            if (_capture is not null && _capture.Def.Id.Value == id && btn.Content is "✕")
            {
                CancelCapture();
                e.Handled = true;
                return;
            }

            // Reset до типової = Rebind до DefaultGesture (уніфікований flow).
            if (!_definitionsById.TryGetValue(id, out var def))
                return;
            if (!def.CurrentGesture.HasValue && !def.IsUnassigned)
                return;

            AttemptRebind(def, def.DefaultGesture);
        }

        // ============ Capture ============

        private void Capture_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (_capture is null)
                return;

            var key = e.Key;

            if (key == Key.Escape)
            {
                CancelCapture();
                e.Handled = true;
                return;
            }

            if (HotkeyCaptureMapper.IsModifierKey(key))
            {
                _capture.Keycap.Content = FormatModifiers(Keyboard.Modifiers) + "+…";
                return;
            }

            var gesture = HotkeyCaptureMapper.TryMap(key, Keyboard.Modifiers);
            if (gesture is null)
            {
                _capture.Keycap.Content = "● Очікування…";
                return;
            }

            e.Handled = true;
            var def = _capture.Def;
            var gestureVal = gesture.Value;

            // Вийти з capture (відписатися від PreviewKeyDown).
            var window = Window.GetWindow(this);
            if (window != null)
                window.PreviewKeyDown -= Capture_PreviewKeyDown;
            _capture = null;

            // Rebind.
            AttemptRebind(def, gestureVal);
        }

        private void CancelCapture()
        {
            if (_capture is null)
                return;

            var window = Window.GetWindow(this);
            if (window != null)
                window.PreviewKeyDown -= Capture_PreviewKeyDown;

            SetNormalState(_capture);
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
                    SetNormalState(_rows[def.Id.Value]);
                    break;

                case RebindResult.Conflict:
                    var conflictDef = _definitionsById.TryGetValue(conflictingId.Value, out var c) ? c : null;
                    var conflictName = conflictDef?.Description ?? conflictingId.Value;
                    _conflictDef = def;
                    _conflictGesture = gesture;
                    ConflictTitle.Text = "Конфлікт комбінацій";
                    ConflictMessage.Text = $"«{conflictName}» уже використовує цю комбінацію.\nПеревизначити? (Та комбінація буде скинута.)";
                    ConflictDialog.Visibility = Visibility.Visible;
                    break;

                case RebindResult.RegistrationFailed:
                    ShowInlineError("Не вдалося зареєструвати комбінацію.\nМожливо, вона зайнята іншим застосунком.");
                    break;
            }
        }

        // ============ Conflict dialog handlers ============

        private void ConflictConfirm_Click(object sender, RoutedEventArgs e)
        {
            ConflictDialog.Visibility = Visibility.Collapsed;
            if (_conflictDef is null || _hotkeyService is null)
                return;

            var r2 = _hotkeyService.Rebind(_conflictDef.Id, _conflictGesture, HotkeyConflictPolicy.Replace, out _);
            if (r2 == RebindResult.Success)
                RebuildList();
            else if (r2 == RebindResult.RegistrationFailed)
                ShowInlineError("Не вдалося зареєструвати комбінацію.\nМожливо, вона зайнята іншим застосунком.");

            _conflictDef = null;
        }

        private void ConflictCancel_Click(object sender, RoutedEventArgs e)
        {
            ConflictDialog.Visibility = Visibility.Collapsed;
            _conflictDef = null;
            // Відновити рядок якщо він був у capture.
            if (_conflictDef is not null && _rows.TryGetValue(_conflictDef.Id.Value, out var row))
                SetNormalState(row);
        }

        private void ShowInlineError(string message)
        {
            ConflictTitle.Text = "Помилка";
            ConflictMessage.Text = message;
            ConflictConfirm.Content = "Зрозуміло";
            ConflictConfirm.Click -= ConflictConfirm_Click;
            ConflictConfirm.Click += (_, _) => { ConflictDialog.Visibility = Visibility.Collapsed; ConflictConfirm.Click -= (_, _) => { }; ConflictConfirm.Content = "Перевизначити"; };
            ConflictCancel.Visibility = Visibility.Collapsed;
            ConflictDialog.Visibility = Visibility.Visible;
        }

        // ============ Reset категорії ============

        private void CategoryReset_Click(object sender, RoutedEventArgs e)
        {
            if (_hotkeyService is null)
                return;

            var modified = _definitionsById.Values
                .Where(d => (d.CurrentGesture.HasValue && d.CurrentGesture.Value != d.DefaultGesture) || d.IsUnassigned)
                .ToList();
            if (modified.Count == 0)
                return;

            _pendingCategoryReset = modified;
            ConflictTitle.Text = "Скидання гарячих клавіш";
            ConflictMessage.Text = $"Скинути {modified.Count} змінених комбінацій до типових?";
            ConflictConfirm.Content = "Скинути";
            ConflictConfirm.Click -= ConflictConfirm_Click;
            ConflictConfirm.Click += CategoryResetConfirm_Click;
            ConflictCancel.Visibility = Visibility.Visible;
            ConflictCancel.Click -= ConflictCancel_Click;
            ConflictCancel.Click += CategoryResetCancel_Click;
            ConflictDialog.Visibility = Visibility.Visible;
        }

        private void CategoryResetConfirm_Click(object sender, RoutedEventArgs e)
        {
            ConflictDialog.Visibility = Visibility.Collapsed;
            RestoreDialogHandlers();

            if (_hotkeyService is null || _pendingCategoryReset is null)
                return;

            foreach (var def in _pendingCategoryReset)
                _hotkeyService.Rebind(def.Id, def.DefaultGesture, HotkeyConflictPolicy.Reject, out _);

            _pendingCategoryReset = null;
            RebuildList();
        }

        private void CategoryResetCancel_Click(object sender, RoutedEventArgs e)
        {
            ConflictDialog.Visibility = Visibility.Collapsed;
            RestoreDialogHandlers();
            _pendingCategoryReset = null;
        }

        private void RestoreDialogHandlers()
        {
            ConflictConfirm.Click -= CategoryResetConfirm_Click;
            ConflictConfirm.Click += ConflictConfirm_Click;
            ConflictCancel.Click -= CategoryResetCancel_Click;
            ConflictCancel.Click += ConflictCancel_Click;
            ConflictConfirm.Content = "Перевизначити";
            ConflictCancel.Visibility = Visibility.Visible;
        }

        // ============ Форматування ============

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
            HotkeyKey.Space => "Space",
            HotkeyKey.Tab => "Tab",
            HotkeyKey.Enter => "Enter",
            HotkeyKey.Insert => "Ins",
            HotkeyKey.Delete => "Del",
            HotkeyKey.PageUp => "PgUp",
            HotkeyKey.PageDown => "PgDn",
            HotkeyKey.OemMinus => "−",
            HotkeyKey.OemPlus => "+",
            HotkeyKey.D0 => "0",
            _ => key.ToString()
        };

        private static string FormatModifiers(ModifierKeys modifiers)
        {
            var parts = new List<string>(4);
            if ((modifiers & ModifierKeys.Control) != 0) parts.Add("Ctrl");
            if ((modifiers & ModifierKeys.Alt) != 0) parts.Add("Alt");
            if ((modifiers & ModifierKeys.Shift) != 0) parts.Add("Shift");
            if ((modifiers & ModifierKeys.Windows) != 0) parts.Add("Win");
            return parts.Count > 0 ? string.Join("+", parts) : "";
        }

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

        // ============ UI helpers ============

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

        private static ControlTemplate MakeKeycapTemplate()
        {
            var template = new ControlTemplate(typeof(Button));
            var factory = new FrameworkElementFactory(typeof(Border));
            factory.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding
            {
                RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent),
                Path = new System.Windows.PropertyPath(Button.BackgroundProperty)
            });
            factory.SetBinding(Border.BorderBrushProperty, new System.Windows.Data.Binding
            {
                RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent),
                Path = new System.Windows.PropertyPath(Button.BorderBrushProperty)
            });
            factory.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            factory.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            factory.AppendChild(cp);
            template.VisualTree = factory;
            return template;
        }

        // ============ Стан рядка ============

        private sealed class RowElements(
            Grid grid, Button keycap, Button icon,
            TextBlock desc, TextBlock defaultLabel,
            HotkeyDefinition def)
        {
            public Grid Grid { get; } = grid;
            public Button Keycap { get; } = keycap;
            public Button Icon { get; } = icon;
            public TextBlock Desc { get; } = desc;
            public TextBlock DefaultLabel { get; } = defaultLabel;
            public HotkeyDefinition Def { get; } = def;
            public bool IsModified { get; set; }
        }
    }
}