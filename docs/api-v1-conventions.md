# Соглашения контрактов пользовательского API v1

Канонической пользовательской REST-границей является `/api/v1/...`.
Изменения внутри v1 допускаются только аддитивные; несовместимое изменение
контракта требует новой версии маршрута.

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
имеет неявной семантики wildcard.

## Границы миграции

`/api/v1/exchange-accounts` является канонической v1-границей lifecycle
биржевого аккаунта. Незаверсионированные маршруты `/api/exchange-accounts/**`
удалены и не имеют compatibility alias.

Для user-owned exchange account resources отсутствующий и чужой идентификатор
возвращают одинаковый `404 ProblemDetails`: `resource_not_found`,
`urn:intelligence-trade:error:resource-not-found`, `Resource not found.`.

`/api/market-analysis/snapshot` и
`/api/market-analysis/{symbol}/llm-payload` остаются отдельными публичными
anonymous-контрактами и не переносятся под `/api/v1`.
