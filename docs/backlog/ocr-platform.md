# OCR Platform — Універсальна платформа комп'ютерного зору та OCR

> Картка KB: §17.8 (буде додано після старту реалізації).
> Статус: 🟡 IN PROGRESS (Implementation Mode)
> Дата останнього оновлення: 2026-07-19

## Контекст

SCLOC-Verse розширюється новою підсистемою — універсальною OCR-платформою для розпізнавання HUD Star Citizen у реальному часі. Перший споживач — **Mining Module** (читання material amount, cluster count, signature value з HUD). Майбутні споживачі: Cargo, Navigation, Salvage, Contract, Ship, HUD Analyzer.

Архітектурне рішення ухвалене після 3 ітерацій forensic-дослідження:
1. Аналіз кодової бази SCLOC-Verse (Composition Root, AntiAfkService, AutoKeyService, HangarOverlayService як шаблони).
2. Дослідження OCR-рушіїв (PaddleOCR PP-OCRv6, Tesseract, Windows.Media.Ocr, RapidOCR, ONNX).
3. Production evidence (SC-Toolbox CIG Staff Pick, Umi-OCR 46k stars, RapidOCR .NET порт).

## Архітектурне рішення (LOCKED — Single Source of Truth)

```
Screen Capture → Computer Vision (OpenCV) → OCR (PaddleOCR PP-OCRv6) → Result Validation → Consumers
```

### Інваріанти (не підлягають перегляду без вагомих технічних причин)

| Інваріант | Обґрунтування |
|---|---|
| **Єдиний OCR — PaddleOCR PP-OCRv6** | Відповідає всім критеріям проєкту (див. матрицю нижче) |
| **Інтеграція через RapidOCRCSharp + ONNX Runtime** | RapidOCR — інтеграційний шар .NET; ядро — моделі PaddleOCR |
| **OpenCV (OpenCvSharp4) — окремий шар CV** | Локалізація та підготовка зображення, не OCR |
| **Жодного Tesseract у базовій архітектурі** | Не потрібен при наявності PaddleOCR |
| **Жодного Windows.Media.Ocr у базовій архітектурі** | Не відповідає критеріям (document-oriented, без fine-tune) |
| **Жодного multi-voter у Phase 1** | Phase 1 — єдина модель; еволюція через fine-tune, не через cascade |
| **Tier PP-OCRv6 обирається після A/B-тесту** | Кандидати: tiny / small / medium — кожен на 30+ SC HUD скріншотах |
| **Усі точні числа (точність %, ms) — після A/B-тесту** | Без припущень як фактів (Evidence First) |

### Матриця відповідності PaddleOCR критеріям проєкту

| Критерій | PaddleOCR PP-OCRv6 | Evidence |
|---|---|---|
| Active development | ✅ Щомісячні релізи (3.7.0, червень 2026) | GitHub |
| Open Source (Apache 2.0) | ✅ | LICENSE |
| ONNX export | ✅ Офіційний pipeline через PaddleX | docs |
| .NET інтеграція | ✅ RapidOCRCSharp | github.com/RapidAI/RapidOCR/dotnet |
| Fine-tuning під SC HUD | ✅ Повний pipeline (tools/train.py) | docs §4 |
| Scene Text | ✅ DB detector | benchmark |
| Digital Displays tuning | ✅ PP-OCRv6 explicit | release notes 3.7.0 |
| Industrial Text tuning | ✅ PP-OCRv6 explicit | release notes 3.7.0 |
| Production references | ✅ Umi-OCR (46k stars), Docling, RAGFlow | dependents |

## Мета (Acceptance Criteria)

### Базові AC для всієї платформи

* **AC1:** Проєкт збирається без помилок та warnings після кожної задачі.
* **AC2:** Усі нові сервіси реєструються через `AppCompositionRoot` (не IoC, ADR-001).
* **AC3:** Усі нові інтерфейси у `Interfaces/`, моделі у `Models/`, сервіси у `Services/<Feature>/`.
* **AC4:** Zero Regression — стабільні файли (Hangar, AntiAfk, AutoKey) не зачеплені.
* **AC5:** Українські коментарі/doc, англійський код (AGENTS.md P0).
* **AC6:** `IDisposable` на кожному сервісі з timer/window.
* **AC7:** `StarCitizenForeground` gate на фоновому циклі (reuse існуючого helper).
* **AC8:** A/B Test report затверджений користувачем перед фіксацією tier.
* **AC9:** OCR Platform не містить жодної mining-логіки (SRP).
* **AC10:** Mining Module ізольований від OCR Platform через interfaces (DIP).

## Варіанти рішення (архітектурні —岁时 удосконалено)

Архітектура вже погоджена в попередніх ітераціях. Тут фіксуємо фінальний вибір без повторного дослідження.

### Обрано: Single-OCR (PaddleOCR) + CV Layer (OpenCV) + Validation Layer

**Обґрунтування:** Multi-voter cascade (як у SC-Toolbox) — це еволюційний шлях, що виник від нестачі off-the-shelf рушіїв. PaddleOCR PP-OCRv6 з явним tuning під digital displays робить cascade зайвим на Phase 1. Еволюція через fine-tune (Phase 2) та custom model (Phase 3) — замість додавання fallback рушіїв.

## Зона впливу

### Нові файли (створюються)

```
SCLOCVerse/
├── Services/
│   ├── OcrPlatform/
│   │   ├── Captures/
│   │   │   ├── IScreenCaptureService.cs         ( Інтерфейс в Interfaces/ )
│   │   │   ├── GdiScreenCaptureService.cs
│   │   │   └── WindowsGraphicsCaptureService.cs  ( Phase 1.5 — опціонально )
│   │   ├── Pipeline/
│   │   │   ├── IImagePipeline.cs
│   │   │   ├── DefaultImagePipeline.cs
│   │   │   └── ImagePipelineOptions.cs
│   │   ├── Vision/
│   │   │   ├── ITemplateMatcher.cs
│   │   │   ├── NccTemplateMatcher.cs
│   │   │   ├── IColorMasker.cs
│   │   │   ├── HsvColorMasker.cs
│   │   │   └── RegionOfInterest.cs
│   │   ├── Engines/
│   │   │   ├── IOcrEngine.cs                     ( в Interfaces/ )
│   │   │   ├── RapidOcrEngine.cs
│   │   │   ├── OcrResult.cs
│   │   │   └── OcrOptions.cs
│   │   ├── Validation/
│   │   │   ├── IResultValidator.cs
│   │   │   ├── ResultValidator.cs
│   │   │   ├── ConsensusBuffer.cs
│   │   │   └── FieldLock.cs
│   │   └── Coordinator/
│   │       ├── IOcrCoordinator.cs                ( в Interfaces/ )
│   │       ├── OcrCoordinator.cs
│   │       ├── OcrRegion.cs
│   │       └── OcrRegionRegistry.cs
│   └── Mining/
│       ├── Signatures/
│       │   ├── IMiningSignatureDatabase.cs       ( в Interfaces/ )
│       │   ├── MiningSignatureDatabase.cs
│       │   ├── DefaultMiningSignatures.cs
│       │   └── MiningMaterial.cs
│       ├── IMiningRecognitionService.cs          ( в Interfaces/ )
│       ├── MiningRecognitionService.cs
│       └── Overlay/
│           ├── IMiningOverlayService.cs          ( в Interfaces/ )
│           └── MiningOverlayService.cs
├── Models/
│   ├── OcrPlatform/
│   │   ├── OcrRegionResult.cs
│   │   └── OcrMatch.cs
│   └── Mining/
│       └── MiningState.cs
├── Controls/
│   └── MiningOverlayWindow.xaml(.cs)
├── Controls/SettingsHub/
│   └── MiningSettingsPane.xaml(.cs)              ( або розширення OverlaySettingsPane )
└── Resources/
    └── OcrModels/                                ( PP-OCRv6 ONNX файли )
        ├── PP-OCRv6_small_rec.onnx
        └── PP-OCRv6_small_det.onnx
```

### Існуючі файли (мінімальні зміни — згододяться)

* `SCLOCVerse.csproj` — додати PackageReference (OpenCvSharp4, RapidOCRCSharp, OnnxRuntime).
* `Composition/AppCompositionRoot.cs` — додати构造цію + dispose нових сервісів.
* `Interfaces/IPreferencesService.cs` — додати Get/Set методи для Mining налаштувань.
* `Services/SettingsService.cs` — реалізувати нові Get/Set.
* `Services/InputSystem/HotkeyIds.cs` — додати `MiningToggle` const.
* `Controls/SettingsHub/OverlaySettingsPane.xaml(.cs)` — додати секцію Mining (або нова Pane).
* `MainWindow.xaml.cs` — мінімальні зміни для інтеграції mining UI (тільки якщо потрібно).

### Не зачіпаються (Zero Regression)

* `HangarTimer/*`, `HangarOverlay*`
* `AntiAfk/*`
* `AutoKey/*`
* `Auth/*`
* `Observability/*`
* `LocalizationServices/*`
* `LiaServices/*`
* `ApplicationUpdate/*`

## Ризики та мітігація

| Ризик | Step | Мітігація |
|---|---|---|
| Розмір інсталятора зросте на ~50-100 МБ | Усі Epics | Використовувати PP-OCRv6_small або tiny (не medium); заміряти після Phase 1 |
| Memory footprint під час роботи | Epic 4 (OCR) | A/B Test (Epic 11); за потреби — PP-OCRv6_tiny |
| Складність OpenCV native deps | Epic 3 (CV) | `OpenCvSharp4.runtime.win`NuGet — все включено |
| Нестабільність ONNX при 10 Hz | Epic 4, Epic 6 | Long-running test в Phase A/B тесті |
| Regression стабільних сервісів | Усі Epics | Не чіпати `Hangar/AntiAfk/AutoKey`; додавати через Composition Root |
| Український текст у commits/документах | Усі Tasks | P0 UTF-8 правило (AGENTS.md) |
| Calibration UX складний для користувача | Epic 10 | MVP: hardcoded positions для 1080p/1440p; Phase 2 — calibrator |

## Quality Gates

* **Core:** Root Cause, Zero Regression, Evidence, UTF-8.
* **Extended (домен CV/OCR):** Simplicity / Anti-Abstraction, Integration First.

## Статус

🟡 IN PROGRESS — Implementation Mode активований. Див. Roadmap нижче.

---

# Roadmap — Epics, Tasks, Steps

## Epic 1: Foundation (Project Structure + Dependencies)

**Мета:** Створити структуру папок + додати NuGet-залежності. Проєкт залишається працездатним (нуль інтеграції, просто scaffolding).

### T1.1 Create folder structure for OCR Platform

**Goal:** Створити порожні папки `Services/OcrPlatform/...`, `Services/Mining/...`, `Models/OcrPlatform/`, `Models/Mining/`.

**Steps:**
1. Створити `SCLOCVerse/Services/OcrPlatform/` з підпапками `Captures/`, `Pipeline/`, `Vision/`, `Engines/`, `Validation/`, `Coordinator/`.
2. Створити `SCLOCVerse/Services/Mining/` з підпапками `Signatures/`, `Overlay/`.
3. Створити `SCLOCVerse/Models/OcrPlatform/`.
4. Створити `SCLOCVerse/Models/Mining/`.
5. У кожну папку додати `.gitkeep` (порожні папки не трекаються git).

**AC:** `dotnet build` successful. Усі папки існують.

---

### T1.2 Add NuGet packages to csproj

**Goal:** Додати NuGet-залежності для CV та OCR. Без коду, тільки PackageReference.

**Steps:**
1. Додати в `SCLOCVerse.csproj`:
   ```xml
   <PackageReference Include="OpenCvSharp4" Version="4.10.0.20240630" />
   <PackageReference Include="OpenCvSharp4.runtime.win" Version="4.10.0.20240630" />
   <PackageReference Include="Microsoft.ML.OnnxRuntime" Version="1.20.1" />
   ```
2. **NOT** додавати RapidOCR поки — дослідимо на T4.1 (можливо `RapidOCRCSharp`, можливо direct ONNX).
3. `dotnet restore`.

**AC:** `dotnet build` successful. Пакети в `obj/project.assets.json`.

---

### T1.3 Reserve HotkeyId for Mining toggle

**Goal:** Додати `HotkeyIds.MiningToggle` const, що буде використано в Epic 8.

**Steps:**
1. Відкрити `Services/InputSystem/HotkeyIds.cs`.
2. Додати `public const string MiningToggle = "Mining.Toggle";` після `AutoKeyToggle`.
3. Build.

**AC:** Build successful. Const доступна.

---

## Epic 2: Screen Capture Layer

**Мета:** Абстракція `IScreenCaptureService` + перша реалізація (GDI CopyFromScreen). Працює з borderless fullscreen SC.

### T2.1 IScreenCaptureService interface

**Goal:** Контракт у `Interfaces/IScreenCaptureService.cs`.

**Steps:**
1. Створити `Interfaces/IScreenCaptureService.cs`:
   ```csharp
   public interface IScreenCaptureService
   {
       Task<BitmapSource> CaptureRegionAsync(Rect region, CancellationToken ct = default);
       bool SupportsExclusiveFullscreen { get; }
   }
   ```
2. Build.

**AC:** Інтерфейс компілюється. Build successful.

---

### T2.2 GdiScreenCaptureService implementation

**Goal:** Перша реалізація через `System.Drawing.Graphics.CopyFromScreen`. Повертає `BitmapSource` (WPF-compatible).

**Steps:**
1. Створити `Services/OcrPlatform/Captures/GdiScreenCaptureService.cs`.
2. Реалізувати через `System.Drawing.Bitmap` + `BitmapSource.Create` або `Imaging.CreateBitmapSourceFromHBitmap`.
3. `SupportsExclusiveFullscreen => false`.
4. Обробка DPI awareness (за потреби).
5. Build.

**AC:** Build successful. Простий unit/integration тест capture регіону 100×100 на екрані повертає non-null bitmap.

---

### T2.3 Register IScreenCaptureService in AppCompositionRoot

**Goal:** Wiring. Сервіс доступний через Composition Root, але поки не використовується.

**Steps:**
1. В `Composition/AppCompositionRoot.cs`:
   - Додати private field `private readonly IScreenCaptureService _screenCaptureService;`.
   - В ctor: `_screenCaptureService = new GdiScreenCaptureService();`.
   - Додати public property `public IScreenCaptureService ScreenCapture => _screenCaptureService;`.
2. Build.

**AC:** Build successful. Property доступна.

---

## Epic 3: Computer Vision Layer (OpenCV)

**Мета:** Базові CV-операції (Greyscale, Resize, Adaptive Threshold, Color Masking) + Template Matching.

### T3.1 IImagePipeline + ImagePipelineOptions

**Goal:** Контракт pipeline обробки зображень.

**Steps:**
1. `Interfaces/IImagePipeline.cs`:
   ```csharp
   public interface IImagePipeline
   {
       Mat Process(Mat input, ImagePipelineOptions options);
   }
   ```
2. `Models/OcrPlatform/ImagePipelineOptions.cs` (record з ResizeFactor, UseAdaptiveThreshold, etc.).
3. Build.

**AC:** Build successful. Інтерфейс та Options доступні.

---

### T3.2 DefaultImagePipeline implementation (Greyscale + Resize)

**Goal:** Базовий pipeline: Greyscale → Resize. Адаптивний threshold — в T3.3.

**Steps:**
1. `Services/OcrPlatform/Pipeline/DefaultImagePipeline.cs`.
2. Реалізувати через OpenCvSharp4: `Cv2.CvtColor` (BGR → Gray), `Cv2.Resize` (factor з options).
3. Build + simple manual test.

**AC:** Build successful. На test image: Greyscale + Resize × 3 працює.

---

### T3.3 Adaptive Threshold в DefaultImagePipeline

**Goal:** Розширити DefaultImagePipeline опціональним Adaptive Threshold (Gaussian).

**Steps:**
1. Додати в `ImagePipelineOptions` поля: `UseAdaptiveThreshold`, `BlockSize`, `C`.
2. В `DefaultImagePipeline.Process`: після Resize виклик `Cv2.AdaptiveThreshold` якщо UseAdaptiveThreshold=true.
3. Build + manual test.

**AC:** Build successful. Бінаризоване зображення corректне на SC HUD crop.

---

### T3.4 HsvColorMasker + IColorMasker

**Goal:** Ізоляція кольорового тексту через HSV inRange.

**Steps:**
1. `Interfaces/IColorMasker.cs`.
2. `Services/OcrPlatform/Vision/HsvColorMasker.cs` з `Mask(Mat input, HsvRange range)`.
3. `Models/OcrPlatform/HsvRange.cs` (Low/High H/S/V).
4. Build.

**AC:** Build successful. Жовтий текст на темному фоні ізольовано.

---

### T3.5 NccTemplateMatcher + ITemplateMatcher

**Goal:** Multi-scale Normalized Cross-Correlation для HUD element localization.

**Steps:**
1. `Interfaces/ITemplateMatcher.cs` з `TemplateMatch[] Find(Mat scene, Mat template, double threshold)`.
2. `Services/OcrPlatform/Vision/NccTemplateMatcher.cs` через `Cv2.MatchTemplate` (TM_CCOEFF_NORMED) + multi-scale.
3. `Models/OcrPlatform/TemplateMatch.cs`.
4. Build + manual test на SC HUD icon.

**AC:** Build successful. Іконка знайдена на скріншоті з confidence > 0.85.

---

## Epic 4: OCR Engine Layer

**Мета:** Інтеграція RapidOCRCSharp + завантаження PP-OCRv6 ONNX моделей.

### T4.1 Research RapidOCR .NET package

**Goal:** Визначити точний NuGet-пакет для інтеграції. **(research task, без коду)**

**Steps:**
1. Перевірити `RapidAI/RapidOCRCSharp` на NuGet — яка остання стабільна версія.
2. Перевірити чи входять туди ONNX models, чи треба завантажувати окремо.
3. Зафіксувати рішення в коментарі csproj.

**AC:** Визначена точна назва та версія NuGet. Додано PackageReference.

---

### T4.2 Download PP-OCRv6_small ONNX models

**Goal:** Завантажити детекцію + розпізнавання моделі як ресурси проєкту.

**Steps:**
1. Створити `SCLOCVerse/Resources/OcrModels/`.
2. Завантажити `PP-OCRv6_small_rec_infer` → конвертувати в ONNX через PaddleX (або взяти готовий з RapidOCR repo).
3. Те саме для `PP-OCRv6_small_det_infer`.
4. Додати файли як `<EmbeddedResource>` або `<Content>` в csproj.

**AC:** Build successful. Файли .onnx розміром 10-20 МБ кожен в Output.

---

### T4.3 IOcrEngine + OcrResult + OcrOptions models

**Goal:** Контракт OCR engine + data models.

**Steps:**
1. `Interfaces/IOcrEngine.cs`:
   ```csharp
   public interface IOcrEngine
   {
       string Name { get; }
       Task<OcrResult> RecognizeAsync(Mat preprocessed, OcrOptions options, CancellationToken ct = default);
   }
   ```
2. `Models/OcrPlatform/OcrResult.cs` (RawText, Confidence, Matches).
3. `Models/OcrPlatform/OcrOptions.cs` (AllowedCharacters, MinConfidence, etc.).
4. Build.

**AC:** Build successful. Контракт доступний.

---

### T4.4 RapidOcrEngine implementation

**Goal:** Реалізація IOcrEngine через RapidOCRCSharp.

**Steps:**
1. `Services/OcrPlatform/Engines/RapidOcrEngine.cs`.
2. Lazy init RapidOCR instance (single instance, thread-safe).
3. Реалізувати RecognizeAsync через RapidOCR API.
4. Build.

**AC:** Build successful. На test image повертає non-null OcrResult.

---

### T4.5 Register IOcrEngine in AppCompositionRoot

**Goal:** Wiring.

**Steps:**
1. В `AppCompositionRoot`: додати field, ctor init, public property.
2. Build.

**AC:** Build successful. Property доступна.

---

## Epic 5: Result Validation Layer

**Мета:** Стабілізація результатів (consensus, locking, lexicon, hysteresis).

### T5.1 IResultValidator + ConfidenceFilter

**Goal:** Базовий контракт + фільтр за confidence.

**Steps:**
1. `Interfaces/IResultValidator.cs`.
2. `Services/OcrPlatform/Validation/ResultValidator.cs`.
3. Реалізувати ConfidenceFilter (відкидає результати з confidence < threshold).
4. Build + unit test.

**AC:** Build successful. Low-confidence результати відкидаються.

---

### T5.2 ConsensusBuffer (5-frame voting)

**Goal:** Rolling buffer останніх 5 результатів, majority vote.

**Steps:**
1. `Services/OcrPlatform/Validation/ConsensusBuffer.cs`.
2. Реалізувати Add(result) → GetConsensus(): значення, що зустрічається в ≥3 з 5 кадрів.
3. Build + unit test.

**AC:** Build successful. Тест: подати 5 результатів, з яких 3 однакові → повертається більшісне значення.

---

### T5.3 FieldLock (crop fingerprint)

**Goal:** Lock результату, поки crop не змінюється (NCC < 0.85).

**Steps:**
1. `Services/OcrPlatform/Validation/FieldLock.cs`.
2. Реалізувати: Update(fingerprint, result) → якщо fingerprint NCC ≥ 0.85 з cached → return cached.
3. Build + unit test.

**AC:** Build successful. Lock утримується при незмінному fingerprint.

---

### T5.4 Integrate validation in ResultValidator

**Goal:** Об'єднати ConfidenceFilter + ConsensusBuffer + FieldLock в один pipeline.

**Steps:**
1. В `ResultValidator.cs` додати orchestration.
2. Build + integration test.

**AC:** Build successful. Pipeline працює end-to-end на test cases.

---

## Epic 6: Coordinator

**Мета:** Оркестрація capture → preprocess → OCR → validation → publish.

### T6.1 OcrRegion + OcrRegionRegistry

**Goal:** Реєстр регіонів, що цікавлять consumers.

**Steps:**
1. `Models/OcrPlatform/OcrRegion.cs` (record: Id, Name, ScreenRect, Options, Enabled).
2. `Interfaces/IOcrRegionRegistry.cs`.
3. `Services/OcrPlatform/Coordinator/OcrRegionRegistry.cs` (in-memory dictionary).
4. Build.

**AC:** Build successful. Можна додати/remove/get regions.

---

### T6.2 IOcrCoordinator + OcrCoordinator skeleton

**Goal:** Skeleton з timer-driven циклом (без OCR поки).

**Steps:**
1. `Interfaces/IOcrCoordinator.cs` (event `OcrRegionReady`).
2. `Services/OcrPlatform/Coordinator/OcrCoordinator.cs`:
   - `System.Threading.Timer` (configurable interval).
   - Foreground gate: `if (!StarCitizenForeground.IsStarCitizenForeground()) return;`.
   - Skeleton цикл (лог, без виклику OCR поки).
3. Реєстрація в AppCompositionRoot.
4. Build.

**AC:** Build successful. Timer запускається (verified via Debug.WriteLine).

---

### T6.3 Wire capture + preprocess + OCR в Coordinator

**Goal:** Повний pipeline в циклі Coordinator.

**Steps:**
1. В `OcrCoordinator.TimerCallback`:
   - For each enabled region: capture → preprocess → OCR → validate → publish.
2. `Parallel.ForEach` або `Task.WhenAll` для незалежних регіонів.
3. Build.

**AC:** Build successful. На 1 тестовому регіоні повертається результат.

---

### T6.4 Publish OcrRegionReady event

**Goal:** Consumer API — подія доступна для підписки.

**Steps:**
1. В `OcrCoordinator`: `public event EventHandler<OcrRegionResult>? OcrRegionReady;`.
2. Raise event після успішної валідації.
3. Build.

**AC:** Build successful. Підписник отримує результати.

---

## Epic 7: Mining Module (перший consumer)

**Мета:** Working Mining overlay end-to-end.

### T7.1 MiningMaterial model + DefaultMiningSignatures

**Goal:** Built-in database SC mining materials.

**Steps:**
1. `Models/Mining/MiningMaterial.cs` (record).
2. `Services/Mining/Signatures/DefaultMiningSignatures.cs` — static Dictionary ~40-50 SC materials (placeholder data поки що).
3. Build.

**AC:** Build successful. DefaultMiningSignatures.Defaults містить ≥10 записів.

---

### T7.2 IMiningSignatureDatabase + implementation

**Goal:** Lookup API + JSON override.

**Steps:**
1. `Interfaces/IMiningSignatureDatabase.cs` (`MiningMaterial? Lookup(string code)`).
2. `Services/Mining/Signatures/MiningSignatureDatabase.cs` (Dictionary + опціональний JSON з `%LocalAppData%\SCLOCVerse\mining-signatures.json`).
3. Build + unit test.

**AC:** Build successful. Lookup повертає material за known code.

---

### T7.3 MiningRecognitionService

**Goal:** Subscribe to OCR results → lookup → MiningState.

**Steps:**
1. `Interfaces/IMiningRecognitionService.cs`.
2. `Services/Mining/MiningRecognitionService.cs`:
   - Subscribe to `IOcrCoordinator.OcrRegionReady`.
   - Lookup у `IMiningSignatureDatabase`.
   - Оновлення `MiningState`.
3. `Models/Mining/MiningState.cs`.
4. Build.

**AC:** Build successful. Service реагує на події.

---

### T7.4 MiningOverlayWindow XAML

**Goal:** Compact overlay badge за шаблоном AutoKeyIndicatorWindow.

**Steps:**
1. `Controls/MiningOverlayWindow.xaml` + `.cs`.
2. WS_EX_TRANSPARENT | WS_EX_LAYERED (copy from AutoKeyIndicatorWindow).
3. Topmost, transparent background.
4. 2 рядки: Material name + Cluster/Amount.
5. Build.

**AC:** Build successful. Вікно відкривається, click-through працює.

---

### T7.5 MiningOverlayService

**Goal:** Lifecycle overlay service за шаблоном IHangarOverlayService.

**Steps:**
1. `Interfaces/IMiningOverlayService.cs`.
2. `Services/Mining/Overlay/MiningOverlayService.cs`:
   - Show/Hide/Update methods.
   - Subscribe to MiningState changes → render in overlay.
3. Build.

**AC:** Build successful. Overlay оновлюється при зміні MiningState.

---

### T7.6 Mining hotkey registration + Composition Root wiring

**Goal:** Final wiring. Сервіс доступний + гаряча клавіша працює.

**Steps:**
1. В `MiningRecognitionService` constructor: register `MiningToggle` hotkey (типа Ctrl+Shift+M).
2. В `AppCompositionRoot`: construct mining services + register to OcrCoordinator + expose properties.
3. В `AppCompositionRoot.Dispose`: dispose mining services.
4. Build.

**AC:** Build successful. При натисканні hotkey — Mining overlay toggles.

---

## Epic 8: Settings Hub Integration

### T8.1 Extend IPreferencesService + SettingsService

**Goal:** Додати Get/Set для Mining налаштувань.

**Steps:**
1. В `Interfaces/IPreferencesService.cs` додати:
   - `bool GetMiningEnabled()` / `SetMiningEnabled(bool)`.
   - `double GetMiningOverlayScale()` / `SetMiningOverlayScale(double)`.
   - `string GetMiningOverlayPosition()` / `SetMiningOverlayPosition(string)`.
2. Реалізувати в `SettingsService.cs`.
3. Build.

**AC:** Build successful. Methods доступні.

---

### T8.2 MiningSettingsPane (нова Pane або секція в OverlaySettingsPane)

**Goal:** UI для налаштувань Mining.

**Steps:**
1. Вирішити: нова Pane `MiningSettingsPane` або розширення `OverlaySettingsPane`.
2. Створити XAML за Design-системою Settings Hub (KB §14.26).
3. Bind до mining services.
4. Build.

**AC:** Build successful. UI відображає та зберігає налаштування.

---

## Epic 9: Calibration Tool

### T9.1 Region selection UI

**Goal:** Користувач вказує де на екрані розпізнавати.

**Steps:**
1. Simple calibrator: overlay-вікно з drag-select → зберігає Rect.
2. Persist у Preferences.
3. Build.

**AC:** Build successful. Користувач може обрати регіон.

---

### T9.2 Hardcoded defaults для 1080p / 1440p

**Goal:** Out-of-box experience без calibration.

**Steps:**
1. Default rects for common resolutions.
2. Build.

**AC:** Build successful. Працює на 1080p без calibrator.

---

## Epic 10: A/B Testing Framework

### T10.1 Test harness

**Goal:** Console app або test для прогону моделей на тестових зображеннях.

**Steps:**
1. Unit/integration тест проєкт або standalone CLI.
2. Завантажити 30+ SC HUD screenshots (manual capture).
3. Run PP-OCRv6_tiny / small / medium на кожному.
4. Build.

**AC:** Build successful. Тест можна запустити.

---

### T10.2 Benchmark report

**Goal:** Звіт з accuracy + latency per tier.

**Steps:**
1. Запустити harness на всіх зображеннях.
2. Згенерувати markdown report.
3. Зафіксувати вибір tier.

**AC:** Report затверджений користувачем. Tier фіксований.

---

## Execution Order (послідовність)

```
Epic 1 (Foundation)        ── T1.1, T1.2, T1.3
Epic 2 (Screen Capture)    ── T2.1, T2.2, T2.3
Epic 3 (CV Layer)          ── T3.1, T3.2, T3.3, T3.4, T3.5
Epic 4 (OCR Engine)        ── T4.1, T4.2, T4.3, T4.4, T4.5
Epic 5 (Validation)        ── T5.1, T5.2, T5.3, T5.4
Epic 6 (Coordinator)       ── T6.1, T6.2, T6.3, T6.4
Epic 7 (Mining Module)     ── T7.1, T7.2, T7.3, T7.4, T7.5, T7.6
Epic 8 (Settings)          ── T8.1, T8.2
Epic 9 (Calibration)       ── T9.1, T9.2
Epic 10 (A/B Testing)      ── T10.1, T10.2
```

**Загальна оцінка:** ~30 задач, кожна 30-120 хв. Total: ~3-4 тижні.

---

## Notes для Implementation Mode

* **Одна задача за раз** — позначай через todowrite.
* **Build verification після кожної задачі** — `dotnet build` обов'язково.
* **Reserve commit перед Epic 1** — згідно AGENTS.md Git rules.
* **Commit після кожної задачі** — українською мовою.
* **Не переходити до наступної задачі** без підтвердження поточної.
