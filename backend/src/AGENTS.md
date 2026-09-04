# AGENTS.md

## Scope
- This file applies to `backend/src`.
- Read nested instructions when working inside `Intelligence.TradeSystem.Api`, `Intelligence.TradeSystem.Exchanges/Bybit`, or `Intelligence.TradeSystem.MarketIntelligence`.

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
- This solution combines public crypto market analysis with a pure business domain for accompanying already-open positions; the primary exchange is Bybit.
- Main dependency direction: `Domain` contracts → `MarketIntelligence` / `Exchanges` → `Application` orchestration → `Api` HTTP surface; `Infrastructure` is composed by `Api` and depends inward on `Application` / `Domain`.
- Stage B domain foundations are implemented in `Intelligence.TradeSystem.Domain`: typed identities, `ExchangeAccount`, `Position` lifecycle and `PositionChange` history, `PortfolioState` and portfolio risk policy, `PositionAssessment`, `Recommendation`, and separate decision vocabularies.
- Position snapshot reconciliation and portfolio assembly live in `Intelligence.TradeSystem.Application/Portfolio`; they orchestrate domain behavior without adding persistence concerns.
- Stage B foundations can now be persisted through Application repository ports implemented by Infrastructure. User authentication, account connection, periodic synchronization, and user-facing recommendation services belong to later stages.
- `Application` does not calculate indicators itself: `PublicMarketDataCollector` fetches raw data, then `MarketSnapshotService` delegates assembly to `MarketIntelligence.Analysis.Assemblers`.
- Deterministic timeframe evaluation (bias, momentum, entry quality, risk flags, trend/level strength labels) lives in `MarketIntelligence/Analysis/Timeframes`; the API only converts the resulting analytical values into the wire payload (`ToString()` on enums, existing string fields). There is no separate `Analytics` project anymore.
- `Application/AI` prepares deterministic textual AI context (`IAiContextFormatter` / `SnapshotTextFormatter`) from `AiAnalysisContext`, which combines public `MarketSnapshot` data with a separate legacy `PortfolioSnapshot`. It performs no trading calculations and does not call any LLM.
- `MarketRegimePolicy` (in `MarketIntelligence/Analysis`) is the single source of market regime classification; no other classifier exists.

## Request and data flow
- Snapshot path: `MarketAnalysisController` -> `IMarketSnapshotService` -> `IPublicMarketDataCollector` -> public exchange capability interfaces (`IMarketDataProvider`, `IDerivativesDataProvider`) -> `MarketIntelligence.Analysis.Assemblers` -> `MarketSnapshot`.
- Key files: `Intelligence.TradeSystem.Api/Controllers/MarketAnalysisController.cs`, `Intelligence.TradeSystem.Application/Market/PublicMarketDataCollector.cs`, `Intelligence.TradeSystem.Application/Market/MarketSnapshotService.cs`, `Intelligence.TradeSystem.MarketIntelligence/Analysis/Assemblers/MarketSnapshotAssembler.cs`.

## Current constraints
- Orchestration is currently `Bybit`-only; both `PublicMarketDataCollector` and `MarketSnapshotService` reject other exchanges.
- Partial snapshots are not supported yet: `SnapshotHealthEvaluator` always returns `IsPartial = false` and `MissingSections = []`.
- Stage B domain state can be persisted, but it is not exposed through a user API or connected to synchronization; do not treat persistence as a completed user workflow.

## Contract-sensitive areas
- Treat `Intelligence.TradeSystem.MarketIntelligence/Snapshots` and `Intelligence.TradeSystem.Api/Models/Payloads` as stable contracts. `MarketSnapshot` contains only public market data and no embedded portfolio. `PortfolioSnapshot`, `OpenPositionSnapshot`, and `PositionSide` remain temporarily in `Intelligence.TradeSystem.Domain/Snapshots`, and the legacy `PortfolioSnapshotAssembler` lives in `Intelligence.TradeSystem.Application/Portfolio`.
- Stage B types in the `Intelligence.TradeSystem.Domain` project root and its `History`, `Portfolio`, `Assessments`, `Recommendations`, `Decisions`, and `Identity` directories are internal business contracts. Preserve their invariants and do not reuse legacy snapshot types as persistence entities.
- EF Core and persistence entities belong only to `Intelligence.TradeSystem.Infrastructure`; Domain and Application must remain persistence-ignorant. Domain rehydration must use explicit restore APIs that preserve typed IDs, timestamps, lifecycle state, and append-only history.
- Production PostgreSQL schema evolves through migrations. Do not add automatic migration execution to API startup.
- Optimistic concurrency (C-04) is implemented for the three mutable repositories only: ExchangeAccountRepository, PositionRepository, RecommendationRepository. It uses a persistence-neutral ConcurrencyVersion / Versioned<T> pair (Intelligence.TradeSystem.Application/Concurrency) surfaced from GetByIdAsync (returns Versioned<T>?) and consumed by SaveAsync(entity, ConcurrencyVersion? expectedVersion) (returns the new ConcurrencyVersion). expectedVersion: null means insert-only (conflicts if the row already exists); a non-null value performs a compare-and-swap against the EF concurrency token and conflicts if the row is missing or was changed concurrently. Every conflict path throws Intelligence.TradeSystem.Application.Concurrency.ConcurrencyConflictException (mapped from DbUpdateConcurrencyException); there is no built-in retry. Domain models do not carry a Version property - it lives only on the corresponding Infrastructure entities (ExchangeAccountEntity, PositionEntity, RecommendationEntity) as a required bigint column (version, default 1, CHECK (version > 0), IsConcurrencyToken()). PositionRepository.SaveAsync stages the version CAS before appending new PositionChange rows so a stale writer's rejected save leaves no history behind (single implicit SaveChangesAsync transaction); do not extend concurrency to PortfolioStateRepository or PositionAssessmentRepository, which remain append-only/immutable by design.
- Prefer additive contract evolution for snapshot and payload changes: extend existing contracts instead of silently renaming, removing, or reinterpreting fields.
- If you change snapshot fields, update all affected assemblers, payload mappers, and tests.
- Important mapping code lives in `Intelligence.TradeSystem.Api/Mappers/LlmPayloadMapperExtensions.cs`; schema version is currently `1.0`, and `GET /api/market-analysis/{symbol}/llm-payload` remains a purely public market contract. Legacy `POST /api/market-analysis/snapshot` still returns a `portfolio` object sourced from `PortfolioSnapshot.Unavailable` (zeroed values, no `isAvailable` field on the wire).
- `AnalysisMode` drives payload shape and primary timeframes: `Intraday = 15m/1h/4h`, `Swing = 1h/4h/1d`, `Portfolio = 4h/1d`.

## Contract change checklist
- If you change snapshot fields, review `MarketIntelligence/Snapshots`, `MarketIntelligence/Analysis/Assemblers`, API payload mappers, and affected tests together.
- If you change API payload/request models, review controller validation, `ProblemDetails` mapping, schema/version assumptions, and API tests together.
- If you change indicator-derived values, review downstream snapshot fields, payload mapping, analytics output, and indicator/analysis tests together.
- If you change exchange-mapped fields, review provider mapping, normalized domain models, application orchestration, and exchange/application tests together.
- If you change `Position`, its lifecycle, or reconciliation behavior, review `PositionChange`, `PositionReconciler`, and the corresponding Domain/Application tests together.
- If you change `PortfolioState` or portfolio risk policy, review portfolio aggregation, risk decisions, reason-code classification, and Domain tests together.
- If you change `PositionAssessment` or `Recommendation`, preserve input-version traceability, validity windows, lifecycle transitions, and the separation between `PositionAction`, `AddDecision`, and `RiskIncreaseDecision`.
- For public or wire-visible contracts, prefer extend-only changes: add new fields or new paths instead of renaming/removing existing members or silently changing established semantics.
- If a change is intentionally breaking, make the breaking impact explicit in the same change set and update dependent consumers, tests, and version/schema assumptions together.
- Prefer additive contract evolution; if a breaking change is truly required, make every dependent layer explicit in the same change set.
- If you change the concurrency contract (ConcurrencyVersion, Versioned<T>, ConcurrencyConflictException, or the Version column/check constraint), review all three repository implementations, their ports, call sites, and the PostgreSQL integration tests together.

## Local patterns
- DI registration is organized via `StartupExtensions` and `AddXyz(...)` methods (`AddApplication`, `AddBybitExchange`).
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
- `Intelligence.TradeSystem.Domain.Tests` is the primary suite for stage B domain invariants; keep it in the solution and update it with domain behavior changes.
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
- If you change indicator calculations, check `Intelligence.TradeSystem.MarketIntelligence/Indicators`, `MarketIntelligence/Analysis/Assemblers`, and `Intelligence.TradeSystem.MarketIntelligence.Tests` for fallback/ordering regressions.
- If you change exchange data collection, check Application market ports, Bybit public/private providers, `CollectedPublicMarketData`, and `Application.Tests` / `Exchanges.Tests`.
- If you change position identity, lifecycle, or reconciliation, check `Intelligence.TradeSystem.Domain/Position.cs`, `Intelligence.TradeSystem.Domain/History`, `Intelligence.TradeSystem.Application/Portfolio/PositionReconciler.cs`, `Intelligence.TradeSystem.Domain.Tests`, and `Intelligence.TradeSystem.Application.Tests`.
- If you change portfolio aggregation or risk rules, check `Intelligence.TradeSystem.Domain/Portfolio`, `Intelligence.TradeSystem.Application/Portfolio/PortfolioStateAssembler.cs`, `ReasonCodeClassification`, and `Intelligence.TradeSystem.Domain.Tests`.
- If you change assessments or recommendations, check `Intelligence.TradeSystem.Domain/Assessments`, `Intelligence.TradeSystem.Domain/Recommendations`, `Intelligence.TradeSystem.Domain/Decisions`, and `Intelligence.TradeSystem.Domain.Tests/AssessmentsAndRecommendationsTests.cs`.
- If you change snapshot assembly, check `MarketIntelligence/Analysis/Assemblers`, `MarketIntelligence/Snapshots`, payload mappers, and `MarketIntelligence.Tests` / `Api.Tests`.
- If you change `EntryQualityEvaluator`, review `TimeframeSummaryBuilder` (riskFlags must stay in sync with quality downgrades), `EntryQualityEvaluatorTests`, and `TimeframeSummaryBuilderTests` in `MarketIntelligence.Tests`.
- If you change `MarketTagsBuilder` or `TradeFlowPressureScoreAdjuster`, review `MarketTagsBuilderTests` and `TradeFlowPressureScoreAdjusterTests` in `MarketIntelligence.Tests`.
- If you change higher-TF level wiring in `LlmPayloadMapperExtensions`, review `LlmPayloadMapperExtensionsTests` in `Api.Tests`.
