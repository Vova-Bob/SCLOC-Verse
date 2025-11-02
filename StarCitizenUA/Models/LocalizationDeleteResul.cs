namespace StarCitizenUA.Models
{
    public sealed record LocalizationDeleteResult(
         bool Success,
         bool UserCfgDeleted,
         bool GlobalIniDeleted,
         string Message);
}
