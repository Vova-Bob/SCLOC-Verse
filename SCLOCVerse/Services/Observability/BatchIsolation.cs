using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;

namespace SCLOCVerse.Services.Observability
{
    /// <summary>
    /// Бінарний поділ батчу для ізоляції poison events (P1.4).
    /// Generic алгоритм, відокремлений від Supabase-інфраструктури для testability.
    ///
    /// Алгоритм: при permanent error ділить батч навпіл і рекурсивно пробує кожну половину.
    /// Валідні події зберігаються (їхня половина проходить INSERT успішно).
    /// Poison — індивідуально підтверджується і скидається.
    ///
    /// Variant D (Try-First): одиночна подія (Count==1) скидається ТІЛЬКИ після
    /// невдалої індивідуальної спроби INSERT. Це гарантує, що валідна подія,
    /// яка опинилась у "отруєній" половині binary split, буде збережена.
    /// </summary>
    internal static class BatchIsolation
    {
        /// <summary>
        /// Виконує binary split isolation для батчу.
        ///
        /// Гарантує:
        /// — жодна валідна подія не скидається (DROP можливий лише після індивідуального permanent failure);
        /// — рекурсія завершується за O(log N) кроків;
        /// — idempotency: повторні вставки безпечні (caller garantує UNIQUE + IgnoreDuplicates).
        /// </summary>
        /// <typeparam name="T">Тип елемента батчу.</typeparam>
        /// <param name="items">Елементи для вставки.</param>
        /// <param name="insertAsync">Функція вставки батчу. Кидає виняток при помилці.</param>
        /// <param name="isPermanentError">Визначає, чи є виняток постійною помилкою (constraint violation).</param>
        /// <param name="onPoisonDrop">Callback для кожної події, індивідуально підтвердженої як poison.</param>
        public static async Task ExecuteAsync<T>(
            List<T> items,
            Func<List<T>, Task> insertAsync,
            Func<Exception, bool> isPermanentError,
            Action<T> onPoisonDrop)
        {
            if (items.Count == 0)
                return;

            try
            {
                await insertAsync(items).ConfigureAwait(false);
                return; // SUCCESS — усі події вставлені.
            }
            catch (Exception ex) when (isPermanentError(ex))
            {
                // Permanent error: батч містить ≥1 poison event.
                if (items.Count == 1)
                {
                    // Variant D: одиночна подія індивідуально перевірена INSERT
                    // і відхилена з permanent error → підтверджений poison → DROP.
                    onPoisonDrop(items[0]);
                    return;
                }

                // Binary split: ділимо навпіл, рекурсивно пробуємо кожну половину.
                // Валідна половина пройде INSERT успішно, отруєна — буде розділена далі.
                var mid = items.Count / 2;
                await ExecuteAsync(
                    items.GetRange(0, mid),
                    insertAsync, isPermanentError, onPoisonDrop).ConfigureAwait(false);
                await ExecuteAsync(
                    items.GetRange(mid, items.Count - mid),
                    insertAsync, isPermanentError, onPoisonDrop).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Transient error під час isolation — best-effort (Стаття 1).
                // Events вже дреновані з черги, не requeue (наслідок архітектурного рішення).
                Debug.WriteLine($"[Telemetry] Isolation: non-permanent error, events втрачено: {ex.Message}");
            }
        }
    }
}
