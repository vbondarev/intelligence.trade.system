# Intelligence.TradeSystem

**Intelligence.TradeSystem** — развивающийся помощник для сопровождения открытых криптовалютных позиций.

Цель проекта — помочь пользователю не только найти потенциальную точку входа, а прежде всего понять, **что делать с уже открытой сделкой**: продолжать удерживать позицию, защитить прибыль, сократить риск, закрыть позицию, изменить защитные уровни или дождаться дополнительного подтверждения.

Система строится вокруг детерминированного backend, который получает фактическое состояние биржевого аккаунта и рынка, формирует оценки и рекомендации, сохраняет их историю и отдаёт одинаковое состояние всем клиентам. ИИ рассматривается как дополнительный слой объяснения, а не как источник бизнес-истины или единственный механизм принятия решений.

> Проект находится в активной разработке. Ниже отдельно описаны **целевой продукт** и **то, что уже реализовано**, чтобы будущие возможности не смешивались с текущими.

---

## Зачем нужен проект

После открытия позиции трейдеру приходится постоянно отвечать на вопросы:

- сохраняется ли исходный торговый сценарий;
- ухудшилась ли структура рынка;
- стоит ли продолжать удерживать позицию;
- когда имеет смысл защитить уже полученную прибыль;
- нужно ли сократить позицию или закрыть её полностью;
- допустимо ли увеличивать позицию;
- не стал ли общий риск портфеля слишком высоким;
- не устарели ли данные, на которых основано решение.

Когда открыто несколько позиций, вручную отслеживать рынок, состояние счёта, риски и изменения каждой сделки становится всё сложнее.

**Целевой Intelligence.TradeSystem должен выполнять эту работу непрерывно:** синхронизировать состояние аккаунта, сопоставлять позиции с рынком и правилами риска, фиксировать существенные изменения и формировать проверяемые рекомендации.

---

## Целевой пользовательский сценарий

Первый основной сценарий продукта выглядит так:

1. Пользователь входит в систему.
2. Подключает биржевой аккаунт с правами **только на чтение**.
3. Backend синхронизирует баланс и открытые позиции.
4. Для каждой позиции система получает актуальный рыночный контекст.
5. Детерминированные правила оценивают состояние позиции, риск и состояние портфеля.
6. Система формирует рекомендацию и причины её появления.
7. Пользователь видит позиции, оценки и историю изменений в адаптивном веб-интерфейсе.
8. При существенном изменении система может отправить уведомление в Telegram.
9. При необходимости ИИ преобразует уже сформированную оценку в понятное человеку объяснение, но не изменяет решение backend.

Примеры будущих рекомендаций:

- удерживать позицию;
- наблюдать и ждать подтверждения;
- защитить прибыль;
- частично сократить позицию;
- закрыть позицию;
- перенести stop-loss;
- частично зафиксировать прибыль;
- не увеличивать позицию;
- разрешить увеличение позиции при выполнении заданных условий.

Автоматическое открытие, усреднение и закрытие сделок **не входят в первый MVP**.

---

## Основные принципы

1. **Backend — единый источник бизнес-истины.** React, Telegram и будущие клиенты должны получать одинаковое состояние и одинаковые решения.
2. **Биржа — источник фактического состояния счёта и позиций.** Локальная система хранит собственную историю и производные оценки, но не подменяет данные биржи.
3. **Детерминированное ядро принимает решение.** Оценка позиции, правила риска и рекомендация не должны зависеть от доступности языковой модели.
4. **ИИ объясняет, а не управляет.** Агентный контур может дополнять анализ и формировать человекочитаемое объяснение, но не обходить ограничения backend.
5. **Безопасность важнее удобства.** Первый сценарий подключения биржи предполагает только права на чтение.
6. **React — основной пользовательский интерфейс.** Telegram используется для важных уведомлений, а не как основное средство управления.
7. **История должна быть проверяемой.** Существенные изменения позиции, оценки, рекомендации и версии правил должны сохраняться так, чтобы решение можно было восстановить и проанализировать.
8. **Backend API является универсальным и не зависит от типа клиента.** Для защищённого API приняты OAuth 2.0 / OpenID Connect, Bearer access token и подписанный JWT как целевой формат первого MVP; browser cookie допускается только на BFF boundary.

---

## Текущее состояние

Этапы A и B завершены. Основа C-05A и изоляция C-06 реализованы: отдельный Authorization Server на ASP.NET Core Identity + OpenIddict выпускает Authorization Code + PKCE токены, `Api` проверяет signed JWT через OIDC discovery/JWKS, а user-owned persistence операции явно ограничены владельцем.

### Уже реализовано

- получение публичных рыночных данных Bybit;
- отдельные публичный и приватный адаптеры Bybit;
- публичный клиент Bybit без пользовательских ключей;
- создание приватного provider для конкретных credentials без глобального authenticated client;
- нейтральные прикладные интерфейсы `IMarketDataProvider`, `IDerivativesDataProvider`, `IPrivateAccountProvider`;
- отдельный модуль `Intelligence.TradeSystem.MarketIntelligence`;
- отдельный deployable `Intelligence.TradeSystem.Identity` с ASP.NET Core Identity, OpenIddict, discovery/JWKS и отдельной PostgreSQL persistence;
- `Intelligence.TradeSystem.Api` как JwtBearer resource server с проверкой issuer, audience, lifetime, signature и `trade.api`;
- integration tests на реальный PostgreSQL, Authorization Code + PKCE, JWS access token и API boundary;
- сопоставление user-delegated OIDC `sub` со стабильным Domain `UserId` и явная маркировка user principal;
- user-scoped Application repository contracts для аккаунтов, позиций, портфелей, оценок и рекомендаций;
- PostgreSQL ownership predicates и integration/E2E tests, скрывающие cross-user access по известным идентификаторам;
- анализ интервалов `15m`, `1h`, `4h`, `1d`;
- расчёт EMA, RSI, ATR, SMA, упрощённого профиля объёма и классификации тренда;
- обработка стакана, потока сделок, funding, open interest и long/short ratio;
- детерминированные `entryQuality`, `riskFlags`, рыночные теги и диагностика индикаторов;
- проверка свежести и частичности рыночных данных;
- публичный `GET /api/market-analysis/{symbol}/llm-payload` со схемой `1.0`;
- legacy `POST /api/market-analysis/snapshot`, сохраняемый для совместимости;
- типизированные идентификаторы пользователя, биржевого аккаунта, позиции и инструмента;
- `ExchangeAccount`, `Position` и устойчивая идентичность биржевой позиции с учётом `positionIdx`;
- существенные изменения позиции `New`, `Updated`, `Increased`, `Reduced`, `Closed`, `MarkedUnknown`, `MarkedStale` и `Recovered`, а также состояния отслеживания `Active`, `Unknown`, `Stale` и `Closed`;
- безопасная сверка снимков и неизменяемая история существенных изменений `PositionChange`;
- `PortfolioState`, агрегирование портфеля и базовая политика увеличения риска;
- неизменяемый `PositionAssessment` и жизненный цикл `Recommendation`;
- отдельные словари `PositionAction`, `AddDecision`, `RiskIncreaseDecision` и `ReasonCode`;
- relational PostgreSQL schema, EF Core migrations, persistence repositories и Testcontainers integration tests для доменного состояния;
- оптимистическая конкурентность (compare-and-swap по версии, без retry) для ExchangeAccount, Position и Recommendation;
- изолированный публичный BTC Daily Check через OpenClaw и Telegram;
- архитектурные, доменные, модульные, прикладные и API-тесты;
- базовые OpenTelemetry и проверки состояния сервиса.

### Есть только как архитектурная заготовка

- legacy-типы `OpenPosition`, `OpenPositionSnapshot`, `PortfolioSnapshot` и их сборщик, сохраняемые для совместимости текущих путей;
- `IPrivateAccountProvider`, чтение баланса и открытых позиций Bybit;
- account-specific private Bybit provider;
- общий фундамент наблюдаемости.

Эти компоненты ещё не связаны с пользователем, постоянным хранилищем и периодической синхронизацией.

### Ещё не реализовано

- безопасное хранение API-ключей Bybit;
- пользовательский сценарий подключения биржевого аккаунта;
- периодическая синхронизация аккаунта и позиций;
- детерминированный сервис оценки позиции и политика формирования рекомендаций;
- API v1 для аккаунтов, позиций, портфеля и рекомендаций;
- SignalR-обновления;
- React-клиент;
- уведомления о рисках конкретных пользовательских позиций.

---

## Архитектура

Целевое направление архитектуры:

```text
Bybit / другие источники
        │
        ▼
Exchange adapters
        │
        ▼
Application + Domain
        │
        ├── состояние аккаунта и позиции
        ├── рыночный контекст
        ├── оценка позиции
        ├── политика риска
        └── рекомендации
        │
        ▼
Backend API — источник бизнес-истины
        │
        ├── REST
        ├── SignalR
        └── события / уведомления
        │
        ├──────────────► React
        ├──────────────► Telegram
        └──────────────► будущие клиенты

Необязательный агентный контур
        ▲
        │ версионированные данные backend
        │
      OpenClaw / LLM
```

Внешний агентный контур не должен обращаться напрямую к пользовательской базе данных или к Bybit от имени пользователя. Он получает только подготовленные backend-контракты.

### Универсальная аутентификация клиентов

`Intelligence.TradeSystem.Api` является client-agnostic resource server. Один и тот же бизнес-API `/api/v1/*` используется React, mobile, desktop, CLI, native clients и будущими интеграциями. Backend остаётся source of truth; тип клиента не создаёт отдельную бизнес-логику или отдельный API-контракт.

Для защищённых endpoints используется:

```text
OAuth 2.0 / OpenID Connect
            ↓
     Bearer access token
            ↓
       signed JWT
```

Реализованный Authorization Server:

```text
ASP.NET Core Identity + OpenIddict
       ↓
Intelligence.TradeSystem.Identity
       ↓
Intelligence.TradeSystem.Api (resource server)
```

Identity host и отдельный migration stream реализованы. Login остаётся минимальным server-rendered flow только для OAuth proof; public registration, React, BFF и Bybit onboarding ещё не реализованы. User isolation выполняется на Application/Infrastructure boundary, но полный user-facing CRUD ещё относится к этапу F.

В Docker Development canonical issuer — `http://localhost:8081`, чтобы browser/native clients могли обращаться к Identity по публичному адресу. API проверяет этот canonical `iss`, а discovery и JWKS получает через internal `Authentication:MetadataAddress` и `Authentication:BackchannelBaseAddress` (`http://identity:8080`). Backchannel меняет только network destination для запросов к известному public issuer и не изменяет protocol metadata; произвольные hosts не переписываются.

`Identity:SigningCertificates` задаёт набор одновременно активных signing credentials для rollover. OpenIddict выбирает credential для новых токенов по своим documented selection rules, включая validity и furthest expiration; порядок JSON-массива не является гарантией. Старый сертификат остаётся зарегистрированным на время overlap, чтобы ранее выданные токены продолжали проверяться; access tokens явно ограничены коротким lifetime в `Identity:AccessTokenLifetime` (MVP default — 15 минут).

Для login BFF использует Authorization Code + PKCE (`S256`). API получает подписанные JWT access tokens и валидирует их через стандартный OIDC discovery/JWKS.

Минимальный защищённый `GET /api/v1/auth/me` возвращает проверенный `userId`; user-owned операции используют тот же validated user-delegated principal.

React рассматривается как browser-клиент через BFF:

```text
React → BFF → Bearer JWT → Intelligence.TradeSystem.Api
```

Secure HttpOnly cookie может использоваться только между React и BFF для browser session и не является authentication contract основного API. Если BFF использует автоматически отправляемую cookie, ему нужна явная CSRF-защита: одного `HttpOnly` недостаточно. Mobile, desktop и CLI используют OAuth/OIDC и Bearer для того же API; предпочтительный сценарий для public clients — Authorization Code + PKCE, а для CLI также возможен Device Authorization Flow.

Выбор зафиксирован в [ADR-0003](docs/adr/0003-authorization-server-selection.md), который дополняет [ADR-0002](docs/adr/0002-universal-api-authentication-strategy.md). Публичные market endpoints, включая `GET /api/market-analysis/{symbol}/llm-payload`, остаются anonymous.

### Основные backend-проекты

| Проект | Назначение |
|---|---|
| `Intelligence.TradeSystem.Domain` | Доменные типы и бизнес-состояние, не зависящие от инфраструктуры |
| `Intelligence.TradeSystem.Application` | Прикладные сценарии и порты для внешних возможностей |
| `Intelligence.TradeSystem.MarketIntelligence` | Расчёты, признаки и снимки публичного рынка |
| `Intelligence.TradeSystem.Exchanges` | Реализации интеграций с биржами; сейчас основной adapter — Bybit |
| `Intelligence.TradeSystem.Infrastructure` | PostgreSQL/EF Core и техническая граница постоянного хранения |
| `Intelligence.TradeSystem.Identity` | Отдельный ASP.NET Core Identity + OpenIddict Authorization Server |
| `Intelligence.TradeSystem.Identity.Migrations` | Одноразовый deployment runner для Identity/OpenIddict migrations |
| `Intelligence.TradeSystem.Api` | HTTP API и composition root |
| `Intelligence.TradeSystem.AppHost` | Локальная оркестрация через .NET Aspire |
| `Intelligence.TradeSystem.ServiceDefaults` | Общая телеметрия и стандартная инфраструктурная конфигурация |

Тестовые проекты отдельно проверяют архитектурные зависимости, чистый домен, API-контракты, прикладную логику, биржевые адаптеры и Market Intelligence.

---

## Market Intelligence — уже работающая часть системы

Текущая наиболее зрелая часть проекта — подсистема публичного рыночного анализа. Она получает данные Bybit и формирует структурированный `MarketSnapshot`, который используется как подготовленный рыночный контекст.

В анализ входят:

- цена и 24-часовой диапазон;
- несколько таймфреймов;
- технические индикаторы;
- тренд и режим рынка;
- объём;
- стакан заявок;
- поток сделок;
- open interest;
- funding;
- long/short ratio;
- уровни поддержки и сопротивления;
- свежесть и полнота данных.

### `entryQuality`

`entryQuality` отвечает на вопрос, насколько текущая рыночная ситуация подходит для рассмотрения входа. Это **не рекомендация по пользовательской позиции** и не будущий `PositionAssessment`.

Оценка учитывает тренд, EMA, RSI, объём, уровни, рыночный режим, качество данных и конфликтующие сигналы.

### `riskFlags`

`riskFlags` объясняют, какие факторы снижают качество потенциального входа: например слабый объём, перегретый RSI, конфликт с EMA, близость сопротивления или недостаточное подтверждение тренда.

### Рыночные теги

`tags` — компактные агрегированные признаки рынка, например:

- `trending`;
- `volatile-regime`;
- `mean-reversion-regime`;
- `bid-pressure`;
- `aggressive-selling`;
- `orderbook-tradeflow-conflict`;
- `oi-declining`;
- `actionable-entry`;
- `weak-entry-confirmation`.

---

## BTC Daily Check и OpenClaw

В репозитории сохраняется отдельный публичный сценарий BTC Daily Check:

```text
backend llm-payload 1.0
        ↓
tech-analysis-agent
        ↓
technical_report
        ↓
chief-market-synthesizer
        ↓
Telegram-публикация
```

Этот сценарий:

- работает с публичными рыночными данными;
- не является системой сопровождения пользовательских позиций;
- не требует доступа агентного контура к пользовательским аккаунтам;
- сохраняет совместимость с `llm-payload` версии `1.0`;
- не определяет архитектуру первого MVP основного продукта.

Дальнейшая роль OpenClaw будет отдельно переосмыслена после проверки первого MVP. Основной продукт должен оставаться работоспособным при полном отключении ИИ.

---

## Публичный API текущего рыночного анализа

### Получить LLM-ready payload

```http
GET /api/market-analysis/{symbol}/llm-payload
```

Пример:

```http
GET /api/market-analysis/BTCUSDT/llm-payload?exchange=Bybit&category=Linear&mode=Intraday
```

Ответ — публичный JSON-контекст схемы `1.0`, подготовленный для внешнего анализа. Путь не требует вызова приватных методов аккаунта.

Поддерживаемые режимы рыночного анализа:

| Режим | Назначение |
|---|---|
| `Intraday` | Внутридневной рыночный контекст |
| `Swing` | Более широкий контекст для удержания сценария дольше одного дня |
| `Portfolio` | Рыночный контекст с набором старших основных таймфреймов; полноценный пользовательский портфельный сценарий ещё не реализован |

### Legacy market snapshot

```http
POST /api/market-analysis/snapshot
```

Пример тела запроса:

```json
{
  "exchange": "Bybit",
  "symbol": "BTCUSDT",
  "category": "Linear"
}
```

Endpoint сохраняется ради совместимости и отладки существующего рыночного анализа.

---

## Запуск backend

### Требования

- .NET 10 SDK;
- доступ к интернету для получения публичных данных Bybit;
- Docker — если используется контейнерный запуск;
- PostgreSQL — если используется Docker Compose или Aspire;
- Docker Compose создаёт named network `trade-agent-network` автоматически.

Публичный рыночный анализ не должен требовать пользовательских API-ключей Bybit. Приватные credentials понадобятся только для будущих сценариев чтения конкретного аккаунта.

### Защита credentials Bybit

Пары `apiKey`/`apiSecret` хранятся только как один authenticated-encrypted payload в PostgreSQL. `ExchangeAccount` не содержит credentials, а master keys не сохраняются в TradeSystem database, логах, ответах API или репозитории. User-scoped store поддерживает создание, чтение, локальную замену пары (`Rotate`), локальный отзыв (`Revoke`) и отдельную перепротекцию существующей строки новым active master key (`Reprotect`). `Revoke` удаляет локальный доступ системы и не удаляет API key на стороне Bybit.

Для локального запуска задайте явный 32-байтный ключ в Base64; ключ не генерируется автоматически при старте:

```bash
export CredentialProtection__ActiveKeyId=local_v1
export CredentialProtection__Keys__local_v1='<base64-32-byte-key>'
```

Ключ можно сгенерировать без вывода значения в журнал:

```bash
export CredentialProtection__Keys__local_v1="$(openssl rand -base64 32)"
```

Для PowerShell сгенерируйте ключ без вывода значения:

```powershell
$bytes = New-Object byte[] 32
$rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
try { $rng.GetBytes($bytes) } finally { $rng.Dispose() }
$env:TRADE_CREDENTIAL_KEY = [Convert]::ToBase64String($bytes)
```

`TRADE_CREDENTIAL_KEY` — переменная хоста только для mapping в Compose. Для прямого `dotnet run` задаются application configuration keys `CredentialProtection__ActiveKeyId` и `CredentialProtection__Keys__local_v1` напрямую. При rollover добавьте новый key id в deployment configuration, оставьте старый key id доступным для чтения, выполните `Reprotect` для всех строк и только после этого удаляйте старый key из key ring. Удаление старого ключа раньше перепротекции делает соответствующие строки нечитаемыми. CI создаёт disposable 32-байтный ключ во время workflow и маскирует его.

### Восстановление зависимостей

Из корня репозитория:

```bash
dotnet restore backend/src/Intelligence.TradeSystem.slnx
```

### Сборка

```bash
dotnet build backend/src/Intelligence.TradeSystem.slnx --configuration Release
```

### Тесты

```bash
dotnet test backend/src/Intelligence.TradeSystem.slnx --configuration Release
```

### Запуск API

Для прямого запуска API задайте application configuration keys (здесь `TRADE_CREDENTIAL_KEY` не используется):

```bash
export CredentialProtection__ActiveKeyId=local_v1
export CredentialProtection__Keys__local_v1='<base64-32-byte-key>'
```

В PowerShell после генерации ключа задайте те же application keys:

```powershell
$env:CredentialProtection__ActiveKeyId = 'local_v1'
$env:CredentialProtection__Keys__local_v1 = $env:TRADE_CREDENTIAL_KEY
```

```bash
cd backend/src
dotnet run --project Intelligence.TradeSystem.Api
```

### Запуск через Aspire

```bash
cd backend/src
dotnet run --project Intelligence.TradeSystem.AppHost
```

### Docker

При первом локальном запуске с новым PostgreSQL volume сгенерируйте ключ и сохраните его в локальном secret mechanism:

```bash
export TRADE_CREDENTIAL_KEY="$(openssl rand -base64 32)"
```

Для следующих запусков с существующим volume используйте тот же `TRADE_CREDENTIAL_KEY`. Новый случайный ключ при каждом старте сделает уже сохранённые credential rows нечитаемыми. Ключ не коммитится и не выводится в лог.

```bash
cd backend
docker compose up --build -d
```

Compose запускает PostgreSQL, затем идемпотентный `identity-db-init`, отдельный Identity migration runner, Identity и API:

```text
postgres → identity-db-init → identity-migrations → identity → api
```

`identity-db-init` создаёт `tradesystem_identity`, если её нет, и безопасно завершается при повторном запуске. Поэтому обычный старт или обновление через `docker compose up --build -d` не удаляет данные и не зависит от состояния `postgres/init`. API-контейнер публикуется на `8080`, Identity — на `8081`; публичный issuer — `http://localhost:8081`, а внутренний API backchannel — `http://identity:8080`. API валидирует canonical public issuer, но discovery/JWKS может загружать через внутренний network route. Discovery: `http://localhost:8081/.well-known/openid-configuration`, JWKS: `http://localhost:8081/.well-known/jwks`, API liveness: `http://localhost:8080/alive`, protected proof endpoint: `http://localhost:8080/api/v1/auth/me`.

Для CI OAuth smoke используется отдельный профиль `ci`: `auth-test-seeder` создаёт тестового пользователя и public client через стандартные Identity/OpenIddict managers, после чего workflow получает настоящий токен Authorization Code + PKCE (`S256`). Password grant, Client Credentials и custom token endpoints не используются.

Полный локальный сброс — отдельная destructive операция, удаляющая локальные PostgreSQL данные:

```bash
docker compose down -v
```

---

### Миграции PostgreSQL

Production schema развивается только через EF Core migrations. Ни один production host не применяет миграции автоматически при старте. Business и Identity используют отдельные migration streams.

```bash
cd backend/src
export ConnectionStrings__TradeSystem='Host=localhost;Port=5432;Database=tradesystem;Username=tradesystem;Password=<password>'
dotnet ef migrations list --project Intelligence.TradeSystem.Infrastructure
dotnet ef database update --project Intelligence.TradeSystem.Infrastructure
dotnet ef migrations add <MigrationName> --project Intelligence.TradeSystem.Infrastructure
```

Identity migrations:

```bash
cd backend/src
export ConnectionStrings__TradeSystemIdentity='Host=localhost;Port=5432;Database=tradesystem_identity;Username=tradesystem;******'
dotnet ef migrations list --project Intelligence.TradeSystem.Identity --startup-project Intelligence.TradeSystem.Identity --context Intelligence.TradeSystem.Identity.Persistence.IdentityDbContext
dotnet ef database update --project Intelligence.TradeSystem.Identity --startup-project Intelligence.TradeSystem.Identity --context Intelligence.TradeSystem.Identity.Persistence.IdentityDbContext
dotnet run --project Intelligence.TradeSystem.Identity.Migrations
```

Для deployment/local orchestration предпочтителен одноразовый `Identity.Migrations` runner: он применяет миграции и завершается с ненулевым кодом при ошибке.

---

## Тестирование и CI

В solution есть отдельные наборы тестов для:

- архитектурных зависимостей;
- доменных инвариантов аккаунта, позиции, портфеля, оценки и рекомендации;
- API-контрактов, включая `llm-payload` 1.0;
- прикладных сервисов;
- PostgreSQL migrations и persistence через Testcontainers;
- PostgreSQL credential security tests: authenticated encryption, tamper/AAD protection, CAS rotation/revocation, key rollover and cascade deletion;
- PostgreSQL user-isolation and real Bearer OAuth/OIDC E2E tests;
- Bybit adapters и их регистрации;
- Market Intelligence и индикаторов.

CI выполняет сборку, тесты, сборку Docker-образов API, Identity и migration runner, затем запускает Compose auth stack и проверяет Identity discovery/JWKS и API liveness.

Release-сборка настроена с `TreatWarningsAsErrors=true`.

---

## Дорожная карта

Полная и актуальная последовательность разработки хранится в [`ROADMAP.md`](ROADMAP.md). Этот документ является основной дорожной картой проекта.

Этапы B и C-06 завершены. Текущий активный этап — **C: хранение, безопасность и пользователи**.

Основная ближайшая последовательность:

1. безопасное хранение ключей Bybit;
2. подключение Bybit-аккаунта и периодическая синхронизация;
3. детерминированная оценка позиции и политика рекомендаций;
4. пользовательский REST API и SignalR;
5. React-панель;
6. непрерывное наблюдение и Telegram-уведомления;
7. измерение качества рекомендаций;
8. переосмысление OpenClaw и расширенного ИИ-контура — после проверки первого MVP.

---

## Граница первого MVP

Первый MVP должен позволить пользователю:

- войти в систему;
- подключить Bybit-аккаунт только для чтения;
- увидеть актуальные открытые позиции и состояние портфеля;
- получить детерминированную оценку каждой позиции;
- увидеть рекомендацию и причины её появления;
- получать обновления без ручного обновления страницы;
- получать важные уведомления;
- просматривать историю существенных изменений и рекомендаций.

Автоматическое исполнение торговых операций остаётся за пределами первого MVP и может рассматриваться только после накопления статистики качества рекомендаций и отдельной проверки рисков.

---

## Текущие ограничения

- основной поддерживаемый источник рыночных данных — Bybit;
- основной фактически работающий внешний сценарий сейчас — публичный рыночный анализ;
- persistence доменного состояния реализована, но пока не подключена к пользовательскому API и workflow синхронизации;
- полноценного пользовательского аккаунта и периодической синхронизации позиций пока нет;
- PostgreSQL schema, migrations и repository implementations существуют; автоматическая синхронизация и пользовательский workflow ещё не реализованы;
- детерминированный сервис, который формирует оценки и рекомендации из рыночного и портфельного контекста, ещё не реализован;
- React-клиент ещё не создан;
- BTC Daily Check остаётся отдельным экспериментальным публичным сценарием;
- качество рыночного анализа зависит от свежести и полноты данных.

---

## Disclaimer

Проект является экспериментальным программным инструментом для исследования и поддержки принятия решений на криптовалютном рынке.

Он не является финансовым советом и не гарантирует прибыльность торговых решений. Криптовалютные рынки крайне волатильны, а любые торговые решения пользователь принимает самостоятельно и на свой риск.
