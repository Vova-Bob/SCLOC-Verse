namespace SCLOCVerse.Services.InputSystem
{
    /// <summary>
    /// Строго типізований ідентифікатор глобальної гарячої клавіші.
    /// </summary>
    public readonly record struct HotkeyId(string Value)
    {
        /// <summary>
        /// Повертає рядкове представлення ідентифікатора.
        /// </summary>
        public override string ToString() => Value;
    }
}
