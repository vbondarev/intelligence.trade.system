# Intelligence Trade Web

React Web-клиент `Intelligence.TradeSystem` — основной адаптивный браузерный интерфейс сопровождения открытых позиций. Клиент отображает состояние, полученное от backend, и не содержит бизнес-логики.

Текущее состояние (G-01 — G-03): вход и browser session через BFF, адаптивная оболочка, управление read-only подключениями Bybit и обзор выбранного подключения — сводка портфеля, текущие и закрытые позиции с фильтрами и ручная синхронизация. Статус следующих этапов определяет [ROADMAP](../../ROADMAP.md).

## Stack

- React 19, TypeScript, Vite, React Router;
- npm с `package-lock.json`; Node.js 24 (`.nvmrc`);
- Vitest и Testing Library для unit/component tests, Playwright для browser E2E;
- собственный CSS без UI framework.

## Структура

| Путь | Назначение |
| --- | --- |
| `src/app` | composition и routing |
| `src/auth` | browser session и вход/выход через BFF |
| `src/connections` | подключения биржевых аккаунтов |
| `src/portfolio` | обзор портфеля и позиций |
| `src/layout` | оболочка приложения |
| `src/pages` | route-level страницы |
| `src/styles` | design tokens и общие стили |
| `src/test` | инфраструктура unit tests |
| `e2e` | browser E2E |
| `nginx` | конфигурация production frontend service |

Ответственность областей, модель состояния и правила расширения описаны в [архитектуре Web-клиента](../../docs/frontend-architecture.md).

## Граница с backend

```text
Browser → relative /bff/** → frontend service (тот же origin) → BFF → API
```

Клиент обращается только к relative `/bff/**`; tokens в JavaScript не попадают. Точный контракт — [Web BFF contract](../../docs/web-bff-contract.md), пользовательский API — [API v1 conventions](../../docs/api-v1-conventions.md).

## Команды

```bash
npm ci
npm run typecheck
npm run lint
npm run test:unit
npm run build
```

`npm run test:e2e` выполняется против запущенного полного Compose stack; требования, переменные окружения и запуск описаны в [руководстве по локальной разработке](../../docs/local-development.md).

## Правила для coding agents

Долговечные правила frontend и routing на специализированные skills — в [AGENTS.md](AGENTS.md).
