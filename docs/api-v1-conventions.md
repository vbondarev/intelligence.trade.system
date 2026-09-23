# Соглашения контрактов пользовательского API v1

Канонической пользовательской REST-границей является `/api/v1/...`.
Изменения внутри v1 допускаются только аддитивные; несовместимое изменение
контракта требует новой версии маршрута.

## Стабильные operationId

`operationId` является частью machine-readable v1 contract и не зависит от
имён controller/action:

| Method | Route | operationId |
|---|---|---|
| GET | `/api/v1/auth/me` | `getCurrentUser` |
| GET | `/api/v1/exchange-accounts` | `listExchangeAccounts` |
| POST | `/api/v1/exchange-accounts` | `createExchangeAccount` |
| POST | `/api/v1/exchange-accounts/{id}/verify` | `verifyExchangeAccount` |
| PUT | `/api/v1/exchange-accounts/{id}/credentials` | `rotateExchangeAccountCredentials` |
| POST | `/api/v1/exchange-accounts/{id}/sync` | `syncExchangeAccount` |
| DELETE | `/api/v1/exchange-accounts/{id}` | `disconnectExchangeAccount` |
| GET | `/api/v1/exchange-accounts/{id}/portfolio` | `getExchangeAccountPortfolio` |
| GET | `/api/v1/positions` | `listPositions` |
| GET | `/api/v1/positions/{id}` | `getPosition` |
| GET | `/api/v1/positions/{id}/market` | `getPositionMarket` |
| GET | `/api/v1/positions/{id}/candles` | `getPositionCandles` |
| GET | `/api/v1/positions/{id}/evaluation` | `getPositionEvaluation` |
| POST | `/api/v1/positions/{id}/evaluation` | `evaluatePosition` |
| GET | `/api/v1/positions/{id}/timeline` | `getPositionTimeline` |

Realtime wire contract `/hubs/v1/updates` описан отдельно в
[`realtime-v1-contract.md`](realtime-v1-contract.md). SignalR используется
только для user-scoped invalidation, а актуальное состояние перечитывается
через REST `/api/v1/...`.

## JSON

Ответы v1 используют имена свойств в camelCase, строковые значения enum в
camelCase, GUID в стандартном строковом представлении и значения
`DateTimeOffset` в формате ISO 8601 с явно определённой семантикой UTC или
смещения. Nullable-значения сохраняют возможность отсутствия и сериализуются как `null`,
если поле является частью контракта. Контроллеры под `/api/v1` возвращают
обычные MVC `ObjectResult`/`Ok(...)`; единые JSON conventions автоматически
применяются инфраструктурой API. Legacy serializer
`api/market-analysis` не изменяется.

Для всех поддерживаемых JSON media types, включая `application/json`,
`text/json`, `application/problem+json` и `application/*+json` vendor types,
применяются одни и те же v1-правила. Выбор JSON-compatible `Accept` не меняет
wire contract. Для других `Accept` используется стандартная MVC
content-negotiation semantics, при этом v1 response не должен сериализоваться
legacy-правилами.

Доменные агрегаты не являются wire-контрактами. Каждый endpoint v1 возвращает
DTO или read model, преобразованную на границе API.

OpenAPI enum values для v1-контрактов обязаны совпадать с фактическими
runtime wire values.

## Ошибки и авторизация

Ошибки используют существующий pipeline ASP.NET Core `ProblemDetails`.
Стабильными полями являются `type`, `title`, `status`, `detail`, `instance`,
`code` и `traceId`; существующие коды ошибок остаются неизменными.
OpenAPI описывает `code` и `traceId` как расширения `ProblemDetails`. Поле
`errors` может присутствовать в validation response, но не является
обязательной частью стабильного ядра. Для каждой user-owned v1 operation
OpenAPI фиксирует Bearer security requirement и статусы `401`/`403`.
`401`/`403`, сформированные auth middleware, не обязаны иметь тот же body,
что и application-level `ProblemDetails`; F-08 не вводит новый error protocol.

Endpoints для пользовательских данных используют проверенный user principal и
аутентификацию Bearer/OIDC. Отсутствующий ресурс и ресурс, принадлежащий
другому пользователю, имеют одинаковый наблюдаемый результат — обычно
`404 Not Found`. Обработка browser-токенов относится к границе BFF и не
добавляется в resource server.

## Пагинация и фильтры

Ответы с cursor pagination используют `items`, `nextCursor` и `hasMore`;
`nextCursor` является opaque-значением, а `totalCount` по умолчанию
отсутствует. Общий диапазон page size — от 1 до 100, значение по умолчанию —
50, если конкретный endpoint использует default. Фильтры и значения по
умолчанию для конкретных endpoints определяются соответствующим API slice.
Неизвестные значения enum и некорректные GUID являются validation errors.
Отсутствующий optional-фильтр не применяется; пустое значение фильтра не
имеет неявной семантики wildcard. Для `GET /api/v1/positions` отсутствие
`trackingState` означает рабочий список `active`, `unknown` и `stale`.
Исторические `closed` позиции выбираются только явным фильтром. `cursor` и
`nextCursor` являются opaque-значениями, которые клиент передаёт без
интерпретации.

### Position timeline

`GET /api/v1/positions/{id}/timeline` возвращает user-scoped историю одной
позиции. Начальные типы item: `positionChange`, `evaluation` и
`recommendation`. `occurredAt` равен соответственно времени изменения
позиции, созданию assessment или созданию recommendation.

Результат упорядочен newest-first по `occurredAt`, затем по внутреннему rank
типа (`recommendation`, `evaluation`, `positionChange`) и source identity
внутри типа. Cursor opaque и versioned; клиент передаёт только `nextCursor`
из предыдущей страницы. `pageSize` использует общий диапазон и default cursor
pagination. Query-параметр `type` repeatable, дедуплицируется и ограничивает
источники item; без него выбираются все начальные типы.

Отсутствующая и чужая позиция имеют одинаковый `404 resource_not_found`.
Lifecycle timestamps recommendation (acknowledged, dismissed, superseded,
expired) не являются отдельными timeline events. Следующие типы item могут
добавляться аддитивно.

## Границы миграции

`/api/v1/exchange-accounts` является канонической v1-границей lifecycle
биржевого аккаунта. Незаверсионированные маршруты `/api/exchange-accounts/**`
удалены и не имеют compatibility alias.

Один `ExchangeAccountId` на всём lifecycle соответствует одному provider-side
биржевому аккаунту. Provider identity является внутренним инвариантом и не
публикуется в v1 response/OpenAPI. Ротация credentials другого внешнего
account/subaccount возвращает `409 ProblemDetails` с
`code = exchange_account_identity_mismatch` и не изменяет persisted
credentials/account state.

Для user-owned exchange account resources отсутствующий и чужой идентификатор
возвращают одинаковый `404 ProblemDetails`: `resource_not_found`,
`urn:intelligence-trade:error:resource-not-found`, `Resource not found.`.

`POST /api/v1/positions/{id}/evaluation` возвращает `409 ProblemDetails` с
`code = position_not_evaluable`, если позиция закрыта, snapshot портфеля
отсутствует или противоречив, либо temporal identity входов не позволяет
безопасно выполнить оценку.

`/api/market-analysis/snapshot` и
`/api/market-analysis/{symbol}/llm-payload` остаются отдельными публичными
anonymous-контрактами и не переносятся под `/api/v1`.
