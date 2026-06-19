using System;

namespace StarCitizenUA.Interfaces
{
    public interface IApplicationVersionProvider
    {
        Version GetCurrentVersion();
    }
}
