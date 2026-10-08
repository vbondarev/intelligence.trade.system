# Документация Intelligence.TradeSystem

Этот каталог разделяет долгосрочное продуктовое видение, продуктовые гипотезы, принятые архитектурные решения и точные технические контракты.

## Источники и их назначение

- [Корневой README](../README.md) — краткое описание продукта, текущее состояние, запуск и основные технические сведения.
- [Локальная разработка](local-development.md) — сборка, запуск и тестирование на Windows, Linux и macOS, Docker Compose/Aspire, локальные secrets, migrations и диагностика окружения.
- [Product Vision](product/vision.md) — долгосрочное целевое состояние продукта и основные продуктовые направления.
- [Capability Map](product/capability-map.md) — каталог текущих и будущих продуктовых возможностей и степень их зрелости.
- [Product Concepts](product/concepts.md) — общий продуктовый язык для Discovery. Эти понятия не являются автоматически Domain entities, API contracts или принятыми архитектурными решениями.
- [Product Scenarios](product/scenarios.md) — целевые пользовательские сценарии и бизнес-потоки без преждевременной фиксации технической реализации.
- [ROADMAP](../ROADMAP.md) — единственный актуальный источник статуса разработки и утверждённой последовательности этапов.
- [ADR](adr/) — принятые архитектурные решения и причины их выбора.
- [API v1 conventions](api-v1-conventions.md) и другие контрактные документы — точное поведение конкретных подсистем.
- [Web BFF contract](web-bff-contract.md) — граница React → frontend service → BFF → API: разделение source/build/container/deployment, same-origin routing, server-side browser session, CSRF, refresh, full SSO logout и explicit management boundary `/bff/me/exchange-accounts/**`.
- [Frontend architecture](frontend-architecture.md) — внутренняя архитектура React Web-клиента: composition и routing, page orchestration, capability-local modules, browser data access, владение состоянием, async consistency, error semantics, styling и testing boundaries. Не заменяет Web BFF contract (browser/BFF/security), API v1 conventions (API contract), руководство по локальной разработке (runtime и запуск) и frontend `AGENTS.md` (правила coding agents); onboarding разработчика — [README Web-клиента](../frontend/intelligence-trade-web/README.md).
- GitHub Issue — согласованный WHAT конкретной задачи.
- Approved Implementation Plan — immutable GitHub Issue comment с canonical marker, опубликованный `vbondarev/OWNER` и доступный по permalink; утверждённые изменения HOW сохраняются append-only Amendments. Если canonical artifact изменён/удалён или effective HOW невозможно восстановить, требуется `STOP → Human Decision`.
- [AGENTS.md](../AGENTS.md) — долговечные правила работы coding agents в репозитории.

## Как читать продуктовую документацию

Product Vision отвечает на вопрос «каким продукт должен стать».

Capability Map отвечает на вопрос «какие возможности существуют, исследуются или рассматриваются».

Product Concepts фиксируют язык обсуждения, а Product Scenarios — пользовательские истории и бизнес-потоки.

ROADMAP отвечает на другой вопрос: «что действительно принято к реализации и в какой последовательности».

Поэтому наличие capability в Product Vision, Capability Map, Concepts или Scenarios:

- не означает её включение в ROADMAP;
- не создаёт обязательства реализации;
- не задаёт порядок разработки;
- не является разрешением агенту создать Issue или начать implementation.

Переход от идеи к реализации требует отдельного Discovery и human decision.

## Жизненный цикл продуктовой идеи

Idea → Research → Concept → Human decision → Architecture / ADR при необходимости → ROADMAP → GitHub Issue → Implementation Plan → Human Gate → durable Approved Plan → Implementation.

Конкретный шаг может быть пропущен только когда он неприменим, но Idea, Research или Concept сами по себе не являются implementation scope. Актуальность WHAT/HOW перед merge подтверждается live External Re-review; Human Merge Gate является окончательной границей допуска к merge.
