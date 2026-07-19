# OCR Platform — Hardening Sprint

> Статус: 🔵 PLANNED
> Дата: 2026-07-19
> База: Final Forensic Review (див. звіт вище)

## Контекст

Після завершення реалізації OCR Platform (10 Epics, 36 файлів, 35 tests pass)
проведено Final Forensic Review, який виявив:
- 2 критичні проблеми (performance bottleneck, відсутність тестів на OCR core)
- 3 medium issues (Mat disposal, timer overlap, DRY)
- 4 low issues

Hardening Sprint — окремий етап для усунення виявлених проблем
БЕЗ нової функціональності.

## Мета (Acceptance Criteria)

- **AC1:** Усі Medium/High memory issues усунені (Mat disposal)
- **AC2:** Timer overlap усунено (one-shot timer pattern)
- **AC3:** Unit тести покривають OCR Engine core (Detector, Recognizer, Preprocessing)
- **AC4:** Профілювання проведено, оптимізовано лише підтверджені вузькі місця
- **AC5:** Повторний forensic review — 0 critical, 0 medium issues
- **AC6:** Build 0 warnings, 0 errors після кожної задачі
- **AC7:** Усі тести pass після кожної задачі

## Задачі

### H1 — Memory Issues (Mat disposal)

**Scope:** Усунути всі Mat disposal проблеми з forensic review.

| Issue | Файл | Фікс |
|---|---|---|
| #2: cbufMat + dilateMat не disposed | PaddleDetector.cs:139-152 | `using var cbufMat` + `using var dilateMat` |
| #3: MiningOverlayService не IDisposable | MiningOverlayService.cs | Реалізувати IDisposable + Close window |
| #4: CalibrationService Window leak | CalibrationService.cs:114-116 | try/finally навколо window.Show/Close |
| #1: Mat disposal в async path | OcrCoordinator.cs:128-139 | Додати коментар-попередження (low фактичний ризик) |

**AC:** Build 0/0, тести pass, Mat disposal deterministic.

---

### H2 — Timer Overlap (one-shot pattern)

**Scope:** Замінити `Timer(dueTime, period)` на one-shot timer + re-arm після cycle.

**Файл:** `OcrCoordinator.cs`

**Поточний код:**
```csharp
_cycleTimer = new Timer(OnCycleTick, null, _cycleInterval, _cycleInterval);
```

**Новий код:**
```csharp
// One-shot: period = Timeout.Infinite (не повторювати автоматично)
_cycleTimer = new Timer(OnCycleTick, null, _cycleInterval, Timeout.Infinite);

// В кінці OnCycleTick (після Parallel.ForEach):
if (IsRunning)
{
    _cycleTimer.Change(_cycleInterval, Timeout.Infinite); // re-arm
}
```

**Перевага:** Немає re-entrant overlap. Якщо cycle > interval — просто skip (наступний cycle почекає).

**AC:** Build 0/0, тести pass, timer не overlap.

---

### H3 — Unit тести для OCR Engine

**Scope:** Додати тести для core OCR компонентів (без реальних моделей — synthetic data).

| Тест | Файл | Що перевіряє |
|---|---|---|
| OcrPreprocessingTests | SCLOCVerse.Tests/OcrPlatform/OcrPreprocessingTests.cs | SubtractMeanNormalize на synthetic 2×2 Mat |
| PaddleRecognizerCtcDecodeTests | SCLOCVerse.Tests/OcrPlatform/CtcDecodeTests.cs | CtcGreedyDecode на synthetic Tensor |
| PaddleDetectorPostProcessTests | SCLOCVerse.Tests/OcrPlatform/DetectorPostProcessTests.cs | PostProcess на synthetic probability map |
| ResultValidatorIntegrationTests | SCLOCVerse.Tests/OcrPlatform/ResultValidatorTests.cs | Validate end-to-end з mock OcrResult |
| DefaultImagePipelineTests | SCLOCVerse.Tests/OcrPlatform/ImagePipelineTests.cs | Process на synthetic Mat |

**Принцип:** Тести НЕ потребують ONNX models. Мокуємо:
- Preprocessing: synthetic Mat 2×2 → перевіряємо tensor values
- CTC decode: synthetic Tensor [1, 5, 10] з known argmax → перевіряємо decoded text
- PostProcess: synthetic probability map [10×10] → перевіряємо contours
- ResultValidator: mock OcrResult → перевіряємо consensus + lock

**AC:** Усі нові тести pass. Загальна кількість тестів ≥ 50.

---

### H4 — Профілювання + оптимізація

**Scope:** Провести профілювання pipeline, оптимізувати лише підтверджені вузькі місця.

**Метод:**
1. Додати `Stopwatch` timing в `OcrCoordinator.ProcessRegion` для кожного кроку:
   - Capture
   - Preprocess (Normalize)
   - Detect
   - Recognize
   - Validate
2. Запустити 100 cycles на synthetic image (без реального SC)
3. Зафіксувати % часу по кроках
4. Оптимізувати лише якщо крок займає > 25% cycle time

**Принцип:** "Не оптимізуй те, що не вимірював" (користувач).

**AC:** Профіль звіт додано. Оптимізовано лише підтверджені bottlenecks.

---

### H5 — Повторний Forensic Review

**Scope:** Повторний forensic review після H1-H4.

**Перевірити:**
- Усі H1 fixes дійсно усунули memory issues
- H2 fix не створив нових threading problems
- H3 тести дійсно покривають core
- H4 оптимізації не погіршили читабельність
- Немає нових critical/medium issues

**AC:** 0 critical, 0 medium issues. OCR Platform production-ready.

---

## Execution Order

```
H1 (Memory) → H2 (Timer) → H3 (Tests) → H4 (Profiling) → H5 (Review)
```

Кожна задача — окремий commit. Build + тести після кожної.

## Notes

- НЕ додавати нову функціональність
- НЕ змінювати архітектуру
- Мінімальні зміни в існуючому коді
- Профілювання перед оптимізацією (принцип користувача)