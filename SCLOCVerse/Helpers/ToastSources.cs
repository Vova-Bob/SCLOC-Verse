namespace SCLOCVerse.Helpers
{
    /// <summary>
    /// Єдине місце toast-source ідентифікаторів для Arguments маршрутизації.
    /// Захист від випадкових розбіжностей рядків ("lia" vs "LIA" тощо).
    /// Мапляться з NotificationSource enum в MainWindow.PresentNotification.
    /// </summary>
    public static class ToastSources
    {
        public const string Lia = "lia";
        public const string Localization = "localization";
        public const string AppUpdate = "app-update";
    }
}