namespace StarCitizenUA.Interfaces
{
    public interface IUpdateScriptBuilder
    {
        string BuildScript(
            string installerPath,
            string applicationExePath);
    }
}
