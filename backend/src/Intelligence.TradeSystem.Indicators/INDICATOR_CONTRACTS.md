# Indicator Contracts

This document describes the production contract for technical indicators used by Intelligence.TradeSystem.

The goal is to make indicator values explicit:
- whether a value is fully available;
- whether it was calculated using fallback logic;
- whether it is unavailable;
- why fallback/unavailable happened.

---

## IndicatorValue

`IndicatorValue` is the structured result type returned by all `Compute(...)` methods.

```csharp
public sealed record IndicatorValue
{
    public decimal? Value { get; init; }
    public bool IsAvailable { get; init; }
    public bool IsFallback { get; init; }
    public IndicatorValueReason Reason { get; init; }
}
```

**Fields:**

| Field | Meaning |
|---|---|
| `Value` | Numeric indicator value. `null` only when `IsAvailable = false`. |
| `IsAvailable` | `true` means the value is safe to use. |
| `IsFallback` | `true` means the value was calculated using fallback logic (e.g. partial window). |
| `Reason` | Why fallback/unavailable happened. `None` for fully normal results only. |

### Rules

- `IsAvailable = true` means `Value` must be non-null.
- `IsAvailable = false` means `Value` must be null.
- `IsFallback = true` means `IsAvailable` must also be true.
- `Reason = None` is valid only for fully available non-fallback values.
- Fallback/unavailable values must always have a non-`None` reason.

---

## IndicatorValueReason

| Reason | Meaning |
|---|---|
| `None` | Value was calculated normally. |
| `EmptyInput` | Input collection was empty. |
| `InsufficientData` | There was not enough data to calculate the indicator. |
| `PartialWindow` | Value was calculated using a partial window fallback. |
| `InvalidInput` | Input data is invalid from the market-data/domain perspective. |

---

## Factory methods

```csharp
IndicatorValue.Available(decimal value)
IndicatorValue.Fallback(decimal value, IndicatorValueReason reason)
IndicatorValue.Unavailable(IndicatorValueReason reason)
```

| Factory | When to use |
|---|---|
| `Available(value)` | Indicator was calculated normally on a full window. |
| `Fallback(value, reason)` | A numeric value exists but was calculated using fallback logic (e.g. partial window). |
| `Unavailable(reason)` | No safe indicator value exists (e.g. insufficient data, empty input). |

**Constraints:**
- `Fallback(..., None)` is **not allowed** — throws `ArgumentException`.
- `Unavailable(None)` is **not allowed** — throws `ArgumentException`.

---

## IndicatorValueExtensions

```csharp
OrNull()
RequireValue()
HasUsableValue()
ShouldReportDiagnostic()
```

### `OrNull()`

- **Preferred method for nullable contracts.**
- Use when mapping indicator values to snapshot/API/LLM payload fields.
- Returns `null` when the indicator is unavailable — explicitly communicates the absence of a value.

### `RequireValue()`

- Use where the absence of a value is a bug, not a normal state.
- Throws `InvalidOperationException` when `IsAvailable = false`.
- Fallback values (`IsFallback = true`) are considered available and are returned without exception.

### `HasUsableValue()`

- Safe availability check. Returns `false` for `null` receiver instead of throwing.
- Returns `true` for both fully-available and fallback values.

### `ShouldReportDiagnostic()`

- Returns `true` when the value is fallback or unavailable.
- Used to decide whether to create an `IndicatorDiagnostic` entry.

---

## Indicator-specific contracts

All calculators expect inputs in **chronological order (oldest → newest)**.

### SmaCalculator

| Case | Result |
|---|---|
| `values == null` | `ArgumentNullException` |
| `period <= 0` | `ArgumentOutOfRangeException` |
| `values.Length == 0` | `Unavailable(EmptyInput)` |
| `values.Length < period` | `Fallback(average of all values, PartialWindow)` |
| `values.Length >= period` | `Available(sma of last period values)` |


### EmaCalculator

| Case | Result |
|---|---|
| `values == null` | `ArgumentNullException` |
| `period <= 0` | `ArgumentOutOfRangeException` |
| `values.Length == 0` | `Unavailable(EmptyInput)` |
| `values.Length < period` | `Fallback(average of all values, PartialWindow)` |
| `values.Length == period` | `Available(SMA seed — not a fallback)` |
| `values.Length > period` | `Available(EMA)` |

- When `values.Length == period`, the result is seeded by SMA and is **not** a fallback — it is `Available`.

### RsiCalculator

| Case | Result |
|---|---|
| `closes == null` | `ArgumentNullException` |
| `period <= 0` | `ArgumentOutOfRangeException` |
| `closes.Length == 0` | `Unavailable(EmptyInput)` |
| `closes.Length < period + 1` | `Unavailable(InsufficientData)` |
| Flat market (no price movement) | `Available(50m)` |
| Only gains | `Available(100m)` |
| Only losses | `Available(0m)` |
| Normal data | `Available(rsi)` |

- **RSI does not use fallback when data is insufficient** — it returns `Unavailable`, never `Fallback`.

### AtrCalculator

| Case | Result |
|---|---|
| `highs`, `lows`, or `closes == null` | `ArgumentNullException` |
| `period <= 0` | `ArgumentOutOfRangeException` |
| Array lengths differ | `ArgumentException` (fail-fast — mismatched arrays indicate a pipeline bug) |
| `count < 2` | `Unavailable(InsufficientData)` |
| `trueRanges.Count < period` | `Fallback(average TR, PartialWindow)` |
| Enough data | `Available(ATR by Wilder smoothing)` |

- ATR requires a minimum of **2 candles**.
- ATR requires `highs`, `lows`, and `closes` arrays to have **the same length** — mismatched lengths are rejected with `ArgumentException`.

---

## Level indicators

`VolumeProfileDetector` returns a `LevelSet` and does **not** use `IndicatorValue`.

> **Production status:** `VolumeProfileDetector` is the active, production-enabled level detector.
> It uses a simplified Volume Profile algorithm (kline volume distributed uniformly across the `Low–High` range) and is **not** a precise Volume-at-Price model.
> The wire value of the `source` field in the LLM payload is always `"volume-profile"` (kebab-case string constant).
> This limitation is communicated to LLM consumers via the `source` field and the `strengthLabel` field.
> Replace this detector only when a full VAP implementation is introduced; update `LevelSource` and the `LevelSourceV1` constant in `LlmPayloadMapperExtensions` together.

---

### VolumeProfileOptions

Configuration for `VolumeProfileDetector.Detect(...)`. Pass `null` to use `VolumeProfileOptions.Default`.

```csharp
public sealed class VolumeProfileOptions
{
    public static readonly VolumeProfileOptions Default = new();

    public int BucketCount { get; }           // default: 100
    public decimal HvnThresholdRatio { get; } // default: 0.70

    public VolumeProfileOptions(int bucketCount = 100, decimal hvnThresholdRatio = 0.70m);
}
```

| Parameter | Default | Constraint | Meaning |
|---|---|---|---|
| `BucketCount` | `100` | Must be `> 0` | Number of equal-width price buckets that divide `[min(Low), max(High)]` |
| `HvnThresholdRatio` | `0.70` | Must be in `(0, 1]` | Fraction of the maximum bucket volume above which a bucket is considered a High Volume Node (HVN) |

---

### LevelSet

The return type of `VolumeProfileDetector.Detect(...)`.

```csharp
public sealed record LevelSet(
    LevelInfo? Support1,
    LevelInfo? Support2,
    LevelInfo? Resistance1,
    LevelInfo? Resistance2
);
```

| Field | Meaning |
|---|---|
| `Support1` | Nearest detected support below `klines[^1].Close`, or `null` if not found |
| `Support2` | Second nearest support below current price, or `null` |
| `Resistance1` | Nearest detected resistance above current price, or `null` |
| `Resistance2` | Second nearest resistance above current price, or `null` |

- **`null` means the level was not detected** — it does **not** mean `0`.
- Never substitute `0m` for a `null` level.

---

### LevelInfo

Each non-null level is a `LevelInfo` record with four fields.

```csharp
public sealed record LevelInfo(
    decimal Price,
    decimal Strength,
    LevelSource Source,
    decimal ClusterVolume
);
```

| Field | Type | Meaning |
|---|---|---|
| `Price` | `decimal` | Volume-weighted centroid of the HVN cluster (price at the centre of mass of the merged buckets) |
| `Strength` | `decimal` | Relative strength of the level in the range `[0, 1]`; see formula below |
| `Source` | `LevelSource` | How the level was detected; currently always `LevelSource.SimplifiedVolumeProfile`; serialized in LLM payload as `"simplified-volume-profile"` (kebab-case) |
| `ClusterVolume` | `decimal` | Total volume of all buckets that make up the cluster |

**`Strength` formula:**

```
Strength = Math.Round(ClusterVolume / maxClusterVolume, 4)
```

Where `maxClusterVolume` is the total volume of the largest HVN cluster in the current profile.

- `Strength = 1.0` → the cluster has the highest volume in the profile.
- `Strength < 1.0` → the cluster is weaker relative to the dominant cluster.
- `Strength = 0.0` → fallback only (cluster volume sum was `0`). Never use `0` as a proxy for "level not found" — use `null` on `LevelSet` fields instead.

---

### LevelSource

```csharp
public enum LevelSource
{
    SimplifiedVolumeProfile = 0
}
```

`SimplifiedVolumeProfile` means the level was detected by the simplified Volume Profile algorithm: kline volume is distributed uniformly across the `Low–High` range. This is **not** a precise Volume-at-Price model.

---

### Rules

- `VolumeProfileDetector` is a simplified volume profile implementation. Do not assume it is a precise VAP model unless explicitly replaced.
- A detected level is always a full `LevelInfo` object — never a plain `decimal`.
- `null` on a `LevelSet` field means the level was not found; there is no fallback numeric substitute.
- `VolumeProfileDetector` does not produce `IndicatorDiagnostics`; missing levels are expressed as `null` fields on `LevelSet`.

---

### Example

**C# snapshot shape:**
```csharp
LevelSet levels = new(
    Support1: null,
    Support2: null,
    Resistance1: new LevelInfo(80751.47m, 1.0000m, LevelSource.SimplifiedVolumeProfile, 524830.5m),
    Resistance2: new LevelInfo(81200.00m, 0.7312m, LevelSource.SimplifiedVolumeProfile, 383401.2m)
);
```

**Corresponding LLM payload shape:**
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

`IndicatorDiagnostic` records explain why an indicator is fallback or unavailable.

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

**Rules:**
- A diagnostic is created when `ShouldReportDiagnostic()` returns `true` (value is fallback or unavailable).
- A diagnostic is **not** created for fully `Available(...)` values.
- Diagnostics must be emitted in a **stable order**: by timeframe (`15m → 1h → 4h → 1d`), then by indicator within each timeframe. Within each timeframe the order is: kline-level diagnostics first (`kline`, `kline.lastFiltered`, `kline.highViolationRate`, `kline.insufficientData`), then scalar indicators (`ema20 → ema50 → ema200 → rsi14 → atr14 → volumeSma20`), then derived indicators (`volumeRatio`).
- Diagnostics are surfaced in API/LLM payloads and analysis warnings — they must not be silently dropped.

**Message format examples:**
```
15m.ema200 calculated using fallback: PartialWindow.
1h.rsi14 unavailable: InsufficientData.
4h.atr14 unavailable: InsufficientData.
```

---

## LLM payload mapping

Rules for mapping `IndicatorValue` results into API/LLM payloads:

- **Do not serialize `IndicatorValue` directly** into API or LLM payloads.
- Indicator fields in LLM payload must be `number` or `null` (use `decimal?` in DTOs).
- Use `OrNull()` when mapping to payload fields.
- Fallback/unavailable reasons are communicated through `indicatorDiagnostics`, not through the scalar field itself.
- **Never use `0m` as a substitute for an unavailable indicator** in new contracts.

**Example:**
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

## Summary behavior with unavailable/fallback indicators

When indicators are unavailable or fallback, summary logic must be conservative:

- **Unavailable RSI** must not create false `rsiOversold` or `rsiOverbought` flags.
- **Unavailable EMA** must not create fake bullish/bearish alignment or trend confirmation.
- **Unavailable ATR** must not be interpreted as zero volatility.
- **`entryQuality = Good`** must not be returned when critical indicators (RSI, ATR) are unavailable.
- **Fallback indicators** may contribute to summary calculations but must reduce confidence and add risk flags.

**Expected risk flags for indicator issues:**

| Flag | Trigger |
|---|---|
| `IndicatorUnavailable` | Any critical indicator is unavailable |
| `IndicatorFallback` | Any indicator was calculated with fallback |
| `RsiUnavailable` | RSI is unavailable |
| `AtrUnavailable` | ATR is unavailable |
| `VolumeDataUnavailable` | VolumeRatio is unavailable |
| `VolumeDataFallback` | VolumeSma20 used partial window |

---

## Scalar indicator API

All scalar indicators expose a single production API:

```csharp
public static IndicatorValue Compute(...)
```

Scalar indicators do not expose legacy numeric `Compute(...)` methods.

Use:

* `result.OrNull()` for nullable snapshot/API/LLM contracts;
* `result.RequireValue()` when the value is mandatory;
* `result.ShouldReportDiagnostic()` to create diagnostics.

Do not convert unavailable indicators to `0m`.

**Example:**
```csharp
var rsi = RsiCalculator.Compute(closes, 14);

if (rsi.ShouldReportDiagnostic())
{
    // create IndicatorDiagnostic
}

var rsiValue = rsi.OrNull();
```

