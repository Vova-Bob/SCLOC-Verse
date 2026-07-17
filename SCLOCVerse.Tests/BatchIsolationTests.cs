using SCLOCVerse.Services.Observability;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace SCLOCVerse.Tests;

/// <summary>
/// Тести для BatchIsolation.ExecuteAsync — алгоритму бінарного поділу
/// для ізоляції poison events.
///
/// Кожен тест перевіряє конкретний інваріант:
/// — валідні події зберігаються;
/// — poison події скидаються;
/// — немає дублікатів;
/// — немає втрати даних.
/// </summary>
public class BatchIsolationTests
{
    // Фейкова помилка: позначаємо poison events рядком "poison" в їхньому значенні.
    private class PoisonException : Exception
    {
        public PoisonException(string message) : base(message) { }
    }

    private class TransientException : Exception
    {
        public TransientException(string message) : base(message) { }
    }

    /// <summary>
    /// Фейкова insert-функція: вставляє всі items, КРІМ тих, що містять "poison".
    /// Якщо в батчі є poison — кидає PoisonException (permanent).
    /// Якщо в батчі є "transient" — кидає TransientException (non-permanent).
    /// Інакше — додає items у inserted-список (успіх).
    /// </summary>
    private static Task FakeInsert(List<string> batch, List<string> inserted)
    {
        if (batch.Any(x => x.Contains("transient", StringComparison.OrdinalIgnoreCase)))
            throw new TransientException("network error");

        if (batch.Any(x => x.Contains("poison", StringComparison.OrdinalIgnoreCase)))
            throw new PoisonException("violates check constraint");

        inserted.AddRange(batch);
        return Task.CompletedTask;
    }

    private static bool IsPermanent(Exception ex) => ex is PoisonException;

    private static Task ExecuteAsync(
        List<string> items,
        List<string> inserted,
        List<string> dropped)
    {
        return BatchIsolation.ExecuteAsync(
            items,
            batch => FakeInsert(batch, inserted),
            IsPermanent,
            item => dropped.Add(item));
    }

    // ═══════════════════════════════════════════════════════════════
    //  Базові сценарії
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task AllValid_AllInserted_NoDrops()
    {
        var items = Enumerable.Range(0, 100).Select(i => $"valid-{i}").ToList();
        var inserted = new List<string>();
        var dropped = new List<string>();

        await ExecuteAsync(items, inserted, dropped);

        Assert.Equal(100, inserted.Count);
        Assert.Empty(dropped);
    }

    [Fact]
    public async Task EmptyBatch_Noop()
    {
        var inserted = new List<string>();
        var dropped = new List<string>();

        await ExecuteAsync(new List<string>(), inserted, dropped);

        Assert.Empty(inserted);
        Assert.Empty(dropped);
    }

    [Fact]
    public async Task SingleValid_Inserted()
    {
        var inserted = new List<string>();
        var dropped = new List<string>();

        await ExecuteAsync(new List<string> { "valid" }, inserted, dropped);

        Assert.Contains("valid", inserted);
        Assert.Empty(dropped);
    }

    [Fact]
    public async Task SinglePoison_Dropped()
    {
        var inserted = new List<string>();
        var dropped = new List<string>();

        await ExecuteAsync(new List<string> { "poison" }, inserted, dropped);

        Assert.Empty(inserted);
        Assert.Contains("poison", dropped);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Сценарії з 1 poison + валідними
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task OneValidOnePoison_ValidSaved_PoisonDropped()
    {
        var inserted = new List<string>();
        var dropped = new List<string>();

        await ExecuteAsync(new List<string> { "valid", "poison" }, inserted, dropped);

        Assert.Contains("valid", inserted);
        Assert.Contains("poison", dropped);
        Assert.Single(inserted);
        Assert.Single(dropped);
    }

    [Fact]
    public async Task OneValidOnePoison_ReversedOrder_ValidSaved()
    {
        var inserted = new List<string>();
        var dropped = new List<string>();

        await ExecuteAsync(new List<string> { "poison", "valid" }, inserted, dropped);

        Assert.Contains("valid", inserted);
        Assert.Contains("poison", dropped);
    }

    [Fact]
    public async Task ThreeEventsOnePoison_AllValidSaved()
    {
        var inserted = new List<string>();
        var dropped = new List<string>();

        await ExecuteAsync(new List<string> { "v1", "poison", "v2" }, inserted, dropped);

        Assert.Contains("v1", inserted);
        Assert.Contains("v2", inserted);
        Assert.Contains("poison", dropped);
        Assert.Equal(2, inserted.Count);
        Assert.Single(dropped);
    }

    [Fact]
    public async Task HundredValidOnePoison_AllValidSaved_PoisonDropped()
    {
        var items = Enumerable.Range(0, 99).Select(i => $"valid-{i}").ToList();
        items.Add("poison");
        var inserted = new List<string>();
        var dropped = new List<string>();

        await ExecuteAsync(items, inserted, dropped);

        Assert.Equal(99, inserted.Count);
        Assert.Single(dropped);
        Assert.Contains("poison", dropped);
        Assert.DoesNotContain("poison", inserted);
    }

    [Fact]
    public async Task PoisonInMiddle_ValidOnBothSidesSaved()
    {
        // Класичний контрприклад аудиту: poison розділяє валідні події.
        var inserted = new List<string>();
        var dropped = new List<string>();

        await ExecuteAsync(new List<string> { "v1", "v2", "v3", "poison", "v4", "v5", "v6" },
            inserted, dropped);

        Assert.Equal(6, inserted.Count);
        Assert.Single(dropped);
        Assert.Contains("poison", dropped);
        // Усі валідні збережені
        foreach (var v in new[] { "v1", "v2", "v3", "v4", "v5", "v6" })
            Assert.Contains(v, inserted);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Кілька poison events
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task TwoPoisonTwoValid_AllHandled()
    {
        var inserted = new List<string>();
        var dropped = new List<string>();

        await ExecuteAsync(new List<string> { "v1", "poison-1", "v2", "poison-2" }, inserted, dropped);

        Assert.Equal(2, inserted.Count);
        Assert.Equal(2, dropped.Count);
        Assert.Contains("v1", inserted);
        Assert.Contains("v2", inserted);
        Assert.Contains("poison-1", dropped);
        Assert.Contains("poison-2", dropped);
    }

    [Fact]
    public async Task MultiplePoison_AllDropped_AllValidSaved()
    {
        var items = new List<string> { "v1", "poison-1", "v2", "poison-2", "v3", "poison-3", "v4", "poison-4", "v5" };
        var inserted = new List<string>();
        var dropped = new List<string>();

        await ExecuteAsync(items, inserted, dropped);

        Assert.Equal(5, inserted.Count);
        Assert.Equal(4, dropped.Count);
    }

    [Fact]
    public async Task AllPoison_AllDropped()
    {
        var items = Enumerable.Range(0, 10).Select(i => $"poison-{i}").ToList();
        var inserted = new List<string>();
        var dropped = new List<string>();

        await ExecuteAsync(items, inserted, dropped);

        Assert.Empty(inserted);
        Assert.Equal(10, dropped.Count);
    }

    [Fact]
    public async Task TwoPoisonsAdjacent_ValidBetweenSaved()
    {
        // Сценарій: два poison підряд, валідні по бокам.
        // [v1, poison-1, poison-2, v2] → split [v1,poison-1] + [poison-2,v2]
        var inserted = new List<string>();
        var dropped = new List<string>();

        await ExecuteAsync(new List<string> { "v1", "poison-1", "poison-2", "v2" }, inserted, dropped);

        Assert.Contains("v1", inserted);
        Assert.Contains("v2", inserted);
        Assert.Contains("poison-1", dropped);
        Assert.Contains("poison-2", dropped);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Idempotency — дублікати
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task NoDuplicates_EachItemInsertedOnce()
    {
        var items = Enumerable.Range(0, 10).Select(i => $"valid-{i}").ToList();
        var inserted = new List<string>();
        var dropped = new List<string>();

        await ExecuteAsync(items, inserted, dropped);

        // Кожна валідна подія вставлена рівно 1 раз
        foreach (var item in items)
            Assert.Equal(1, inserted.Count(x => x == item));
    }

    [Fact]
    public async Task NoDuplicates_WithPoison_ValidInsertedOnce()
    {
        var inserted = new List<string>();
        var dropped = new List<string>();

        await ExecuteAsync(new List<string> { "v1", "poison", "v2" }, inserted, dropped);

        Assert.Equal(1, inserted.Count(x => x == "v1"));
        Assert.Equal(1, inserted.Count(x => x == "v2"));
    }

    // ═══════════════════════════════════════════════════════════════
    //  Transient error під час isolation
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task TransientDuringBatch_AllEventsLost_NoException()
    {
        var inserted = new List<string>();
        var dropped = new List<string>();

        // transient events → TransientException → best-effort loss
        await ExecuteAsync(new List<string> { "transient-1", "transient-2" }, inserted, dropped);

        // Transient error → events втрачено (best-effort, Стаття 1).
        // Не кидає, не дропає (не permanent).
        Assert.Empty(inserted);
        Assert.Empty(dropped);
    }

    [Fact]
    public async Task TransientSingleEvent_EventLost()
    {
        var inserted = new List<string>();
        var dropped = new List<string>();

        await ExecuteAsync(new List<string> { "transient" }, inserted, dropped);

        Assert.Empty(inserted);
        Assert.Empty(dropped);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Large batches
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task ThousandEventsOnePoison_999ValidSaved()
    {
        var items = Enumerable.Range(0, 999).Select(i => $"valid-{i}").ToList();
        items.Insert(500, "poison");
        var inserted = new List<string>();
        var dropped = new List<string>();

        await ExecuteAsync(items, inserted, dropped);

        Assert.Equal(999, inserted.Count);
        Assert.Single(dropped);
    }

    [Fact]
    public async Task ThousandAllPoison_AllDropped()
    {
        var items = Enumerable.Range(0, 1000).Select(i => $"poison-{i}").ToList();
        var inserted = new List<string>();
        var dropped = new List<string>();

        await ExecuteAsync(items, inserted, dropped);

        Assert.Empty(inserted);
        Assert.Equal(1000, dropped.Count);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Варіант D — ключова перевірка
    //  Count==1 після рекурсії, що містить валідну подію
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task VariantD_ValidEventInPoisonedHalf_SavedNotDropped()
    {
        // Це САМЕ ТЕСТ, що перевіряє баг аудиту:
        // [v1, v2, v3, poison] → split → [v3, poison] → split → [v3]
        // У старому коді v3 дропалась (Count==1 без перевірки).
        // У Variant D v3 повинна бути INSERT-ed і збережена.
        var inserted = new List<string>();
        var dropped = new List<string>();

        await ExecuteAsync(new List<string> { "v1", "v2", "v3", "poison" }, inserted, dropped);

        Assert.Contains("v3", inserted);
        Assert.DoesNotContain("v3", dropped);
        Assert.Contains("poison", dropped);
    }

    [Fact]
    public async Task VariantD_ValidPairedWithPoison_BothIndividuallyTested()
    {
        // [v, p] → split → [v] + [p]
        // v повинна пройти індивідуальний INSERT → SUCCESS
        // p повинна пройти індивідуальний INSERT → FAIL → DROP
        var inserted = new List<string>();
        var dropped = new List<string>();

        await ExecuteAsync(new List<string> { "valid", "poison" }, inserted, dropped);

        Assert.Contains("valid", inserted);
        Assert.Contains("poison", dropped);
        // Жодна валідна подія не дропнута
        Assert.DoesNotContain("valid", dropped);
    }

    [Fact]
    public async Task VariantD_Count1_AfterRecursion_ValidInsertNotDrop()
    {
        // Спеціально: 3 events, poison в кінці.
        // split [v1] + [v2, poison] → [v2] + [poison]
        // v2 досягає Count==1 через рекурсію → INSERT → SUCCESS
        var inserted = new List<string>();
        var dropped = new List<string>();

        await ExecuteAsync(new List<string> { "v1", "v2", "poison" }, inserted, dropped);

        Assert.Contains("v1", inserted);
        Assert.Contains("v2", inserted);  // КЛЮЧОВИЙ: v2 збережена, не дропнута
        Assert.Contains("poison", dropped);
    }
}
