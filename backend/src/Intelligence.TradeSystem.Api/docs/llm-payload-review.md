# LlmPayload Endpoint — Code Review (2026-04-28, v2)

Ревью production-ready готовности эндпоинта `GET /api/market-analysis/{symbol}/llm-payload`.  
Исходный код: `MarketAnalysisController`, `LlmPayloadMapperExtensions`, `LlmTimeframeSummaryBuilder`, `EntryQualityEvaluator`.  
**v2** — повторный анализ после устранения CRIT-1, CRIT-2, CRIT-3, LOW-3.

---

## ✅ Сильные стороны

| # | Описание |
|---|----------|
| 1 | **Чёткая архитектура** — Controller → Contracts → Mappers → Models/Payloads. SRP соблюдён на каждом слое. |
| 2 | **Детерминированный summary-pipeline** — `LlmTimeframeSummaryBuilder` вычисляет поля в строгом порядке (Bias → IsTrendConfirmed → MomentumState → EntryQuality → RiskFlags) с документированными инвариантами. |
| 3 | **Двухуровневые тесты** — unit-тесты на логику маппера (`EntryQualityEvaluatorTests`, `LlmTimeframeSummaryBuilderTests`) работают без HTTP-стека; endpoint-тесты покрывают 200 / 400 / 503 через `WebApplicationFactory`. |
| 4 | **`MockBehavior.Strict` во всех мок-объектах** — защита от неожиданных вызовов в тестах. |
| 5 | **`JsonIgnoreCondition.WhenWritingNull`** на `Portfolio` и `AggregatedContext` — payload чист, лишние ключи не сериализуются. |
| 6 | **`InternalsVisibleTo` + `internal`-маперы** — правильная инкапсуляция: `EntryQualityEvaluator` и `LlmTimeframeSummaryBuilder` не экспонируются публично. |
| 7 | **Стандартный `ProblemDetails`** для всех ошибок — 400 / 503 оформлены единообразно. |
| 8 | **Строго типизированные enum-ы** — `MomentumState`, `PressureLabel`, `EntryQuality`, `TimeframeBias`, `TrendStrengthLabel` исключают строковые опечатки в логике маппинга. |
| 9 | **`PortfolioSnapshot.IsAvailable`** — путь `IsAvailable = false` реализован и покрыт тестами. |

---

## ❌ Критические / высокоприоритетные проблемы

### ~~CRIT-1 — Неверный XML-doc `IsTrendConfirmed` в `LlmTimeframeSummaryPayload`~~ ✅ Исправлено

---

### ~~CRIT-2 — `BuildPortfolio` не имеет пути `IsAvailable = false`~~ ✅ Исправлено

---

### ~~CRIT-3 — `MomentumState` и `PressureLabel` — сырые строки~~ ✅ Исправлено

---

### NEW-1 — Мёртвые константы в `LlmTimeframeSummaryBuilder` генерируют предупреждения компилятора

**Файл:** `Intelligence.TradeSystem.Api/Mappers/LlmTimeframeSummaryBuilder.cs:27-28`

```csharp
private const decimal TrendStrengthStrongThreshold   = 0.80m;  // ← не используется
private const decimal TrendStrengthModerateThreshold = 0.50m;  // ← не используется
```

Логика расчёта меток силы тренда перенесена в `TrendStrengthLabelMapper`.
Константы осиротели, генерируют `CS0169`/`CS0414`.
Предупреждения компилятора — шум, маскирующий реальные проблемы.

**Решение:** удалить обе константы.

---

### NEW-2 — XML-комментарий `LlmTimeframeSummaryBuilder` ссылается на строку вместо enum

**Файл:** `Intelligence.TradeSystem.Api/Mappers/LlmTimeframeSummaryBuilder.cs:23`

```csharp
/// - MomentumState == "Healthy" →  IsTrendConfirmed == true && Bias != Neutral
```

После CRIT-3 `MomentumState` — строго типизированный enum. Комментарий устарел.

**Решение:**
```csharp
/// - MomentumState == Healthy →  IsTrendConfirmed == true &amp;&amp; Bias != Neutral
```

---

## ⚠️ Средний приоритет

### MED-1 — Все 4 таймфрейма всегда возвращаются, независимо от `AnalysisMode`

**Файл:** `LlmMarketAnalysisPayload.cs`, `LlmPayloadMapperExtensions.cs`

`AnalysisMode.Portfolio` задаёт `PrimaryTimeframes = ["4h", "1d"]`, но `M15` и `H1`
всё равно сериализуются в payload. Это:
- расходует токены LLM без пользы;
- противоречит декларируемой семантике `AnalysisMode`.

**Решение (варианты):**
- **Option A** — не сериализовать нерелевантные таймфреймы (компактный payload);
- **Option B** — добавить `isPrimary: bool` в `LlmTimeframePayload`, LLM расставит приоритеты сам.

---

### MED-2 — Тихий fallback в `AnalysisModeDefaults.GetPrimaryTimeframes`

**Файл:** `Intelligence.TradeSystem.Api/Models/Payloads/AnalysisModeDefaults.cs:17`

```csharp
_ => ["15m", "1h", "4h"],  // ← неизвестный режим молча возвращает intraday
```

**Решение:**
```csharp
_ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown AnalysisMode.")
```

---

### MED-3 — Тестовый пробел: 400 от сервиса не покрыт для LlmPayload

**Файл:** `Intelligence.TradeSystem.Api.Tests/LlmPayloadEndpointTests.cs`

Ветки `ArgumentException` и `NotSupportedException` из `BuildSnapshotAsync` присутствуют в контроллере, но не верифицированы тестом для `/llm-payload`.

**Решение:** добавить:
```csharp
[Fact] LlmPayload_Returns_BadRequest_When_Service_Throws_ArgumentException()
[Fact] LlmPayload_Returns_BadRequest_When_Service_Throws_NotSupportedException()
```

---

### MED-4 — Тестовый пробел: `AnalysisMode.Portfolio` не верифицирован

**Файл:** `Intelligence.TradeSystem.Api.Tests/LlmPayloadEndpointTests.cs`

Проверены `Intraday` (`["15m", "1h", "4h"]`) и `Swing` (`["1h", "4h", "1d"]`),
но `Portfolio` (`["4h", "1d"]`) не тестируется.

---

## 💡 Низкий приоритет / технический долг

### LOW-1 — `LevelStrengthV1 = 0.7m` и `LevelSourceV1 = "volume-profile"` захардкодены

**Файл:** `LlmPayloadMapperExtensions.cs:177-178`

V1-заглушка без TODO-метки. **Решение:** добавить `// TODO(v2): вычислять из реальных данных volume-profile`.

---

### LOW-2 — `SchemaVersion = "1.0"` без стратегии версионирования

**Файл:** `LlmPayloadMapperExtensions.cs:11`

При изменении контракта payload нет явного процесса bump версии.
**Решение:** вынести в `IOptions<LlmPayloadOptions>` или ввести `CHANGELOG.md` для payload-схемы.

---

### ~~LOW-3 — `GetPayloadAsync` в `LlmMomentumStateMappingTests` spin-up на каждый тест~~ ✅ Исправлено

---

### NEW-3 — `BuildOpenPosition.Side` — избыточный switch

**Файл:** `LlmPayloadMapperExtensions.cs:310`

```csharp
Side = s.Side switch {
    PositionSide.Long  => "Long",    // == PositionSide.Long.ToString()
    PositionSide.Short => "Short",   // == PositionSide.Short.ToString()
    _                  => s.Side.ToString()
},
```

`Long.ToString()` уже возвращает `"Long"`. Switch дублирует `.ToString()` и при добавлении нового значения enum не защищает от регрессии.

**Решение:**
```csharp
Side = s.Side.ToString(),
```

---

## Сводная таблица

| ID | Статус | Приоритет | Категория | Краткое описание |
|----|--------|-----------|-----------|-----------------|
| CRIT-1 | ✅ | — | Документация | Неверный XML-doc `IsTrendConfirmed` |
| CRIT-2 | ✅ | — | Логика | Нет пути `IsAvailable = false` для Portfolio |
| CRIT-3 | ✅ | — | Типобезопасность | `MomentumState` / `PressureLabel` — сырые строки |
| NEW-1 | 🔴 | Критический | Качество кода | Мёртвые константы в `LlmTimeframeSummaryBuilder` — предупреждения компилятора |
| NEW-2 | 🔴 | Критический | Документация | XML-doc ссылается на строку `"Healthy"` вместо `MomentumState.Healthy` |
| MED-1 | 🟠 | Средний | Контракт | Все 4 таймфрейма в payload независимо от `AnalysisMode` |
| MED-2 | 🟠 | Средний | Надёжность | Тихий fallback в `AnalysisModeDefaults` |
| MED-3 | 🟠 | Средний | Тесты | 400 от сервиса не покрыт для LlmPayload |
| MED-4 | 🟠 | Средний | Тесты | `AnalysisMode.Portfolio` Primary Timeframes не верифицирован |
| LOW-1 | 🟡 | Низкий | Технический долг | `LevelStrengthV1 = 0.7m` — неотмеченная заглушка |
| LOW-2 | 🟡 | Низкий | Технический долг | `SchemaVersion` без стратегии версионирования |
| LOW-3 | ✅ | — | Производительность тестов | `WithWebHostBuilder` в каждом тесте `LlmMomentumStateMappingTests` |
| NEW-3 | 🟡 | Низкий | Качество кода | Избыточный switch в `BuildOpenPosition.Side` |















