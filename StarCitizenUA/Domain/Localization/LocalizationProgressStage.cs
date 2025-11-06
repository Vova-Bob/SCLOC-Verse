namespace StarCitizenUA.Domain.Localization;

/// <summary>
/// Етапи процесу встановлення локалізації для прогресу в UI.
/// </summary>
public enum LocalizationProgressStage
{
    Початок,
    ОтриманняРелізу,
    ПеревіркаМетаданих,
    ЗапитДоСервера,
    Завантаження,
    ПеревіркаЦілісності,
    Завершено,
    Пропущено,
    Помилка
}
