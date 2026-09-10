# AGENTS.md

## Область действия

Этот файл применяется к `backend/src` и дополняет корневой `../../AGENTS.md` правилами .NET-решения. Для `Intelligence.TradeSystem.Api`, `Intelligence.TradeSystem.MarketIntelligence` и `Intelligence.TradeSystem.Exchanges/Bybit` учитывай также их локальные `AGENTS.md`.

Не копируй сюда текущее состояние этапов, номера PR и подробности уже завершённых реализаций — для этого используется `ROADMAP.md`, ADR и контрактные документы.

## Структура решения

Сохраняй ответственность проектов:

- `Intelligence.TradeSystem.Domain` — бизнес-модель, идентичности, жизненный цикл позиций, портфель, оценки и рекомендации;
- `Intelligence.TradeSystem.MarketIntelligence` — детерминированные рыночные расчёты, диагностика и публичные market snapshots;
- `Intelligence.TradeSystem.Application` — прикладные сценарии и оркестрация;
- `Intelligence.TradeSystem.Infrastructure` — EF Core, PostgreSQL, защищённое хранение и технические реализации application ports;
- `Intelligence.TradeSystem.Exchanges` — адаптеры бирж;
- `Intelligence.TradeSystem.Api` — HTTP boundary и composition root;
- `Intelligence.TradeSystem.Identity` — отдельный OAuth/OIDC authorization server;
- `Intelligence.TradeSystem.ServiceDefaults` и `Intelligence.TradeSystem.AppHost` — общая эксплуатационная и Aspire-обвязка.

`Domain` не зависит от persistence/HTTP/Bybit. `MarketIntelligence` не выполняет IO. `Application` не зависит от EF Core или конкретного exchange SDK. Bybit transport types не должны выходить за exchange adapter.

## Общие правила реализации

- Предпочитай небольшие изменения, сохраняющие существующие DI-границы и wire-контракты.
- Не дублируй детерминированные вычисления в API или Application: рыночные расчёты принадлежат `MarketIntelligence`.
- Не используй legacy snapshot-типы как persistence entities нового домена.
- Пользовательские repository/application операции должны сохранять явный `UserId` scope и cross-user isolation.
- `MarketSnapshot` остаётся публичным и не содержит позиции, портфель, `UserId`, `ExchangeAccountId` или credentials.
- Биржевые credentials не являются частью Domain aggregate; расшифрованные значения должны жить только как краткоживущие transient inputs.
- PostgreSQL schema меняется migrations. Не добавляй автоматическое применение migrations в startup API без отдельного решения.
- Для mutable aggregate сохраняй существующий optimistic concurrency/CAS contract. Не переноси version token в Domain только ради persistence.
- Для синхронизации сохраняй монотонность observation state и идемпотентность повторных наблюдений.
- Transactional outbox остаётся атомарным с бизнес-состоянием и использует at-least-once delivery; consumers должны учитывать повторную доставку.

## Аутентификация и авторизация

Подробные решения находятся в:

- `docs/adr/0002-universal-api-authentication-strategy.md`;
- `docs/adr/0003-authorization-server-selection.md`.

Стабильные ограничения:

- `Api` — resource server, а выпуск токенов выполняет отдельный `Identity` host;
- защищённый business API использует OAuth 2.0 / OpenID Connect и Bearer access tokens;
- не создавай собственный login/JWT/refresh-token протокол в `Api`;
- Domain `UserId` не должен зависеть от email, username, `ClaimsPrincipal` или конкретного identity provider;
- machine/service principal не получает user-owned данные автоматически;
- browser cookie допустим только на BFF boundary и требует отдельной CSRF-защиты.

## Контрактно-чувствительные изменения

При изменении публичного snapshot/payload:

- проверь `MarketIntelligence/Snapshots`;
- assemblers/mappers;
- API contract tests;
- `schemaVersion` и downstream consumers.

При изменении `Position` или reconciliation:

- проверь `PositionChange`;
- `PositionReconciler`;
- Domain/Application tests;
- persistence/concurrency integration tests, если меняется сохранение.

При изменении `PortfolioState`, `PositionAssessment` или `Recommendation` сохраняй воспроизводимость входа, reason codes, validity/lifecycle semantics и разделение решений по действию над позицией и увеличению риска.

При изменении persistence/security/concurrency выполняй PostgreSQL integration tests. При изменении exchange mapping — exchange tests и затронутые Application tests.

## Tooling

Основные файлы решения:

- `Intelligence.TradeSystem.slnx` — solution entrypoint;
- `Directory.Build.props` — общие build/language settings;
- `Directory.Build.targets` — общие build validations;
- `Directory.Packages.props` — единственный источник версий NuGet-пакетов.

Текущая базовая платформа: `.NET 10`, C# 14, nullable enabled, central package management.

Не предполагай автоматически наличие или необходимость `global.json`, `.config/dotnet-tools.json` или Microsoft.Testing.Platform. Добавляй подобную инфраструктуру только в рамках отдельной задачи.

`Console.Write*` запрещён общими build rules; используй `ILogger` там, где logging допустим архитектурой слоя.

## Сборка и тесты

Из `backend/src`:

```bash
dotnet restore Intelligence.TradeSystem.slnx
dotnet build Intelligence.TradeSystem.slnx --configuration Release --no-restore
dotnet test Intelligence.TradeSystem.slnx --configuration Release --no-build --logger "console;verbosity=minimal"
```

`Infrastructure.IntegrationTests` и `Authentication.IntegrationTests` используют реальную инфраструктуру через Testcontainers, поэтому для полного прогона нужен Docker.

После изменений composition root, persistence, authentication или Docker-конфигурации учитывай полный CI, включая PostgreSQL provisioning и OAuth/OIDC smoke tests.

## Skills

Общие правила работы с внешними skills заданы в корневом `AGENTS.md`. Не копируй содержимое skill в этот файл. Используй специализированный skill только тогда, когда текущая задача действительно соответствует его назначению.
