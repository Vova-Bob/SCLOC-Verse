using System.Windows;

namespace SCLOCVerse.Controls.Dialogs
{
    /// <summary>
    /// Опис кнопки уніфікованого діалогу.
    /// </summary>
    public sealed class DialogButtonInfo
    {
        public DialogButtonInfo(string text, MessageBoxResult result, bool isDefault = false, bool isDestructive = false)
        {
            Text = text;
            Result = result;
            IsDefault = isDefault;
            IsDestructive = isDestructive;
        }

        public string Text { get; }
        public MessageBoxResult Result { get; }
        public bool IsDefault { get; }
        public bool IsDestructive { get; }
    }
}
