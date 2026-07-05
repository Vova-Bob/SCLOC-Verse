using SCLOCVerse.Interfaces;

namespace SCLOCVerse.Services.UiPolicy
{
    /// <summary>
    /// Звичайний інтерактивний запуск (користувач клікнув по ярлику).
    /// Усі UI-елементи дозволені.
    /// </summary>
    public sealed class InteractiveUiPolicy : IUiInteractionPolicy
    {
        public bool CanShowModalDialogs => true;
        public bool CanShowStartupPrompts => true;
        public bool CanShowInAppToast => true;
    }

    /// <summary>
    /// Запуск у фоновому режимі (--minimized, автозапуск з Windows).
    /// Модальні діалоги та стартові промпти заборонені — вони підняли б
    /// приховане вікно з трея. In-app toast дозволений (не виден у треї).
    /// </summary>
    public sealed class BackgroundUiPolicy : IUiInteractionPolicy
    {
        public bool CanShowModalDialogs => false;
        public bool CanShowStartupPrompts => false;
        public bool CanShowInAppToast => true;
    }
}
