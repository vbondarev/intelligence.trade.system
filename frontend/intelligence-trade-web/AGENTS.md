# AGENTS.md

## Область действия

Этот файл применяется к `frontend/intelligence-trade-web` и дополняет корневой `../../AGENTS.md` правилами React-клиента (Web client).

## Источники истины

- [README Web-клиента](README.md) — onboarding: назначение, stack, структура и команды.
- [Архитектура Web-клиента](../../docs/frontend-architecture.md) — внутренняя архитектура React: routing, page orchestration, capability modules, владение состоянием, async consistency, error semantics, styling и testing boundaries.
- [Web BFF contract](../../docs/web-bff-contract.md) — граница browser/BFF и security.
- [API v1 conventions](../../docs/api-v1-conventions.md) — пользовательский API contract.
- [Локальная разработка](../../docs/local-development.md) — локальный runtime и тестирование.

## Skills

- Реализация React-кода и производительность — `.agents/skills/react-best-practices/SKILL.md`.
- Reusable component API, composition, context/provider patterns, разрастание boolean props — `.agents/skills/composition-patterns/SKILL.md`.
- UI/UX/accessibility review — `.agents/skills/trade-system-web-design-review/SKILL.md`.

Приоритет: Issue и Approved Implementation Plan → repository rules и этот файл → frontend architecture и contracts → рекомендации skill. React skills — external upstream artifacts с примерами Next.js и сторонних библиотек: рекомендация skill не разрешает новую dependency, framework или изменение архитектуры и применяется только когда соответствует React + Vite SPA проекта.

## Стек

- React + TypeScript + Vite; package manager — npm, lock-file — `package-lock.json`, версия Node.js — `.nvmrc`.
- UI framework или design system не добавляй без отдельного решения: стили — собственный CSS.

## Build и deployment boundary

- Frontend — самостоятельная build, container и deployment unit. Vite собирает artifact в `dist/`; каталог generated и не коммитится.
- Frontend не знает физического расположения backend: не указывай в Vite, Dockerfile и других конфигурациях пути в `backend/`, BFF project или его output.
- Production image (`Dockerfile`, context — этот каталог): Node.js stage собирает `dist`, nginx runtime раздаёт только `dist` и `nginx/default.conf.template`. Image не содержит .NET, backend source/binaries и server secrets.
- nginx — единый browser-facing origin: React static assets и SPA fallback, а `/bff/**`, `/signin-oidc` и `/signout-callback-oidc` проксируются во внутренний BFF. Эти paths никогда не попадают в SPA fallback.
- Адрес BFF задаётся только deployment setting `BFF_UPSTREAM` (`scheme://host[:port]`) и не попадает в React bundle. Server secrets (client secret, dev password, tokens, credential keys) frontend container не получает.
- Public scheme, который nginx передаёт BFF в `X-Forwarded-Proto`, задаётся только обязательным deployment setting `PUBLIC_SCHEME`: `http` при прямом HTTP-доступе browser к frontend (local Compose/Aspire), `https`, если TLS завершается перед frontend container. Не выводи его из `$scheme` и не пересылай `X-Forwarded-Proto` client. Без корректных `BFF_UPSTREAM` и `PUBLIC_SCHEME` container не стартует; envsubst подставляет только эти две переменные.
- Отдельный browser origin для BFF, CORS и CDN не используются.

## Граница с backend

- Browser общается с BFF только через relative URLs того же origin (`/bff/**`); internal адрес BFF, прямые запросы к `Api` и Identity token endpoint в клиенте не используются.
- Access и refresh tokens никогда не попадают в JavaScript.
- Auth state не хранится в `localStorage`, `sessionStorage`, IndexedDB или cookies, доступных JavaScript; источник истины — `GET /bff/auth/session`.
- Business rules, торговые вычисления и правила риска не реализуются на клиенте: клиент отображает состояние, полученное от backend.

## UI

- Интерфейс dark-first.
- Каждая страница должна быть пригодна для mobile и desktop; адаптивность проверяется responsive E2E.

## Проверки

Из `frontend/intelligence-trade-web`: `npm run typecheck`, `npm run lint`, `npm run test:unit`, `npm run build`; image — `docker build .`. Browser E2E (`npm run test:e2e`) выполняется против запущенного Compose stack через public origin frontend service.
