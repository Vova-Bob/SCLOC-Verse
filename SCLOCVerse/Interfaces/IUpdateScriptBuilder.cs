namespace SCLOCVerse.Interfaces
{
    public interface IUpdateScriptBuilder
    {
        string BuildScript(
            string installerPath,
            string applicationExePath);
    }
}
