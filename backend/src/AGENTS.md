# AGENTS.md

## Область действия

Этот файл применяется к `backend/src` и дополняет корневой `../../AGENTS.md` правилами .NET-решения. Для `Intelligence.TradeSystem.Api`, `Intelligence.TradeSystem.MarketIntelligence`, `Intelligence.TradeSystem.Infrastructure`, `Intelligence.TradeSystem.Identity`, `Intelligence.TradeSystem.Web`, `Intelligence.TradeSystem.Web/ClientApp` и `Intelligence.TradeSystem.Exchanges/Bybit` учитывай также их локальные `AGENTS.md`.

Не копируй сюда текущее состояние этапов, номера PR и подробности уже завершённых реализаций — для этого используется `ROADMAP.md`, ADR и контрактные документы.

## Язык исходного текста

XML documentation (`summary`, `remarks`, `param`, `returns`, `exception`, `value`) и обычные `//`/`/* */` comments в C# пишутся естественным русским повествовательным текстом. `cref`, имена параметров, типов, членов и другие идентификаторы не переводятся. Технические термины сохраняются в естественной форме согласно общей языковой политике репозитория. Не добавляй комментарии только для пересказа очевидного кода.

## Структура решения

Сохраняй ответственность проектов:

- `Intelligence.TradeSystem.Domain` — бизнес-модель, идентичности, жизненный цикл позиций, портфель, оценки и рекомендации;
- `Intelligence.TradeSystem.MarketIntelligence` — детерминированные рыночные расчёты, диагностика и публичные market snapshots;
- `Intelligence.TradeSystem.Application` — прикладные сценарии и оркестрация;
- `Intelligence.TradeSystem.Infrastructure` — EF Core, PostgreSQL, защищённое хранение и технические реализации application ports;
- `Intelligence.TradeSystem.Exchanges` — адаптеры бирж;
- `Intelligence.TradeSystem.Api` — HTTP boundary и composition root;
- `Intelligence.TradeSystem.Identity` — отдельный OAuth/OIDC authorization server;
- `Intelligence.TradeSystem.Web` — host React-клиента и BFF: browser session и посредничество с OAuth tokens без business logic;
- `Intelligence.TradeSystem.ServiceDefaults` и `Intelligence.TradeSystem.AppHost` — общая эксплуатационная и Aspire-обвязка.

`Domain` не зависит от persistence/HTTP/Bybit. `MarketIntelligence` не выполняет IO. `Application` не зависит от EF Core или конкретного exchange SDK. Bybit transport types не должны выходить за exchange adapter. `Web` не ссылается на Domain, Application, Infrastructure, MarketIntelligence, Exchanges, Api и Identity и обращается к `Api` только по HTTP.

## Общие правила реализации

- Предпочитай небольшие изменения, сохраняющие существующие DI-границы и wire-контракты.
- Не дублируй детерминированные вычисления в API или Application: рыночные расчёты принадлежат `MarketIntelligence`.
- Не используй legacy snapshot-типы как persistence entities нового домена.
- Пользовательские repository/application операции должны сохранять явный `UserId` scope и cross-user isolation.
- `MarketSnapshot` остаётся публичным и не содержит позиции, портфель, `UserId`, `ExchangeAccountId` или credentials.
- Биржевые credentials не являются частью Domain aggregate; расшифрованные значения должны жить только как краткоживущие transient inputs.
- Для синхронизации сохраняй монотонность observation state и идемпотентность повторных наблюдений.
- Изменения Identity persistence schema должны синхронизироваться с отдельным sibling project `Intelligence.TradeSystem.Identity.Migrations`; `Identity/AGENTS.md` не распространяется на него автоматически.

## Конфигурация служебных и design-time процессов

- EF Core design-time factories и `Intelligence.TradeSystem.Identity.Migrations` получают connection strings только из environment variables: `ConnectionStrings__TradeSystemIdentity` для Identity и `ConnectionStrings__TradeSystem` для business persistence. Implicit fallback на `appsettings*.json`, generic host или command-line providers не добавляй.
- Missing, blank и синтаксически некорректная PostgreSQL connection string отклоняются fail-fast до database operation; design-time validation не проверяет доступность PostgreSQL.
- Configuration diagnostics не должны содержать connection strings, passwords и другой secret material, в том числе через `InnerException`.
- `Intelligence.TradeSystem.Authentication.TestSeeder` — явное исключение: он использует стандартный `IConfiguration` host, сам валидирует только `TestSeeder:*`, а Identity persistence configuration валидирует `AddIdentityPersistence`.
- Configuration этих one-shot/service процессов — startup-only snapshot: изменение environment/configuration применяется только restart/re-run. Runtime reload, hot reload и `IOptionsMonitor` вводятся только отдельным решением.

## Concurrency и порядок блокировок

- Workflows, которые одновременно сериализуют `Position` и `ExchangeAccount`, сохраняют единый порядок блокировок `position(s) → account`; не вводи обратный порядок `account → position(s)`.
- Lifecycle-операции, которые одновременно изменяют account и credentials, сохраняют порядок `account → credential`.
- После захвата locks повторно проверяй релевантное состояние. Изменившийся набор или версии должны приводить к контролируемому `ConcurrencyConflictException` и bounded retry на уровне owning workflow, а не к stale write.
- Изменение этих правил считается concurrency-sensitive изменением и требует PostgreSQL integration/concurrency coverage на реальной БД.

## Аутентификация и авторизация

Подробные решения находятся в:

- `docs/adr/0002-universal-api-authentication-strategy.md`;
- `docs/adr/0003-authorization-server-selection.md`.

Стабильные ограничения:

- `Api` — resource server, а выпуск токенов выполняет отдельный `Identity` host;
- защищённый business API использует OAuth 2.0 / OpenID Connect и Bearer access tokens;
- не создавай собственный login/JWT/refresh-token протокол в `Api`;
- для операций над user-owned данными `UserId` определяется только из аутентифицированного и проверенного principal через существующий current-user boundary; не принимай доверенный `UserId` из route, query, header или request DTO;
- Domain `UserId` не должен зависеть от email, username, `ClaimsPrincipal` или конкретного identity provider;
- machine/service principal не получает user-owned данные автоматически;
- browser cookie допустим только на BFF boundary и требует отдельной CSRF-защиты.

## Контрактно-чувствительные изменения

При изменении публичного snapshot/payload:

- проверь `Intelligence.TradeSystem.MarketIntelligence/Snapshots`;
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
- `Directory.Packages.props` — единственный источник версий NuGet-пакетов;
- `../../global.json` — зафиксированная версия .NET SDK для локальной сборки и CI.

Текущая базовая платформа: `.NET 10`, C# 14, nullable enabled, central package management.

Корневой `global.json` является частью воспроизводимой build-конфигурации проекта: не удаляй и не обходи его без отдельного решения. Наличие `.config/dotnet-tools.json` или переход на Microsoft.Testing.Platform не предполагай автоматически — подобную инфраструктуру добавляй только в рамках отдельной задачи.

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

Общие правила работы со skills и routing к workflow skills заданы в корневом `AGENTS.md`. Не копируй содержимое skill в этот файл. Используй специализированные project-owned и external skills только тогда, когда текущая задача соответствует их назначению.
