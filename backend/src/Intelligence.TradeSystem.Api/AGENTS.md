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
- `Mappers/LlmPayloadMapperExtensions.cs` converts `MarketAnalysisSnapshot` into the public LLM payload contract.

## Endpoint and validation patterns
- Keep controller actions thin: validate request, call orchestration service, translate exceptions into `ProblemDetails`.
- Follow the existing validation style in `MarketAnalysisController`: local helper methods, explicit required-field messages, and normalized strings via `Trim()`.
- JSON enums are configured as strings only in `Program.cs`; do not introduce integer enum payloads.
- Preserve the current error mapping: `ArgumentException`/`NotSupportedException` → `400`, market data availability issues → `503`, provider HTTP failures → `502`.

## Payload contract rules
- Public payload models under `Models/Payloads` are contract-sensitive; prefer additive changes.
- Follow extend-only design for public payloads and request models: add new optional fields or new endpoints/paths instead of renaming, removing, or silently reinterpreting existing fields.
- `LlmPayloadMapperExtensions` currently fixes `SchemaVersion = "1.0"`; do not change it silently.
- `AnalysisContext.UsesAggregatedContext` is always `false` today.
- `Portfolio` is serialized only when `includePortfolio=true`; if requested but unavailable, use `isAvailable: false` instead of omitting business meaning.
- `AggregatedContext` is reserved for future use and must remain `null` until API support is intentionally added.

## Snapshot health behavior
- `Services/SnapshotHealthEvaluator.cs` currently reports freshness and warnings, but not partial snapshots.
- Do not add `MissingSections` / `IsPartial` behavior unless you update the evaluator, payload contract, and API tests together.
- `AnalysisModeDefaults` controls primary timeframes for payloads and warning generation:
  - `Intraday` → `15m`, `1h`, `4h`
  - `Swing` → `1h`, `4h`, `1d`
  - `Portfolio` → `4h`, `1d`

## When changing code here
- If you change an endpoint contract, update controller docs, payload/request models, mapper logic, and `Intelligence.TradeSystem.Api.Tests`.
- If you change payload shape, inspect `LlmPayloadEndpointTests`, `SnapshotHealthWarningsBuilderTests`, and any consumers of `schemaVersion` / `analysisContext`.
- If you change DI wiring in `Program.cs`, preserve `AddServiceDefaults()`, Swagger XML comments, and the current registration order for analytics/application/exchange services.
