# OCR Platform — Profiling Report (H4)

> Статус: ✅ Профілювання додано (Stopwatch в ProcessRegion)
> Дата: 2026-07-19
> Метод: Stopwatch per-step timing в OcrCoordinator.ProcessRegion

## Методологія

Додано `Stopwatch` timing в `OcrCoordinator.ProcessRegion` для кожного кроку:
1. **Capture** — GDI CopyFromScreen + BitmapSource → Mat conversion
2. **Preprocess** — Greyscale → Resize ×3 → Adaptive Threshold
3. **OCR** — PaddleDetector + PaddleRecognizer (ONNX inference)
4. **Validate** — Confidence Filter + Consensus + FieldLock

Timing виводиться через `Debug.WriteLine` на кожному cycle:
```
[OcrCoordinator] 'mining.material_amount': capture=2ms preprocess=15ms ocr=45ms validate=1ms total=63ms
```

## Аналіз hot path (code review based)

### Крок 2: Preprocess (найбільший ризик)

**OcrPreprocessing.SubtractMeanNormalize** — pixel-by-pixel `Mat.At<Vec3b>`:

```csharp
for (var y = 0; y < rows; y++)
    for (var x = 0; x < cols; x++)
        var pixel = bgr.At<Vec3b>(y, x);  // per-pixel indexer call
```

**Оцінка:** Для регіону 80×24 (SC HUD) після Resize ×3 = 240×72 = 17,280 pixels × 3 channels = 51,840 indexer calls. На сучасному CPU ~5-10ms. Прийнятно для 5 Hz.

**Для великих регіонів (full screen 1920×1080):** 6M × 3 = 18M calls → 500-2000ms. Неприйнятно.

**Висновок:** Для SC HUD (малі регіони 80×24) — прийнятно. Для великих регіонів — потрібна оптимізація.

### Крок 3: OCR (найбільший час)

**PaddleDetector:** ONNX inference + postprocessing (threshold + contours + minBox + unclip)
- Inference: 5-15ms (mobile model, small input)
- PostProcess: 2-5ms (loops over contours)

**PaddleRecognizer:** ONNX inference + CTC decode
- Inference: 5-15ms
- CTC decode: 1-3ms (double loop over timeSteps × numClasses)

**Загалом OCR:** 10-40ms per region

### Крок 1: Capture

GDI CopyFromScreen для 80×24: <1ms
BitmapSource → Mat conversion: 1-2ms
**Загалом:** 1-3ms

### Крок 4: Validate

Dictionary lookup + ConsensusBuffer.Add + GetConsensus: <1ms
**Загалом:** <1ms

## Прогнозований breakdown для SC HUD (80×24 region, 5 Hz)

| Крок | Час (ms) | % cycle |
|---|---:|---:|
| Capture | 1-3 | 3-10% |
| Preprocess | 3-10 | 10-30% |
| OCR (det+rec) | 15-40 | 50-80% |
| Validate | <1 | <3% |
| **Total** | **20-54** | **100%** |

## Висновок профілювання

**OCR Engine (ONNX inference) — основний bottleneck (50-80%).** Це очікувано — нейромережева inference завжди домінує.

**Preprocess (Mat.At pixel loop) — 10-30%.** Для SC HUD розмірів — прийнятно. Оптимізація через unsafe pointer дасть ~2-5ms savings — **НЕ виправдано** для поточного розміру регіонів.

**Рекомендація:** НЕ оптимізувати Preprocess наразі. Профілювання на реальному SC HUD (Phase 2) покаже фактичні цифри. Якщо preprocess > 25% — тоді unsafe pointer.

## Наступні кроки

1. Запустити SCLOC-Verse з SC (Phase 2 — real testing)
2. Зібрати 100+ cycles з Debug.WriteLine timing
3. Зафіксувати фактичний breakdown
4. Оптимізувати лише підтверджені bottlenecks > 25%