# ADR-0006: Realtime-наблюдение Bybit и reconciliation фактического состояния

## Статус

Принято.

Решение фиксирует архитектурные решения Issue #194 и дополняет действующие решения ROADMAP о разделении `sync` и `evaluation` и о SignalR как канале invalidation. [ADR-0002](0002-universal-api-authentication-strategy.md) и [ADR-0003](0003-authorization-server-selection.md) остаются действующими; ни одно существующее ADR этим решением не заменяется.

## Контекст

Backend уже получает фактическое состояние read-only Bybit-аккаунтов через полную REST-синхронизацию: ручную операцию пользователя и фоновую синхронизацию активных аккаунтов. Синхронизация сохраняет баланс, позиции и account-scoped `PortfolioState`, защищена watermark, CAS/retry и идемпотентной обработкой повторных и устаревших наблюдений. Принятые изменения публикуются как versioned application events через transactional outbox, а user-scoped SignalR boundary `/hubs/v1/updates` сообщает клиентам об изменении resource; полное состояние клиент перечитывает через REST.

Этого достаточно, чтобы Web обновлялся относительно backend, но недостаточно, чтобы Web обновлялся относительно биржи: актуальность фактического состояния зависит от ручной или периодической синхронизации.

Целевой пользовательский путь первого Web MVP:

```text
Bybit factual state
→ backend state
→ persistence / application events
→ SignalR invalidation
→ BFF
→ React
```

При этом нужно сохранить существующие границы: backend остаётся единственным источником бизнес-истины, `sync` и `evaluation` остаются разными операциями, а SignalR не становится вторым источником полного состояния. Realtime transport биржи допускает разрывы, повторные, задержанные и пришедшие не по порядку observations, а private account events не гарантируют событие на каждое изменение market-derived показателей.

## Решение

### AD-01. Exchange realtime является server-side ingress

Bybit остаётся внешним источником фактов о биржевом аккаунте. Realtime-наблюдение биржи выполняется только backend: browser не подключается к Bybit, не получает пользовательские credentials и не строит собственное business state из raw exchange events. Клиенты получают фактическое состояние только после того, как оно принято backend.

### AD-02. Exchange transport изолирован адаптером

Provider-specific transport data нормализуются внутри exchange boundary. Raw Bybit types, topic payloads и SDK-модели не становятся Domain, Application или API contracts; остальная система видит только exchange-neutral observations.

### AD-03. Private facts и market-derived valuation — разные категории данных

Различаются:

- факты, наблюдаемые непосредственно из приватного состояния exchange account;
- показатели, зависящие от текущего рыночного состояния;
- производные portfolio/position read models.

Изменение рыночной цены и производного PnL нельзя выдавать за private account observation, если биржа такой semantics не гарантирует. Архитектура не предполагает, что любое изменение unrealized PnL сопровождается private wallet/position event; актуальность market-derived valuation обеспечивается отдельно от приватного наблюдения и явно обозначается как производная от рынка.

### AD-04. Realtime уменьшает задержку, но не заменяет reconciliation

Realtime-наблюдение оптимизирует задержку получения изменений. Полная account synchronization остаётся authoritative reconciliation mechanism для начального состояния, восстановления после разрыва наблюдения или возможной потери изменений, контрольной сверки и ручного recovery. WebSocket transport не становится единственным источником восстановления состояния.

Ручная синхронизация сохраняется как явная пользовательская recovery/reconciliation операция, но перестаёт быть обязательным нормальным способом поддержания актуальности Web.

Если realtime-наблюдение нарушено и актуальность фактического состояния не подтверждается, это состояние представляется явно как degraded/stale и не считается молча свежим.

### AD-05. Существующие monotonicity/idempotency invariants сохраняются

Duplicate, delayed и out-of-order observations не отменяют гарантий Stage D: watermark-монотонность, CAS/retry и идемпотентная обработка повторных наблюдений действуют для любого источника наблюдений, включая realtime и reconciliation.

### AD-06. Второй downstream event pipeline не создаётся

Принятые изменения business state используют существующие application events и transactional outbox с at-least-once semantics. Realtime ingress не создаёт параллельный пользовательский event bus и не переизобретает уже существующие события открытия, изменения и закрытия позиции.

### AD-07. SignalR остаётся invalidation channel

Client-facing SignalR event сообщает, что resource изменился. Полное актуальное состояние после события или reconnect клиент получает через REST. `/hubs/v1/updates` не превращается в state replication protocol, а его event names и payloads этим решением не меняются.

### AD-08. Browser realtime остаётся за BFF boundary

Browser подключается к realtime только через BFF-compatible integration поверх той же пользовательской identity. Access token не попадает в JavaScript, а browser не подключается напрямую к resource server или бирже с пользовательскими credentials.

### AD-09. Factual update не означает automatic evaluation

Realtime-обновление фактического account/position state и evaluation/recommendation остаются разными lifecycle. Изменение фактического состояния само по себе не запускает скрытую повторную оценку и не публикует новую рекомендацию. Автоматическая повторная оценка, условия и частота её запуска, сравнение результатов и monitoring events относятся к Stage H.

### AD-10. Интеграция остаётся read-only

Realtime-наблюдение использует только read-only возможности биржи. Write/trading API, создание, изменение и закрытие ордеров или позиций этим решением не вводятся.

## Последствия

- Stage G становится cross-stack Web/realtime этапом: критерий его завершения — законченный пользовательский путь от изменения фактического состояния биржи до автоматического обновления открытого Web, а не только frontend code.
- Stage H сужается до интеллектуального сопровождения: повторная оценка, сравнение, определение существенности и monitoring events работают поверх уже актуального фактического состояния и не отвечают за получение изменений с биржи.
- API v1 и SignalR v1 contracts этим ADR не изменяются: [Realtime contract v1](../realtime-v1-contract.md) и [API v1 conventions](../api-v1-conventions.md) остаются действующими.
- Текущая REST-синхронизация, ручная и фоновая, сохраняется и остаётся основой convergence фактического состояния.
- Implementation realtime-наблюдения Bybit (G-06 в ROADMAP на момент принятия решения) ещё отсутствует. Конкретные topics, клиенты, количество соединений, структура workers и services, reconnect/retry, интервалы, configuration, telemetry и стратегия масштабирования определяются отдельным Issue и его Approved Implementation Plan.
