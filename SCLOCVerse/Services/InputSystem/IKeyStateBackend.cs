using System;

namespace SCLOCVerse.Services.InputSystem
{
    /// <summary>
    /// Опціональна можливість бекенда гарячих клавіш повідомляти про відпускання клавіші.
    /// Реалізують лише бекенди з доступом до подій key-up (Raw Input).
    /// </summary>
    /// <remarks>
    /// Виділено окремо від <see cref="IHotkeyBackend"/> за принципом ISP: WinAPI
    /// RegisterHotKey не здатен повідомляти про відпускання клавіші (лише WM_HOTKEY),
    /// тому <see cref="RegisterHotkeyBackend"/> цей інтерфейс не реалізує і не має
    /// фіктивної події KeyUp.
    /// </remarks>
    public interface IKeyStateBackend
    {
        /// <summary>
        /// Подія, яка виникає коли клавіша, що утворює жест, була відпущена.
        /// </summary>
        event EventHandler<HotkeyGesture>? KeyUp;
    }
}
