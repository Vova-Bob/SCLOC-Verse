namespace SCLOCVerse.Notifications;

/// <summary>
/// Канонічні назви каналів сповіщень.
/// Збігаються зі значеннями notification_queue.provider у БД.
/// Жодної магії (користувач п.7): реєстрація та зіставлення йде за константами.
/// </summary>
public static class NotificationChannel
{
    public const string Discord = "Discord";
    public const string Email = "Email";
    public const string Telegram = "Telegram";
}
