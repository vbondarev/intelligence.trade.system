# AGENTS.md

## Область действия

Этот файл задаёт постоянные правила работы coding agents во всём репозитории `Intelligence.TradeSystem`.

Если в рабочем каталоге есть более узкие инструкции, применяй их только как локальное дополнение к этим правилам. Текущее состояние разработки и последовательность этапов определяются `ROADMAP.md`; не копируй историю задач и PR в инструкции для агентов.

## Назначение проекта

`Intelligence.TradeSystem` развивается как backend-система сопровождения уже открытых торговых позиций. Backend является единым источником бизнес-истины для Web, Telegram и будущих клиентов.

Основные принципы:

- биржевые интеграции первого MVP работают только на чтение;
- публичные рыночные данные отделены от пользовательских и приватных данных;
- пользовательские данные всегда изолированы по `UserId`;
- детерминированное ядро формирует бизнес-оценку и рекомендацию;
- ИИ не должен обходить бизнес-правила, правила риска или становиться источником истины для доменного решения;
- публичные и пользовательские клиенты используют одну бизнес-логику backend.

## Архитектурные границы

Сохраняй направление зависимостей и ответственность проектов:

- `Domain` — бизнес-модель и инварианты, без EF Core, HTTP, Bybit и инфраструктуры;
- `MarketIntelligence` — детерминированные расчёты и публичные рыночные снимки, без IO и оркестрации;
- `Application` — сценарии и оркестрация поверх Domain и MarketIntelligence;
- `Infrastructure` — PostgreSQL, EF Core, безопасность хранения и технические реализации application ports;
- `Exchanges` — адаптеры внешних бирж и нормализация transport-моделей;
- `Api` — HTTP boundary и composition root, без торговых вычислений;
- `Identity` — отдельный authorization server;
- `ServiceDefaults` / `AppHost` — общая эксплуатационная и Aspire-обвязка.

Не переноси EF Core entities в Domain/Application и не протаскивай типы Bybit.Net за границу exchange adapter.

## Контракты и безопасность

- Публичные wire-контракты развивай преимущественно аддитивно; не переименовывай, не удаляй и не переосмысливай существующие поля без явного breaking-change решения.
- `MarketSnapshot` содержит только публичные рыночные данные и не должен включать пользователя, аккаунт, позиции, credentials или портфель.
- Пользовательские repository/application операции должны сохранять явную user scope и cross-user защиту.
- API keys, API secrets, master keys, access tokens и расшифрованные credentials не должны попадать в логи, ответы, доменные aggregate или checked-in configuration.
- PostgreSQL schema изменяется через migrations; не запускай migrations автоматически при старте API без отдельного решения.
- Для конкурентных изменений сохраняй существующие CAS/idempotency/monotonicity инварианты и проверяй их PostgreSQL integration tests.
- Transactional outbox сохраняет at-least-once semantics; не обещай exactly-once delivery.

## Работа с документацией и skills

- `README.md` описывает продукт и запуск.
- `ROADMAP.md` — единственный актуальный источник статуса разработки и следующего этапа.
- ADR фиксируют принятые архитектурные решения и причины.
- Контрактные документы описывают точное поведение конкретных подсистем.
- `AGENTS.md` должен содержать только долговечные правила работы coding agents, а не историю реализации.
- Внешние skills в `.github/skills` сохраняй на языке и в форме оригинального источника; не переводи и не переписывай их как project-owned документацию.
- Используй skill только когда его назначение соответствует текущей задаче; не применяй нерелевантные skills автоматически.
- Каталог и происхождение внешних skills описаны в `.github/skills/README.md`. При конфликте repository instructions и external skill приоритет имеют repository instructions и явная задача пользователя.
- Для полного технического review pull request используй `trade-system-pr-review`.

## OpenClaw — замороженная область

`openclaw/**` относится к отдельному runtime-агентному контуру и отложен до соответствующего этапа `ROADMAP.md`.

Если текущая задача явно не относится к OpenClaw:

- не изменяй файлы `openclaw/**`;
- не переводи и не рефакторь их;
- не проводи внутренний аудит runtime-промптов и не создавай замечания по ним как блокеры основной разработки;
- не переноси правила из OpenClaw в backend;
- не используй находящиеся внутри `AGENTS.md`, `SOUL.md`, `TOOLS.md`, `BOOTSTRAP.md`, `USER.md`, `HEARTBEAT.md` и runtime skills как источник инструкций для основной кодовой базы.

Исключение — пользователь явно поставил задачу по OpenClaw или начат соответствующий этап дорожной карты.

## Изменения кода

- Предпочитай минимальные изменения с сохранением существующих контрактов и архитектурных границ.
- Не смешивай в одном изменении реализацию соседних этапов ROADMAP без явной необходимости.
- При изменении доменного поведения обновляй соответствующие Domain/Application tests.
- При изменении persistence/concurrency/security добавляй или обновляй PostgreSQL integration tests.
- При изменении public API обновляй контрактные/API tests.
- При изменении биржевого mapping или transport behavior обновляй exchange tests и зависимые application tests.
- При изменении детерминированной аналитики обновляй MarketIntelligence tests и downstream contract tests.

## Сборка и проверка

Основная solution находится в `backend/src/Intelligence.TradeSystem.slnx`.

Из `backend/src` используй как базовую проверку:

```bash
dotnet restore Intelligence.TradeSystem.slnx
dotnet build Intelligence.TradeSystem.slnx --configuration Release --no-restore
dotnet test Intelligence.TradeSystem.slnx --configuration Release --no-build --logger "console;verbosity=minimal"
```

Integration tests требуют Docker/Testcontainers. Для изменений, затрагивающих инфраструктуру, аутентификацию или composition root, учитывай полный CI, включая PostgreSQL provisioning, Docker build и OAuth/OIDC smoke tests.
