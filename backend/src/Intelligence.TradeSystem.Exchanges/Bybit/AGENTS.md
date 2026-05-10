# AGENTS.md

## Scope
- Applies to `Intelligence.TradeSystem.Exchanges/Bybit`.
- Read `../../AGENTS.md` first for solution-level architecture and constraints.

## Inheritance
- Shared repository rules for skills, build/test workflow, optional `dotnet-tools.json`, and the role of `copilot-instructions.md` are defined in `../../AGENTS.md`.
- Shared anti-assumption rules, contract change checklists, and build/test baselines also live in `../../AGENTS.md` and should not be restated here unless the Bybit folder truly needs a stricter local rule.
- This file should stay focused on Bybit-specific provider boundaries, mapping rules, logging, and transport normalization behavior.

## Do / Don't
- Do keep Bybit-specific DTOs, enum mapping, and transport quirks inside this folder.
- Do normalize transport data into domain models at the boundary and update exchange tests with behavior changes.
- Don't leak Bybit transport types into `Domain`, `Application`, or public contracts.
- Don't log secrets or change current null/empty failure behavior without updating dependent consumers and tests.

## What this folder does
- `BybitProvider.cs` is the single exchange adapter currently used by the application layer.
- `ToBybitTypeMapperExtensions.cs` maps domain enums into Bybit.Net enums for outbound requests.
- `ToDomainTypeMapperExtensions.cs` maps Bybit.Net models into normalized domain models and snapshots.

## Provider boundaries
- Keep Bybit-specific enums, DTO quirks, and request details inside this folder.
- `BybitProvider` should return normalized domain models (`Ticker`, `OrderBook`, `Kline`, `FundingRateEntry`, etc.), not raw Bybit responses.
- The application layer depends on capability interfaces, so preserve `BybitProvider` as the implementation behind `IMarketDataProvider`, `IDerivativesDataProvider`, and `IPrivateAccountProvider`.

## Current behavior to preserve
- Failed exchange calls generally log and return `null` / empty collections instead of throwing transport-specific exceptions from the provider.
- Spot market requests reject derivatives-only operations (`funding`, `open interest`, `long/short ratio`, `positions`) with `ArgumentException`.
- Position queries filter out zero-quantity positions before mapping.
- Mapping code currently normalizes missing Bybit bid/ask values to `0m` for domain tickers.

## Mapping and time rules
- Keep numeric/string normalization at the mapping boundary; do not leak Bybit transport types into `Domain` or `Application`.
- Preserve UTC handling: timestamps from Bybit are mapped into `DateTimeOffset` with zero offset where needed.
- Keep category/interval/account-type switch expressions exhaustive and fail fast on unsupported enum values.
- If you add a new mapped field, update both provider code and exchange tests together.

## Logging and diagnostics
- Use the existing `BybitProviderLogMessages` pattern for structured logs instead of ad-hoc message strings.
- Include symbol, category, interval, or account type in failures when those inputs are relevant.
- Never log API keys or other secrets.

## When changing code here
- If you change Bybit request parameters or mapping behavior, inspect `Intelligence.TradeSystem.Exchanges.Tests` and `Intelligence.TradeSystem.Application.Tests`.
- If you add a new capability to `BybitProvider`, also update `Intelligence.TradeSystem.Exchanges/StartupExtensions.cs` if the DI surface changes.
- If you introduce new domain fields, trace the impact into `CollectedMarketData`, `MarketDataCollector`, `MarketAnalysisService`, and any downstream assemblers.

