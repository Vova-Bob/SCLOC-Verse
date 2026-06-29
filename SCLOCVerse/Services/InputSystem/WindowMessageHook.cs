using System;

namespace SCLOCVerse.Services.InputSystem
{
    /// <summary>
    /// Сигнатура хука віконної процедури, що використовується бекендами гарячих клавіш.
    /// </summary>
    /// <param name="hwnd">Хендл вікна.</param>
    /// <param name="msg">Код повідомлення.</param>
    /// <param name="wParam">Додатковий параметр повідомлення.</param>
    /// <param name="lParam">Додатковий параметр повідомлення.</param>
    /// <param name="handled">Позначає, чи повідомлення оброблено.</param>
    /// <returns>Результат обробки повідомлення.</returns>
    public delegate IntPtr WindowMessageHook(
        IntPtr hwnd,
        int msg,
        IntPtr wParam,
        IntPtr lParam,
        ref bool handled);
}
