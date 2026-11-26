# Карта кодовой базы

Выбери маршрут по симптому или типу изменения. Начни с контракта,
точки входа и ближайшего теста. Расширяй поиск, когда нужно проследить
вызов дальше или проверить соседнюю границу. [Оглавление документации](README.md)

## Realtime позиции

Подходит для задач про `position.updated`, переподключение и карточку позиции,
которая показывает устаревшие данные.

- **Ожидаемое поведение.** Начни с разделов о событиях и восстановлении через REST. [Realtime v1](realtime-v1-contract.md)
- **Точка входа.** Если событие доходит до браузера, найди обработчики `position.updated` и `recovery` в [PositionPage.tsx](../frontend/intelligence-trade-web/src/pages/PositionPage.tsx). Если событие теряется раньше, проследи цепочку через [RealtimeProvider.tsx](../frontend/intelligence-trade-web/src/realtime/RealtimeProvider.tsx) и [BrowserUpdatesHub.cs](../backend/src/Intelligence.TradeSystem.Bff/Realtime/BrowserUpdatesHub.cs).
- **Проверка.** Для поведения карточки начни с [PositionPage.test.tsx](../frontend/intelligence-trade-web/src/pages/PositionPage.test.tsx). Для границы BFF открой [BrowserUpdatesHubTests.cs](../backend/src/Intelligence.TradeSystem.Bff.Tests/BrowserUpdatesHubTests.cs). Для доставки события и последующего REST-чтения смотри [realtime-full-stack.spec.ts](../frontend/intelligence-trade-web/e2e/realtime-full-stack.spec.ts).

## Вход и сессия через BFF и Identity

Подходит для задач про перенаправление на вход, продление и истечение сессии,
выход, диалог повторного входа и клавиатурный фокус.

- **Ожидаемое поведение.** Для состояния серверной сессии и browser-facing endpoints начни с [Web BFF contract](web-bff-contract.md#server-side-browser-session).
- **Сервер возвращает неверное состояние или перенаправление.** Для входа, чтения сессии и выхода смотри [AuthEndpoints.cs](../backend/src/Intelligence.TradeSystem.Bff/Endpoints/AuthEndpoints.cs). Для срока и продления — [AuthEndpoints.SessionTiming.cs](../backend/src/Intelligence.TradeSystem.Bff/Endpoints/AuthEndpoints.SessionTiming.cs). Если сбой происходит на странице входа Identity, начни с [AccountController.cs](../backend/src/Intelligence.TradeSystem.Identity/Controllers/AccountController.cs). Ближайшие проверки — [SessionEndpointTests.cs](../backend/src/Intelligence.TradeSystem.Bff.Tests/SessionEndpointTests.cs), [SessionTimingTests.cs](../backend/src/Intelligence.TradeSystem.Bff.Tests/SessionTimingTests.cs) и [login-session.spec.ts](../frontend/intelligence-trade-web/e2e/login-session.spec.ts).
- **Ответ сервера корректен, но диалог или фокус ведёт себя неверно.** Начни с [SessionDeadline.tsx](../frontend/intelligence-trade-web/src/auth/SessionDeadline.tsx) и [session-timing.spec.ts](../frontend/intelligence-trade-web/e2e/session-timing.spec.ts). Для фактических объявлений читалки экрана используй [ручной протокол NVDA](testing/g09-session-expiry-nvda.md). Если интерфейс неверно определяет само состояние сессии, проверь [AuthProvider.tsx](../frontend/intelligence-trade-web/src/auth/AuthProvider.tsx).

## REST API v1

Подходит для задач про конкретную операцию API, формат JSON, HTTP-ошибку,
фильтрацию, следующую страницу результатов или OpenAPI.

- **Ожидаемое поведение.** Найди операцию в [таблице маршрутов](api-v1-conventions.md#стабильные-operationid) и открой её раздел. Для истории позиции смотри [Position timeline](api-v1-conventions.md#position-timeline).
- **Точка входа.** Найди операцию по `[Route]` и HTTP-атрибуту в [контроллерах API](../backend/src/Intelligence.TradeSystem.Api/Controllers/). Список и карточка позиции находятся в [PositionsController.cs](../backend/src/Intelligence.TradeSystem.Api/Controllers/PositionsController.cs), история — в [PositionTimelineController.cs](../backend/src/Intelligence.TradeSystem.Api/Controllers/PositionTimelineController.cs). Типы запросов и ответов ищи в [Contracts/V1](../backend/src/Intelligence.TradeSystem.Api/Contracts/V1/).
- **Проверка.** Для поведения конкретной операции начни с профильного теста контроллера. Для истории позиции, фильтров и курсора открой [PositionTimelineControllerTests.cs](../backend/src/Intelligence.TradeSystem.Api.Tests/PositionTimelineControllerTests.cs). Для общих правил API смотри [V1ApiContractTests.cs](../backend/src/Intelligence.TradeSystem.Api.Tests/V1ApiContractTests.cs). Для схемы и `operationId` — [V1OpenApiContractTests.cs](../backend/src/Intelligence.TradeSystem.Api.Tests/V1OpenApiContractTests.cs) и [V1OperationIds.cs](../backend/src/Intelligence.TradeSystem.Api/OpenApi/V1OperationIds.cs).
