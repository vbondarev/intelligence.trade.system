# Контракты индикаторов

Этот документ описывает production-контракт технических индикаторов, используемых в Intelligence.TradeSystem.

Цель — сделать состояние значений индикаторов явным:
- доступно ли значение полностью;
- было ли оно рассчитано с использованием fallback-логики;
- недоступно ли оно;
- по какой причине использован fallback или значение недоступно.

---

## IndicatorValue

`IndicatorValue` — структурированный тип результата, возвращаемый всеми методами `Compute(...)`.

```csharp
public sealed record IndicatorValue
{
    public decimal? Value { get; init; }
    public bool IsAvailable { get; init; }
    public bool IsFallback { get; init; }
    public IndicatorValueReason Reason { get; init; }
}
```

**Поля:**

| Поле | Значение |
|---|---|
| `Value` | Числовое значение индикатора. `null` только при `IsAvailable = false`. |
| `IsAvailable` | `true` означает, что значение безопасно использовать. |
| `IsFallback` | `true` означает, что значение рассчитано с использованием fallback-логики (например, по неполному окну). |
| `Reason` | Причина использования fallback или недоступности. `None` используется только для полностью штатных результатов. |

### Правила

- `IsAvailable = true` означает, что `Value` не может быть `null`.
- `IsAvailable = false` означает, что `Value` должен быть `null`.
- `IsFallback = true` означает, что `IsAvailable` также должен быть `true`.
- `Reason = None` допустим только для полностью доступных значений, рассчитанных без fallback.
- Fallback- и недоступные значения всегда должны иметь причину, отличную от `None`.

---

## IndicatorValueReason

| Причина | Значение |
|---|---|
| `None` | Значение рассчитано штатно. |
| `EmptyInput` | Входная коллекция пуста. |
| `InsufficientData` | Для расчёта индикатора недостаточно данных. |
| `PartialWindow` | Значение рассчитано по неполному окну с использованием fallback. |
| `InvalidInput` | Входные данные некорректны с точки зрения рыночных данных или домена. |

---

## Фабричные методы

```csharp
IndicatorValue.Available(decimal value)
IndicatorValue.Fallback(decimal value, IndicatorValueReason reason)
IndicatorValue.Unavailable(IndicatorValueReason reason)
```

| Фабрика | Когда использовать |
|---|---|
| `Available(value)` | Индикатор штатно рассчитан по полному окну. |
| `Fallback(value, reason)` | Числовое значение существует, но рассчитано с использованием fallback-логики (например, по неполному окну). |
| `Unavailable(reason)` | Безопасного значения индикатора нет (например, недостаточно данных или вход пуст). |

**Ограничения:**
- `Fallback(..., None)` **запрещён** — выбрасывается `ArgumentException`.
- `Unavailable(None)` **запрещён** — выбрасывается `ArgumentException`.

---

## IndicatorValueExtensions

```csharp
OrNull()
RequireValue()
HasUsableValue()
ShouldReportDiagnostic()
```

### `OrNull()`

- **Предпочтительный метод для nullable-контрактов.**
- Используется при преобразовании значений индикаторов в поля snapshot/API/LLM payload.
- Возвращает `null`, если индикатор недоступен, явно сообщая об отсутствии значения.

### `RequireValue()`

- Используется там, где отсутствие значения является ошибкой, а не штатным состоянием.
- Выбрасывает `InvalidOperationException`, если `IsAvailable = false`.
- Fallback-значения (`IsFallback = true`) считаются доступными и возвращаются без исключения.

### `HasUsableValue()`

- Безопасная проверка доступности. Возвращает `false` для `null` receiver вместо исключения.
- Возвращает `true` как для полностью доступных, так и для fallback-значений.

### `ShouldReportDiagnostic()`

- Возвращает `true`, если значение рассчитано через fallback или недоступно.
- Используется для определения необходимости создания записи `IndicatorDiagnostic`.

---

## Контракты конкретных индикаторов

Все калькуляторы ожидают входные данные в **хронологическом порядке (от старых к новым)**.

### SmaCalculator

| Случай | Результат |
|---|---|
| `values == null` | `ArgumentNullException` |
| `period <= 0` | `ArgumentOutOfRangeException` |
| `values.Length == 0` | `Unavailable(EmptyInput)` |
| `values.Length < period` | `Fallback(среднее всех значений, PartialWindow)` |
| `values.Length >= period` | `Available(SMA последних period значений)` |

### EmaCalculator

| Случай | Результат |
|---|---|
| `values == null` | `ArgumentNullException` |
| `period <= 0` | `ArgumentOutOfRangeException` |
| `values.Length == 0` | `Unavailable(EmptyInput)` |
| `values.Length < period` | `Fallback(среднее всех значений, PartialWindow)` |
| `values.Length == period` | `Available(начальное значение SMA — не fallback)` |
| `values.Length > period` | `Available(EMA)` |

- При `values.Length == period` начальное значение рассчитывается через SMA и **не считается fallback** — результат имеет состояние `Available`.

### RsiCalculator

| Случай | Результат |
|---|---|
| `closes == null` | `ArgumentNullException` |
| `period <= 0` | `ArgumentOutOfRangeException` |
| `closes.Length == 0` | `Unavailable(EmptyInput)` |
| `closes.Length < period + 1` | `Unavailable(InsufficientData)` |
| Флэт без движения цены | `Available(50m)` |
| Только рост | `Available(100m)` |
| Только снижение | `Available(0m)` |
| Штатные данные | `Available(rsi)` |

- **RSI не использует fallback при недостатке данных** — возвращается `Unavailable`, но никогда не `Fallback`.

### AtrCalculator

| Случай | Результат |
|---|---|
| `highs`, `lows` или `closes == null` | `ArgumentNullException` |
| `period <= 0` | `ArgumentOutOfRangeException` |
| Длины массивов различаются | `ArgumentException` (fail-fast: несовпадающие массивы указывают на ошибку конвейера) |
| `count < 2` | `Unavailable(InsufficientData)` |
| `trueRanges.Count < period` | `Fallback(среднее TR, PartialWindow)` |
| Данных достаточно | `Available(ATR со сглаживанием Wilder)` |

- Для ATR требуется минимум **2 свечи**.
- Массивы `highs`, `lows` и `closes` для ATR должны иметь **одинаковую длину**; несовпадающие длины отклоняются с `ArgumentException`.

---

## Индикаторы уровней

`VolumeProfileDetector` возвращает `LevelSet` и **не** использует `IndicatorValue`.

> **Production-статус:** `VolumeProfileDetector` — активный детектор уровней, используемый в production.
> Он применяет упрощённый алгоритм Volume Profile (объём свечи равномерно распределяется по диапазону `Low–High`) и **не** является точной моделью Volume-at-Price.
> Wire-значение поля `source` в LLM payload всегда равно `"volume-profile"` (строковая константа в kebab-case).
> Это ограничение передаётся LLM-потребителям через поля `source` и `strengthLabel`.
> Заменяй этот детектор только при появлении полноценной реализации VAP; при этом одновременно обновляй `LevelSource` и константу `LevelSourceV1` в `LlmPayloadMapperExtensions`.

---

### VolumeProfileOptions

Конфигурация для `VolumeProfileDetector.Detect(...)`. Передай `null`, чтобы использовать `VolumeProfileOptions.Default`.

```csharp
public sealed class VolumeProfileOptions
{
    public static readonly VolumeProfileOptions Default = new();

    public int BucketCount { get; }           // по умолчанию: 100
    public decimal HvnThresholdRatio { get; } // по умолчанию: 0.70

    public VolumeProfileOptions(int bucketCount = 100, decimal hvnThresholdRatio = 0.70m);
}
```

| Параметр | По умолчанию | Ограничение | Значение |
|---|---|---|---|
| `BucketCount` | `100` | Должен быть `> 0` | Количество ценовых корзин одинаковой ширины, разделяющих диапазон `[min(Low), max(High)]` |
| `HvnThresholdRatio` | `0.70` | Должен находиться в `(0, 1]` | Доля максимального объёма корзины, выше которой корзина считается High Volume Node (HVN) |

---

### LevelSet

Тип результата `VolumeProfileDetector.Detect(...)`.

```csharp
public sealed record LevelSet(
    LevelInfo? Support1,
    LevelInfo? Support2,
    LevelInfo? Resistance1,
    LevelInfo? Resistance2
);
```

| Поле | Значение |
|---|---|
| `Support1` | Ближайшая обнаруженная поддержка ниже `klines[^1].Close` или `null`, если уровень не найден |
| `Support2` | Вторая ближайшая поддержка ниже текущей цены или `null` |
| `Resistance1` | Ближайшее обнаруженное сопротивление выше текущей цены или `null` |
| `Resistance2` | Второе ближайшее сопротивление выше текущей цены или `null` |

- **`null` означает, что уровень не обнаружен** — это **не** означает `0`.
- Никогда не подставляй `0m` вместо уровня `null`.

---

### LevelInfo

Каждый ненулевой уровень представлен record `LevelInfo` с четырьмя полями.

```csharp
public sealed record LevelInfo(
    decimal Price,
    decimal Strength,
    LevelSource Source,
    decimal ClusterVolume
);
```

| Поле | Тип | Значение |
|---|---|---|
| `Price` | `decimal` | Взвешенный по объёму центр HVN-кластера — цена в центре масс объединённых корзин |
| `Strength` | `decimal` | Относительная сила уровня в диапазоне `[0, 1]`; формула приведена ниже |
| `Source` | `LevelSource` | Способ обнаружения уровня; сейчас всегда `LevelSource.SimplifiedVolumeProfile`; в LLM payload сериализуется как `"volume-profile"` (kebab-case) |
| `ClusterVolume` | `decimal` | Суммарный объём всех корзин, составляющих кластер |

**Формула `Strength`:**

```
Strength = Math.Round(ClusterVolume / maxClusterVolume, 4)
```

где `maxClusterVolume` — суммарный объём крупнейшего HVN-кластера в текущем профиле.

- `Strength = 1.0` → кластер имеет максимальный объём в профиле.
- `Strength < 1.0` → кластер слабее относительно доминирующего кластера.
- `Strength = 0.0` → только fallback (суммарный объём кластера равен `0`). Никогда не используй `0` как замену состояния «уровень не найден» — вместо этого используй `null` в полях `LevelSet`.

---

### LevelSource

```csharp
public enum LevelSource
{
    SimplifiedVolumeProfile = 0
}
```

`SimplifiedVolumeProfile` означает, что уровень обнаружен упрощённым алгоритмом Volume Profile: объём свечи равномерно распределяется по диапазону `Low–High`. Это **не** точная модель Volume-at-Price.

---

### Правила

- `VolumeProfileDetector` — упрощённая реализация Volume Profile. Не считай её точной VAP-моделью, пока она явно не заменена соответствующей реализацией.
- Обнаруженный уровень всегда является полноценным объектом `LevelInfo`, а не обычным `decimal`.
- `null` в поле `LevelSet` означает, что уровень не найден; числовой fallback отсутствует.
- `VolumeProfileDetector` не создаёт `IndicatorDiagnostics`; отсутствующие уровни представлены полями `null` в `LevelSet`.

---

### Пример

**Форма C# snapshot:**
```csharp
LevelSet levels = new(
    Support1: null,
    Support2: null,
    Resistance1: new LevelInfo(80751.47m, 1.0000m, LevelSource.SimplifiedVolumeProfile, 524830.5m),
    Resistance2: new LevelInfo(81200.00m, 0.7312m, LevelSource.SimplifiedVolumeProfile, 383401.2m)
);
```

**Соответствующая форма LLM payload:**
```json
{
  "support1": null,
  "support2": null,
  "resistance1": {
    "price": 80751.47,
    "strength": 1.0000,
    "strengthLabel": "Strong",
    "source": "volume-profile",
    "distancePct": 0.42,
    "clusterVolume": 524830.5
  },
  "resistance2": {
    "price": 81200.00,
    "strength": 0.7312,
    "strengthLabel": "Moderate",
    "source": "volume-profile",
    "distancePct": 0.91,
    "clusterVolume": 383401.2
  }
}
```

---

## IndicatorDiagnostics

Записи `IndicatorDiagnostic` объясняют, почему индикатор рассчитан через fallback или недоступен.

```csharp
public sealed record IndicatorDiagnostic
{
    public string Timeframe { get; init; }
    public string Indicator { get; init; }
    public IndicatorValueReason Reason { get; init; }
    public bool IsFallback { get; init; }
    public string Message { get; init; }
}
```

**Правила:**
- Диагностика создаётся, когда `ShouldReportDiagnostic()` возвращает `true` (значение является fallback или недоступно).
- Для полностью доступных значений `Available(...)` диагностика **не** создаётся.
- Диагностика должна формироваться в **стабильном порядке**: сначала по таймфрейму (`15m → 1h → 4h → 1d`), затем по индикатору внутри таймфрейма. В каждом таймфрейме порядок следующий: сначала диагностика уровня свечей (`kline`, `kline.lastFiltered`, `kline.highViolationRate`, `kline.insufficientData`), затем скалярные индикаторы (`ema20 → ema50 → ema200 → rsi14 → atr14 → volumeSma20`), затем производные индикаторы (`volumeRatio`).
- Диагностика передаётся в API/LLM payload и предупреждения анализа; её нельзя молча отбрасывать.

**Примеры формата `Message`:**
```
15m.ema200 calculated using fallback: PartialWindow.
1h.rsi14 unavailable: InsufficientData.
4h.atr14 unavailable: InsufficientData.
```

---

## Преобразование в LLM payload

Правила преобразования результатов `IndicatorValue` в API/LLM payload:

- **Не сериализуй `IndicatorValue` напрямую** в API или LLM payload.
- Поля индикаторов в LLM payload должны быть `number` или `null` (в DTO используй `decimal?`).
- При преобразовании в поля payload используй `OrNull()`.
- Причины fallback/недоступности передаются через `indicatorDiagnostics`, а не через само скалярное поле.
- **Никогда не используй `0m` вместо недоступного индикатора** в новых контрактах.

**Пример:**
```json
{
  "rsi14": null,
  "atr14": 245.5,
  "indicatorDiagnostics": [
    {
      "timeframe": "1h",
      "indicator": "rsi14",
      "reason": "InsufficientData",
      "isFallback": false,
      "message": "1h.rsi14 unavailable: InsufficientData."
    }
  ]
}
```

---

## Поведение сводки при недоступных и fallback-индикаторах

Когда индикаторы недоступны или рассчитаны через fallback, логика сводки должна быть консервативной:

- **Недоступный RSI** не должен создавать ложные флаги `rsiOversold` или `rsiOverbought`.
- **Недоступная EMA** не должна создавать ложное бычье/медвежье выравнивание или подтверждение тренда.
- **Недоступный ATR** не должен интерпретироваться как нулевая волатильность.
- **`entryQuality = Good`** не должен возвращаться, если критические индикаторы (RSI, ATR) недоступны.
- **Fallback-индикаторы** могут участвовать в расчётах сводки, но должны снижать уверенность и добавлять флаги риска.

**Ожидаемые флаги риска при проблемах с индикаторами:**

| Флаг | Условие |
|---|---|
| `IndicatorUnavailable` | Любой критический индикатор недоступен |
| `IndicatorFallback` | Любой индикатор рассчитан через fallback |
| `RsiUnavailable` | RSI недоступен |
| `AtrUnavailable` | ATR недоступен |
| `VolumeDataUnavailable` | VolumeRatio недоступен |
| `VolumeDataFallback` | VolumeSma20 использовал неполное окно |

---

## API скалярных индикаторов

Все скалярные индикаторы предоставляют один production API:

```csharp
public static IndicatorValue Compute(...)
```

Скалярные индикаторы не предоставляют устаревшие числовые методы `Compute(...)`.

Используй:

* `result.OrNull()` для nullable-контрактов snapshot/API/LLM;
* `result.RequireValue()`, когда значение обязательно;
* `result.ShouldReportDiagnostic()` для создания диагностики.

Не преобразовывай недоступные индикаторы в `0m`.

**Пример:**
```csharp
var rsi = RsiCalculator.Compute(closes, 14);

if (rsi.ShouldReportDiagnostic())
{
    // создать IndicatorDiagnostic
}

var rsiValue = rsi.OrNull();
```
