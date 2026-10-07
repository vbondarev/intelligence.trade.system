# Соглашения контрактов пользовательского API v1

Канонической пользовательской REST-границей является `/api/v1/...`.

До первого стабильного публичного релиза пользовательский API v1 находится в
стадии стабилизации контракта. Несовместимые изменения v1 допускаются только
как явно согласованное архитектурное или контрактное решение в GitHub Issue и
Approved Implementation Plan и не требуют сохранения compatibility aliases.
После объявления v1 стабильным развитие становится преимущественно
аддитивным; несовместимые изменения требуют новой версии API либо отдельно
согласованной стратегии миграции.

## Стабильные operationId

`operationId` является частью machine-readable v1 contract и не зависит от
имён controller/action:

| Method | Route | operationId |
|---|---|---|
| GET | `/api/v1/auth/me` | `getCurrentUser` |
| GET | `/api/v1/me/exchange-accounts` | `listExchangeAccounts` |
| POST | `/api/v1/me/exchange-accounts` | `createExchangeAccount` |
| PATCH | `/api/v1/me/exchange-accounts/{id}` | `renameExchangeAccount` |
| POST | `/api/v1/me/exchange-accounts/{id}/verify` | `verifyExchangeAccount` |
| PUT | `/api/v1/me/exchange-accounts/{id}/credentials` | `rotateExchangeAccountCredentials` |
| DELETE | `/api/v1/me/exchange-accounts/{id}` | `disconnectExchangeAccount` |
| POST | `/api/v1/exchange-accounts/{id}/sync` | `syncExchangeAccount` |
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
Явные request/model validation errors возвращают `400 validation_failed`.
`ArgumentException` и `NotSupportedException`, выброшенные
`IMarketSnapshotService` после API validation, не являются ошибками
пользовательского ввода: они проходят через `ApiExceptionHandler` и возвращают
безопасный `500 internal_error` с `detail = null`, сохраняя server diagnostics.
Прочие framework/programming exceptions также не классифицируются по CLR type
как validation errors и обрабатываются централизованно как `500 internal_error`.
Внутренние exception messages и stack traces не входят в HTTP response.
OpenAPI описывает `code`, `traceId` и используемое отдельными ошибками
`reason` как расширения `ProblemDetails`. Поле `errors` может присутствовать
в validation response со структурированными ошибками model binding, но не
является обязательной частью остальных ответов. Клиенты определяют обработку
по HTTP status и машинным полям, а не по `detail`, `title` или тексту исключения.
OpenAPI фиксирует Bearer security requirement и статусы `401`/`403`.

Для каждой user-owned v1 operation OpenAPI описывает `application/problem+json`
для `401` и `403`. Authentication challenge возвращает `401` с
`code = authentication_required`, `type =
urn:intelligence-trade:error:authentication-required` и
`title = Authentication required.` Стандартный `WWW-Authenticate: Bearer`
сохраняется; token validation details в ответ не включаются. Authorization
policy forbid возвращает `403` с `code = access_forbidden`, `type =
urn:intelligence-trade:error:access-forbidden` и `title = Access forbidden.`.
Этот код отличается от `exchange_permissions_rejected`, обозначающего отказ
прав у внешнего exchange API key; application/business `403` сохраняет
собственный код.

`POST /api/v1/positions/{id}/evaluation` при `409 position_not_evaluable`
возвращает `reason` как одно из типизированных стабильных v1 значений:
`closedPosition`, `portfolioUnavailable`, `portfolioInconsistent` или
`temporalInconsistency`. Wire values задаются явным mapping и не зависят от
имён Application enum. В общей OpenAPI `ProblemDetails` schema свойство
`reason` optional и связано с `PositionNotEvaluableReasonV1`; в указанном
ответе `409` оно всегда заполнено.

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

`/api/v1/me/exchange-accounts` является канонической v1-границей управления
подключениями биржевых аккаунтов текущего пользователя: list, create/reconnect,
rename, verify, rotate credentials и disconnect. Прежние management routes
`/api/v1/exchange-accounts`, `/api/v1/exchange-accounts/{id}`,
`/api/v1/exchange-accounts/{id}/verify` и
`/api/v1/exchange-accounts/{id}/credentials` удалены по pre-release policy и
не имеют compatibility alias; незаверсионированные `/api/exchange-accounts/**`
также удалены. Account-scoped `POST /api/v1/exchange-accounts/{id}/sync` и
`GET /api/v1/exchange-accounts/{id}/portfolio` сохраняются без изменений и под
`/api/v1/me/exchange-accounts` не публикуются.

`ExchangeAccountResponse` содержит обязательный пользовательский
`displayName`. Название задаётся при create, нормализуется trim, не может быть
пустым и ограничено 100 символами после trim; уникальность и регистр не
проверяются. Название не является provider identity и не заполняется
автоматически из данных биржи. `PATCH /api/v1/me/exchange-accounts/{id}` с
телом `{ "displayName": "..." }` изменяет только название, разрешён в любом
статусе, включая отключённое подключение, и возвращает `200` с обновлённым
`ExchangeAccountResponse`; повторное то же название после нормализации не
изменяет состояние. Невалидное название возвращает `400 validation_failed`.

`GET /api/v1/me/exchange-accounts` возвращает все подключения текущего
пользователя, включая отключённые (`connectionStatus = disabled`), в
стабильном порядке по идентификатору. Background и manual sync отключённые
подключения по-прежнему не обрабатывают.

Один `ExchangeAccountId` на всём lifecycle соответствует одному provider-side
биржевому аккаунту. Provider identity является внутренним инвариантом и не
публикуется в v1 response/OpenAPI/`ProblemDetails`. Ротация credentials
другого внешнего account/subaccount возвращает `409 ProblemDetails` с
`code = exchange_account_identity_mismatch` и не изменяет persisted
credentials/account state.

Обратный инвариант также действует: в области одного пользователя
`UserId + ExchangeId + ProviderIdentity` соответствует не более чем одному
`ExchangeAccountId`, включая отключённые подключения. Пользователь может
подключить несколько provider-side аккаунтов одной биржи (например, Bybit
master account и subaccounts) — каждый получает собственный
`ExchangeAccountId`. Credentials не являются identity подключения: новая пара
API key/secret того же provider-side аккаунта не создаёт новое подключение.
Одинаковая provider identity у разных пользователей не конфликтует и не
раскрывает существование чужого подключения.

`POST /api/v1/me/exchange-accounts` после обязательной read-only verification
разрешает подключение так:

- ранее неизвестный provider-side аккаунт — `201 Created` с новым
  `ExchangeAccountResponse`;
- существующее отключённое подключение того же provider-side аккаунта
  восстанавливается с прежним `ExchangeAccountId`, сохранённой историей,
  прежним `displayName` и новыми проверенными credentials — `200 OK` с
  существующим `ExchangeAccountResponse`; `displayName` из запроса в этом
  случае не применяется;
- существующее неотключённое подключение того же provider-side аккаунта не
  изменяется — `409 ProblemDetails` с
  `code = exchange_account_already_exists`; для замены credentials активного
  подключения используется `PUT /api/v1/me/exchange-accounts/{id}/credentials`.

Неуспешная verification (`exchange_credentials_invalid`,
`exchange_permissions_rejected`, `exchange_unavailable` или неподтверждённая
provider identity) не восстанавливает отключённое подключение и не сохраняет
новые credentials. Проигравшая конкурентная попытка подключения или
восстановления того же provider-side аккаунта завершается
`409 concurrency_conflict` без частично созданного или восстановленного
состояния.

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
