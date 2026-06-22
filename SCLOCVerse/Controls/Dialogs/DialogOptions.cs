using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace SCLOCVerse.Controls.Dialogs
{
    /// <summary>
    /// Параметри для конфігурації BaseDialog.
    /// </summary>
    public sealed class DialogOptions
    {
        public string Title { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public DialogType Type { get; set; } = DialogType.Info;
        public MessageBoxButton Buttons { get; set; } = MessageBoxButton.OK;
        public Window? Owner { get; set; }
        public IReadOnlyList<DialogButtonInfo>? CustomButtons { get; set; }
        public Brush? AccentBrush { get; set; }
    }
}
