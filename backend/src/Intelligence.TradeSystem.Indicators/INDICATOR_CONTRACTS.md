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

`VolumeProfileDetector` returns nullable levels and does **not** use `IndicatorValue`.

- `null` means the level was not detected — it does **not** mean `0`.
- Never substitute `0m` for a missing support or resistance level.
- `VolumeProfileDetector` is a simplified volume profile implementation (V1): fixed 100-bucket profile, adjacent strong HVN buckets merged into clusters, two closest support/resistance levels returned relative to the last close.

**Example payload:**
```json
{
  "support1": null,
  "resistance1": 80751.47
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
- Diagnostics must be emitted in a **stable order**: by timeframe (`15m → 1h → 4h → 1d`), then by indicator within each timeframe (`ema20 → ema50 → ema200 → rsi14 → atr14 → volumeSma20`).
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

