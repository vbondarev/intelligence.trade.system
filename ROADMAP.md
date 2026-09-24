# Дорожная карта разработки Intelligence.TradeSystem

Версия документа: 3.34
Дата актуализации: 25 сентября 2026 года
Проверенная база: реализация Issue #152
Последняя учтённая задача: Issue #152 «Tech-G06. Нормализовать composition root и Program.cs executable-проектов»
Текущий этап: **G — основной React-клиент**
Статус документа: **основная и единственная актуальная дорожная карта проекта**

## 1. Цель продукта

Долгосрочная цель Intelligence.TradeSystem зафиксирована в [Product Vision](docs/product/vision.md): единая интеллектуальная торговая среда, которая видит рынок, знает портфель пользователя, сопровождает сделки, объясняет происходящее, учится на истории пользователя, помогает исследовать рынок и постепенно автоматизирует рутинные действия.

Текущий ROADMAP намеренно реализует первую основную продуктовую вертикаль — помощника по сопровождению уже открытых сделок — и создаёт безопасный фундамент для дальнейшего развития. Будущие продуктовые направления и уровень их зрелости фиксируются отдельно в [Capability Map](docs/product/capability-map.md) и не меняют последовательность ROADMAP без отдельного human decision.

В рамках текущей вертикали система должна:

- подключать биржевые аккаунты пользователей только для чтения;
- синхронизировать баланс, открытые позиции и состояние портфеля;
- оценивать каждую позицию с учётом рынка, риска и портфеля;
- формировать проверяемые рекомендации: удерживать, защитить прибыль, сократить, закрыть, перенести стоп или частично зафиксировать прибыль;
- объяснять причины рекомендаций понятным языком;
- показывать одинаковое состояние и одинаковые решения во всех клиентах;
- сохранять историю изменений позиции, оценок и рекомендаций.

## 2. Неизменные архитектурные решения

1. Backend — единый источник бизнес-истины для React, Telegram и будущих клиентов.
2. Биржа — внешний источник фактического состояния счёта и позиций.
3. React — основной адаптивный веб-интерфейс.
4. REST используется для начальной загрузки и команд, SignalR — для последующих обновлений.
5. Telegram используется для важных уведомлений, а не как основной интерфейс управления.
6. Внешний агентный контур, включая OpenClaw, получает только подготовленные данные через версионированные контракты backend и не обращается напрямую к Bybit или базе данных.
7. Детерминированное ядро формирует оценку и действие; ИИ только объясняет готовое решение.
8. Схема `llm-payload` версии `1.0` сохраняется для изолированного публичного сценария BTC Daily Check до отдельного решения о его миграции.
9. Автоматическое открытие, усреднение и закрытие сделок не входят в первый MVP.
10. `Intelligence.TradeSystem.Api` — client-agnostic resource server: один business API `/api/v1/*` используется Web, mobile, desktop, CLI и будущими клиентами.
11. Для защищённого API приняты OAuth 2.0 / OpenID Connect, Bearer access tokens и signed JWT как целевой формат access token первого MVP; browser cookie допускается только на BFF boundary.
12. Authorization Server реализован как отдельный ASP.NET Core Identity + OpenIddict host; `Api` использует JwtBearer discovery/JWKS. User isolation C-06 реализована на Application/Infrastructure boundary.
13. User-delegated `sub` сопоставляется со стабильным Domain `UserId`; machine principal не является Domain user.
14. Политика рекомендаций использует внешнюю версионируемую конфигурацию: пороги, лимиты, коэффициенты, временные параметры и другие настраиваемые значения загружаются через типизированный и валидируемый `PolicyDefinition`. Критические safety-инварианты остаются в C# и не могут быть отключены конфигурацией. Архитектура допускает последующий переход к более декларативным правилам и Rule Engine без переработки доменной оценки позиции.
15. Пользовательский API моделирует пользовательские сценарии, а не один в один внутренние доменные агрегаты. Текущая оценка и рекомендация позиции публикуются как единый согласованный `evaluation` read model; внутренние `PositionAssessment` и `Recommendation` сохраняют независимые доменные и persistence lifecycle.
16. `PortfolioState` первого MVP относится к одному биржевому аккаунту. Поэтому пользовательский portfolio API является account-scoped; общий cross-account `/api/v1/portfolio` не вводится до появления отдельной межаккаунтной portfolio analytics.
17. Синхронизация биржевого аккаунта и оценка позиции — разные операции. `sync` обновляет приватное состояние аккаунта, баланса и позиций из биржи; `evaluation` использует сохранённое состояние позиции/портфеля и актуальный публичный рыночный контекст для расчёта assessment и recommendation. Evaluation не скрывает stale/partial private state автоматическим sync и обязана сохранять safety semantics этапа E.
18. SignalR является каналом user-scoped уведомления об изменении состояния, а не вторым источником полной бизнес-истины. После события или восстановления соединения клиент перечитывает актуальный resource через REST. Нативные/token-клиенты предъявляют Bearer access token непосредственно API; browser-клиент не получает access token в JavaScript и подключается к realtime через browser-specific BFF integration поверх той же пользовательской identity.
19. Публичный market-analysis API и `llm-payload` 1.0 остаются отдельным публичным сценарием для рыночного анализа и BTC Daily Check. Legacy-совместимость относится прежде всего к `POST /api/market-analysis/snapshot`; новый React-клиент получает пользовательский рыночный контекст и свечи через position-scoped `/api/v1/*` endpoints и не зависит от market-analysis API.
20. Один `ExchangeAccountId` на всём lifecycle соответствует одному provider-side биржевому аккаунту. Provider identity является обязательным внутренним инвариантом account, для Bybit определяется по стабильному `userID`, сохраняется при connect и не меняется при verify/rotation; credentials другого provider account/subaccount отклоняются без перепривязки истории.

## 3. Обозначения статуса

- ✅ Завершено — критерии этапа реализованы в `develop` и защищены соответствующими тестами; это не означает автоматическую реализацию пользовательского интерфейса или runtime-trigger, если они явно отнесены к последующим этапам.
- 🚧 Текущий этап — следующий этап, на котором сосредоточена разработка.
- 🟡 Частично — есть архитектурная заготовка или часть сценария, но пользовательская возможность ещё не завершена.
- ⬜ Не начато — значимой реализации в проверенной ветке нет.
- 🔵 Вне первого MVP — осознанно отложено до проверки рекомендательного режима.

### Сводный прогресс

| Этап | Содержание | Статус |
|---|---|---|
| A | Архитектурный фундамент | ✅ Завершён |
| B | Бизнес-домен аккаунта, позиции, оценки и рекомендации | ✅ Завершён |
| C | Хранение, безопасность и пользователи | ✅ Завершён |
| D | Подключение Bybit и синхронизация | ✅ Завершён |
| E | Оценка позиции и рекомендации | ✅ Завершён |
| F | Пользовательский API и SignalR | ✅ Завершён |
| G | React-клиент | 🚧 Текущий этап |
| H | Непрерывное наблюдение | ⬜ Не начат |
| I | Telegram-уведомления и объяснения | 🟡 Есть отдельный BTC Daily Check |
| J | Проверка качества рекомендаций | ⬜ Не начат |
| K | Переосмысление OpenClaw и расширенный ИИ-анализ | 🔵 Вне первого MVP |
| L | Эксплуатационная готовность | 🟡 Есть базовые OpenTelemetry, CI quality gates и адреса проверки состояния |
| M | Расширение продукта | 🔵 Вне первого MVP |
| N | Контролируемое исполнение | 🔵 Вне первого MVP |

### Техническая подготовка перед этапом D

- **Tech-01** ✅ (PR #69): приватный exchange boundary использует явные result-контракты без exception-driven API.
- **Tech-02** ✅ (PR #71): единый `ProblemDetails` contract, центральный `IExceptionHandler` и безопасное mapping exception → HTTP.
- **Tech-03** ✅ (PR #73): структурированное логирование, прикладная телеметрия и контролируемая устойчивость внешних вызовов.

### Техническая подготовка перед этапом E

- **Tech-E01** ✅ (PR #91): нормализованы общие и backend-инструкции для coding agents; `openclaw/**` зафиксирован как отдельная замороженная область до этапа K.
- **Tech-E02** ✅ (PR #91): внешний набор Agent Skills сокращён до конкретных project-scoped skills, перенесён в общий для Codex и Copilot каталог `.agents/skills` и снабжён фиксированным происхождением upstream-копий.
- **Tech-E03** ✅ (PR #91): project-owned XML-документация C# переведена на русский язык без изменения поведения кода и машинных контрактов.
- **Tech-E04** ✅ (PR #91): ROADMAP и связанная проектная документация сверены с фактическим состоянием после Stage D; устранены устаревшие формулировки перед E-01.

### Техническая подготовка перед этапом F

- **Tech-F01** ✅ (Issue #104 / PR #105): `RecommendationService` больше не допускает fail-late конфигурацию persistence; обязательные repository/transaction dependencies задаются через конструктор, а сервис регистрируется только вместе с persistence.
- **Tech-F02** ✅ (PR #105): .NET SDK зафиксирован через `global.json`, Docker SDK/runtime images приведены к фиксированным версиям.
- **Tech-F03** ✅ (PR #105): CI запускается на pull request и push в `develop`/`main`, добавлены aggregate line coverage gate с порогом 92% и проверка direct/transitive NuGet vulnerabilities.
- **Tech-F04** ✅ (PR #105): coverage aggregator различает production assembly/source file/line, дедуплицирует одну production source line между несколькими test projects и защищён regression tests.

### Техническая подготовка перед этапом G

- **Tech-G01** ✅ (Issue #142): coverage quality gate учитывает только hand-written production code, исключает build-generated и EF migration artifacts, показывает breakdown по production assemblies; публичный Bybit adapter усилен contract tests для mapping и provider boundary.
- **Tech-G02** ✅ (Issue #144): API имеет единый DB-backed runtime; обязательная persistence configuration приводит к startup failure при ошибке, PostgreSQL outage отражается через readiness, а `/alive` сохраняет liveness-семантику.
- **Tech-G03** ✅ (Issue #146): PostgreSQL integration tests сохраняют реальное Testcontainers coverage, но переносят обычную миграцию во fixture lifecycle, разделяют обычные сценарии на независимые группы A/B и держат migration-specific проверки на свежих per-scenario БД; по аудируемому benchmark median suite time снижен с 50.52s до 32.58s (35.5% improvement) без сокращения coverage.
- **Tech-G04** ✅ (Issue #148): `Authentication.IntegrationTests` используют один PostgreSQL Testcontainer с отдельными `TradeSystem` и `TradeSystemIdentity` databases; migrations, hosts, certificates и базовый Identity/OpenIddict seed выполняются на fixture lifecycle, а mutable state явно очищается между сценариями. Сохранены реальные OAuth/OIDC, JwtBearer, PostgreSQL и SignalR проверки. В сопоставимом Release/no-build benchmark с одной warm-up итерацией и пятью успешными измерениями все прогоны дали 32/32: before median — 266.81s (267.39 / 266.25 / 266.65 / 266.81 / 266.94), after median — 28.30s (28.17 / 28.29 / 28.30 / 28.43 / 28.52), improvement — 89.4%. Инициализация `AuthenticationIntegrationFixture` — 9.16s; самый медленный оставшийся сценарий `Updates_hub_closes_an_authenticated_websocket_when_the_token_expires` — 9.02s.
- **Tech-G05** ✅ (Issue #150): production EF Core read path списка позиций подтверждён opt-in PostgreSQL benchmark на `postgres:16-alpine` с deterministic seed из 250 000 positions, 8 users и 24 accounts. Финальный путь сохраняет single-query first page/explicit-account и использует bounded continuation: lookup owned accounts, один bounded query для одного account и bounded per-account fan-out для нескольких accounts с deterministic merge. Cursor predicate разделён на взаимоисключающие timestamp/UUID ranges, поэтому PostgreSQL использует seek conditions в deep continuation plans; Closed на 25/50/75/95% показал 90/92/90/92 removed rows. После исправления parser root `Plan` counters доступны: AFTER matrix зафиксировала `read=0` и фактические shared-hit counters для каждого query/depth. В AFTER-прогоне 3-account continuation выполнял 4 round trips и возвращал 153 bounded candidates; 12-account engineering probe — 13 round trips и 607 candidates. Mandatory first-page scenarios оставались representative (94–28 029 candidates), а `Rows Removed by Filter` агрегируется с учётом `Actual Loops`; total buffers не суммируются по дереву. Финальный набор indexes: `ix_positions_list_order`, `ix_positions_list_account_order`, partial `ix_positions_list_closed_order` по `(exchange_account_id, first_detected_at DESC, position_id DESC)` и SQL-managed `lower(instrument_id)` index. Benchmark не входит в обычный CI test loop и включается только через `ITS_RUN_POSITION_LIST_PERFORMANCE=1`.
- **Tech-G06** ✅ (Issue #152): нормализованы API composition root и concern-based host configuration без изменения runtime-поведения; authentication, realtime, error handling и serialization перенесены в focused registrations, `Authentication.TestSeeder` разделён на composition и one-shot operation с idempotency/password-mismatch smoke coverage. `Identity` и `Identity.Migrations` оставлены без искусственного structural refactor.

## 4. Подтверждённое состояние проекта

### Уже реализовано

- ✅ Получение публичных рыночных данных Bybit.
- ✅ Расчёт EMA, RSI, ATR, SMA, упрощённого профиля объёма и классификации тренда.
- ✅ Анализ четырёх интервалов: 15 минут, 1 час, 4 часа и 1 день.
- ✅ Обработка стакана, потока сделок, ставки финансирования, открытого интереса и соотношения лонгов/шортов.
- ✅ Детерминированные `entryQuality`, `riskFlags`, рыночные теги, диагностика индикаторов и проверка свежести данных.
- ✅ Публичный `GET /api/market-analysis/{symbol}/llm-payload` со схемой `1.0`.
- ✅ Устаревший, но сохраняемый ради совместимости `POST /api/market-analysis/snapshot`.
- ✅ Изолированная OpenClaw-цепочка технического анализа и публикации публичного BTC-обзора в Telegram.
- ✅ Отдельный проект `Intelligence.TradeSystem.MarketIntelligence` с вычислениями и снимками рынка.
- ✅ Рыночный `MarketSnapshot` отделён от пользовательского `PortfolioSnapshot`.
- ✅ Публичный путь построения `llm-payload` не вызывает приватные методы Bybit.
- ✅ Добавлены интерфейсы возможностей биржи: `IMarketDataProvider`, `IDerivativesDataProvider`, `IPrivateAccountProvider`.
- ✅ Введены типизированные идентификаторы пользователя, биржевого аккаунта, позиции и инструмента.
- ✅ Реализованы `ExchangeAccount`, `Position` и устойчивая биржевая идентичность позиции с учётом `positionIdx`.
- ✅ Реализованы существенные изменения `New`, `Updated`, `Increased`, `Reduced`, `Closed`, `MarkedUnknown`, `MarkedStale` и `Recovered`, состояния отслеживания `Active`, `Unknown`, `Stale` и `Closed`, а также безопасная сверка снимков.
- ✅ Существенные изменения позиции фиксируются неизменяемыми записями `PositionChange`.
- ✅ Реализованы `PortfolioState`, агрегирование портфеля и базовая политика увеличения риска.
- ✅ Реализованы неизменяемый `PositionAssessment`, жизненный цикл `Recommendation` и раздельные словари `PositionAction`, `AddDecision`, `RiskIncreaseDecision` и `ReasonCode`.
- ✅ Реализованы единый воспроизводимый вход оценки позиции и `PositionAssessmentService`; направление позиции, market/portfolio context, fresh/partial/uncertain data и configuration identity учитываются явно.
- ✅ Реализована внешняя строгая JSON-конфигурация `PolicyDefinition` с canonical SHA-256 identity и валидацией.
- ✅ Реализована чистая детерминированная `RecommendationPolicy` со всеми семью `PositionAction`, независимым `AddDecision`, typed reasons, confidence/priority и консервативным maximum additional size.
- ✅ Реализованы typed invalidation/reevaluation conditions, `ValidUntil`, policy identity и persistence continuation metadata.
- ✅ Реализована `RecommendationStabilityPolicy`: semantic comparison, cooldown, hysteresis, pending confirmation и safety bypass.
- ✅ В PR #102 реализованы применение stability policy в `RecommendationService`, persisted baseline-bound pending state, PostgreSQL state repository, CAS/retry, user isolation и атомарная публикация/замена current recommendation.
- ✅ Safety guards запрещают повышение риска при stale, partial, uncertain и degraded данных независимо от внешней конфигурации.
- ✅ Сценарные тесты Stage E покрывают long/short, trend/flat, RSI, low volume/quality, liquidation, stop/breakeven и concentration в рамках текущей portfolio risk model.
- ✅ Добавлены архитектурные, доменные, прикладные, API-, модульные и интеграционные тесты.
- ✅ Базовая обвязка OpenTelemetry и проверки состояния сервиса присутствует в `ServiceDefaults`.
- ✅ Создан `Infrastructure` с EF Core `DbContext`, PostgreSQL provider, migrations, repository implementations и Testcontainers integration tests; Application repository ports подключены к сценариям подключения, синхронизации и recommendation workflow.
- ✅ Публичные и приватные возможности Bybit разделены; public client не использует пользовательские credentials, а private provider создаётся для конкретных credentials.
- ✅ Реализована основа OAuth/OIDC-аутентификации: отдельный Identity host, Identity/OpenIddict persistence, Authorization Code + PKCE (S256), signed non-encrypted JWT, discovery/JWKS и JwtBearer resource server.
- ✅ Реализована изоляция C-06: user-delegated principal явно маркируется, `sub` преобразуется в Domain `UserId`, user-owned repository operations требуют явный scope, а cross-user reads/writes проверены на PostgreSQL и через реальный Bearer E2E.
- ✅ PostgreSQL schema и migrations реализованы; постоянное хранение доменного состояния доступно через Application repository ports.
- ✅ Реализовано безопасное хранение API credentials Bybit в authenticated encrypted form; user-scoped store поддерживает CAS rotate/revoke и master-key reprotection, без secrets в БД, логах и ответах.
- ✅ Реализованы подключение Bybit-аккаунта только для чтения, ручная и фоновая синхронизация баланса, открытых позиций и `PortfolioState`.
- ✅ F-02 публикует канонический `/api/v1/exchange-accounts`: список подключений, connect, verify, безопасную ротацию credentials, sync и disconnect; pre-v1 routes удалены.
- ✅ F-03 публикует `/api/v1/positions` с SQL-side cursor pagination, фильтрами `exchangeAccountId`/`trackingState`/`symbol`/`side`, стабильным порядком и opaque versioned cursor.
- ✅ F-03 публикует `/api/v1/positions/{id}` как user-scoped current-state карточку без `PositionChanges`, timeline, evaluation и market context, а `/api/v1/exchange-accounts/{id}/portfolio` — account-scoped summary без встроенного списка позиций.
- ✅ F-04 публикует user-scoped `/api/v1/positions/{id}/market` и `/api/v1/positions/{id}/candles`, получая market identity из позиции и не смешивая пользовательское состояние с public market-analysis API.
- ✅ F-05 публикует user-scoped GET/POST evaluation с согласованными assessment + nullable current recommendation, temporal/input identity и сохранением safety semantics stale/partial/uncertain данных.
- ✅ F-06 публикует user-scoped timeline позиции с persisted position changes, assessments/evaluations и recommendations, cursor pagination и repeatable type filter.
- ✅ F-07 публикует user-scoped SignalR boundary `/hubs/v1/updates` с invalidation-only событиями `exchangeAccount.updated`, `portfolio.updated`, `position.updated` и `evaluation.updated`; native/token clients используют Bearer, browser integration остаётся за BFF этапа G, а актуальное состояние после события или reconnect восстанавливается через REST.
- ✅ `ExchangeAccount` хранит обязательную provider-side identity (для Bybit — `userID`); CAS/persistence запрещают её перепривязку к существующему `ExchangeAccountId`, а credentials другого account/subaccount отклоняются как controlled conflict.
- ✅ Синхронизация защищена независимыми watermark для баланса и позиций, CAS/retry на persistence boundary и идемпотентной обработкой повторных и устаревших наблюдений без повторного provider IO.
- ✅ Реализован PostgreSQL transactional outbox для versioned application events и SignalR invalidation: at-least-once dispatcher, idempotency consumers по EventId и causal ordering по PositionId + PositionChangeSequence; dispatcher включён по умолчанию после регистрации handlers для всех persisted event types.
- ✅ Реализован общий process-local кэш публичного `MarketSnapshot` с коротким TTL и per-key single-flight; ключ содержит только `ExchangeId`, нормализованный `Symbol` и `MarketCategory`, без пользовательских и приватных измерений.
- ✅ В PR #105 устранена частично сконфигурированная DI-модель `RecommendationService`, зафиксирован SDK и усилен CI quality gate перед этапом F.

### Есть только как заготовка

- 🟡 Legacy-типы `OpenPosition`, `OpenPositionSnapshot`, `PortfolioSnapshot` и их сборщик сохраняются для совместимости текущих путей, но не заменяют новый домен `Position` и `PortfolioState`.
- 🟡 Наблюдаемость имеет общий технический фундамент и телеметрию синхронизации, но непрерывное наблюдение за позициями и пользовательские уведомления относятся к этапам H–I.

### Пока отсутствует

- ⬜ React-клиент и BFF.
- ⬜ Непрерывный цикл повторной оценки активных позиций.
- ⬜ Уведомления о риске конкретной позиции.
- ⬜ Расширенная portfolio analytics и correlation model; это развитие перенесено в этап M и не является критерием завершения Stage E.

## 5. Дорожная карта

### Этап A. Завершить архитектурный фундамент

Статус этапа: ✅ Завершён.

Цель: довести начатое в PR #28 разделение до устойчивой целевой границы, не меняя поведение рыночного анализа.

| Код | Задача | Статус | Критерий завершения |
|---|---|---|---|
| A-01 | Зафиксировать контракты `llm-payload` 1.0 и legacy snapshot | ✅ | Контрактные API-тесты проходят |
| A-02 | Выделить `MarketIntelligence` и перенести вычислительную логику | ✅ | Модуль не зависит от API, Bybit.Net и инфраструктуры |
| A-03 | Отделить публичный `MarketSnapshot` от портфеля | ✅ | Публичный путь не вызывает `IPrivateAccountProvider` |
| A-04 | Ввести нейтральные интерфейсы возможностей биржи | ✅ | Прикладной слой зависит от интерфейсов, а не от Bybit.Net |
| A-05 | Разделить адаптер Bybit на Public, Private, Mapping и ClientFactory | ✅ | Публичный клиент не содержит пользовательских ключей; приватный создаётся для конкретного аккаунта перед реализацией синхронизации |
| A-06 | Перенести интерфейсы из общего `Abstractions` в прикладные функциональные области | ✅ | Прикладные порты находятся рядом со своими сценариями |
| A-07 | Удалить временный `IBybitProvider` после перевода потребителей | ✅ | В solution нет зависимостей от интерфейса совместимости |
| A-08 | Актуализировать README под новое видение продукта | ✅ | README различает текущие возможности и целевой продукт |

Архитектурный фундамент завершён в PR #28 и #34. Этапы B–E также завершены; текущий этап — F: пользовательский REST API и SignalR.

### Этап B. Создать бизнес-домен сопровождения позиций

Статус этапа: ✅ Завершён.

| Код | Задача | Статус | Критерий завершения |
|---|---|---|---|
| B-01 | Ввести типизированные идентификаторы пользователя, аккаунта, позиции и инструмента | ✅ | Идентификаторы нельзя случайно смешать между сущностями |
| B-02 | Создать `ExchangeAccount` (рабочее название в старом плане — `TradingAccount`) | ✅ | Есть системный ID, биржа, статус подключения, возможности, время синхронизации и последняя ошибка; секреты не входят в доменную сущность |
| B-03 | Создать `Position` и устойчивую идентичность позиции | ✅ | Учтены аккаунт, символ, сторона и Bybit `positionIdx`; хранятся первое обнаружение и последнее обновление |
| B-04 | Реализовать переходы позиции | ✅ | Проверены события New, Updated, Increased, Reduced, Closed, MarkedUnknown, MarkedStale и Recovered, а также состояния Active, Unknown, Stale и Closed |
| B-05 | Создать историю существенных изменений позиции | ✅ | Хранятся размер, средняя цена, mark price, PnL, ликвидация, причина и время изменения |
| B-06 | Создать `PortfolioState` и базовую политику риска | ✅ | Считаются экспозиция, концентрация, PnL, используемый и свободный капитал, свежесть |
| B-07 | Создать неизменяемый `PositionAssessment` | ✅ | Хранятся входные версии, причины, версия правил и срок действия |
| B-08 | Создать жизненный цикл `Recommendation` | ✅ | Поддержаны Active, Acknowledged, Dismissed, Superseded и Expired |
| B-09 | Зафиксировать действия, решение по увеличению позиции и коды причин | ✅ | Действие по позиции отделено от разрешения или запрета увеличения риска |

Результат этапа: в PR #38, #40, #42, #44 и #46 создан и покрыт тестами чистый домен без EF Core, ASP.NET Core, Bybit.Net и LLM SDK.

### Этап C. Добавить хранение, безопасность и пользователей

Статус этапа: ✅ Завершён.

| Код | Задача | Статус | Критерий завершения |
|---|---|---|---|
| C-01 | Создать проект `Infrastructure` | ✅ | Зависимости соответствуют архитектурным правилам |
| C-02 | Подключить PostgreSQL и миграции | ✅ | Чистая PostgreSQL база разворачивается первой содержательной migration; design-time factory поддерживает list/update |
| C-03 | Сохранять аккаунты, позиции, версии, портфели, оценки и рекомендации | ✅ | Состояние и история восстанавливаются после перезапуска через Application repository ports |
| C-04 | Добавить оптимистическую конкурентность | ✅ | Compare-and-swap через версии для ExchangeAccount/Position/Recommendation, без retry на обычном repository save; покрыто PostgreSQL-тестами |
| C-05 | Принять ADR по универсальной стратегии аутентификации | ✅ | Принят client-agnostic contract: OAuth 2.0/OpenID Connect, Bearer access tokens и signed JWT для защищённого API; browser cookie допускается только на BFF boundary |
| C-05A | Реализовать основу OAuth/OIDC-аутентификации универсального API | ✅ | Реализованы explicit migration lifecycle, отдельные Identity/OpenIddict persistence и deployable host, Authorization Code + PKCE (S256), signed short-lived non-encrypted JWT, public issuer/internal backchannel для discovery/JWKS, discovery scopes, lockout, overlapping signing keys, JwtBearer validation, Compose-level protected Bearer smoke и PostgreSQL integration tests; stable user-delegated `sub` → Domain `UserId`, public endpoints anonymous. См. ADR-0003 |
| C-06 | Реализовать разграничение данных по `UserId` | ✅ | User-delegated `sub` сопоставляется с Domain `UserId`; user-owned операции изолированы по владельцу и подтверждены PostgreSQL и Bearer E2E-тестами |
| C-07 | Реализовать шифрование, отзыв и ротацию ключей Bybit | ✅ | Ключи не хранятся открыто и не попадают в ответы и логи; user-scoped store поддерживает CAS rotate/revoke и master-key reprotection |
| C-08 | Добавить интеграционные тесты с PostgreSQL | ✅ | Проверены migrations, relational constraints, persistence round-trip, concurrency, user isolation, OAuth/OIDC и credential security на реальном PostgreSQL через Testcontainers |

### Этап D. Подключить аккаунт Bybit только для чтения и синхронизацию

Статус этапа: ✅ Завершён.

| Код | Задача | Статус | Критерий завершения |
|---|---|---|---|
| D-01 | Подключить, проверить и отключить аккаунт | ✅ | При подключении подтверждаются права только на чтение |
| D-02 | Синхронизировать баланс и открытые позиции | ✅ | Баланс, открытые позиции и PortfolioState синхронизируются и сохраняются |
| D-03 | Реализовать согласование состояния с биржей | ✅ | Partial/Failed observations не закрывают позиции; неподтверждённые данные переходят в Unknown/Stale; последний известный balance сохраняется; успешное наблюдение восстанавливает Connected/Active state |
| D-04 | Обеспечить идемпотентность и защиту от ответов не по порядку | ✅ | Повторный запуск не создаёт дубликаты и не откатывает новую версию |
| D-05 | Добавить фоновую синхронизацию активных аккаунтов | ✅ | Известны задержка задания и время последней успешной синхронизации |
| D-06 | Публиковать события открытия, изменения, закрытия и ошибки синхронизации | ✅ | Последующие подсистемы получают устойчивые прикладные события |
| D-07 | Добавить общий кэш публичного рыночного снимка | ✅ | Один process-local снимок используется несколькими пользователями без смешивания приватных данных; cache key содержит только ExchangeId, Symbol и MarketCategory, а single-flight устраняет дублирующие сборки |

Результат этапа: пользователь подключает Bybit с правами только на чтение; состояние баланса и позиций сохраняется, восстанавливается после перезапуска и монотонно сходится при ручных, фоновых и конкурентных sync. Публичный `MarketSnapshot` кэшируется в процессе с коротким TTL и single-flight, без пользовательских или приватных измерений.

### Этап E. Реализовать детерминированное сопровождение позиции

Статус этапа: ✅ Завершён.

Граница этапа: этап E завершается на детерминированной оценке позиции, формировании рекомендации, её стабилизации и application/persistence workflow публикации. Пользовательский REST/SignalR-доступ к этим возможностям относится к этапу F, а непрерывный автоматический запуск и повторная оценка активных позиций — к этапу H.

Рекомендация состоит из двух независимых решений:

1. `RecommendedAction` — что делать с уже открытой позицией: `Hold`, `Watch`, `ProtectProfit`, `Reduce`, `Close`, `MoveStop` или `TakePartialProfit`.
2. `AddDecision` — допустимо ли увеличивать риск: `NotEvaluated`, `DoNotAdd` или `AddAllowed`.

Такое разделение не допускает двусмысленности вроде «удерживать позицию» и одновременно неявно разрешать её усреднение. `AddAllowed` проходит отдельные hard guards портфельного риска, расстояния до ликвидации, слома исходного сценария и ограничивается рассчитанным maximum additional size.

Политика рекомендаций отделена от вычисления оценки позиции. Внешняя конфигурация хранит пороги, лимиты, коэффициенты и временные параметры в строгом JSON и загружается в типизированный `PolicyDefinition` с обязательной валидацией. Критические safety-инварианты, включая запрет повышения риска при stale/partial/uncertain данных, остаются в C# и не могут быть отключены внешней конфигурацией. Каждая использованная политика имеет стабильную version/hash identity, поэтому рекомендация воспроизводима по тем же входным данным. Полноценный собственный DSL не является обязательным результатом первого MVP.

| Код | Задача | Статус | Критерий завершения |
|---|---|---|---|
| E-01 | Собрать единый вход оценки: позиция, рынок, портфель, политика риска, свежесть и идентичность конфигурации политики | ✅ | Входные данные версионируются и воспроизводимы; зафиксированы версия и hash применяемого `PolicyDefinition` |
| E-02 | Реализовать `PositionAssessmentService` | ✅ | Для одной позиции создаётся объяснимая неизменяемая оценка |
| E-03 | Оценивать тренд, моментум, уровни, PnL, волатильность, стоп, безубыток и ликвидацию | ✅ | Все вычисленные признаки имеют коды причин и тесты; направление позиции учитывается при интерпретации рынка |
| E-04 | Реализовать версионируемую `RecommendationPolicy` с внешним `PolicyDefinition` | ✅ | Параметры политики загружаются из строгой JSON-конфигурации; identity вычисляется как canonical SHA-256; одинаковый assessment, policy и `asOf` дают одинаковый результат |
| E-05 | Добавить `RecommendedAction` | ✅ | Поддержаны Hold, Watch, ProtectProfit, Reduce, Close, MoveStop и TakePartialProfit; каждое действие имеет typed reasons, confidence, priority и ограниченный assessment validity срок |
| E-06 | Добавить `AddDecision` | ✅ | `DoNotAdd` объясняет запрет; `AddAllowed` проходит hard guards, фиксирует conditions и рассчитывает консервативный maximum additional position value/quantity |
| E-07 | Добавить условия отмены и следующей проверки | ✅ | PR #98: рекомендация содержит typed invalidation/reevaluation conditions, valid-until, policy identity и PostgreSQL continuation metadata |
| E-08 | Защититься от дребезга рекомендаций | ✅ | E-08.1 PR #100: чистая доменная anti-chatter/stability policy; E-08.2 PR #102: application orchestration, PostgreSQL pending state, CAS/retry, user isolation и атомарная публикация/замена recommendation |
| E-09 | Запретить повышение риска при устаревших, неполных или неопределённых данных | ✅ | Hard guard выполняется до configurable rules: legacy/degraded data всегда дают Watch + DoNotAdd; внешняя policy не может его отключить |
| E-10 | Добавить сценарные тесты long/short и пограничных рисков | ✅ | Покрыты trend, flat, RSI, low volume/quality, liquidation, stop/breakeven, concentration и long/short в рамках текущей portfolio risk model; correlation model перенесена в этап M как расширение portfolio analytics |

Результат этапа: система детерминированно оценивает позицию и формирует устойчивую рекомендацию без зависимости от ИИ и без исполнения сделок; одинаковые входные данные и зафиксированная версия внешней политики воспроизводят одно и то же решение, а критические ограничения безопасности остаются частью кода. Persisted anti-chatter state предотвращает ненужную смену рекомендаций, а publication/replacement current recommendation выполняются атомарно и user-scoped.

### Этап F. Создать пользовательский API и обновления в реальном времени

Статус этапа: ✅ Завершён.

Цель: опубликовать уже реализованные возможности backend через стабильный client-agnostic API, ориентированный на реальные пользовательские сценарии, а не на прямое отображение внутренних доменных агрегатов.

Граница этапа:

- F не реализует React/BFF — это этап G;
- F не запускает непрерывную переоценку позиций — это этап H;
- F не вводит Telegram UX для acknowledge/dismiss — это этап I;
- F не создаёт cross-account portfolio analytics — это последующее расширение этапа M;
- публичный market-analysis API сохраняется независимо и не становится контрактом React-клиента; `POST /api/market-analysis/snapshot` остаётся legacy endpoint;
- `/api/v1/exchange-accounts` является каноническим lifecycle-контрактом read-only биржевых аккаунтов; временный pre-v1 `api/exchange-accounts` удалён в F-02, compatibility alias отсутствует;
- F реализует SignalR boundary самого resource server, но browser transport остаётся частью BFF-интеграции этапа G: access token не передаётся в browser JavaScript.

Минимальный API:

```text
# Биржевые аккаунты
POST   /api/v1/exchange-accounts
GET    /api/v1/exchange-accounts
POST   /api/v1/exchange-accounts/{id}/verify
PUT    /api/v1/exchange-accounts/{id}/credentials
POST   /api/v1/exchange-accounts/{id}/sync
DELETE /api/v1/exchange-accounts/{id}
GET    /api/v1/exchange-accounts/{id}/portfolio

# Позиции
GET    /api/v1/positions
GET    /api/v1/positions/{id}
GET    /api/v1/positions/{id}/market
GET    /api/v1/positions/{id}/candles
GET    /api/v1/positions/{id}/evaluation
POST   /api/v1/positions/{id}/evaluation
GET    /api/v1/positions/{id}/timeline

# Поддерживающий endpoint аутентифицированного пользователя — уже существует
GET    /api/v1/auth/me

# Realtime resource-server boundary
/hubs/v1/updates
```

Семантика ключевых ресурсов:

1. `exchange-accounts/{id}/sync` синхронизирует приватное состояние биржевого аккаунта: баланс, позиции и account-scoped `PortfolioState`. Он не запускает скрытую переоценку всех позиций.
2. `positions/{id}/evaluation` объединяет текущий `PositionAssessment` и связанную текущую `Recommendation` в один согласованный пользовательский read model. `POST` явно запускает переоценку позиции, но не подменяет stale/partial private state автоматическим account sync. Read model обязан явно показывать временную и версионную идентичность расчёта: как минимум `evaluatedAt`, `validUntil`, версии/идентичность входной позиции, portfolio state и market snapshot либо эквивалентные стабильные поля, позволяющие отличить старый evaluation от свежего `/market`. `recommendation` допускается `null`, если для assessment нет текущей опубликованной recommendation.
3. `exchange-accounts/{id}/portfolio` отражает текущий `PortfolioState` одного биржевого аккаунта. Отдельный `/portfolio/risk` не нужен: портфельные метрики входят в portfolio response, а решение о допустимости увеличения риска конкретной позиции входит в evaluation.
4. `GET /api/v1/positions` является user-scoped постраничным списком. Для первого MVP контракт должен поддерживать как минимум фильтры `exchangeAccountId`, `trackingState`, `symbol` и `side`. По умолчанию выдаются актуальные позиции, требующие сопровождения (`Active`, `Unknown`, `Stale`); закрытые позиции не смешиваются с основным рабочим списком и доступны только при явном фильтре/историческом сценарии.
5. `positions/{id}/market` возвращает актуальный пользовательский market context для карточки позиции; `positions/{id}/candles` возвращает свечи для графика. React не должен использовать public market-analysis API как основной UI contract.
6. `positions/{id}/timeline` на этапах F–G объединяет доступную пользовательскую историю существенных изменений позиции, assessments/evaluations и recommendations и поддерживает cursor pagination и фильтры. Рыночные события сопровождения, которых ещё нет до H-04, не являются критерием готовности F/G; после реализации H-04 timeline может быть аддитивно расширен такими событиями.
7. `PUT /exchange-accounts/{id}/credentials` проверяет новую read-only пару credentials и ротирует её только для того же provider-side аккаунта: стабильные `ExchangeAccountId` и provider identity не меняются; credentials другого account/subaccount отклоняются без mutation persisted credentials/account state.
8. SignalR отправляет только user-scoped сообщения об изменении/инвалидации состояния (`exchangeAccount.updated`, `portfolio.updated`, `position.updated`, `evaluation.updated` или эквивалентные версионированные контракты). Полное актуальное состояние клиент получает через REST. Native/token clients подключаются к hub с Bearer access token; browser использует browser-specific BFF integration, определяемую в G-01/G-06, и не получает access token в JavaScript.
9. Публичные операции `acknowledge` и `dismiss` не входят в F до определения их пользовательской семантики. В частности, `dismiss` нельзя публиковать как API-команду до решения, должно ли отклонение скрывать рекомендацию, приостанавливать её или действовать до существенного изменения состояния.
10. OpenAPI и контрактные тесты сопровождают API инкрементально: каждый PR F-02 — F-07, который добавляет или меняет публичный v1/realtime contract, обязан обновить соответствующие OpenAPI/API tests. SignalR wire contract проверяется отдельно от OpenAPI: имена client-facing событий и сериализованные payload schemas должны быть зафиксированы serialization/approval tests. F-08 является финальной фиксацией полноты, стабильности и пригодности контракта для последующей генерации React client/types, а не первым моментом документирования API.

| Код | Задача | Статус | Критерий завершения |
|---|---|---|---|
| F-01 | Зафиксировать структуру `/api/v1`, миграцию pre-v1 routes и стабильные пользовательские контракты | ✅ | Зафиксированы v1 DTO/read models, JSON/ProblemDetails/cursor conventions, route-scoped serialization и OpenAPI enum contract; pre-v1 `api/exchange-accounts` временно сохранён без v1 alias; public market-analysis boundary не изменена; решения покрыты API/HTTP contract tests |
| F-02 | Реализовать API жизненного цикла биржевого аккаунта | ✅ | Пользователь может получить список подключений, подключить read-only аккаунт с обязательной provider identity, повторно проверить credentials/permissions, безопасно ротировать credentials только в пределах того же provider-side аккаунта без смены `ExchangeAccountId`/identity, запустить sync и отключить аккаунт; OpenAPI/API tests обновлены вместе с контрактом |
| F-03 | Реализовать read API позиций и account-scoped портфеля | ✅ | Доступны постраничный список с фильтрами `exchangeAccountId`/`trackingState`/`symbol`/`side`, карточка позиции и `PortfolioState` конкретного exchange account; по умолчанию закрытые позиции не смешиваются с активным рабочим списком; cross-user доступ скрыт; общий cross-account `/portfolio` не имитируется без соответствующей доменной модели; OpenAPI/API tests обновлены |
| F-04 | Реализовать position-scoped market context и свечи | ✅ | Страница позиции получает рыночные показатели и candle series через `/api/v1`, не завися от public market-analysis API; backend определяет exchange/symbol/category из user-scoped позиции; OpenAPI/API tests обновлены |
| F-05 | Реализовать единый evaluation workflow и read model | ✅ | `GET evaluation` возвращает согласованные assessment + nullable current recommendation и явные `evaluatedAt`/`validUntil`/input version-or-identity metadata; `POST evaluation` запускает расчёт без неявного private sync и сохраняет safety semantics stale/partial/uncertain данных; OpenAPI/API tests обновлены |
| F-06 | Реализовать timeline позиции, cursor pagination и фильтры | ✅ | История позиции, assessments/evaluations и recommendation changes доступны единым пользовательским timeline без загрузки всей истории; market monitoring events не требуются до H-04; OpenAPI/API tests обновлены |
| F-07 | Реализовать SignalR и user-scoped группы/события инвалидации | ✅ | Пользователь не может подписаться на данные другого пользователя; native/token clients используют Bearer; browser token не раскрывается JavaScript и будущая browser-интеграция оставлена за BFF в G; после события или reconnect клиент может восстановить актуальное состояние через REST; имена client-facing событий и сериализованные payload contracts покрыты serialization/approval tests, а несовместимое изменение wire contract требует новой версии |
| F-08 | Финализировать OpenAPI и контрактные проверки пользовательского API | ✅ | OpenAPI полностью описывает auth, ProblemDetails, pagination, filters, enums и v1 endpoints; проверена согласованность REST и realtime контрактов F-02 — F-07 и пригодность для последующей генерации типов/клиента React |

Результат этапа: backend предоставляет стабильный пользовательский API для управления read-only биржевыми подключениями, чтения позиции и account-scoped портфеля, получения рынка/свечей, явного evaluation и timeline; SignalR безопасно сообщает об изменениях, а REST остаётся источником актуального состояния. Контракты сопровождаются OpenAPI/tests по мере появления, а browser authentication boundary не нарушает BFF-модель.

### Этап G. Создать основной React-клиент

Статус этапа: ⬜ Не начат в проверенном репозитории.

| Код | Задача | Статус | Критерий завершения |
|---|---|---|---|
| G-01 | Создать каркас адаптивного приложения, BFF и вход пользователя | ⬜ | Работают React shell, BFF/session integration с `Intelligence.TradeSystem.Identity`, OAuth/OIDC login flow, CSRF protection для cookie-based BFF session, защищённые маршруты и восстановление browser session; access token не попадает в browser JavaScript |
| G-02 | Реализовать управление подключением Bybit только для чтения | ⬜ | Пользователь может добавить, проверить, безопасно заменить credentials и отключить аккаунт |
| G-03 | Реализовать сводку account-scoped портфеля и список позиций | ⬜ | Видны PnL, риск, свежесть, состояние синхронизации, позиции под наблюдением и критические позиции выбранного подключения; список использует фильтры v1 API и не смешивает закрытую историю с активными позициями по умолчанию |
| G-04 | Реализовать страницу позиции | ⬜ | Видны параметры сделки, график, market context, ключевые уровни, evaluation, рекомендация, причины, временная валидность и условия пересмотра; UI способен отличить свежий market context от более старого evaluation |
| G-05 | Реализовать timeline позиции | ⬜ | Видны увеличение, уменьшение и закрытие позиции, assessments/evaluations и изменения рекомендации через единый постраничный timeline; рыночные события сопровождения добавляются после появления H-04 и не блокируют завершение G |
| G-06 | Подключить SignalR с восстановлением через REST через BFF-compatible browser integration | ⬜ | Browser подключается к realtime без выдачи access token в JavaScript; realtime-события инвалидируют соответствующее клиентское состояние; после разрыва соединения клиент перечитывает актуальные REST resources |
| G-07 | Добавить адаптивность и базовую доступность | ⬜ | Основные сценарии работают на телефоне и компьютере |
| G-08 | Зафиксировать базовую дизайн-систему и обработку ошибок | ⬜ | Одни и те же состояния риска, загрузки, stale и ошибок отображаются единообразно |

Результат этапов C, D, F и G: готова панель портфеля, работающая только с чтением биржевых данных.

### Этап H. Реализовать непрерывное наблюдение

Статус этапа: ⬜ Не начат.

Цель: перейти от анализа по запросу к постоянному сопровождению только активных позиций.

| Код | Задача | Статус | Критерий завершения |
|---|---|---|---|
| H-01 | Добавить фоновые задания повторной оценки | ⬜ | Для каждой активной позиции известно время последней и следующей проверки |
| H-02 | Определить частоту и условия повторной оценки | ⬜ | Частота учитывает риск, волатильность, свежесть и ограничения Bybit; полный анализ не запускается без необходимости |
| H-03 | Сравнивать новую оценку с предыдущей | ⬜ | Выделяются только существенные изменения, а не каждое движение цены |
| H-04 | Публиковать события сопровождения и расширить timeline соответствующими событиями | ⬜ | Поддержаны изменение рекомендации, тренда, уровня, OI/funding, портфельного риска и приближение к ликвидации; значимые market-monitoring events становятся доступны в position timeline без изменения базового F-06 контракта |
| H-05 | Публиковать события жизненного цикла позиции | ⬜ | Поддержаны увеличение, уменьшение и закрытие позиции |
| H-06 | Обеспечить идемпотентность и наблюдаемость заданий | ⬜ | Повтор задания не создаёт дубликаты; видны задержка, длительность и ошибки |

### Этап I. Добавить уведомления и объяснения

Статус этапа: 🟡 Публичный Telegram-обзор существует; уведомления о позициях не начаты.

Принцип: по умолчанию система молчит и уведомляет только при существенном изменении.

| Код | Задача | Статус | Критерий завершения |
|---|---|---|---|
| I-01 | Добавить надёжную очередь исходящих уведомлений | ⬜ | Событие не теряется при временной ошибке Telegram |
| I-02 | Отправлять только важные события позиции | ⬜ | Уведомляются Close, Reduce, слом сценария, рост риска, опасная ликвидация и критический stale |
| I-03 | Добавить дедупликацию, задержку объединения событий и паузу между уведомлениями | ⬜ | Повтор одного состояния не создаёт спам |
| I-04 | Добавить ссылку на позицию и пользовательские настройки | ⬜ | Пользователь выбирает классы уведомлений и может открыть соответствующую страницу в Web |
| I-05 | Создать генератор человекочитаемых объяснений | ⬜ | Объяснение строится только из оценки, рекомендации и кодов причин |
| I-06 | Сделать ИИ необязательным | ⬜ | При недоступном ИИ рекомендации продолжают формироваться и отображаться через детерминированный шаблон |
| I-07 | Определить пользовательскую семантику acknowledge/dismiss рекомендаций перед публикацией write API | ⬜ | Зафиксировано, как acknowledge/dismiss влияют на current recommendation, повторное появление одинакового решения, snooze/notification UX и историю; только после этого добавляются соответствующие API-команды |
| I-08 | Сохранить BTC Daily Check как отдельный публичный сценарий до решения о его миграции | ✅ | Существующая OpenClaw-цепочка совместима с `llm-payload` 1.0, не зависит от пользовательских позиций и не ограничивает развитие основного продукта |

Результат этапов H и I: система непрерывно сопровождает позиции, а важные изменения приходят в Telegram без повторного спама.

### Этап J. Измерять качество рекомендаций

Статус этапа: ⬜ Не начат; вне первого MVP, но обязателен до исполнения сделок.

| Код | Задача | Статус | Критерий завершения |
|---|---|---|---|
| J-01 | Сохранять рекомендацию, исходную оценку и идентичность политики | ⬜ | Любое решение воспроизводится по входным данным, версии и hash применённого `PolicyDefinition` |
| J-02 | Сохранять последующее движение рынка и позиции | ⬜ | Доступны фактические результаты после рекомендации |
| J-03 | Определить окна и правила оценки | ⬜ | Для каждого действия задан проверяемый критерий успеха или ошибки |
| J-04 | Рассчитывать качество по типам решений | ⬜ | Отдельно оцениваются Hold, Watch, Reduce, Close, защита прибыли, DoNotAdd и AddAllowed |
| J-05 | Проверять калибровку уверенности | ⬜ | Заявленная уверенность сравнивается с фактической точностью |
| J-06 | Сравнивать версии правил и моделей | ⬜ | Новую версию конфигурации политики, декларативных правил или модели можно проверить в теневом режиме и сопоставить с предыдущей |
| J-07 | Добавить отчёты качества | ⬜ | Видны выборка, период, версия правил, ошибки и ограничения интерпретации |

### Этап K. Переосмыслить OpenClaw и расширить ИИ-анализ

Статус этапа: 🔵 Вне первого MVP.

Принцип: OpenClaw является необязательным агентным контуром. Backend сохраняет окончательное детерминированное решение, правила риска и работоспособность продукта при полном отключении ИИ.

| Код | Задача | Статус | Критерий завершения |
|---|---|---|---|
| K-01 | Определить роль OpenClaw и границы агентного контура | ⬜ | Зафиксировано, какие задачи выполняют backend и OpenClaw, какие данные доступны агентам и какие решения им запрещено изменять |
| K-02 | Зафиксировать контракты агентных отчётов | ⬜ | Для каждого отчёта определены схема, свежесть, уверенность, версия и поведение при отсутствии данных |
| K-03 | Адаптировать технического агента к контексту позиции | ⬜ | Агент анализирует готовые данные позиции и рынка, не обращаясь к внутреннему состоянию напрямую |
| K-04 | Добавить агентов внешних источников | ⬜ | Новости, макроэкономика, социальные и при необходимости on-chain данные представлены отдельными формализованными отчётами |
| K-05 | Создать синтезатор позиции | ⬜ | Он сопоставляет конфликтующие сигналы, но не обходит ограничения риска и рекомендацию backend |
| K-06 | Сохранять входы, версии моделей, результаты и стоимость | ⬜ | Любой результат ИИ можно проверить, аудировать и сопоставить с использованными данными |
| K-07 | Ограничить частоту и стоимость ИИ-запросов | ⬜ | OpenClaw запускается только для существенных событий или по явному запросу пользователя |
| K-08 | Добавить контрактные и сквозные тесты агентного контура | ⬜ | Проверяется путь от данных позиции до объяснения без реальной публикации; отказ OpenClaw не нарушает работу основного продукта |

### Этап L. Подготовить систему к эксплуатации

Статус этапа: 🟡 Есть общий фундамент OpenTelemetry, адреса проверки состояния и базовые CI quality gates. Минимальная безопасность аккаунтов реализована раньше на этапе C; здесь завершается эксплуатационная готовность всей цепочки. Задачи L-01 — L-05 выполняются параллельно продуктовым этапам и обязательны до пилотного запуска с реальными пользователями.

| Код | Задача | Статус | Критерий завершения |
|---|---|---|---|
| L-01 | Добавить структурированные журналы с correlation/user/account/position ID | ⬜ | Полный сценарий можно проследить без утечки секретов |
| L-02 | Добавить трассировку и метрики синхронизации, наблюдения и рекомендаций | ⬜ | Видны задержка заданий, ошибки синхронизации и устаревшие данные |
| L-03 | Добавить проверки PostgreSQL, Bybit и фоновых заданий | ⬜ | Проверки отражают готовность зависимостей, а не только процесс API |
| L-04 | Добавить rate limiting, retry и circuit breaker | ⬜ | Ограничения и временные ошибки не создают лавину повторов |
| L-05 | Описать резервное копирование, восстановление и ротацию секретов | ⬜ | Процедуры проверены на тестовом окружении |
| L-06 | Выполнить нагрузочные и отказоустойчивые испытания | ⬜ | Проверены деградация Bybit, БД, SignalR, Telegram и ИИ |
| L-07 | Добавить эксплуатационные оповещения | ⬜ | Критические ошибки и рост задержки обнаруживаются без ручной проверки |
| L-08 | Удалить оставшиеся временные проекты и контракты | ⬜ | Нет неиспользуемых AI-контрактов |

### Этап M. Расширить продукт

Статус этапа: 🔵 Вне первого MVP.

Возможные направления после проверки основной ценности:

- Binance, OKX и другие биржи;
- spot и другие типы инструментов;
- общий cross-account portfolio read model;
- расширенная портфельная аналитика и корреляции;
- correlation model для cross-position/asset risk;
- поиск новых торговых возможностей;
- торговый журнал;
- пользовательские стратегии и профили риска;
- мобильное приложение;
- дополнительные каналы уведомлений.

### Этап N. Перейти к контролируемому исполнению сделок

Статус этапа: 🔵 Вне первого MVP. Этап запрещено начинать до получения достаточной статистики на этапе J.

Последовательность:

1. Журнал решений пользователя.
2. Имитационная торговля.
3. Испытания через Bybit testnet.
4. Независимый валидатор риска и аварийная остановка.
5. Полуавтоматическое исполнение только с явным подтверждением пользователя в Web или Telegram.
6. Ограниченные операции: сокращение, закрытие, изменение stop-loss/take-profit и только затем ограниченное добавление.
7. Полный аудит всех команд и результатов биржи.
8. Автоматическое исполнение рассматривать лишь после отдельного решения о качестве, ответственности и рисках.

## 6. Рекомендуемая очередь ближайших PR

| Очередь | Предлагаемый PR | Связанные задачи |
|---:|---|---|
| 1 | Создать адаптивную React-панель | G-01 — G-08 |
| 2 | Добавить фоновые циклы наблюдения | H-01 — H-06 |
| 3 | Добавить Telegram-уведомления и детерминированные объяснения | I-01 — I-08 |
| 4 | Подготовить пилотную эксплуатацию и операционные процедуры | L-01 — L-07 |
| 5 | Добавить сбор фактических результатов и метрики качества | J-01 — J-07 |
| 6 | Завершить удаление временных компонентов после перевода всех потребителей | L-08 |

Этапы A–F и технические задачи перед G завершены и больше не входят в очередь ближайших PR. Последовательность перехода к клиенту: Tech-G05 → Tech-G06 → G-01. OpenAPI/API tests обновляются в каждом PR, затрагивающем публичный контракт; SignalR event names/payload schemas дополнительно фиксируются отдельными realtime serialization/approval tests. Существующий BTC Daily Check остаётся изолированным публичным сценарием. Переосмысление OpenClaw, расширение агентного контура и его автоматические сквозные тесты перенесены на этап K после проверки первого MVP. Этап N не начинается до накопления статистики J.

## 7. Граница первого MVP

Первый MVP считается завершённым, когда пользователь может:

1. Войти в систему.
2. Подключить Bybit-аккаунт с правами только на чтение, проверить его и при необходимости безопасно заменить credentials без потери идентичности подключения.
3. Увидеть актуальный account-scoped баланс/портфель и все открытые позиции в адаптивном веб-интерфейсе.
4. Открыть карточку позиции и увидеть её состояние, рыночный контекст, график, риск и свежесть данных.
5. Получить согласованный evaluation: детерминированную оценку и рекомендацию с понятными причинами, сроком действия/свежестью и отдельным решением, допустимо ли увеличивать риск.
6. Просмотреть единый timeline существенных изменений позиции, оценок и рекомендаций.
7. Оставить систему наблюдать за позицией без постоянно открытого веб-интерфейса.
8. Получить в Telegram критическое уведомление без повторного спама.
9. Продолжить использовать действующий BTC Daily Check.
10. Использовать систему в пилоте с журналами, метриками, проверками зависимостей и процедурами защиты секретов.

В MVP не входят:

- автоматическое открытие, закрытие или усреднение;
- запись торговых ключей с правами исполнения;
- безусловная рекомендация увеличивать убыточную позицию;
- зависимость торгового решения от доступности ИИ;
- общий cross-account portfolio и расширенная межаккаунтная аналитика;
- расширенный анализ новостей, макроэкономики, социальных сетей и on-chain данных;
- расширенная correlation model и количественная оптимизация portfolio risk;
- количественная оптимизация правил на накопленной статистике;
- мобильные нативные и VR-клиенты;
- поддержка нескольких бирж.

## 8. Общий критерий готовности задачи

Задача переводится в статус «Завершено», только если:

1. Код находится в `develop` после слияния PR.
2. Сборка и все затронутые тесты проходят.
3. Для нового поведения добавлены модульные, интеграционные или контрактные тесты.
4. Не нарушены направления зависимостей.
5. Изменения публичных контрактов совместимы либо версионированы.
6. Секреты и приватные данные не попадают в журналы и ответы.
7. `README.md` и `ROADMAP.md` обновлены при изменении фактического состояния этапов или архитектуры; применимые `AGENTS.md` также обновлены, если изменение делает их долговечные инструкции неверными или неполными.
8. Если изменение затрагивает `llm-payload` 1.0, совместимость с BTC Daily Check проверена либо принято и зафиксировано отдельное решение о миграции.
9. Если изменение затрагивает формирование рекомендаций, версия и hash применяемого `PolicyDefinition` сохраняются для воспроизводимости, а критические safety-инварианты не могут быть отключены внешней конфигурацией.
10. Если изменение затрагивает user-facing API или SignalR contract этапа F, соответствующие OpenAPI/API/realtime contract tests обновлены в том же PR; для SignalR отдельно зафиксированы client-facing event names и serialized payload schemas через serialization/approval tests; финальная задача F-08 не заменяет эту обязанность.

## 9. Правила ведения дорожной карты

После каждого слитого PR:

1. Проверить фактический результат в `develop`.
2. Изменить статусы только подтверждённых задач.
3. Добавить номер PR и коммит в журнал изменений.
4. Зафиксировать новые решения, риски и переносы задач.
5. Выбрать один следующий PR из ближайшей очереди.
6. Не считать архитектурную заготовку завершённой пользовательской возможностью.
7. Обновить дату, текущий активный этап и ссылки на связанные Issue/PR.
8. Если изменилось направление продукта, сначала обновить этот документ, затем код.

## Future Product Directions

Текущий ROADMAP не исчерпывает долгосрочное Product Vision. Отдельно исследуются и концептуально развиваются следующие направления:

- Trading Journal и Journal analytics;
- Market Screener и сохранённые скринеры;
- AI Explanation, AI Copilot, AI Trading Coach, Context Synthesis и AI Research;
- Social / Strategy Intelligence, включая Follow Trader, Shadow Copy и внутреннее Copy Trading;
- профили и подписки на торговые стратегии;
- наблюдение и анализ внешних торговых ботов, включая возможную интеграцию с GinArea;
- подготовка торговых действий и контролируемое исполнение;
- Controlled Agentic Trading после накопления достаточной статистики качества и появления строгих risk gates.

Полный каталог и степень зрелости этих возможностей фиксируются в [Capability Map](docs/product/capability-map.md), а продуктовые термины и пользовательские сценарии — в [Product Concepts](docs/product/concepts.md) и [Product Scenarios](docs/product/scenarios.md).

Порядок перечисления в этом разделе и в Capability Map **не является порядком реализации**. Перевод Idea / Research / Concept в ROADMAP, GitHub Issue или implementation требует отдельного human decision.

---

## 10. Журнал изменений

| Дата | Версия | Изменение |
|---|---|---|
| 2026-09-25 | 3.34 | Issue #152 завершает Tech-G06: API composition root разделён по concerns с явным HTTP pipeline в `Program.cs`, добавлены regression-проверка shared realtime singleton и Compose smoke для повторного seeding/password mismatch; `Authentication.TestSeeder` нормализован без изменения OAuth/OIDC semantics, `Api/AGENTS.md` синхронизирован. Следующим функциональным шагом остаётся G-01 — основной React-клиент. |
| 2026-09-23 | 3.28 | Issue #142 завершает Tech-G01 перед этапом G: исправлен coverage quality gate для hand-written production code с диагностикой по assemblies, добавлены regression tests tooling и расширены contract tests публичного Bybit adapter без изменения runtime-поведения. |
| 2026-09-22 | 3.26 | Issue #138 сформировал отдельный продуктовый слой документации: Product Vision, Capability Map, Product Concepts и Product Scenarios отделены от текущего ROADMAP. Документ одновременно синхронизирован с уже merged Issue #136 / PR #137: F-07 отмечен завершённым, user-scoped SignalR `/hubs/v1/updates` и включённый outbox dispatcher отражены в текущем состоянии, следующим шагом назначен F-08. Долгосрочные Journal, Screener, AI, Social/Copy Trading и GinArea-направления зафиксированы без изменения порядка этапов F–N. |
| 2026-09-21 | 3.25 | Issue #134 завершает F-06: добавлен user-scoped `GET /api/v1/positions/{id}/timeline`, объединяющий persisted position changes, assessments/evaluations и recommendations с bounded PostgreSQL projections, deterministic newest-first ordering, versioned opaque cursor и repeatable type filter. Domain не изменялся; по PostgreSQL query-plan evidence через EF Core migration добавлены chronology indexes `ix_position_changes_position_occurred_at_sequence` и `ix_recommendations_position_created_at_id`; F-07 остаётся следующим шагом. |
| 2026-09-21 | 3.24 | Issue #128 завершает F-05: добавлены user-scoped GET/POST evaluation, latest assessment query, evaluation-time portfolio freshness, typed configuration с risk limits `20/200/50`, explicit v1 read model и stable `position_not_evaluable` error. Следующий шаг — F-06: timeline позиции. |
| 2026-09-21 | 3.23 | Issue #126 завершает F-04: добавлены user-scoped position market identity projection, `/api/v1/positions/{id}/market` через существующий cached public snapshot pipeline и `/api/v1/positions/{id}/candles` с bounded interval/limit contract, explicit v1 DTO mapping, OpenAPI synchronization и API/Application/PostgreSQL/architecture coverage. Следующий шаг — F-05: единый evaluation workflow и read model. |
| 2026-09-20 | 3.22 | Issue #124 / PR #125 завершает F-03: добавлены user-scoped read endpoints позиций и account-scoped portfolio, SQL-side filtering/seek pagination без загрузки `PositionChanges`, versioned opaque cursor, explicit v1 DTO/enums, metadata-driven OpenAPI authorization и API/PostgreSQL contract coverage. Следующий шаг — F-04: position-scoped market context и свечи. |
| 2026-09-20 | 3.21 | PR #117 завершил F-02: канонический `/api/v1/exchange-accounts` покрывает list/connect/verify/credential rotation/sync/disconnect, pre-v1 routes удалены, user scope и стабильные ProblemDetails/OpenAPI contracts проверены тестами. Для exchange account введена обязательная provider-side identity (Bybit `userID`): один `ExchangeAccountId` сохраняет один внешний аккаунт на всём lifecycle, rotation другого account/subaccount отклоняется без mutation, persistence/CAS запрещает rebinding. Добавлена migration `provider_account_id NOT NULL`, PostgreSQL race/rollback/invariant coverage и корректное различение permission-denied при чтении positions. Следующий шаг — F-03: read API позиций и account-scoped portfolio. |
| 2026-09-18 | 3.20 | PR #112 завершил F-01: зафиксированы canonical `/api/v1` contracts, typed DTO/read models, route-scoped JSON conventions с единым поведением для JSON media types, стабильный ProblemDetails и cursor pagination foundation; добавлены HTTP/API contract tests и v1-scoped OpenAPI enum synchronization. Pre-v1 `api/exchange-accounts` сохранён без v1 alias, public market-analysis boundary не изменена. Следующим шагом остаётся F-02 — lifecycle подключений к биржевым аккаунтам. |
| 2026-09-16 | 3.19 | По review PR #110 устранены замечания Codex/Copilot: SignalR wire contract уточнён как отдельный от OpenAPI и требует serialization/approval tests для client-facing event names и payload schemas; в Stage M разделены cross-account read model и расширенная portfolio analytics без дублирования; ADR-0002 синхронизируется с актуальными примерами `/api/v1` и этапами F. |
| 2026-09-16 | 3.18 | По review Issue #109 уточнены границы Stage F/G/H: SignalR browser integration закреплена за BFF без выдачи access token в JavaScript; в состав F-01 включено решение по миграции существующего pre-v1 `api/exchange-accounts`; для списка позиций зафиксированы pagination/default active states и фильтры; `evaluation` получил обязательные temporal/input identity metadata и nullable recommendation; G-05 больше не зависит от market-monitoring events до H-04; публичный market-analysis API отделён от legacy `snapshot`; OpenAPI/API contract tests должны сопровождать каждый PR F-02 — F-07, а F-08 выполняет финальную проверку полноты. |
| 2026-09-16 | 3.17 | Issue #109: перед реализацией этапа F пересмотрен пользовательский API. API больше не копирует доменные агрегаты один в один: введён единый position `evaluation` для assessment + current recommendation, account-scoped portfolio, position-scoped market/candles и единый timeline. `sync` отделён от evaluation; добавлены verify и безопасная ротация credentials; удалены из плана неоднозначный `refresh`, отдельный `/portfolio/risk`, общий `/portfolio` без доменной модели и преждевременные acknowledge/dismiss commands. SignalR зафиксирован как user-scoped invalidation channel с восстановлением через REST. Этап F разбит на F-01 — F-08 и отдельные ближайшие PR; acknowledge/dismiss перенесены в I, cross-account portfolio — в M. |
| 2026-09-16 | 3.16 | PR #102 merged в `develop` и завершил E-08.2: применение `RecommendationStabilityPolicy`, persisted baseline-bound pending state, CAS/retry, user isolation и атомарную публикацию/замену recommendation. Stage E отмечен завершённым; correlation model перенесена в Stage M и больше не блокирует E-10. PR #105 завершил техническую стабилизацию перед F: обязательные DI dependencies, conditional persistence registration, `global.json`, CI push checks, aggregate coverage gate и NuGet vulnerability check. Уточнены семантика статуса «Завершено», граница E/F/H и правило синхронизации применимых `AGENTS.md` с изменениями архитектуры/tooling. Текущий этап — F. |
| 2026-09-15 | 3.15 | PR #100 merged в `develop`; E-08.2 реализует применение `RecommendationStabilityPolicy`, persisted baseline-bound pending state, CAS, user isolation, partial unique current index и транзакционную публикацию successor. До merge PR #102 E-08 оставался 🟡; следующим этапом становился F. |
| 2026-09-14 | 3.14 | В PR #100 к Issue #99 исправляются review findings E-08.1: candidate/pending temporal validation и replay idempotency, risk-safe policy/priority ordering, mixed capacity semantics, inherited portfolio reasons и strict JSON regression coverage. PR ещё не merged; следующим остаётся E-08.2. |
| 2026-09-14 | 3.13 | PR #98 merged в `develop`; E-07 отмечен завершённым. В E-08.1 добавлена чистая доменная anti-chatter/stability policy с semantic comparison, typed decisions/reasons, cooldown, hysteresis, safety bypass и strict stability profile в policy hash. Persistence и orchestration замещения остаются E-08.2. |
| 2026-09-13 | 3.12 | PR #96 merged в `develop`; E-07 реализован в PR #98 к Issue #97: добавлены typed continuation conditions/evaluator, safety-safe Watch fallback, policy identity/expiry invalidation, strict persistence JSON, PostgreSQL migration и precision-safe lifecycle round-trip. Следующим остаётся E-08; PR #98 ещё не merged. |
| 2026-09-13 | 3.11 | Закрыты финальные safety замечания PR #96: trusted evaluation и persistence rehydration больше не являются публичными creation paths, degraded compatibility creation ограничен `Watch + DoNotAdd`, отсутствие policy path стало fail-fast; E-07/E-08 не начаты. |
| 2026-09-12 | 3.10 | В PR #96 исправлены review issues E.2: NonProtective stop, legacy creation bypass, preservation of compatibility reasons, truthful Watch/liquidation reasons, portfolio-safe headroom reasons, gross-exposure concentration sizing, semantic jsonb immutability comparison и runtime/API/Docker policy wiring; E-07/E-08 не начаты. |
| 2026-09-11 | 3.9 | Реализован PR E.2 (Issue #95): добавлены строгий внешний JSON `PolicyDefinition` с canonical SHA-256 identity, чистая детерминированная `RecommendationPolicy`, все семь `PositionAction`, структурированные confidence/priority/action reasons, независимый `AddDecisionResult` с hard guards и консервативным maximum additional size, policy identity binding, immutable decision persistence и безопасное legacy-восстановление; E-07 и E-08 не начаты, E-10 остаётся частичным. |
| 2026-09-11 | 3.8 | Уточнён статус PR E.1: добавлены level/current-price и data-quality regression cases, исправлена trailing-stop semantics, расширена configuration identity и structured persistence coverage; E-09 остаётся частичным до enforcement recommendation actions, E-10 — до появления correlation-модели. |
| 2026-09-11 | 3.7 | Реализован PR E.1: единый воспроизводимый вход и `PositionAssessmentService`, структурированные детерминированные признаки позиции, неотключаемый запрет повышения риска при stale/partial/uncertain данных, long/short и граничные сценарии; E-04 — E-08 оставлены для следующего PR. |
| 2026-09-11 | 3.6 | Перед этапом E зафиксировано направление к внешне конфигурируемой политике рекомендаций: параметры политики загружаются через валидируемый `PolicyDefinition`, каждая версия идентифицируется version/hash для воспроизводимости, критические safety-инварианты остаются в C#, а архитектура должна позволять последующий переход к декларативным правилам и Rule Engine без обязательной реализации собственного DSL в Stage E. |
| 2026-09-10 | 3.5 | Выполнена техническая подготовка перед этапом E в Issue #90 / PR #91: нормализованы инструкции для coding agents, OpenClaw изолирован до этапа K, набор project-scoped Agent Skills приведён к общей для Codex и Copilot структуре, XML-документация C# переведена на русский язык и ROADMAP синхронизирован с завершённым Stage D. Следующим остаётся E-01. |
| 2026-09-10 | 3.4 | Реализован D-07 (Issue #88, PR #89): добавлен process-local HybridCache для финального публичного `MarketSnapshot` с TTL 1 секунда, per-key single-flight, independently owned DI scope для source build, cancellation-safe ожиданием, fail-fast options validation и telemetry; Stage D завершён, следующим выбран E-01. |
| 2026-09-09 | 3.3 | Исправлены замечания D-06 в PR #87: no-op sync не создаёт lifecycle event, position events несут PositionChangeSequence и temporal metadata, dispatcher сериализует одну позицию внутри batch, default Enabled=false, bounded claim identity и добавлены dispatcher pipeline tests; D-07 остаётся следующим этапом. |
| 2026-09-09 | 3.2 | Реализован D-06 (Issue #86): добавлены versioned position/sync-degraded events, PostgreSQL transactional outbox в общей sync-транзакции, at-least-once API dispatcher с lease/retry и explicit EventId idempotency contract; следующим выбран D-07. |
| 2026-09-09 | 3.1 | Реализован D-05: добавлены system-scoped keyset-кандидаты Connected/Unavailable, bounded background sweep со scope на аккаунт, TimeProvider-расписание без overlap/catch-up storm, scheduler lag, LastSyncedAt telemetry и PostgreSQL candidate tests; следующая задача — D-06. |
| 2026-09-09 | 3.0 | Реализован D-04: добавлен persisted account observation watermark, bounded CAS retry без повторного Bybit IO, безопасные AlreadyApplied/Superseded outcomes, защита от старых position observations и PostgreSQL race tests; следующим выбран D-05. |
| 2026-09-08 | 2.9 | Реализован D-03: degraded balance/positions observations сохраняют правдивый PortfolioState, переводят неподтверждённые позиции в Unknown/Stale, не сдвигают LastSyncedAt и восстанавливаются следующим полным наблюдением; следующий этап — D-04. |
| 2026-09-08 | 2.8 | Реализован D-02: добавлены единый application-сценарий ручной синхронизации Bybit Unified/Linear, account-scoped reconciliation с CAS, сохранение PortfolioState и атомарная persistence phase; следующий этап — D-03. |
| 2026-09-08 | 2.7 | По результатам проверки `develop` на коммите `74a91f4` этап C и C-08 отмечены завершёнными: PostgreSQL integration suites покрыли migrations, persistence, concurrency, user isolation, OAuth/OIDC и credential security. Документация синхронизирована с кодовой базой, следующим этапом назначен D-01. |
| 2026-09-08 | 2.6 | В PR #73 завершён Tech-03: добавлены структурированное логирование, прикладная телеметрия и контролируемая устойчивость внешних вызовов. Техническая подготовка перед этапом D завершена. |
| 2026-09-08 | 2.5 | В PR #71 завершён Tech-02: добавлены единый `ProblemDetails` contract, центральный `IExceptionHandler` и безопасное mapping exception → HTTP. |
| 2026-09-07 | 2.4 | Выполнена техническая подготовка перед этапом D: приватный exchange boundary получил явный результат баланса, нейтральную классификацию failures, сохранённую cancellation-семантику и coverage artifact. D-01 по-прежнему не начат; HTTP error handling и resilience остаются отдельными задачами. |
| 2026-09-07 | 2.3 | В Issue #66 и PR #67 реализована защита Bybit credentials: AES-256-GCM payload с AAD user/account identity, внешний key ring, CAS rotate/revoke/reprotect, PostgreSQL security tests и безопасная Compose/CI/Aspire конфигурация. Следующий этап — D-01; C-08 остаётся частично выполненным. |
| 2026-09-07 | 2.2 | В Issue #64 и PR #65 реализован C-06: user-delegated `sub` сопоставляется с Domain `UserId`, user-owned repository operations получают явный scope, foreign identifiers не раскрывают данные и не изменяют CAS/history, а PostgreSQL и Bearer E2E tests подтверждают изоляцию. Следующая задача — C-07. |
| 2026-09-07 | 2.1 | В PR #63 завершено исправление C-05A: добавлены explicit Identity migration runner и Compose/Aspire ordering, lockout policy/tests, public issuer и internal metadata separation, overlapping signing certificates/JWKS rollover, explicit short access-token lifetime, runtime auth smoke и обновлены migration/Docker/CI instructions. Следующим этапом остаётся C-06. |
| 2026-09-07 | 2.0 | Реализован C-05A: отдельный Identity host на ASP.NET Core Identity + OpenIddict, отдельная PostgreSQL persistence и migration stream, Authorization Code + PKCE (S256), signed JWT access tokens, discovery/JWKS, независимая JwtBearer validation в Api и PostgreSQL integration proof. Следующим этапом остаётся C-06. |
| 2026-09-05 | 1.9 | ADR-0003: ASP.NET Core Identity + OpenIddict выбран как self-hosted Authorization Server; Identity boundary отделена от resource server; Identity/OpenIddict persistence отделена от business persistence; C-05A больше не выбирает provider, а реализует принятое решение. Обновлены README, AGENTS и ближайшая очередь runtime PR; решение зафиксировано в PR #61. |
| 2026-09-05 | 1.8 | ADR-0001 сохранён как историческое решение и помечен Superseded; принят ADR-0002 по client-agnostic authentication contract: OAuth 2.0/OpenID Connect, Bearer access tokens и signed JWT для защищённого API. Зафиксированы границы Authorization Server и Resource Server, user-delegated `sub` → Domain `UserId`; machine/service principals отделены от Domain users, React → BFF → API, Authorization Code + PKCE для public clients, Device Authorization/PKCE для CLI, Client Credentials для будущих machine clients, anonymous public market endpoints и отдельная ответственность C-06 за authorization/isolation. C-05A переработан под выбор Authorization Server и JWT Bearer foundation; BFF закреплён за G-01. |
| 2026-09-04 | 1.7 | Принят ADR-0001 по аутентификации пользователей (C-05): для первого Web MVP выбраны ASP.NET Core Identity и secure HttpOnly cookie, с единым authenticated principal для REST и будущего browser SignalR; зафиксированы CSRF, cookie security, стабильный `UserId`, публичные и приватные endpoints, а также отложенные native/OIDC-сценарии. Runtime authentication выделена в отдельный следующий шаг C-05A; C-06 остаётся отдельным этапом UserId isolation. |
| 2026-09-04 | 1.6 | Реализована оптимистическая конкурентность (C-04): persistence-neutral ConcurrencyVersion/Versioned<T> и ConcurrencyConflictException в Application; GetByIdAsync/SaveAsync репозиториев ExchangeAccount, Position и Recommendation переведены на compare-and-swap по версии (без retry); добавлена миграция AddConcurrencyVersion с безопасным backfill существующих строк; добавлены детерминированные PostgreSQL-тесты на конфликт версий, откат истории позиции при устаревшей записи, dynamic-only обновления, последовательные версии, blind overwrite и удалённую строку. C-04 завершён; auth, user isolation и security остаются в следующих PR. |
| 2026-09-04 | 1.5 | В PR #52 исправлены восстановление dynamic-only состояния позиции, канонизация PostgreSQL timestamps и валидация inherited reasons для Assessment/Recommendation. C-02 и C-03 остаются завершёнными, C-08 выполнен частично; concurrency, auth, user isolation и security остаются в следующих PR. |
| 2026-09-04 | 1.4 | Добавлено постоянное relational-хранение доменного состояния: migrations, repository ports, persistence entities, explicit rehydration и PostgreSQL Testcontainers round-trip tests. C-02 и C-03 завершены, C-08 выполнен частично; concurrency, auth и user isolation остаются в следующих PR. |
| 2026-09-04 | 1.3 | Учтены PR #38, #40, #42, #44 и #46. Этап B завершён: добавлены типизированные идентификаторы, домены аккаунта и позиции, жизненный цикл и история позиции, состояние и риск портфеля, оценки и рекомендации. Текущим этапом назначен C — хранение, безопасность и пользователи. |
| 2026-09-02 | 1.2 | Учтены PR #34 и завершение разделения биржевого слоя Bybit; задачи A-05 — A-07 отмечены завершёнными. Сквозная проверка прежней OpenClaw-цепочки удалена из этапа A. BTC Daily Check зафиксирован как изолированный публичный сценарий, а переосмысление роли OpenClaw, контракты и сквозные тесты агентного контура перенесены на этап K после проверки первого MVP. |
| 2026-09-01 | 1.1 | Проанализирован `ROADMAP.md` из `task/31-add_roadmap`. Добавлены непрерывное наблюдение, проверка качества рекомендаций, расширенный ИИ-анализ, продуктовые детали React и уведомлений. Действие по позиции отделено от решения об увеличении риска. Текущим этапом назначен домен аккаунта и позиции. |
| 2026-09-01 | 1.0 | Создана актуальная дорожная карта. Учтён PR #28 и состояние `develop` на коммите `5bf3a7a`. |