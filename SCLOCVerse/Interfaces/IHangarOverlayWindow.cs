namespace SCLOCVerse.Interfaces
{
    /// <summary>
    /// Специфічні методи overlay-вікна Hangar Timer, яких немає у базового Window.
    /// Класичне (HangarOverlayWindow) та компактне (HangarCompactOverlayWindow) вікна
    /// реалізують цей контракт. Загальні Window-методи (Show/Hide/Close/Activate/
    /// LocationChanged/Left/Top/IsVisible) викликаються через базовий клас Window.
    /// </summary>
    public interface IHangarOverlayWindow
    {
        /// <summary>Встановити текст підказки гарячих клавіш (класичний вигляд).</summary>
        void SetHotkeyHint(string text);

        /// <summary>Перемкнути click-through (Win32 WS_EX_TRANSPARENT).</summary>
        void ToggleClickThrough();

        /// <summary>Тимчасово вимкнути click-through під час drag.</summary>
        void BeginTemporaryDragMode();

        /// <summary>Відновити click-through після drag.</summary>
        void EndTemporaryDragMode();
    }
}