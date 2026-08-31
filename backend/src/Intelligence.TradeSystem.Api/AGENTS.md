# AGENTS.md

## Scope
- Applies to `Intelligence.TradeSystem.Api`.
- Read `../AGENTS.md` first for solution-level architecture and workflow.

## Inheritance
- Shared repository rules for skills, build/test workflow, optional `dotnet-tools.json`, and the role of `copilot-instructions.md` are defined in `../AGENTS.md`.
- Shared anti-assumption rules, contract change checklists, and build/test baselines also live in `../AGENTS.md` and should not be restated here unless the API folder truly needs a stricter local rule.
- Apply `api-design` from `../AGENTS.md` when changing `Models/Payloads`, request/response models, or any public wire-visible API behavior.
- This file should stay focused on API-specific HTTP, payload, validation, and DI behavior.

## Do / Don't
- Do keep controllers thin, validation explicit, and exception-to-`ProblemDetails` mapping consistent.
- Do preserve `AddServiceDefaults()`, current JSON enum configuration, and the existing registration order in `Program.cs` unless the task explicitly changes them.
- Don't move business assembly logic into controllers.
- Don't silently change public payload contracts, enum serialization shape, or schema-version semantics.

## What this project does
- `Program.cs` wires controllers, Swagger, service defaults, application services, and the Bybit exchange registration.
- `Controllers/MarketAnalysisController.cs` is the main entrypoint for snapshot and LLM payload flows.
- `Mappers/LlmPayloadMapperExtensions.cs` converts `MarketAnalysisSnapshot` into the public LLM payload contract by calling `MarketIntelligence.Analysis.Timeframes.TimeframeSummaryBuilder` and mapping the resulting `TimeframeSummary` (analytical enums) into the existing string wire fields via `ToString()`.
- Deterministic timeframe evaluation (`EntryQualityEvaluator`, `TimeframeSummaryBuilder`, label mappers) lives in `Intelligence.TradeSystem.MarketIntelligence/Analysis/Timeframes`, not in this project. The API only consumes the results and maps them to payload DTOs.

## Endpoint and validation patterns
- Keep controller actions thin: validate request, call orchestration service, translate exceptions into `ProblemDetails`.
- Follow the existing validation style in `MarketAnalysisController`: local helper methods, explicit required-field messages, and normalized strings via `Trim()`.
- JSON enums are configured as strings only in `Program.cs`; do not introduce integer enum payloads.
- Preserve the current error mapping: `ArgumentException`/`NotSupportedException` → `400`, market data availability issues → `503`, provider HTTP failures → `502`.

## Payload contract rules
- Public payload models under `Models/Payloads` are contract-sensitive; prefer additive changes.
- Follow extend-only design for public payloads and request models: add new optional fields or new endpoints/paths instead of renaming, removing, or silently reinterpreting existing fields.
- `LlmPayloadMapperExtensions` currently fixes `SchemaVersion = "1.0"`; do not change it silently.
- `GET /api/market-analysis/{symbol}/llm-payload` accepts only `exchange`, `category`, and `mode`.

## Snapshot health behavior
- `Services/SnapshotHealthEvaluator.cs` currently reports freshness and warnings, but not partial snapshots.
- Do not add `MissingSections` / `IsPartial` behavior unless you update the evaluator, payload contract, and API tests together.
- `AnalysisModeDefaults` controls primary timeframes for payloads and warning generation:
  - `Intraday` → `15m`, `1h`, `4h`
  - `Swing` → `1h`, `4h`, `1d`
  - `Portfolio` → `4h`, `1d`

## Mapper pipeline rules (implemented in `MarketIntelligence/Analysis/Timeframes`, consumed by the API mapper)

### EntryQualityEvaluator invariants
- Neutral bias → always `Poor` (immediate return; no further evaluation).
- `distancePct == 0` is valid (retest scenario); do not treat as invalid or Poor.
- `distancePct < 0` means wrong-side-of-price → returns `Poor`; level is not applicable.
- Base quality: confirmed trend + distance ≤ 0.75% → `Good`; distance ≤ 1.50% → `Fair`; otherwise `Poor`.
- Downgrades (strictest result wins when multiple apply):
  - Volume: ratio < 0.25 → `Poor`; ratio < 0.50 or null → cap `Fair`.
  - EMA conflict: both EMA20/EMA50 conflict → `Poor`; one conflicts or null → cap `Fair`.
  - Freshness: `!fresh` → cap `Fair`; `!fresh` + low volume → `Poor`.
  - MarketRegime: null/empty → cap `Fair`; `Neutral` + (low volume || EMA conflict) → `Poor`; `Neutral` alone → cap `Fair`.
  - Level strength ≤ 0.35 → cap `Fair`.
  - Opposite level dist < 0.30% → cap `Fair`; dist < 0.15% + Moderate/Strong strength → `Poor`.
- Higher-TF opposite level is evaluated with the same rules as the current-TF level; the closer candidate wins.

### RiskFlags
- Every entryQuality downgrade must be accompanied by a riskFlag that explains the reason.
- `TrendConfirmedButEntryFiltered`: always set when `isTrendConfirmed == true` AND `entryQuality != Good`.
- `NearResistance` (current-TF) and `NearHigherTimeframeResistance` (higher-TF) are distinct flags; do not merge them.
- Same separation applies to `NearSupport` vs `NearHigherTimeframeSupport` for bearish bias.

### Higher-TF opposite level resolution
- Bullish bias: opposite level = nearest `Resistance1` from higher timeframes.
- Bearish bias: opposite level = nearest `Support1` from higher timeframes.
- `dist < 0` → ignored (level is on the wrong side of price).
- `dist == 0` → valid.
- Pick the closer candidate between the current-TF level and the higher-TF level before passing to `EntryQualityEvaluator`.

### TradeFlowPressureScore caps
- Sign is always preserved; caps are applied to the absolute value and the sign is reapplied.
- Strictest cap wins when multiple factors apply simultaneously.
- Freshness: age > 2×maxAge → ×0.25; age > maxAge → ×0.50.
- Window duration: < 10s → ×0.25; [10s, 30s) → ×0.35; [30s, 60s) → ×0.50.
- Volume: < 1 BTC → ×0.35; [1 BTC, 3 BTC) → ×0.50.
- Conflict (orderBook vs tradeFlow sign mismatch): ×0.50; conflict + stale or short-window → ×0.25.

### Tags composition
- Tags are assembled in priority order: tradeFlow quality → OI direction → market regime → price proximity → low-volume → orderBook → aggression → funding → RSI → timeframe structure → level proximity → other.
- `MeanReversion` regime maps to tag `mean-reversion-regime`; never `unknown-market-regime`.
- `unknown-market-regime` is only for null, empty, or unrecognized `MarketRegime` values.
- TradeFlow quality tags: `short-tradeflow-window`, `stale-tradeflow`, `low-tradeflow-volume`, `orderbook-tradeflow-conflict`, `weak-tradeflow-confirmation`.
- Max 20 tags per snapshot; `MarketTagsBuilder` enforces the limit.
- Do not use V1 legacy tags (`"trending"`, `"neutral"`) in new code.

## When changing code here
- If you change an endpoint contract, update controller docs, payload/request models, mapper logic, and `Intelligence.TradeSystem.Api.Tests`.
- If you change payload shape, inspect `LlmPayloadEndpointTests`, `SnapshotHealthWarningsBuilderTests`, and any consumers of `schemaVersion` / `analysisContext`.
- If you change DI wiring in `Program.cs`, preserve `AddServiceDefaults()`, Swagger XML comments, and the current registration order for application/exchange services.
- If you change `EntryQualityEvaluator` or `TimeframeSummaryBuilder`, run `EntryQualityEvaluatorTests` and `TimeframeSummaryBuilderTests` in `Intelligence.TradeSystem.MarketIntelligence.Tests`, plus `LlmPayloadMapperExtensionsTests` in `Intelligence.TradeSystem.Api.Tests`.
- If you change `MarketTagsBuilder` or `TradeFlowPressureScoreAdjuster`, run `MarketTagsBuilderTests` and `TradeFlowPressureScoreAdjusterTests` in `Intelligence.TradeSystem.MarketIntelligence.Tests`.
