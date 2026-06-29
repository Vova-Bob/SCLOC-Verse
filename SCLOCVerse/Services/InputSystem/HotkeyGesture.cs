namespace SCLOCVerse.Services.InputSystem
{
    /// <summary>
    /// Жест глобальної гарячої клавіші — комбінація модифікаторів і клавіші.
    /// </summary>
    public readonly record struct HotkeyGesture(HotkeyModifiers Modifiers, HotkeyKey Key);
}
