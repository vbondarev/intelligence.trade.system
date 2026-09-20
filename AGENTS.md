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
- GitHub Issue для задачи разработки является источником её согласованного scope, требований и критериев приёмки.
- ADR фиксируют принятые архитектурные решения и причины.
- Контрактные документы описывают точное поведение конкретных подсистем.
- `AGENTS.md` должен содержать только долговечные правила работы coding agents, а не историю реализации.
- При изменении архитектуры, tooling или долговечных правил репозитория проверяй применимые `AGENTS.md` и обновляй их вместе с `README.md`/`ROADMAP.md`, если прежняя инструкция стала неверной или неполной.
- Внешние skills в `.agents/skills` сохраняй на языке и в форме оригинального источника; не переводи и не переписывай их как project-owned документацию.
- Используй skill только когда его назначение соответствует текущей задаче; не применяй нерелевантные skills автоматически.
- Каталог и происхождение внешних skills описаны в `.agents/skills/README.md`. При конфликте repository instructions и external skill приоритет имеют repository instructions и явная задача пользователя.
- Для полного технического review pull request используй `trade-system-pr-review`.

## Agent-first workflow разработки

Процесс разработки состоит из двух связанных контуров: Discovery формирует согласованный `WHAT`, Delivery реализует его через управляемый human-gate lifecycle.

Короткая семантика:

- Discussion / Discovery — формируем решение;
- GitHub Issue — source of truth для `WHAT`: согласованный scope, требования и критерии приёмки;
- утверждённый Implementation Plan — source of truth для `HOW` конкретной реализации;
- implementation prompt — `DO IT`;
- `AGENTS.md` — постоянные `RULES`.

### Discovery: от идеи до Issue

Используй последовательность:

`Discussion / Research → Human decisions → Issue`.

- Если задача ещё не зафиксирована в Issue, сначала исследуй проблему, варианты, риски, scope/out-of-scope и acceptance criteria. Не превращай неоднозначную идею в implementation автоматически.
- Архитектурные, продуктовые и scope-решения принимает человек. Агент может подготовить варианты и последствия, но не подменяет human decision.
- После согласования `WHAT` создай или синхронизируй GitHub Issue. Только после этого Issue становится source of truth для Delivery.
- `ROADMAP.md` определяет текущий этап и последовательность развития, но не заменяет Issue конкретной задачи.

### Delivery: от Issue до Human Merge Gate

Для нетривиальной задачи используй последовательность:

`Issue → Agent Implementation Plan → Human Plan Review → Human Gate → Implementation → Self-review → Fixes → Tests / CI-equivalent checks → Documentation sync → Final self-review → Commit → Push → Draft PR → PR sanity check → External Review → Review fixes → Re-review → Human Merge Gate`.

#### Plan и Human Gate

- Перед изменением кода изучи связанный Issue, `ROADMAP.md`, применимые `AGENTS.md`, path-specific instructions, ADR, contract docs и релевантную кодовую базу.
- Implementation Plan составляет агент после анализа этих источников. План должен перечислять затрагиваемые компоненты, архитектурные последствия, тесты, документацию, риски и границы изменения.
- Для нетривиальной задачи не начинай implementation до явного прохождения Human Gate — утверждения Implementation Plan человеком.
- Если Plan выявил неоднозначность, новый архитектурный выбор, конфликт требований или необходимость выйти за scope Issue, остановись и передай вопрос человеку. После human decision при необходимости сначала синхронизируй Issue, затем Plan и только после повторного Human Gate продолжай.
- Не расширяй scope соседними улучшениями, рефакторингом или следующими пунктами `ROADMAP.md` без явного решения человека. Отдельный долг фиксируй как follow-up.

#### Implementation, self-review и проверки

- Реализуй только согласованные Issue + approved Plan. Если во время implementation возникает новое архитектурное решение, scope expansion или конфликт source-of-truth, остановись и вернись к human decision вместо самостоятельного выбора.
- После завершения реализации перечитай полный diff относительно целевой ветки и выполни основной self-review до commit/PR.
- Во время основного self-review повторно сверь реализацию с Issue и approved Plan; проверь архитектурные границы, backward compatibility, security/concurrency, тесты, документацию, случайные изменения, временный debug-код и секреты. Найденные замечания исправь.
- Запусти все проверки, применимые к изменению. Неприменимые проверки не обозначай как успешные — явно укажи, почему они не требовались.
- Если реализация делает `README.md`, `ROADMAP.md`, ADR, contract docs или применимые `AGENTS.md` неверными или неполными, синхронизируй их до финального self-review, если Issue не задаёт другую границу.
- После fixes/tests/docs выполни короткий final self-review: ещё раз сверь final diff с Issue/Plan и убедись, что code/tests/docs согласованы.

#### Commit, Draft PR и sanity check

- Commit messages и Pull Request должны явно ссылаться на номер Issue.
- После успешного final self-review можно выполнить commit/push и открыть Draft PR.
- PR должен описывать фактическую реализацию, отклонения от плана, выполненные проверки, риски и намеренно исключённый scope.
- До External Review выполни PR sanity check: правильные base/head, `Closes #Issue`, ожидаемый diff, отсутствие случайных файлов и запуск применимого CI.

#### External Review и review fixes

- Self-review и External Review — разные этапы: self-review выполняет автор реализации, External Review — другой reviewer/agent/человек по актуальному head PR.
- Review comments не исполняй механически. Сначала проверь замечание по текущему коду, Issue, approved Plan, тестам и архитектурным правилам.
- Подтверждённое замечание исправь, добавь/обнови тесты при необходимости, повтори применимые проверки и ответь в review thread.
- Уже исправленное или устаревшее замечание объясни и resolve; дубликат свяжи с каноническим thread и закрой.
- Если review comment требует нового архитектурного решения, меняет scope или противоречит Issue/Plan/контракту, остановись и передай вопрос человеку. После решения синхронизируй Issue/Plan и повтори Human Gate для затронутого изменения.
- Review fixes выполняй в существующей branch/PR. Возврат PR в Draft после каждого review fix не обязателен, если отдельное правило или человек этого не требует.
- После существенных review fixes выполни self-review затронутого и общего diff, проверки и Re-review актуального head.

#### Human Merge Gate

- Не выполняй merge Pull Request без явного указания пользователя, даже если CI успешен, PR mergeable и все review threads закрыты.
- Merge является обязательным финальным Human Gate.

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
