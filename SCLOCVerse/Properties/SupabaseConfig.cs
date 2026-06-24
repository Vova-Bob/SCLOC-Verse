namespace SCLOCVerse.Properties
{
    /// <summary>
    /// Конфігурація Supabase для SCLOC-Verse.
    /// Значення за замовчуванням для dev-середовища.
    /// Для production рекомендується використовувати environment variables.
    /// </summary>
    public static class SupabaseConfig
    {
        public static string DefaultUrl => "https://nrytczdbhehiotflaagl.supabase.co";

        public static string DefaultAnonKey => "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6Im5yeXRjemRiaGVoaW90ZmxhYWdsIiwicm9sZSI6ImFub24iLCJpYXQiOjE3ODIyNDE0OTEsImV4cCI6MjA5NzgxNzQ5MX0.SWG7PGjg0IuneJULK5ieUB8ogyIuQ3Dgjhf686iuM84";
    }
}
