using System;

namespace SCLOCVerse.Services.InputSystem
{
    /// <summary>
    /// Абстракція над повідомленням вікна. Ізолює бекенд від Win32 API.
    /// </summary>
    public readonly record struct WindowMessage(int Message, IntPtr WParam, IntPtr LParam);
}
