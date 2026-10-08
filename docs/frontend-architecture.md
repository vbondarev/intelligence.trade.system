# Архитектура React Web-клиента

Документ фиксирует принятую внутреннюю архитектуру `frontend/intelligence-trade-web` после G-01 — G-03. Он описывает фактически существующие решения и долговечные ограничения, но не проектирует будущие этапы: новая библиотека, архитектурная методология, directory layout или state-management подход принимаются только отдельным Human Decision.

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
| остальные | redirect на `/` | — |

`ProtectedRoute` показывает loading screen, пока session не определена, и перенаправляет на `/` без authenticated session. `AppShell` содержит общую навигацию, logout и вывод ошибки session; страницы рендерятся через `Outlet`.

## 4. Page orchestration

Страницы `src/pages/*` — route-level orchestration: они связывают capability modules, состояние страницы, URL state и отображение loading/error/empty outcomes. Например, `HomePage` выбирает подключение, читает портфель и позиции, управляет фильтрами и ручной синхронизацией, а представление делегирует компонентам `portfolio`.

Страница не становится business layer: она не вычисляет бизнес-показатели и не интерпретирует бизнес-правила, а только выбирает, какой ответ backend показать.

## 5. Capability-local organization

`auth`, `connections` и `portfolio` — сложившийся precedent локализации тесно связанной функциональности: типы ответа, BFF API access, formatting и UI-компоненты конкретной capability лежат рядом.

Это не принятие Feature-Sliced Design или другой сторонней методологии. Симметрия не требуется: `connections` сейчас содержит только типы и API, а UI управления подключениями находится в `ConnectionsPage`. Capability module создаётся, когда код действительно связан одной capability, а не «для порядка».

## 6. Browser data access

Каждая capability имеет собственный модуль доступа к BFF: `authApi.ts`, `connectionsApi.ts`, `portfolioApi.ts`. Общие принципы:

- запросы используют relative `/bff/**` и `credentials: 'same-origin'`;
- чтение пользовательского состояния выполняется с `cache: 'no-store'`;
- unsafe-запросы получают antiforgery token и передают его по правилам [Web BFF contract](web-bff-contract.md);
- входящий JSON рассматривается как `unknown` и проверяется type guards до использования; некорректный ответ становится ошибкой, а не частично типизированным объектом;
- ошибки представлены capability-specific классами: `AuthApiError` хранит machine-readable HTTP `status`, а `ConnectionsApiError` и `PortfolioApiError` дополнительно разбирают стабильные `ProblemDetails.code` и `traceId`; `ConnectionsApiError` также может содержать field-level `errors`;
- параметры запросов передаются в форме, которую ожидает API; валидацию, фильтрацию и разбор cursor выполняет backend.

Generic API client в текущей архитектуре отсутствует. Его введение, как и генерация TypeScript client из OpenAPI, — отдельное архитектурное решение.

## 7. Владение состоянием

| Вид состояния | Источник и место хранения | Precedent |
| --- | --- | --- |
| Server/business state | backend/BFF; клиент хранит последний полученный ответ и перечитывает его через REST | список подключений, портфель, позиции; после mutation или sync данные перечитываются, а не правятся локально |
| Воспроизводимое состояние представления | URL query parameters | `HomePage`: `account`, `view`, `state`, `side`, `symbol` |
| Локальное interaction/request state | `useState`/`useRef` страницы или компонента | loading, pending, notices, открытая панель, загрузка следующей страницы, состояние sync |
| Cross-cutting browser session | `AuthProvider` и `AuthContext`; источник — `GET /bff/auth/session` | session не хранится в `localStorage`, `sessionStorage`, IndexedDB или доступных JavaScript cookies |

URL-state используется, когда состояние представления должно переживать reload и передаваться ссылкой. Это не правило «всё состояние в URL»: временное interaction/request state остаётся локальным, если нет причины поднимать его выше.

Отдельный state-management или data-fetching framework не используется.

## 8. Async consistency

Асинхронные результаты не должны перезаписывать более новое состояние:

- effect-запросы отменяются через `AbortController` при unmount или смене входных данных;
- generation/reference guards отбрасывают поздние ответы и ошибки более ранних запросов;
- account-scoped результат хранится вместе с идентификатором подключения или ключом запроса и не показывается для другого подключения;
- continuation по cursor относится только к поколению первой страницы, для которого получен cursor.

## 9. Error semantics

UI использует только доступные для конкретной capability machine-readable признаки: HTTP `status`, а для connections/portfolio — также стабильный `ProblemDetails.code`. `AuthApiError` сейчас содержит только `status` и не разбирает `ProblemDetails`. `title` и `detail` не используются для ветвления и не показываются как основной текст ошибки. `traceId` показывается как диагностический код там, где соответствующий client API его разбирает. `401` завершает browser session flow с возможностью войти снова; для connections/portfolio тот же session-ended outcome также определяется по `authentication_required`.

Тексты ошибок пока задаются на уровне страниц. Единый product error UX относится к G-08.

## 10. Presentation и formatting

Backend задаёт semantics значений; frontend отвечает только за представление. Formatting — числа, деньги, относительное время, подписи enum — локализован в capability, например `portfolio/portfolioFormatting.ts`. `null` от backend отображается как отсутствующее значение, а не как ноль. Formatting не пересчитывает и не агрегирует бизнес-показатели.

## 11. Styling

Используется собственный CSS без UI framework:

- `styles/tokens.css` — foundation: CSS custom properties цветов, типографики, отступов и радиусов, `color-scheme: dark`;
- `styles/app.css` — общие стили приложения и компонентов.

Интерфейс dark-first. Tokens — основа визуальной согласованности, но не завершённая design system; её формирование относится к G-08.

## 12. Responsive и accessibility

- Каждая страница должна быть пригодна для mobile и desktop; responsive E2E проверяет основные сценарии на mobile и desktop viewport и отсутствие горизонтальной прокрутки.
- Базовый уровень доступности обеспечивается семантическим HTML, связанными labels, landmarks/regions с доступными именами и `role="status"`/`role="alert"` для асинхронных сообщений.
- Систематическая работа над адаптивностью и доступностью относится к G-07.

## 13. Testing boundaries

- Unit/component tests — Vitest + Testing Library, colocated `*.test.ts(x)` рядом с кодом; общая инфраструктура и fetch mock — `src/test`. Они проверяют client logic: разбор ответов, построение запросов, URL-state, async consistency и отображение outcomes.
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
