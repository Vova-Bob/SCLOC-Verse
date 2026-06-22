using SCLOCVerse.Interfaces;
using System;
using System.Reflection;

namespace SCLOCVerse.Services.ApplicationUpdate
{
    public class ApplicationVersionProvider : IApplicationVersionProvider
    {
        public Version GetCurrentVersion()
        {
            var assembly = Assembly.GetEntryAssembly() ?? typeof(App).Assembly;
            var version = assembly.GetName().Version;

            return version ?? new Version(0, 0, 0, 0);
        }
    }
}
