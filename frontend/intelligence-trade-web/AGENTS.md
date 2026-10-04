# AGENTS.md

## Область действия

Этот файл применяется к `frontend/intelligence-trade-web` и дополняет корневой `../../AGENTS.md` правилами React-клиента (Web client).

## Стек

- React + TypeScript + Vite; package manager — npm, lock-file — `package-lock.json`, версия Node.js — `.nvmrc`.
- UI framework или design system не добавляй без отдельного решения: стили — собственный CSS.

## Build boundary

- Frontend собирает собственный artifact в `dist/`; каталог generated и не коммитится.
- Frontend не знает физического расположения `Intelligence.TradeSystem.Bff`: не указывай в Vite и других конфигурациях пути в backend или BFF `wwwroot`. BFF build и Docker получают готовый `dist` и размещают его в runtime `wwwroot`.
- В runtime клиент обслуживается BFF с того же origin; отдельный frontend origin, CORS и CDN не используются.

## Граница с backend

- Browser общается только с BFF того же origin (`/bff/**`); прямые запросы к `Api` или Identity token endpoint не выполняй.
- Access и refresh tokens никогда не попадают в JavaScript.
- Auth state не хранится в `localStorage`, `sessionStorage`, IndexedDB или cookies, доступных JavaScript; источник истины — `GET /bff/auth/session`.
- Business rules, торговые вычисления и правила риска не реализуются на клиенте: клиент отображает состояние, полученное от backend.

## UI

- Интерфейс dark-first.
- Каждая страница должна быть пригодна для mobile и desktop; адаптивность проверяется responsive E2E.

## Проверки

Из `frontend/intelligence-trade-web`: `npm run typecheck`, `npm run lint`, `npm run test:unit`, `npm run build`. Browser E2E (`npm run test:e2e`) выполняется против запущенного Compose stack.
