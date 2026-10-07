# Intelligence.TradeSystem

**Intelligence.TradeSystem — единая интеллектуальная торговая среда, которая видит рынок, знает портфель пользователя, сопровождает сделки, объясняет происходящее, учится на истории пользователя, помогает исследовать рынок и постепенно автоматизирует рутинные действия — при этом критические торговые решения остаются проверяемыми и ограниченными детерминированными правилами риска.**

Текущая разработка сосредоточена на первой основной продуктовой вертикали: безопасном сопровождении уже открытых криптовалютных позиций. Backend получает фактическое состояние биржевого аккаунта и рынка, формирует воспроизводимые оценки и рекомендации, сохраняет историю и отдаёт одинаковое состояние всем клиентам.

Долгосрочный продукт шире первого MVP: в Product Vision зафиксированы будущие направления Market Intelligence, Trading Journal, Social & Strategy, внешняя автоматизация и горизонтальный AI Intelligence Layer. Эти направления не являются автоматически утверждённым порядком разработки.

Подробности:

- [Product Vision](docs/product/vision.md) — каким должен стать продукт;
- [Capability Map](docs/product/capability-map.md) — какие возможности реализованы, планируются или исследуются;
- [ROADMAP](ROADMAP.md) — единственный актуальный источник статуса и последовательности разработки;
- [Документация](docs/README.md) — навигация по source-of-truth документам.

> Проект находится в активной разработке. Product Vision описывает долгосрочную цель, а ROADMAP и раздел «Текущее состояние» ниже — фактическую границу текущей реализации.

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
9. **User-facing API описывает пользовательские сценарии, а не внутреннюю схему домена.** Текущая оценка и рекомендация публикуются как единый `evaluation`, состояние портфеля первого MVP привязано к конкретному биржевому аккаунту, а SignalR только сообщает об изменении — актуальное состояние всегда перечитывается через REST.
10. **BFF остаётся browser security boundary.** Access token не должен попадать в JavaScript React-клиента; browser REST/SignalR integration строится через BFF поверх той же OAuth/OIDC identity, тогда как native/token clients могут предъявлять Bearer непосредственно API.

---

## Текущее состояние

Этапы **A, B, C, D, E и F завершены**. F-08 зафиксировал полный runtime-generated OpenAPI contract пользовательского `/api/v1`, стабильные operationId, auth/ProblemDetails/pagination/enum invariants и REST ↔ SignalR recovery mapping. User-scoped SignalR invalidation boundary доступна по `/hubs/v1/updates`. В рамках G-01 появились React-клиент `frontend/intelligence-trade-web` с адаптивной оболочкой и отдельный BFF `Intelligence.TradeSystem.Bff` — независимые deployment units за единым browser-facing origin: OAuth/OIDC login, server-side browser session и full SSO logout; browser SignalR integration относится к G-06. G-02 добавил первый пользовательский экран: управление read-only подключениями Bybit в Web через явные BFF endpoints. Статус этапа и следующий шаг определяются только `ROADMAP.md`. Backend умеет синхронизировать read-only Bybit-аккаунт, строить воспроизводимую `PositionAssessment`, детерминированно формировать `Recommendation`, сохранять recommendation lifecycle и защищаться от дребезга решений через persisted stability state. Отдельный Authorization Server на ASP.NET Core Identity + OpenIddict выпускает Authorization Code + PKCE токены, `Api` проверяет signed JWT через OIDC discovery/JWKS, user-owned persistence операции явно ограничены владельцем, а credentials Bybit защищены authenticated encryption и внешним key ring.

F-02 завершён: v1 API публикует lifecycle read-only подключений, включая проверку, ротацию credentials, sync и отключение; в G-02 management routes перенесены на `/api/v1/me/exchange-accounts`. F-03 завершён: доступны user-scoped список и карточка позиций, а также account-scoped portfolio summary с cursor pagination, фильтрами и `204 No Content` до первого snapshot. F-04 завершён: позиция получает актуальный public market context и bounded candle series через `/api/v1/positions/{id}/market` и `/api/v1/positions/{id}/candles`; public market-analysis остаётся отдельным API. F-05 завершён: `/api/v1/positions/{id}/evaluation` объединяет latest assessment и current effective recommendation, а явный POST workflow использует user-scoped portfolio и cached public market snapshot без скрытого private sync. F-06 завершён: `/api/v1/positions/{id}/timeline` объединяет значимые изменения позиции, assessments/evaluations и recommendations в user-scoped timeline с opaque cursor pagination и repeatable type filter. F-07 завершён: user-scoped SignalR invalidation boundary `/hubs/v1/updates` сообщает об изменении account, portfolio, position и evaluation, а актуальное состояние восстанавливается через REST. F-08 завершён: OpenAPI и contract tests защищают полную границу F и пригодность контракта для будущей генерации клиента. Остальные пользовательские экраны React и browser realtime integration относятся к последующим задачам этапа G.

### Уже реализовано

- получение публичных рыночных данных Bybit;
- отдельные публичный и приватный адаптеры Bybit;
- публичный клиент Bybit без пользовательских ключей;
- создание приватного provider для конкретных credentials без глобального authenticated client;
- нейтральные прикладные интерфейсы `IMarketDataProvider`, `IDerivativesDataProvider`, `IPrivateAccountProvider`;
- отдельный модуль `Intelligence.TradeSystem.MarketIntelligence`;
- отдельный deployable `Intelligence.TradeSystem.Identity` с ASP.NET Core Identity, OpenIddict, discovery/JWKS и отдельной PostgreSQL persistence;
- `Intelligence.TradeSystem.Api` как JwtBearer resource server с проверкой issuer, audience, lifetime, signature и `trade.api`;
- адаптивная dark-first React-оболочка `frontend/intelligence-trade-web` (nginx frontend service, проксирующий `/bff/**` и OIDC callbacks) и `Intelligence.TradeSystem.Bff` как внутренний BFF: confidential OIDC client `trade-web-bff` с Authorization Code + PKCE, server-side browser session без выдачи tokens JavaScript, refresh access token, CSRF-защита unsafe `/bff/**` и full SSO logout через standard OIDC end-session ([Web BFF contract](docs/web-bff-contract.md));
- `Intelligence.TradeSystem.Api` требует PostgreSQL и запускается только в полноценном DB-backed runtime-режиме: обязательная конфигурация проверяется при startup, `/alive` отражает liveness процесса, а `/healthz` — readiness зависимостей, включая PostgreSQL;
- integration tests на реальный PostgreSQL, Authorization Code + PKCE, JWS access token и API boundary;
- сопоставление user-delegated OIDC `sub` со стабильным Domain `UserId` и явная маркировка user principal;
- user-scoped Application repository contracts для аккаунтов, позиций, портфелей, оценок и рекомендаций;
- PostgreSQL ownership predicates и integration/E2E tests, скрывающие cross-user access по известным идентификаторам;
- безопасное хранение API credentials Bybit: AES-256-GCM с AAD, внешний key ring и user-scoped CAS rotate/revoke/reprotect;
- анализ интервалов `15m`, `1h`, `4h`, `1d`;
- расчёт EMA, RSI, ATR, SMA, упрощённого профиля объёма и классификации тренда;
- обработка стакана, потока сделок, funding, open interest и long/short ratio;
- детерминированные `entryQuality`, `riskFlags`, рыночные теги и диагностика индикаторов;
- проверка свежести и частичности рыночных данных;
- публичный `GET /api/market-analysis/{symbol}/llm-payload` со схемой `1.0`;
- legacy `POST /api/market-analysis/snapshot`, сохраняемый для совместимости;
- типизированные идентификаторы пользователя, биржевого аккаунта, позиции и инструмента;
- `ExchangeAccount`, `Position` и устойчивая идентичность биржевой позиции с учётом `positionIdx`;
- подключение Bybit-аккаунта, ручная и фоновая синхронизация баланса, позиций и `PortfolioState`;
- канонический management-контракт `/api/v1/me/exchange-accounts` для list/connect/reconnect/rename/verify/credential rotation/disconnect с user scope; account-scoped `POST /api/v1/exchange-accounts/{id}/sync` и `GET /api/v1/exchange-accounts/{id}/portfolio` сохраняются отдельно; OpenAPI/API contract tests фиксируют отсутствие прежних management routes;
- канонический `/api/v1/positions` с user-scoped SQL-side cursor pagination и фильтрами `exchangeAccountId`/`trackingState`/`symbol`/`side`;
- `/api/v1/positions/{id}` с current-state карточкой без истории, timeline, evaluation и market context;
- `/api/v1/positions/{id}/timeline` с объединённой user-scoped историей `positionChange`/`evaluation`/`recommendation`, newest-first deterministic ordering, opaque cursor pagination и repeatable `type` filter;
- `/api/v1/exchange-accounts/{id}/portfolio` с account-scoped latest summary без встроенного списка позиций и с `204 No Content` до первого snapshot;
- `/api/v1/positions/{id}/market` с отдельным v1 market context и `/api/v1/positions/{id}/candles` с bounded oldest-to-newest candle series, разрешёнными через user-owned position identity;
- обязательная provider-side identity exchange account (для Bybit — `userID`): один `ExchangeAccountId` остаётся связан с одним внешним аккаунтом, а credentials другого account/subaccount не могут перепривязать существующую историю;
- существенные изменения позиции `New`, `Updated`, `Increased`, `Reduced`, `Closed`, `MarkedUnknown`, `MarkedStale` и `Recovered`, а также состояния отслеживания `Active`, `Unknown`, `Stale` и `Closed`;
- безопасная сверка снимков и неизменяемая история существенных изменений `PositionChange`;
- `PortfolioState`, агрегирование портфеля и базовая политика увеличения риска;
- неизменяемый `PositionAssessment` и жизненный цикл `Recommendation`;
- отдельные словари `PositionAction`, `AddDecision`, `RiskIncreaseDecision` и `ReasonCode`;
- единый воспроизводимый вход оценки позиции и `PositionAssessmentService`, учитывающий позицию, рынок, портфель, свежесть данных и policy identity;
- версионируемый строгий JSON `PolicyDefinition` с canonical SHA-256 identity и валидацией;
- детерминированная `RecommendationPolicy`, отделённая от расчёта `PositionAssessment`;
- действия `Hold`, `Watch`, `ProtectProfit`, `Reduce`, `Close`, `MoveStop`, `TakePartialProfit` и независимое решение `AddDecision`;
- неотключаемые safety guards, запрещающие повышение риска при stale, partial или uncertain данных;
- typed invalidation/reevaluation conditions, `ValidUntil`, policy identity и persistence continuation metadata;
- `RecommendationStabilityPolicy` с anti-chatter, cooldown/hysteresis и safety bypass;
- применение stability policy в `RecommendationService`, persisted pending stability state в PostgreSQL, CAS/retry и атомарная публикация/замена current recommendation;
- user-scoped persistence оценки, recommendation и stability state с PostgreSQL integration tests на concurrency и isolation;
- relational PostgreSQL schema, EF Core migrations, persistence repositories и Testcontainers integration tests для доменного состояния;
- PostgreSQL transactional outbox для versioned application events и SignalR invalidation; события durable сохраняются до появления downstream consumer. Доставка имеет at-least-once semantics, `EventId` используется для idempotency, а `PositionId + PositionChangeSequence` — для causal ordering. Dispatcher включён по умолчанию после регистрации handlers для всех persisted event types; operational-параметры задаются в `ApplicationEventOutboxDispatcher` (polling, batch, concurrency, lease и retry delay). Realtime contract описан в [`docs/realtime-v1-contract.md`](docs/realtime-v1-contract.md);
- общий process-local кэш финальных публичных `MarketSnapshot` с коротким TTL и per-key single-flight; ключ содержит только `ExchangeId`, нормализованный через `Trim()` `Symbol` и `MarketCategory`, без `UserId` и приватного состояния;
- оптимистическая конкурентность (compare-and-swap по версии) для ExchangeAccount, Position и Recommendation; recommendation publication использует bounded retry после свежего persistence read;
- воспроизводимая среда сборки через `global.json` и фиксированные .NET SDK/runtime версии в Docker;
- CI на pull request и push в `develop`/`main`, aggregate line coverage gate, NuGet vulnerability check, Docker/Identity/PostgreSQL/OAuth smoke проверки;
- изолированный публичный BTC Daily Check через OpenClaw и Telegram;
- архитектурные, доменные, модульные, прикладные, API- и интеграционные тесты;
- базовые OpenTelemetry и проверки состояния сервиса.
- стабильная contract foundation пользовательского `/api/v1`: typed DTO/read models, JSON conventions, ProblemDetails contract, cursor pagination foundation и API/HTTP contract tests;

### Есть только как архитектурная заготовка

- legacy-типы `OpenPosition`, `OpenPositionSnapshot`, `PortfolioSnapshot` и их сборщик, сохраняемые для совместимости текущих путей;
- инфраструктура структурированного логирования, OpenTelemetry и устойчивости внешних вызовов; полный operational-контур наблюдения и пользовательских уведомлений относится к последующим этапам.

Фоновая синхронизация выбирает только активные Bybit-аккаунты (`Connected` и `Unavailable`) и использует существующий application sync workflow.

### Ещё не реализовано

- остальные пользовательские экраны React поверх `/api/v1` (портфель, позиции, карточка и timeline) и browser SignalR integration через BFF;
- непрерывный цикл повторной оценки активных позиций;
- уведомления о рисках конкретных пользовательских позиций;
- общий cross-account portfolio и расширенная портфельная аналитика, включая correlation model.

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

### Пользовательский API этапа F

Основной `/api/v1/*` строится вокруг пользовательских сценариев, а не прямого опубликования доменных сущностей:

```text
exchange account
  ├── verify / rotate credentials / sync / disconnect
  └── account-scoped portfolio

position
  ├── current state
  ├── market context
  ├── candles
  ├── evaluation = assessment + current recommendation
  └── timeline
```

`sync` и `evaluation` принципиально разделены. Sync получает приватное фактическое состояние аккаунта с биржи. Evaluation использует сохранённое состояние позиции/портфеля и актуальные публичные рыночные данные; он не скрывает stale или partial private data автоматической синхронизацией и сохраняет safety semantics детерминированного ядра.

`Evaluation` должен не только содержать assessment/recommendation, но и явно показывать свою временную и входную идентичность (`evaluatedAt`, `validUntil` и version/identity входов либо эквивалентные стабильные поля). Это позволяет UI отличать более старый evaluation от свежего `/market`; текущая recommendation может отсутствовать и поэтому является nullable частью read model.

`PortfolioState` первого MVP относится к одному exchange account, поэтому API портфеля также account-scoped. Общий cross-account portfolio появится только вместе с отдельной межаккаунтной аналитической моделью.

`GET /api/v1/positions` является постраничным user-scoped списком и в первом MVP должен поддерживать фильтры как минимум по `exchangeAccountId`, `trackingState`, `symbol` и `side`. Основной список по умолчанию предназначен для сопровождаемых состояний `Active`, `Unknown` и `Stale`; закрытые позиции запрашиваются явно и не смешиваются с рабочим списком.

Timeline на этапах F–G включает доступную историю позиции, assessments/evaluations и recommendations. Существенные market-monitoring events появятся только после H-04 и не блокируют завершение F/G; после появления они могут быть добавлены в тот же timeline аддитивно.

SignalR сообщает, что пользовательское состояние изменилось, но не заменяет REST. Native/token clients предъявляют Bearer непосредственно hub. Browser-клиент использует BFF-compatible integration поверх той же identity и не получает access token в JavaScript; после realtime-события или восстановления соединения клиент перечитывает актуальный resource через REST.

`/api/v1/me/exchange-accounts` является каноническим management-контрактом read-only подключений текущего пользователя: список, включая отключённые, подключение, переименование, проверка, замена credentials и отключение. Account-scoped `POST /api/v1/exchange-accounts/{id}/sync` и `GET /api/v1/exchange-accounts/{id}/portfolio` остаются без изменений. Прежние management routes `GET`/`POST /api/v1/exchange-accounts`, `DELETE /api/v1/exchange-accounts/{id}`, `POST /api/v1/exchange-accounts/{id}/verify`, `PUT /api/v1/exchange-accounts/{id}/credentials` и незаверсионированные `/api/exchange-accounts/**` удалены без compatibility alias.

Публичный market-analysis API, включая `GET /api/market-analysis/{symbol}/llm-payload` 1.0, остаётся отдельным поддерживаемым публичным сценарием и основой BTC Daily Check. Legacy является `POST /api/market-analysis/snapshot`; будущий React-клиент не использует market-analysis API как основной пользовательский контракт.

OpenAPI и API contract tests обновляются инкрементально вместе с каждым PR этапа F, который меняет публичный контракт. Финальная задача F-08 проверяет полноту и стабильность всей v1-границы и пригодность для генерации клиентских типов, а не впервые документирует уже реализованные endpoints.

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

Identity host и отдельный migration stream реализованы. Login остаётся минимальным server-rendered flow Identity; public registration не реализована. React-клиент входит через BFF. Bybit onboarding доступен через защищённый `/api/v1/me/exchange-accounts` и Web-страницу «Подключения», а user-facing read API позиций, account-scoped portfolio, market context, evaluation, timeline и realtime invalidation boundary реализованы в F-03—F-07, а последующие этапы определяются `ROADMAP.md`. User isolation выполняется на Application/Infrastructure boundary.

В Docker Development canonical issuer — `http://localhost:8081`, чтобы browser/native clients могли обращаться к Identity по публичному адресу. API проверяет этот canonical `iss`, а discovery и JWKS получает через internal `Authentication:MetadataAddress` и `Authentication:BackchannelBaseAddress` (`http://identity:8080`). Backchannel меняет только network destination для запросов к известному public issuer и не изменяет protocol metadata; произвольные hosts не переписываются.

`Identity:SigningCertificates` задаёт набор одновременно активных signing credentials для rollover. OpenIddict выбирает credential для новых токенов по своим documented selection rules, включая validity и furthest expiration; порядок JSON-массива не является гарантией. Старый сертификат остаётся зарегистрированным на время overlap, чтобы ранее выданные токены продолжали проверяться; access tokens явно ограничены коротким lifetime в `Identity:AccessTokenLifetime` (MVP default — 15 минут).

Для login BFF использует Authorization Code + PKCE (`S256`). API получает подписанные JWT access tokens и валидирует их через стандартный OIDC discovery/JWKS.

Минимальный защищённый `GET /api/v1/auth/me` возвращает проверенный `userId`; user-owned операции используют тот же validated user-delegated principal.

React рассматривается как browser-клиент через BFF:

```text
React → BFF → Bearer JWT → Intelligence.TradeSystem.Api
```

Secure HttpOnly cookie может использоваться только между React и BFF для browser session и не является authentication contract основного API. Если BFF использует автоматически отправляемую cookie, ему нужна явная CSRF-защита: одного `HttpOnly` недостаточно. Access token при BFF-подходе не выдаётся browser JavaScript; это правило действует и для browser SignalR. Mobile, desktop и CLI используют OAuth/OIDC и Bearer для того же API; предпочтительный сценарий для public clients — Authorization Code + PKCE, а для CLI также возможен Device Authorization Flow.

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
| `Intelligence.TradeSystem.Bff` | ASP.NET Core BFF за frontend reverse proxy: browser session и посредничество с tokens без business logic; React assets не раздаёт |
| `Intelligence.TradeSystem.AppHost` | Локальная оркестрация через .NET Aspire |
| `Intelligence.TradeSystem.ServiceDefaults` | Общая телеметрия и стандартная инфраструктурная конфигурация |

React-клиент (Web client) находится вне .NET-решения, в `frontend/intelligence-trade-web`, и собирается, упаковывается в собственный nginx image и развёртывается независимо от BFF.

Тестовые проекты отдельно проверяют архитектурные зависимости, чистый домен, API-контракты, прикладную логику, persistence/concurrency, биржевые адаптеры и Market Intelligence.

---

## Market Intelligence — уже работающая часть системы

Подсистема публичного рыночного анализа получает данные Bybit и формирует структурированный `MarketSnapshot`, который используется как подготовленный рыночный контекст для оценки позиции и других сценариев.

Готовый публичный `MarketSnapshot` переиспользуется между request scopes через короткоживущий process-local cache. Для одинаковых `ExchangeId + Symbol + MarketCategory` сборка выполняется один раз на cache miss (single-flight) в независимо управляемом DI scope; `AnalysisMode`, пользовательские данные, приватные account state, credentials, Redis, PostgreSQL cache persistence и background refresh в этот кэш не входят.

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

`entryQuality` отвечает на вопрос, насколько текущая рыночная ситуация подходит для рассмотрения входа. Это **не рекомендация по пользовательской позиции** и не `PositionAssessment`.

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
| `Portfolio` | Рыночный контекст с набором старших основных таймфреймов; account-scoped пользовательский portfolio API реализован в F-03, а общий cross-account portfolio относится к последующему расширению |

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

## Локальная разработка

Рекомендуемый способ запуска полного приложения — Docker Compose. Он поднимает PostgreSQL, Identity, API, внутренний BFF и самостоятельный React/nginx frontend в согласованной локальной топологии.

Для первого запуска:

1. задайте локальные secrets `TRADE_CREDENTIAL_KEY`, `TRADE_WEB_BFF_CLIENT_SECRET` и `TRADE_WEB_DEV_PASSWORD`;
2. при необходимости задайте `TRADE_WEB_DEV_USERNAME` (по умолчанию `trade-dev-user`);
3. из каталога `backend` запустите:

```bash
docker compose up --build -d
docker compose ps -a
```

Browser-facing приложение доступно по `http://localhost:8082`. Identity публикуется на `http://localhost:8081`, API — на `http://localhost:8080`; BFF отдельный host port не публикует и доступен frontend только по внутренней Docker network.

```text
Browser → frontend (:8082)
            ├── React SPA
            └── /bff/**, OIDC callbacks → BFF
                                         ├── Identity (:8081)
                                         └── API (:8080)
```

`identity-db-init` и `identity-migrations` являются одноразовыми процессами, поэтому их состояние `Exited (0)` после успешного старта нормально.

Подробная инструкция по требованиям, secrets, сборке и запуску на **Windows, Linux и macOS**, selective rebuild отдельных сервисов, Aspire, backend/frontend tests, Playwright E2E, migrations, диагностике и сбросу локального окружения находится в [руководстве по локальной разработке](docs/local-development.md).

> При существующем PostgreSQL volume используйте тот же `TRADE_CREDENTIAL_KEY`: новый случайный master key сделает ранее сохранённые encrypted Bybit credentials нечитаемыми. Development-пользователь Identity также не меняет пароль автоматически — сохранённый `TRADE_WEB_DEV_PASSWORD` должен совпадать с паролем существующего пользователя.

---

## Тестирование и CI

В solution есть отдельные наборы тестов для:

- архитектурных зависимостей;
- доменных инвариантов аккаунта, позиции, портфеля, оценки и рекомендации;
- `PositionAssessmentService`, recommendation policy и anti-chatter/stability policy;
- persistence recommendation lifecycle, stability state, concurrency и user isolation;
- API-контрактов, включая `llm-payload` 1.0;
- прикладных сервисов;
- PostgreSQL migrations и persistence через Testcontainers;
- PostgreSQL credential security tests: authenticated encryption, tamper/AAD protection, CAS rotation/revocation, key rollover and cascade deletion;
- PostgreSQL user-isolation and real Bearer OAuth/OIDC E2E tests;
- BFF: session, refresh, CSRF, logout, forwarded headers и отсутствие frontend hosting; React unit/component tests (Vitest) и browser E2E (Playwright) через frontend service;
- Bybit adapters и их регистрации;
- Market Intelligence и индикаторов.

CI использует SDK из `global.json`, выполняется для pull request и push в `develop`/`main`, проверяет NuGet direct/transitive dependencies на известные vulnerabilities, запускает весь test suite и aggregate line coverage gate с минимальным порогом **92%**. Отдельный frontend job на Node.js 24 выполняет `npm ci`, typecheck, lint, unit tests и Vite build React-клиента. После тестов workflow собирает Docker-образы API, Identity, migration runner, BFF (context `backend`) и frontend (context `frontend/intelligence-trade-web`), проверяет их взаимную независимость, запускает Compose auth stack с disposable secrets и проверяет PostgreSQL/Identity initialization, discovery/JWKS, API liveness, API readiness, настоящий Authorization Code + PKCE OAuth/OIDC protected API smoke и Playwright browser E2E через public origin frontend: same-origin routing, login, сохранение session без выдачи tokens JavaScript, full SSO logout, `prompt=login` и адаптивную вёрстку.

Release-сборка настроена с `TreatWarningsAsErrors=true` для проектных предупреждений; известные SDK/tooling warnings оцениваются отдельно и не скрываются отключением анализаторов.

---

## Дорожная карта

Полная и актуальная последовательность разработки хранится в [`ROADMAP.md`](ROADMAP.md). Этот документ является основной дорожной картой проекта.

Этапы **A–F**, технические задачи **Tech-G01 — Tech-G13**, **G-01** и **G-02** завершены. Tech-G09 завершил воспроизводимый agent-first lifecycle с durable Approved Plan, Amendments и обязательной проверкой PR workflow. Tech-G10 нормализовал HTTP boundary API-контроллеров без изменения публичных контрактов. Tech-G11 унифицировал загрузку и startup validation runtime-конфигурации `SnapshotFreshness` без изменения публичных API-контрактов. Tech-G12 усилил fail-fast проверку критичной authentication/persistence/security-конфигурации без изменения публичных и бизнес-контрактов. Tech-G13 унифицировал configuration contract migrations/design-time tooling и закрепил startup-only lifecycle служебных процессов без изменения runtime architecture и публичных контрактов. G-01 добавил самостоятельный React-клиент и BFF с OAuth/OIDC login, server-side browser session и full SSO logout за единым browser-facing origin. G-02 реализовал в Web управление read-only подключениями Bybit: список, добавление, переименование, проверку, замену ключей, отключение и восстановление. Следующий обязательный шаг — **G-03: сводка account-scoped портфеля и список позиций**. Подробный scope и дальнейшая последовательность определяются `ROADMAP.md`.

Этап F завершён последовательными небольшими изменениями: F-01 зафиксировал стабильные v1-контракты и стратегию миграции pre-v1 `api/exchange-accounts`; F-02 завершил первоначальный канонический lifecycle биржевого аккаунта (`/api/v1/exchange-accounts`) и удалил pre-v1 маршруты; G-02 позднее перенёс management-операции в `/api/v1/me/exchange-accounts`, сохранив account-scoped sync/portfolio routes; F-03 добавил позиции и account-scoped portfolio; F-04 добавил position-scoped market/candles; F-05 добавил evaluation workflow и read model; F-06 добавил position timeline с cursor pagination и type filtering; F-07 добавил user-scoped SignalR invalidation с REST recovery; F-08 завершил проверку полноты OpenAPI/contract tests и пригодности v1-контракта для будущей генерации клиента. OpenAPI/API tests продолжают обновляться в каждом PR, который добавляет или меняет публичный контракт.

Основная ближайшая последовательность:

1. G-03 — G-08: пользовательские сценарии основного React-клиента, начиная со сводки account-scoped портфеля и списка позиций;
2. непрерывное наблюдение за активными позициями и отказоустойчивость доставки application events;
3. Telegram-уведомления и детерминированные объяснения;
4. подготовка пилотной эксплуатации;
5. измерение качества рекомендаций;
6. переосмысление OpenClaw и расширенного ИИ-контура — после проверки первого MVP.

---

## Граница первого MVP

Первый MVP должен позволить пользователю:

- войти в систему;
- подключить и проверить Bybit-аккаунт только для чтения, при необходимости безопасно заменить credentials без потери идентичности подключения;
- увидеть актуальные открытые позиции и account-scoped состояние портфеля;
- фильтровать позиции по подключению и состоянию, не смешивая закрытую историю с активным рабочим списком;
- открыть позицию и увидеть её состояние, рыночный контекст и график;
- получить согласованный `evaluation`, включающий детерминированную оценку, временную/input identity расчёта и текущую рекомендацию, если она опубликована;
- получать realtime-уведомления об изменении состояния с восстановлением актуальных данных через REST без выдачи browser access token JavaScript-клиенту;
- получать важные уведомления;
- просматривать единый timeline существенных изменений позиции, оценок и рекомендаций; market-monitoring events добавляются после появления непрерывного наблюдения.

Автоматическое исполнение торговых операций остаётся за пределами первого MVP и может рассматриваться только после накопления статистики качества рекомендаций и отдельной проверки рисков. Общий cross-account portfolio также не входит в первый MVP: текущий `PortfolioState` относится к одному биржевому аккаунту.

---

## Текущие ограничения

- основной поддерживаемый источник рыночных данных — Bybit;
- основной внешний сценарий включает публичный рыночный анализ и read-only синхронизацию Bybit-аккаунтов;
- persistence доменного состояния, оценок, рекомендаций и stability state реализована; F-01 зафиксировал основу user-facing API v1, F-02 завершил канонический lifecycle API биржевых аккаунтов, F-03 добавил positions и account-scoped portfolio, F-04 добавил position-scoped market/candles, F-05 добавил evaluation workflow, F-06 добавил position timeline с cursor pagination и type filtering, а F-07 добавил user-scoped realtime invalidation;
- PostgreSQL schema, migrations и repository implementations поддерживают ручную/фоновую синхронизацию и recommendation workflow; торговое исполнение отсутствует, а пользовательские биржевые credentials первого MVP имеют только права чтения;
- канонический `/api/v1/me/exchange-accounts` публикует management read-only подключений; прежние management routes `GET`/`POST /api/v1/exchange-accounts`, `DELETE /api/v1/exchange-accounts/{id}`, `POST /api/v1/exchange-accounts/{id}/verify`, `PUT /api/v1/exchange-accounts/{id}/credentials` и pre-v1 `api/exchange-accounts` удалены и возвращают `404`, а `POST /api/v1/exchange-accounts/{id}/sync` и `GET /api/v1/exchange-accounts/{id}/portfolio` остаются;
- повторная оценка рекомендаций пока вызывается прикладным workflow, а непрерывный monitoring loop относится к этапу H;
- React-клиент содержит адаптивную оболочку, login/logout, состояние session и управление read-only подключениями Bybit; остальные пользовательские экраны относятся к последующим задачам этапа G;
- browser SignalR integration через BFF ещё не реализована и относится к G-06; access token не выдаётся browser JavaScript-коду;
- BFF хранит browser sessions в памяти процесса: restart BFF завершает sessions, а горизонтальное масштабирование без sticky sessions не поддерживается;
- общий cross-account portfolio, correlation model и расширенная portfolio analytics перенесены в последующее расширение продукта;
- BTC Daily Check остаётся отдельным экспериментальным публичным сценарием;
- качество рыночного анализа зависит от свежести и полноты данных.

---

## Disclaimer

Проект является экспериментальным программным инструментом для исследования и поддержки принятия решений на криптовалютном рынке.

Он не является финансовым советом и не гарантирует прибыльность торговых решений. Криптовалютные рынки крайне волатильны, а любые торговые решения пользователь принимает самостоятельно и на свой риск.
