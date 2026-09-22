# Realtime contract v1

## Граница

Пользовательская realtime boundary находится по адресу:

```text
/hubs/v1/updates
```

Это SignalR hub, а не REST endpoint. Hub не публикуется в OpenAPI и не
предоставляет клиентских методов подписки или выбора `UserId`.

## Аутентификация и адресация

Подключение разрешено только authenticated user principal, у которого:

- есть scope `trade.api`;
- `trade_principal_type=user`;
- `sub` является непустым GUID, сопоставленным с Domain `UserId`.

SignalR user identifier вычисляется сервером из того же проверенного principal и
передаётся в формате GUID `D`. Сообщение отправляется через `Clients.User(...)`
на все одновременные подключения этого пользователя. Machine/service principals,
anonymous clients, некорректный `sub` и токены без `trade.api` отклоняются.

Resource server принимает bearer token через стандартный `Authorization` header.
Query-string access token не поддерживается. Browser-specific transport и BFF
граница относятся к этапу G; access token не должен попадать в JavaScript
клиента.

## События и payload

SignalR является каналом invalidation. После каждого события клиент перечитывает
актуальное состояние через REST API. Payload не является копией REST DTO и не
содержит `userId`, aggregate, credentials или market snapshot.

Стабильные client-facing event names:

| Event | Payload |
|---|---|
| `exchangeAccount.updated` | `eventId`, `occurredAt`, `exchangeAccountId` |
| `portfolio.updated` | `eventId`, `occurredAt`, `exchangeAccountId` |
| `position.updated` | `eventId`, `occurredAt`, `positionId` |
| `evaluation.updated` | `eventId`, `occurredAt`, `positionId` |

Пример:

```json
{
  "eventId": "11111111-1111-1111-1111-111111111111",
  "occurredAt": "2026-09-22T04:36:39.873+03:00",
  "exchangeAccountId": "22222222-2222-2222-2222-222222222222"
}
```

Свойства сериализуются в `camelCase`, GUID — в стандартной строковой форме,
`occurredAt` — как ISO 8601 `DateTimeOffset`. Внутренние application events и
публичные SignalR events являются разными контрактами. Транзакционные изменения
публикуют versioned application events в существующий PostgreSQL outbox, а API
adapter преобразует их в минимальные realtime messages.

## Invalidation semantics

- Account lifecycle mutations (`connect`, изменивший состояние `verify`,
  credential rotation и `disconnect`) публикуют `exchangeAccount.updated`.
- Applied synchronization публикует `exchangeAccount.updated` и
  `portfolio.updated`; degraded synchronization использует
  `exchange-account.sync-degraded` как account invalidation и также публикует
  `portfolio.updated`.
- Position lifecycle events публикуются как `position.updated`.
- Сохранённый новый evaluation публикуется как `evaluation.updated`, даже если
  recommendation stability policy сохранила текущую рекомендацию или оставила
  candidate в pending confirmation.
- No-op, superseded и неуспешные операции, которые не сохранили изменение,
  invalidation не создают.

## Доставка и восстановление

Доставка application events и realtime notifications имеет at-least-once
семантику. Duplicate, delayed и out-of-order invalidations допустимы: клиент не
строит состояние из событий и повторно читает REST resource. Потеря соединения
также не восстанавливается через историю SignalR.

После reconnect клиент выполняет обычный REST refresh:

```text
connection lost
    ↓
connection restored
    ↓
REST refresh
```

В v1 нет replay, resume cursor, acknowledgement, persisted subscriptions,
durable SignalR history или exactly-once гарантии.

## Совместимость

Внутри `/hubs/v1/updates` event names и смысл существующих полей не меняются.
Новые события могут добавляться обратно совместимо. Несовместимое изменение
требует новой major boundary, например `/hubs/v2/updates`.
