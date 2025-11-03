namespace StarCitizenUA.Interfaces
{
    public interface ISettingsService
    {
        string? GetGameFolder();
        string? GetVoiceAttackFolder();
        bool TrySetGameFolder(string? path);
        bool TrySetVoiceAttackFolder(string? path);
        void ClearGameFolder();
        void ClearVoiceAttackFolder();
    }
}
