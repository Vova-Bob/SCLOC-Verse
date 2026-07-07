using SCLOCVerse.Models.Observability;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace SCLOCVerse.Interfaces
{
    /// <summary>
    /// Єдиний дозволений канал спостережуваності SCLOC-Verse
    /// (Конституція, Стаття 7 — Single Sanctioned Sink; Стаття 12 — Single Ingestion Contract).
    /// </summary>
    /// <remarks>
    /// Контракт методу <see cref="Track"/>:
    ///  - синхронний, O(1), ніколи не кидає (Стаття 1 — Absolute Isolation);
    ///  - не блокує UI (Стаття 2) — лише кладе в неблокуючу чергу;
    ///  - жоден викликач не обробляє результат.
    ///  - <paramref name="level"/> визначає, чи відправляється подія (Phase 3.5 Telemetry Policy,
    ///    KB §5.8). Рішення приймає виключно реалізація (TelemetryClient.Track) —
    ///    заборонено if(AdvancedDiagnostics) у викликачах (KB §14.19 #121).
    ///
    /// Контракт методу <see cref="FlushAsync"/>:
    ///  - гарантовано відправляє чергу до таймауту (Стаття 16 — Terminal Flush);
    ///  - ніколи не кидає (Стаття 1) — помилки всередині логуються й ковтаються;
    ///  - викликається з exception-path (catch) ПЕРЕД throw;, щоб terminal event
    ///    гарантовано дістався БД навіть за подальшого crash процесу.
    /// </remarks>
    public interface ITelemetryService
    {
        /// <summary>
        /// Зафіксувати структуровану подію спостережуваності.
        /// </summary>
        /// <param name="component">Категорія: Application/Auth/Installation/Localization/Updater/LIA/Network/UnhandledException.</param>
        /// <param name="operation">Операція всередині компонента (Start, SignIn, Sync, Install, ...).</param>
        /// <param name="outcome">Started/Succeeded/Failed/Cancelled/Skipped.</param>
        /// <param name="context">Опціональний diagnostic-контекст (обов'язковий для Failed).</param>
        /// <param name="level">Рівень телеметрії (KB §5.8): Mandatory (default, завжди), Diagnostic (при AdvancedDiagnostics ON), Local (ніколи). НЕ визначає Category — це окрема вісь (KB §14.19 #117).</param>
        void Track(string component, string operation, string outcome, TelemetryContext? context = null, TelemetryLevel level = TelemetryLevel.Mandatory);

        /// <summary>
        /// Примусово відправити накопичену чергу подій упродовж <paramref name="timeout"/>.
        /// Призначено для виклику з exception-path одразу після <c>Track(...Failed...)</c>,
        /// щоб terminal event гарантовано дістався БД до того, як виняток пошириться
        /// вище і потенційно зруйнує процес (через secondary exception у finally/Dispose).
        /// </summary>
        /// <param name="timeout">Максимальний час очікування відправки. За вичерпання — метод повертається.</param>
        /// <param name="cancellationToken">Токен скасування.</param>
        /// <returns>Task, що завершується після відправки або таймауту (завжди без винятків).</returns>
        Task FlushAsync(TimeSpan timeout, CancellationToken cancellationToken = default);
    }
}
