# AGENTS.md

## Область действия

Этот файл задаёт постоянные правила coding agents для всего репозитория. Более узкие `AGENTS.md` и path-specific instructions дополняют его для соответствующих файлов.

## Языковая политика

Человекочитаемый текст проекта, включая комментарии, XML documentation, документацию, Issue, Implementation Plan, Pull Request и review-текст, пишется на русском языке. Общеупотребимые технические термины сохраняй на языке оригинала, если это естественная и однозначная форма для разработчиков.

Не переводи имена типов, методов, свойств, enum, namespace, файлов, API routes, operationId, wire values, команды, названия библиотек, продуктов, протоколов и GitHub jobs/checks. Не добавляй комментарии только ради формального документирования: они должны объяснять причину, ограничение, инвариант или нетривиальное поведение.

## Назначение проекта

`Intelligence.TradeSystem` развивается как backend-система сопровождения уже открытых торговых позиций. Backend является единым источником бизнес-истины для Web, Telegram и будущих клиентов.

- Биржевые интеграции первого MVP работают только на чтение.
- Публичные рыночные данные отделены от пользовательских и приватных данных.
- Пользовательские данные всегда изолированы по `UserId`.
- Детерминированное ядро формирует бизнес-оценку и рекомендацию.
- ИИ не обходит бизнес-правила и правила риска и не становится источником истины для доменного решения.
- Публичные и пользовательские клиенты используют одну бизнес-логику backend.

## Архитектурные границы

Сохраняй направление зависимостей и ответственность проектов:

- `Domain` — бизнес-модель и инварианты без EF Core, HTTP, Bybit и инфраструктуры.
- `MarketIntelligence` — детерминированные расчёты и публичные рыночные снимки без IO и оркестрации.
- `Application` — сценарии и оркестрация поверх Domain и MarketIntelligence.
- `Infrastructure` — PostgreSQL, EF Core, безопасность хранения и технические реализации application ports.
- `Exchanges` — адаптеры внешних бирж и нормализация transport-моделей.
- `Api` — HTTP boundary и composition root без торговых вычислений.
- `Identity` — отдельный authorization server.
- `Web` — host React-клиента и BFF: browser session и посредничество с OAuth tokens без business logic; к `Api` обращается только по HTTP с Bearer token.
- `ServiceDefaults` / `AppHost` — общая эксплуатационная и Aspire-обвязка.

Не переноси EF Core entities в Domain/Application и не протаскивай типы Bybit.Net за границу exchange adapter.

## Контракты и безопасность

- Публичные wire-контракты развивай преимущественно аддитивно; не переименовывай, не удаляй и не переосмысливай существующие поля без явного breaking-change решения.
- `MarketSnapshot` содержит только публичные рыночные данные: не включай в него пользователя, аккаунт, позиции, credentials или портфель.
- Пользовательские repository/application операции сохраняют явный `UserId` scope и cross-user защиту.
- API keys, API secrets, master keys, access tokens и расшифрованные credentials не должны попадать в логи, ответы, доменные aggregate или checked-in configuration.
- PostgreSQL schema меняется через migrations; не применяй migrations автоматически при старте API без отдельного решения.
- Для конкурентных изменений сохраняй существующие CAS/idempotency/monotonicity invariants и проверяй их PostgreSQL integration tests.
- Transactional outbox сохраняет at-least-once semantics; не обещай exactly-once delivery.

## Документация и skills

- `README.md` описывает продукт на обзорном уровне, текущее состояние и запуск; не дублируй в нём подробную долгосрочную продуктовую документацию.
- `docs/README.md` — точка входа в документацию и описание назначения source-of-truth документов.
- `docs/product/vision.md` фиксирует долгосрочную цель и направления продукта.
- `docs/product/capability-map.md` описывает текущие и будущие capabilities; порядок записей не задаёт последовательность разработки.
- `docs/product/concepts.md` фиксирует продуктовый язык для Discovery. Product Concept не становится автоматически Domain entity, API contract, persistence model или принятым архитектурным решением.
- `docs/product/scenarios.md` фиксирует пользовательские сценарии без преждевременного выбора технической реализации.
- `ROADMAP.md` — единственный актуальный источник статуса разработки и утверждённой последовательности этапов.
- GitHub Issue — source of truth для согласованного scope, требований и acceptance criteria (`WHAT`).
- Approved Implementation Plan — source of truth для согласованной реализации (`HOW`).
- ADR фиксируют принятые архитектурные решения; contract docs описывают точное поведение подсистем.
- `AGENTS.md` хранит долговечные правила, а Agent Skills — специализированные процедуры; не записывай в AGENTS.md историю реализации и PR.
- Capability со статусом Idea, Research или Concept не разрешает создать Issue, добавить этап в ROADMAP или начать implementation без отдельного human decision.
- При изменении архитектуры, tooling или долговечных repository rules синхронизируй применимые инструкции и документы, если прежнее описание стало неверным или неполным.
- Внешние skills в `.agents/skills` сохраняй на языке и в форме оригинального источника. При конфликте приоритет имеют repository instructions и явная задача пользователя.
- `.agents/skills/README.md` фиксирует происхождение external skills и правила их обновления. Используй skill только когда его назначение соответствует задаче.

## Agent-first workflow

Соблюдай source-of-truth hierarchy: Issue задаёт `WHAT`, Approved Implementation Plan — `HOW`, `AGENTS.md` — долговечные `RULES`, а implementation prompt — выполнение согласованного решения.

Выбирай project-owned workflow skill по стадии:

- Discovery до согласованного Issue: `.agents/skills/trade-system-discovery/SKILL.md`.
- Delivery после Issue до PR sanity check: `.agents/skills/trade-system-delivery/SKILL.md`.
- External Review текущего PR: `.agents/skills/trade-system-pr-review/SKILL.md`.

Для нетривиальной задачи implementation запрещён до явного Human Gate — утверждения Implementation Plan человеком. Не принимай самостоятельно новое архитектурное решение и не расширяй scope; при конфликте источников или необходимости изменить согласованный `WHAT` остановись и верни вопрос человеку.

После Human Gate сохраняй утверждённый Implementation Plan отдельным GitHub Issue comment с первой строкой `# Approved Implementation Plan`; implementation начинается только после получения permalink. Canonical base Plan и Amendments утверждаются `vbondarev` с `author_association=OWNER`, остаются immutable и не редактируются. Изменения утверждённого `HOW` оформляются отдельными append-only Amendments после нового Human Gate. Если canonical artifact изменён/удалён или effective HOW невозможно восстановить, остановись и запроси Human Decision. PR должен позволять восстановить Issue, base Plan и все Amendments; implementation и review не зависят от предыдущей AI-сессии.

Self-review автора и External Review — разные этапы. External Re-review выполняется по текущему GitHub state; изменение reviewed PR head, base или effective HOW инвалидирует предыдущий Re-review. Review comments не являются автоматическими командами: проверяй их по текущему коду, Issue, Approved Plan и применимым правилам.

Не выполняй merge без отдельного явного Human Merge Gate пользователя, независимо от состояния CI и review threads; Human Merge Gate является окончательной границей допуска к merge.

## OpenClaw — замороженная область

`openclaw/**` относится к отдельному runtime-агентному контуру и отложен до соответствующего этапа `ROADMAP.md`.

Если задача явно не относится к OpenClaw:

- не изменяй, не переводи и не рефакторь файлы `openclaw/**`;
- не проводи внутренний аудит runtime-промптов и не создавай замечания по ним как блокеры основной разработки;
- не переноси правила из OpenClaw в backend;
- не используй находящиеся внутри `AGENTS.md`, `SOUL.md`, `TOOLS.md`, `BOOTSTRAP.md`, `USER.md`, `HEARTBEAT.md` и runtime skills как инструкции для основной кодовой базы.

## Изменения кода

- Предпочитай минимальные изменения с сохранением существующих контрактов, DI-границ и направления зависимостей.
- Не смешивай соседние этапы ROADMAP без явной необходимости.
- При изменении доменного поведения обновляй соответствующие Domain/Application tests.
- При изменении persistence/concurrency/security добавляй или обновляй PostgreSQL integration tests.
- При изменении public API обновляй контрактные/API tests.
- При изменении биржевого mapping или transport behavior обновляй exchange tests и зависимые application tests.
- При изменении детерминированной аналитики обновляй MarketIntelligence tests и downstream contract tests.

## Сборка и проверки

Основная solution находится в `backend/src/Intelligence.TradeSystem.slnx`. Из `backend/src` используй базовые команды:

```bash
dotnet restore Intelligence.TradeSystem.slnx
dotnet build Intelligence.TradeSystem.slnx --configuration Release --no-restore
dotnet test Intelligence.TradeSystem.slnx --configuration Release --no-build --logger "console;verbosity=minimal"
```

Integration tests требуют Docker/Testcontainers. Для изменений инфраструктуры, аутентификации или composition root учитывай полный CI, включая PostgreSQL provisioning, Docker build и OAuth/OIDC smoke tests.
