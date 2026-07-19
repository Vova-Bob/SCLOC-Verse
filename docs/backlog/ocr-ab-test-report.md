# OCR Platform — A/B Test Report

> Статус: 🔵 PENDING — вимагає тестування на реальних SC HUD скріншотах
> Дата: 2026-07-19
> Пов'язаний план: [`docs/backlog/ocr-platform.md`](ocr-platform.md)

## Мета

Визначити оптимальний PP-OCRv6 tier (tiny / small / medium) для SC HUD
на основі реальних вимірів точності, швидкості та memory footprint.

## Unit Tests (завершено)

| Компонент | Тестів | Статус |
|---|---:|---|
| ConsensusBuffer | 7 | ✅ PASS |
| FieldLock | 6 | ✅ PASS |
| BatchIsolation (існуючі) | 22 | ✅ PASS |
| **Разом** | **35** | **✅ ALL PASS** |

## A/B Test Protocol (для проведення користувачем)

### Підготовка

1. Зробити 30+ скріншотів SC mining HUD (різні матеріали, освітлення, resolutions).
2. Скопіювати в `SCLOCVerse.Tests/Assets/sc-hud-screenshots/`.
3. Запустити test harness з `dotnet test`.

### Метрики

| Метрика | Опис | Ціль |
|---|---|---|
| Per-digit accuracy | % правильно розпізнаних цифр | ≥ 95% |
| End-to-end cycle time | Capture → Pipeline → OCR → Validate (ms) | ≤ 100ms |
| Memory footprint | RSS під час 10 Hz тривалого циклу | ≤ 200 MB |
| Stability (1 hour) | Memory leaks, crash, degradation | 0 leaks |

### Кандидати

| Tier | Det size | Rec size | Очікувана точність | Очікуваний час |
|---|---:|---:|---|---|
| PP-OCRv6_tiny | 1.9 МБ | 4.4 МБ | TBD | TBD |
| PP-OCRv6_small | 9.6 МБ | 20.4 МБ | TBD | TBD |
| PP-OCRv6_medium | 59.4 МБ | 73.3 МБ | TBD | TBD |

### Поточний MVP

PP-OCRv5_mobile (det 4.7 МБ + rec 16 МБ) — завантажено в T4.2.
Версія PP-OCRv6 ще не завантажена (чекає на A/B тест).

## Результати (PENDING — заповнити після тестування)

### PP-OCRv5_mobile (поточний MVP)

| Метрика | Значення |
|---|---|
| Per-digit accuracy | TBD |
| End-to-end cycle time | TBD |
| Memory footprint | TBD |
| Stability (1 hour) | TBD |

### PP-OCRv6_tiny

| Метрика | Значення |
|---|---|
| Per-digit accuracy | TBD |
| End-to-end cycle time | TBD |
| Memory footprint | TBD |
| Stability (1 hour) | TBD |

### PP-OCRv6_small

| Метрика | Значення |
|---|---|
| Per-digit accuracy | TBD |
| End-to-end cycle time | TBD |
| Memory footprint | TBD |
| Stability (1 hour) | TBD |

## Рекомендація (PENDING)

Після завершення A/B тесту — обрати tier з найкращим балансом:
точність ≥ 95% + cycle ≤ 100ms + memory ≤ 200 MB.

## Висновок

Unit тести Validation Layer (ConsensusBuffer + FieldLock) — ✅ PASS.
Інтеграційні тести на реальних SC HUD — вимагають проведення користувачем.