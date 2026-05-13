# AGENTS.md

## Scope
- Applies to `Intelligence.TradeSystem.Indicators`.
- Read `../AGENTS.md` first for solution-level architecture, orchestration boundaries, and snapshot flow.

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
- `SmaCalculator` averages the last `period` values, returns `0m` for an empty array, and averages all available values when the series is shorter than `period`.
- `EmaCalculator` seeds EMA with SMA of the first `period` values; if the series is shorter than `period`, it returns the average of all values, and `0m` for an empty array.
- `RsiCalculator` uses Wilder smoothing, returns `null` when `closes.Length < period + 1` (insufficient data), and keeps the neutral/edge-case behavior `all flat -> 50`, `only gains -> 100`, `only losses -> 0`.
- `AtrCalculator` uses the shortest shared length of `highs/lows/closes`, returns `0m` when fewer than two candles are available, and falls back to average true range when there are fewer than `period` TR values.
- `TrendClassifier` requires strict alignment for directed trends (`EMA20 > EMA50 > EMA200` with price above `EMA200`, or the bearish mirror). Directed trends start at `0.80` strength, can gain at most `0.20` from `volumeRatio`, and sideways scores must stay `<= 0.49`.
- `VolumeProfileDetector` uses a fixed 100-bucket profile, merges adjacent strong HVN buckets into clusters, and returns the two closest supports/resistances relative to `klines[^1].Close`.

## Testing patterns to follow
- Indicator tests are highly contract-oriented: preserve boundary cases, exact fallback values, and deterministic output.
- Reuse `Intelligence.TradeSystem.Indicators.Tests/Helpers/KlineFactory.cs` for UTC, one-hour, deterministic candle sequences instead of ad-hoc fixtures.
- When changing formulas or thresholds, update the matching calculator/levels/trend tests together; many tests intentionally guard against off-by-one, wrong seed window, and accidental reordering regressions.

## When changing code here
- If you change a calculator return contract, update any dependent assembler expectations in `Intelligence.TradeSystem.Analysis/Assemblers` as well as `Intelligence.TradeSystem.Indicators.Tests`.
- If you change `TrendClassifier` or `VolumeProfileDetector`, review downstream snapshot fields that surface trend/levels in `Domain/Snapshots` and API payloads.

