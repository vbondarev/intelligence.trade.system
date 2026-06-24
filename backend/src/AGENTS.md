# AGENTS.md

## Scope
- This file applies to `backend/src`.
- Read nested instructions when working inside `Intelligence.TradeSystem.Api`, `Intelligence.TradeSystem.Exchanges/Bybit`, or `Intelligence.TradeSystem.Indicators`.

## Instruction layering
- This file is the source of truth for repository-wide tooling, build, test, and skill-activation rules.
- Nested `AGENTS.md` files should only add narrower folder-specific constraints and should not restate or contradict shared repository rules unless the folder truly needs a stricter local rule.
- Keep long-lived repository guidance here, including anti-assumption rules, contract checklists, and build/test baselines; avoid splitting the same rule across multiple agent-instruction files.

## Skill activation map
- `modern-csharp-coding-standards`: default guidance for new or refactored C# code across the solution.
- `type-design-performance`: apply when designing types, choosing collection/API shapes, or touching hot-path logic.
- `api-design`: apply when changing public API contracts, request/response models, payload schemas, serialized snapshot shape, or any wire-visible behavior that downstream consumers may depend on.
- `dotnet-project-structure`: apply when changing solution/build layout (`*.slnx`, `Directory.Build.props`, `Directory.Packages.props`, shared MSBuild conventions). Do not assume `global.json` exists or should be introduced unless explicitly requested.
- `dotnet-local-tools`: apply only if `.config/dotnet-tools.json` is introduced or the task is specifically about standardizing local CLI tooling. The repository does not currently require local tools.
- `run-tests`: apply for test execution and filtering. Current test stack is `xUnit` + `Microsoft.NET.Test.Sdk`; no repository-level `Microsoft.Testing.Platform` or `global.json` test runner configuration is currently in use.
- `OpenTelemetry-NET-Instrumentation`: apply only when changing observability/instrumentation code, custom `ActivitySource`/`Meter` usage, or telemetry-related contracts.

## Tooling decisions in this repository
- `global.json` is intentionally absent at the moment; do not document it as required infrastructure.
- `.config/dotnet-tools.json` is optional future infrastructure. If introduced later, pin versions in the manifest and document `dotnet tool restore` usage from the repository root.
- `copilot-instructions.md` is optional. Prefer keeping durable repository guidance in `AGENTS.md`; add `copilot-instructions.md` only if a short editor-specific layer is needed, and keep it aligned with this file instead of duplicating architecture or contract rules.

## Agent workflow defaults
- Read this file before making repository-wide assumptions about tooling, build, tests, or architecture.
- When a nested `AGENTS.md` exists, combine it with this file: keep shared rules from here and apply nested rules only for the local folder.
- Prefer minimal changes that preserve current contracts, DI shape, payload shape, and orchestration boundaries.
- Do not introduce optional infrastructure (`global.json`, `.config/dotnet-tools.json`, `copilot-instructions.md`) unless the task explicitly requires it.
- When changing tests or test commands, assume the current runner flow is `dotnet test` with xUnit + `Microsoft.NET.Test.Sdk` and VSTest-compatible CLI behavior.

## What not to infer automatically
- Do not infer that `global.json` should exist, should be added, or is missing by mistake.
- Do not infer that `.config/dotnet-tools.json` is required just because the repository has multiple projects or shared build logic.
- Do not infer that `copilot-instructions.md` is required when `AGENTS.md` already covers the durable repository guidance.
- Do not infer that `Microsoft.Testing.Platform` is enabled unless the repository explicitly adds its configuration.
- Do not infer support for partial snapshots or additional exchanges/providers beyond the currently documented constraints.
- Do not infer that package versions, shared build settings, or common test behavior should be duplicated into individual project files when they already belong to centralized MSBuild files.
- Do not infer that `Intelligence.TradeSystem.Ai` or `Intelligence.TradeSystem.Ai.Tests` are ready for use; both are currently empty placeholder projects with no source files.

## Big picture
- This solution builds structured crypto market snapshots and LLM-ready JSON payloads; the primary exchange is Bybit.
- Main dependency direction: `Domain` contracts → `Indicators` / `Analysis` / `Analytics` / `Exchanges` → `Application` orchestration → `Api` HTTP surface.
- `Application` does not calculate indicators itself: `MarketDataCollector` fetches raw data, then `MarketAnalysisService` delegates assembly to `Analysis.Assemblers`.
- `Analytics` interprets an existing `MarketAnalysisSnapshot` without recalculating market data.

## Request and data flow
- Snapshot path: `MarketAnalysisController` → `IMarketAnalysisService` → `IMarketDataCollector` → capability interfaces backed by `BybitProvider` → `Analysis.Assemblers` → `MarketAnalysisSnapshot`.
- Key files: `Intelligence.TradeSystem.Api/Controllers/MarketAnalysisController.cs`, `Intelligence.TradeSystem.Application/MarketDataCollector.cs`, `Intelligence.TradeSystem.Application/MarketAnalysisService.cs`, `Intelligence.TradeSystem.Analysis/Assemblers/MarketAnalysisSnapshotAssembler.cs`.

## Current constraints
- Orchestration is currently `Bybit`-only; both `MarketDataCollector` and `MarketAnalysisService` reject other exchanges.
- Partial snapshots are not supported yet: `SnapshotHealthEvaluator` always returns `IsPartial = false` and `MissingSections = []`.

## Contract-sensitive areas
- Treat `Intelligence.TradeSystem.Domain/Snapshots` and `Intelligence.TradeSystem.Api/Models/Payloads` as stable contracts.
- Prefer additive contract evolution for snapshot and payload changes: extend existing contracts instead of silently renaming, removing, or reinterpreting fields.
- If you change snapshot fields, update all affected assemblers, payload mappers, and tests.
- Important mapping code lives in `Intelligence.TradeSystem.Api/Mappers/LlmPayloadMapperExtensions.cs`; schema version is currently `1.0`.
- `AnalysisMode` drives payload shape and primary timeframes: `Intraday = 15m/1h/4h`, `Swing = 1h/4h/1d`, `Portfolio = 4h/1d`.

## Contract change checklist
- If you change snapshot fields, review `Domain/Snapshots`, `Analysis.Assemblers`, API payload mappers, and affected tests together.
- If you change API payload/request models, review controller validation, `ProblemDetails` mapping, schema/version assumptions, and API tests together.
- If you change indicator-derived values, review downstream snapshot fields, payload mapping, analytics output, and indicator/analysis tests together.
- If you change exchange-mapped fields, review provider mapping, normalized domain models, application orchestration, and exchange/application tests together.
- For public or wire-visible contracts, prefer extend-only changes: add new fields or new paths instead of renaming/removing existing members or silently changing established semantics.
- If a change is intentionally breaking, make the breaking impact explicit in the same change set and update dependent consumers, tests, and version/schema assumptions together.
- Prefer additive contract evolution; if a breaking change is truly required, make every dependent layer explicit in the same change set.

## Local patterns
- DI registration is organized via `StartupExtensions` and `AddXyz(...)` methods (`AddApplication`, `AddAnalytics`, `AddBybitExchange`).
- Keep orchestrators thin and push deterministic calculations into assemblers, formatters, and calculators.
- `TimeframeSnapshotAssembler` sorts klines by `StartTime` ascending before indicator calculation; preserve that assumption if you touch timeframe assembly.
- `MarketTagsBuilder` is the single source of snapshot tag ordering and whitelist.
- `Console.Write*` is banned by `Directory.Build.targets`; use `ILogger`.

## Build, test, run
- Solution file: `Intelligence.TradeSystem.slnx`.
- Authoritative build/test/tooling layers:
  - `Intelligence.TradeSystem.slnx` is the solution entrypoint.
  - `Directory.Build.props` is the shared source of compile/language/build defaults.
  - `Directory.Build.targets` is the shared source of repo-wide build validations and enforcement.
  - `Directory.Packages.props` is the single source of truth for package versions.
  - Individual `*.csproj` files should keep only project-specific deltas rather than duplicating shared settings.
- Shared project settings: `net10.0`, C# `14`, nullable enabled, centralized package versions via `Directory.Packages.props`.
- Shared build behavior is defined through `Directory.Build.props` and `Directory.Build.targets`.
- Verified from `backend/src`:
  - `dotnet build .\Intelligence.TradeSystem.slnx --no-restore`
  - `dotnet test .\Intelligence.TradeSystem.slnx --no-build --logger "console;verbosity=minimal"`
- Use `Intelligence.TradeSystem.AppHost` for Aspire orchestration, or run `Intelligence.TradeSystem.Api` directly when debugging HTTP behavior.

## Observability baseline
- Common OpenTelemetry wiring already lives in `Intelligence.TradeSystem.ServiceDefaults/Extensions.cs` and is activated by `AddServiceDefaults()` in service entrypoints such as `Intelligence.TradeSystem.Api/Program.cs`.
- When changing telemetry, preserve the current pattern: shared defaults in `ServiceDefaults`, app-specific additions only where they materially belong, and no instrumentation that changes business behavior.
- Do not add new `ActivitySource`/`Meter` usage or app-specific telemetry conventions unless the change truly introduces new observability needs.
- Treat telemetry shape as an operational contract: avoid ad-hoc naming or behavior changes that would fragment existing observability patterns.

## Impact map
- Use this section as a quick dependency lookup after applying the `Contract change checklist`; it complements the checklist rather than replacing it.
- If you change indicator calculations, check `Intelligence.TradeSystem.Indicators`, `Analysis.Assemblers`, and `Intelligence.TradeSystem.Indicators.Tests` for fallback/ordering regressions.
- If you change exchange data collection, check `Abstractions`, `BybitProvider`, `CollectedMarketData`, and `Application.Tests` / `Exchanges.Tests`.
- If you change snapshot assembly, check `Analysis.Assemblers`, `Domain/Snapshots`, payload mappers, and `Analysis.Tests` / `Api.Tests`.
- If you change `EntryQualityEvaluator`, review `LlmTimeframeSummaryBuilder` (riskFlags must stay in sync with quality downgrades), `EntryQualityEvaluatorTests`, and `LlmTimeframeSummaryBuilderTests` in `Api.Tests`.
- If you change `MarketTagsBuilder` or `TradeFlowPressureScoreAdjuster`, review `MarketTagsBuilderTests` and `TradeFlowPressureScoreAdjusterTests` in `Analysis.Tests`.
- If you change higher-TF level wiring in `LlmPayloadMapperExtensions`, review `LlmPayloadMapperExtensionsTests` in `Api.Tests`.
