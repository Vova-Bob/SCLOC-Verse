namespace SCLOCVerse.Models.AutoKey
{
    /// <summary>
    /// Стан модуля Auto Key (відображається індикатором-оверлеєм).
    /// </summary>
    public enum AutoKeyState
    {
        /// <summary>Сервіс вимкнено. Індикатор приховано (сіра крапка зарезервована).</summary>
        Off,

        /// <summary>Активне вікно Star Citizen — натискання виконуються. Зелена крапка.</summary>
        Running,

        /// <summary>Увімкнено, але Star Citizen не активний — очікування. Жовта крапка.</summary>
        Paused
    }
}
