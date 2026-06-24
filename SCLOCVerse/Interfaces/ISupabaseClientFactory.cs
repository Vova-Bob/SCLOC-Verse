using Supabase;

namespace SCLOCVerse.Interfaces
{
    /// <summary>
    /// Фабрика створення налаштованого Supabase клієнта.
    /// </summary>
    public interface ISupabaseClientFactory
    {
        Client CreateClient();
    }
}
