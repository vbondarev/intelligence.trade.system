# Архитектура React Web-клиента

Документ фиксирует принятую внутреннюю архитектуру `frontend/intelligence-trade-web` после G-01 — G-05. Он описывает фактически существующие решения и долговечные ограничения, но не проектирует будущие этапы: новая библиотека, архитектурная методология, directory layout или state-management подход принимаются только отдельным Human Decision.

Смежные источники истины:

- [Web BFF contract](web-bff-contract.md) — граница browser → frontend service → BFF → API, session, CSRF, refresh и token isolation;
- [API v1 conventions](api-v1-conventions.md) — пользовательский API contract и ProblemDetails;
- [Локальная разработка](local-development.md) — запуск, сборка и тестирование;
- [README Web-клиента](../frontend/intelligence-trade-web/README.md) — onboarding разработчика;
- [frontend `AGENTS.md`](../frontend/intelligence-trade-web/AGENTS.md) — долговечные правила для coding agents.

## 1. Назначение и границы

Web-клиент — React + TypeScript + Vite SPA и самостоятельная build/container/deployment unit. Он отображает состояние, полученное от backend, и не является источником бизнес-истины:

- торговые вычисления, risk/recommendation semantics, server-side фильтрация и агрегация выполняются backend и не дублируются в React;
- все защищённые пользовательские операции browser выполняет через same-origin BFF по relative URLs `/bff/**`;
- access и refresh tokens не попадают в JavaScript state.

Детали security boundary — session cookie, CSRF, refresh, logout — определяет только [Web BFF contract](web-bff-contract.md).

## 2. Текущая структура

| Каталог | Ответственность |
| --- | --- |
| `src/main.tsx` | entry point: подключение глобальных стилей и рендер `App` |
| `src/app` | application composition и routing |
| `src/auth` | browser session: provider, context, route guard, BFF auth API |
| `src/connections` | типы и BFF API управления подключениями биржевых аккаунтов |
| `src/portfolio` | типы, BFF API, formatting и UI-компоненты обзора портфеля и позиций |
| `src/position` | типы, BFF API, formatting, chart adapter, навигация и UI-компоненты страницы отдельной позиции |
| `src/layout` | оболочка приложения и общий loading screen |
| `src/pages` | route-level страницы |
| `src/styles` | design tokens и общие стили |
| `src/test` | общая инфраструктура unit/component tests |
| `e2e` | browser E2E tests (Playwright) |

Каталоги вроде `shared`, `features`, `entities` или `services` в текущей архитектуре отсутствуют и не подразумеваются.

## 3. Composition и routing

Цепочка composition: `main.tsx → App → AuthProvider → BrowserRouter → AppRoutes`.

| Route | Страница | Доступ |
| --- | --- | --- |
| `/` | `LandingPage` | публичный |
| `/app` | `HomePage` — обзор выбранного подключения | `ProtectedRoute` + `AppShell` |
| `/app/settings/connections` | `ConnectionsPage` — управление подключениями | `ProtectedRoute` + `AppShell` |
| `/app/positions/:id` | `PositionPage` — позиция, график, рыночный контекст, оценка и рекомендация, история событий | `ProtectedRoute` + `AppShell` |
| остальные | redirect на `/` | — |

Карточка позиции в списке `HomePage` ведёт на `PositionPage` и передаёт в router state путь возврата с текущими query parameters. Страница принимает только путь `/app` или `/app?…`; при прямом переходе или ином значении возврат ведёт в обзор подключения позиции.

`ProtectedRoute` показывает loading screen, пока session не определена, и перенаправляет на `/` без authenticated session. `AppShell` содержит общую навигацию, logout и вывод ошибки session; страницы рендерятся через `Outlet`.

## 4. Page orchestration

Страницы `src/pages/*` — route-level orchestration: они связывают capability modules, состояние страницы, URL state и отображение loading/error/empty outcomes. Например, `HomePage` выбирает подключение, читает портфель и позиции, управляет фильтрами и ручной синхронизацией, а представление делегирует компонентам `portfolio`.

Страница не становится business layer: она не вычисляет бизнес-показатели и не интерпретирует бизнес-правила, а только выбирает, какой ответ backend показать.

## 5. Capability-local organization

`auth`, `connections`, `portfolio` и `position` — сложившийся precedent локализации тесно связанной функциональности: типы ответа, BFF API access, formatting и UI-компоненты конкретной capability лежат рядом.

Это не принятие Feature-Sliced Design или другой сторонней методологии. Симметрия не требуется: `connections` сейчас содержит только типы и API, а UI управления подключениями находится в `ConnectionsPage`. Capability module создаётся, когда код действительно связан одной capability, а не «для порядка».

## 6. Browser data access

Каждая capability имеет собственный модуль доступа к BFF: `authApi.ts`, `connectionsApi.ts`, `portfolioApi.ts`, `positionApi.ts`. Общие принципы:

- запросы используют relative `/bff/**` и `credentials: 'same-origin'`;
- чтение пользовательского состояния выполняется с `cache: 'no-store'`;
- unsafe-запросы получают antiforgery token и передают его по правилам [Web BFF contract](web-bff-contract.md);
- входящий JSON рассматривается как `unknown` и проверяется type guards до использования; некорректный ответ становится ошибкой, а не частично типизированным объектом;
- ошибки представлены capability-specific классами: `AuthApiError` хранит machine-readable HTTP `status`, а `ConnectionsApiError`, `PortfolioApiError` и `PositionApiError` дополнительно разбирают стабильные `ProblemDetails.code` и `traceId`; `ConnectionsApiError` также может содержать field-level `errors`, а `PositionApiError` — известный `reason` ответа `409 position_not_evaluable`;
- параметры запросов передаются в форме, которую ожидает API; валидацию, фильтрацию и разбор cursor выполняет backend.

Generic API client в текущей архитектуре отсутствует. Его введение, как и генерация TypeScript client из OpenAPI, — отдельное архитектурное решение.

## 7. Владение состоянием

| Вид состояния | Источник и место хранения | Precedent |
| --- | --- | --- |
| Server/business state | backend/BFF; клиент хранит последний полученный ответ и перечитывает его через REST | список подключений, портфель, позиции; после mutation или sync данные перечитываются, а не правятся локально |
| Воспроизводимое состояние представления | URL path и query parameters | `HomePage`: `account`, `view`, `state`, `side`, `symbol`; `PositionPage`: `id` в path и `interval` графика |
| Навигационный контекст | router state | путь возврата из `PositionPage` в список позиций с теми же фильтрами |
| Локальное interaction/request state | `useState`/`useRef` страницы или компонента | loading, pending, notices, открытая панель, загрузка следующей страницы, состояние sync и выполняемой оценки, раскрытие и фильтр типов истории позиции |
| Cross-cutting browser session | `AuthProvider` и `AuthContext`; источник — `GET /bff/auth/session` | session не хранится в `localStorage`, `sessionStorage`, IndexedDB или доступных JavaScript cookies |

URL-state используется, когда состояние представления должно переживать reload и передаваться ссылкой. Это не правило «всё состояние в URL»: временное interaction/request state остаётся локальным, если нет причины поднимать его выше.

Отдельный state-management или data-fetching framework не используется.

## 8. Async consistency

Асинхронные результаты не должны перезаписывать более новое состояние:

- effect-запросы отменяются через `AbortController` при unmount или смене входных данных;
- generation/reference guards отбрасывают поздние ответы и ошибки более ранних запросов;
- account-scoped результат хранится вместе с идентификатором подключения или ключом запроса и не показывается для другого подключения;
- continuation по cursor относится только к поколению первой страницы, для которого получен cursor;
- `PositionPage` загружает позицию, рыночный контекст, свечи и оценку независимыми запросами без общего `Promise.all`: ошибка одной области не скрывает остальные. Результат каждой области связан с `positionId`, свечи — ещё и с `interval`; результат команды оценки применяется, только если пользователь остался на той же позиции.
- История позиции (`position/PositionTimeline.tsx`) — независимая область страницы. Блок изначально свёрнут: первая страница запрашивается только при первом раскрытии, продолжение — явной командой «Показать ещё», актуальное состояние — командой «Обновить»; polling, sync и оценку блок не запускает. Каждое чтение относится к поколению загрузки: смена фильтра типов, обновление и unmount отменяют прежний запрос через `AbortController`, а поздний ответ или ошибка прежнего поколения не применяются. Обновление и смена фильтра сбрасывают cursor и заменяют загруженные страницы новой первой страницей. При смене позиции компонент монтируется заново (`key` по `id`) и не переносит историю другой позиции. Компонент отображается только после успешной загрузки принадлежащей пользователю позиции: её `settlementAsset` нужен для корректной подписи исторических `positionValue` и `unrealizedPnl`. После монтирования timeline не зависит от загрузки рыночного контекста, свечей и текущей оценки: их ошибки не блокируют историю, а ошибка истории не скрывает остальные области страницы.

## 9. Error semantics

UI использует только доступные для конкретной capability machine-readable признаки: HTTP `status`, а для connections/portfolio/position — также стабильный `ProblemDetails.code` (для `409 position_not_evaluable` — и `reason`). `AuthApiError` сейчас содержит только `status` и не разбирает `ProblemDetails`. `title` и `detail` не используются для ветвления и не показываются как основной текст ошибки. `traceId` показывается как диагностический код там, где соответствующий client API его разбирает. `401` завершает browser session flow с возможностью войти снова; для connections/portfolio/position тот же session-ended outcome также определяется по `authentication_required`. `404` позиции означает недоступную страницу без различия между несуществующей и чужой позицией.

Тексты ошибок пока задаются на уровне страниц. Единый product error UX относится к G-09.

## 10. Presentation и formatting

Backend задаёт semantics значений; frontend отвечает только за представление. Formatting — числа, деньги, относительное время, подписи enum — локализован в capability, например `portfolio/portfolioFormatting.ts`. `null` от backend отображается как отсутствующее значение, а не как ноль. Formatting не пересчитывает и не агрегирует бизнес-показатели.

Оценка и рекомендация позиции показываются только из ответа backend: отсутствие результата или рекомендации обозначается явно и не заменяется действием «Удерживать». Frontend не вводит собственных порогов свежести: он показывает backend `DataQuality`, истечение `validUntil` оценки и рекомендации относительно текущего времени и то, что рыночный контекст страницы новее снимка, использованного в оценке. Статус валидности обновляется на открытой странице в момент наступления ближайшего срока (`position/useValidityClock.ts`) без повторных запросов, переоценки или sync. Условия увеличения позиции (`addDecision.conditions`) показываются как зафиксированные backend требования без проверки их выполнения; их отсутствие не означает отсутствия ограничений.

События истории позиции показываются в порядке ответа API (newest-first) без локальной сортировки, группировки и восстановления отсутствующих данных. Изменение позиции подписывается как наблюдённое изменение состояния, а не как ордер или исполнение; отсутствующее предыдущее состояние (`before = null`) обозначается явно. Историческая оценка и рекомендация показывают сохранённые данные момента их формирования: истёкший `validUntil` не помечается как проблема, а статус рекомендации подписан как текущий сохранённый статус записи.

### Свечной график

График позиции использует TradingView Lightweight Charts (API v5: `createChart`, `CandlestickSeries`, `addSeries`) без React wrapper:

- `position/positionChartAdapter.ts` преобразует свечи API в UTC seconds в строго возрастающем порядке, отбрасывая некорректные значения, и выбирает уровни: entry, break-even, SL, TP, liquidation и support/resistance timeframe, соответствующего интервалу графика. Собственных уровней frontend не вычисляет;
- `position/PositionChart.tsx` создаёт chart один раз после появления контейнера, обновляет серию и price lines без пересоздания chart, следует за размером контейнера через `autoSize` и вызывает `chart.remove()` при unmount;
- attribution logo TradingView остаётся включённым.

## 11. Styling

UI kit — [Mantine](https://mantine.dev); `MantineProvider` подключён в `app/App.tsx` с `forceColorScheme="dark"`, светлая схема не поддерживается. Новые типовые UI-элементы реализуются с помощью Mantine; собственные компоненты допускаются, если библиотека не предоставляет подходящего решения или требуется специализированное поведение. Существующие экраны сохраняют собственный CSS; их массовая миграция в эту задачу не входит.

- `styles/theme.ts` — тема Mantine: палитры `dark` и `accent` и шрифт синхронизированы с `tokens.css`;
- `styles/tokens.css` — foundation: CSS custom properties цветов, типографики, отступов и радиусов, `color-scheme: dark`;
- `styles/app.css` — общие стили приложения и компонентов; подключается после `@mantine/core/styles.css` и имеет приоритет над глобальными стилями Mantine;
- `postcss.config.js` — `postcss-preset-mantine` и breakpoint-переменные Mantine для собственных CSS-файлов.

Интерфейс dark-first. Tokens — основа визуальной согласованности, но не завершённая design system; её формирование относится к G-09.

## 12. Responsive и accessibility

- Каждая страница должна быть пригодна для mobile и desktop; responsive E2E проверяет основные сценарии на mobile и desktop viewport и отсутствие горизонтальной прокрутки.
- Базовый уровень доступности обеспечивается семантическим HTML, связанными labels, landmarks/regions с доступными именами и `role="status"`/`role="alert"` для асинхронных сообщений.
- Систематическая работа над адаптивностью и доступностью относится к G-08.

## 13. Testing boundaries

- Unit/component tests — Vitest + Testing Library, colocated `*.test.ts(x)` рядом с кодом; общая инфраструктура, fetch mock и fixtures — `src/test`. jsdom не реализует canvas, поэтому component tests подменяют `lightweight-charts` и проверяют интеграцию с её API, а отрисовку графика проверяет browser E2E. Они проверяют client logic: разбор ответов, построение запросов, URL-state, async consistency и отображение outcomes.
- Browser E2E — Playwright в `e2e`, выполняется против полного Compose stack через public origin frontend: routing, login/session/logout и responsive сценарии. Business data в E2E при необходимости подменяется на уровне browser routes.
- Frontend tests не дублируют проверки контрактов BFF/API: они покрываются backend, BFF и contract tests. Full-stack path выполняется в CI.

## 14. Как расширять frontend

1. Определи route/page responsibility: новая страница или часть существующей.
2. Backend/BFF остаётся источником server/business state; недостающая бизнес-семантика добавляется на backend, а не вычисляется в React.
3. Связанный код capability локализуй в capability module, когда он действительно cohesive; не создавай глобальные слои «для симметрии».
4. Выбирай место состояния по разделу 7: URL — для воспроизводимого представления, локальное — для interaction/request state.
5. Новая dependency, framework или архитектурный паттерн требуют отдельного решения; рекомендации external skills таким решением не являются.
6. Client logic покрывай unit tests; при изменении runtime UI behavior обновляй responsive E2E.
7. Обновляй этот документ при изменении долговечной frontend architecture.
