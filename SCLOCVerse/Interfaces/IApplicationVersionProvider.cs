using System;

namespace SCLOCVerse.Interfaces
{
    public interface IApplicationVersionProvider
    {
        Version GetCurrentVersion();
    }
}
