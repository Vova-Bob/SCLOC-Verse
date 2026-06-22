namespace SCLOCVerse.Models
{
    public sealed record LocalizationInstallResult(
         bool Success,
         string EnvironmentName,
         string GlobalIniPath,
         string? UserCfgPath,
         string Message);
}
