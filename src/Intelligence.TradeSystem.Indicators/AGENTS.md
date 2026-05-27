# AGENTS.md

## Scope
- Applies to `Intelligence.TradeSystem.Indicators`.
- Read `../AGENTS.md` first for solution-level architecture, orchestration boundaries, and snapshot flow.

See also: `INDICATOR_CONTRACTS.md` for the detailed missing-data, fallback and diagnostics policy.

## Inheritance
- Shared repository rules for skills, build/test workflow, optional `dotnet-tools.json`, and the role of `copilot-instructions.md` are defined in `../AGENTS.md`.
- Shared anti-assumption rules, contract change checklists, and build/test baselines also live in `../AGENTS.md` and should not be restated here unless the Indicators project truly needs a stricter local rule.
- This file should stay focused on deterministic indicator logic, ordering assumptions, and calculator-level contracts.

## Do / Don't
- Do keep indicator code deterministic, side-effect free, and chronologically ordered by input assumptions.
- Do update indicator tests together with formula or threshold changes.
- Don't add logging, IO, exchange calls, or service orchestration into this project.
- Don't change fallback values or seed-window behavior without updating dependent assemblers and tests.

## What this project does
- This project contains deterministic, pure indicator logic used by `Analysis.Assemblers`; it does not call exchanges or services directly.
- Key areas: `Calculators/*` for scalar indicators, `Levels/VolumeProfileDetector.cs` for support/resistance extraction, and `Trend/TrendClassifier.cs` for market-trend labeling.

## Input assumptions to preserve
- Callers provide market series in chronological order (oldest → newest); several calculations use the last element as the current state.
- `TimeframeSnapshotAssembler` sorts klines by `StartTime` before calling into indicators; preserve that expectation if you change series-based logic.
- Keep calculations deterministic and side-effect free; indicator code should stay static/pure and free of logging/IO.

## Current calculation contracts
- `SmaCalculator` averages the last `period` values, returns `Unavailable(EmptyInput)` for an empty array, and returns `Fallback(average, PartialWindow)` when the series is shorter than `period`.
- `EmaCalculator` seeds EMA with SMA of the first `period` values; if the series is shorter than `period`, it returns `Fallback(average, PartialWindow)`, and `Unavailable(EmptyInput)` for an empty array.
- `RsiCalculator` uses Wilder smoothing, returns `Unavailable(InsufficientData)` when `closes.Length < period + 1`, and keeps the neutral/edge-case behavior `all flat -> 50`, `only gains -> 100`, `only losses -> 0`. RSI does **not** use fallback for insufficient data.
- `AtrCalculator.Compute(...)` requires `highs`, `lows`, and `closes` arrays to have the same length and throws `ArgumentException` when lengths differ. Returns `Unavailable(InsufficientData)` when fewer than two candles are available, and `Fallback(average TR, PartialWindow)` when True Range count is less than `period`.
- `TrendClassifier` requires strict alignment for directed trends (`EMA20 > EMA50 > EMA200` with price above `EMA200`, or the bearish mirror). Directed trends start at `0.80` strength, can gain at most `0.20` from `volumeRatio`, and sideways scores must stay `<= 0.49`.
- `VolumeProfileDetector.Detect(klines, options?)` merges adjacent strong HVN buckets into clusters and returns the two closest supports/resistances relative to `klines[^1].Close`. Parameters are supplied via `VolumeProfileOptions` (defaults: `BucketCount = 100`, `HvnThresholdRatio = 0.70`); pass `null` to use `VolumeProfileOptions.Default`.

## Testing patterns to follow
- Indicator tests are highly contract-oriented: preserve boundary cases, exact fallback values, and deterministic output.
- Reuse `Intelligence.TradeSystem.Indicators.Tests/Helpers/KlineFactory.cs` for UTC, one-hour, deterministic candle sequences instead of ad-hoc fixtures.
- When changing formulas or thresholds, update the matching calculator/levels/trend tests together; many tests intentionally guard against off-by-one, wrong seed window, and accidental reordering regressions.

## When changing code here
- If you change a calculator return contract, update any dependent assembler expectations in `Intelligence.TradeSystem.Analysis/Assemblers` as well as `Intelligence.TradeSystem.Indicators.Tests`.
- If you change `TrendClassifier` or `VolumeProfileDetector`, review downstream snapshot fields that surface trend/levels in `Domain/Snapshots` and API payloads.

## Scalar indicator API

- Scalar indicators expose a single public API: `Compute(...)`, returning `IndicatorValue`.
- Do not add numeric `Compute(...)` overloads returning `decimal` or `decimal?`.
- Do not add `ComputeValue(...)` methods.
- Do not convert unavailable indicator values to `0m` in new snapshot/API/LLM contracts.
- Use nullable values (`decimal?`) for unavailable indicators in DTOs and payloads.
- Use `IndicatorDiagnostics` to surface fallback/unavailable reasons.
- Do not serialize `IndicatorValue` directly into API/LLM payloads.
- API/LLM payloads should expose scalar indicator fields as `number` or `null`, with reasons in `indicatorDiagnostics`.
- `IndicatorValue.Available(...)` means the indicator was calculated normally on a full window.
- `IndicatorValue.Fallback(...)` means a numeric value exists but was calculated using fallback logic, usually `PartialWindow`.
- `IndicatorValue.Unavailable(...)` means no safe indicator value exists.

## No fake-zero mapping

- Never map unavailable indicator values to `0m` in new contracts.
- Use `null` + `IndicatorDiagnostics` instead.

## Level / Volume Profile contracts

- `VolumeProfileDetector.Detect(Kline[] klines, VolumeProfileOptions? options = null)` returns a `LevelSet` with four nullable `LevelInfo?` fields: `Support1`, `Support2`, `Resistance1`, `Resistance2`.
- `null` in any `LevelInfo?` field means the level was not detected — it does **not** mean `0`.
- Do not use `0m` to represent a missing support or resistance level.
- Parameters are configured through `VolumeProfileOptions`. Defaults: `BucketCount = 100`, `HvnThresholdRatio = 0.70`. Pass `null` to use `VolumeProfileOptions.Default`.
- `VolumeProfileDetector` is a simplified volume profile implementation (not a precise Volume-at-Price model) unless explicitly replaced.
