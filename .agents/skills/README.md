# Skills проекта

Каталог `.agents/skills` содержит project-owned skills и выбранные external Agent Skills для `Intelligence.TradeSystem`.

## Project-owned skills

Project-owned skills не заменяют долговечные правила из применимых `AGENTS.md` и делятся на две категории.
Имена в этом реестре должны совпадать с каталогами и frontmatter project-owned skills, а каждый skill указывается ровно в одной категории; CI проверяет эту структуру и обязательные routing-ссылки.

### Workflow skills

Глобальные стадии agent-first lifecycle `Discovery → Delivery → External Review`. Routing на них обязателен в корневом `AGENTS.md` и `.github/copilot-instructions.md`.

| Skill | Ответственность |
| --- | --- |
| `trade-system-discovery` | Discovery до согласованного GitHub Issue |
| `trade-system-delivery` | Plan, Human Gate и Delivery до Draft PR sanity check |
| `trade-system-pr-review` | External Review текущего Pull Request |

### Specialized skills

Процедуры конкретной технической области. Они не являются стадиями lifecycle и не добавляют обязательных workflow gates; routing на них задают локальные instructions своей области.

| Skill | Ответственность |
| --- | --- |
| `trade-system-web-design-review` | UI/UX/accessibility review React Web-клиента по локальному snapshot Web Interface Guidelines; routing — `frontend/intelligence-trade-web/AGENTS.md` |

## External skills

- External skills не переводятся и не редактируются локально.
- Repository instructions и явная задача пользователя имеют приоритет над external skill.
- Framework-specific рекомендации external skill (например, Next.js или server-side) не становятся архитектурными требованиями проекта, если не соответствуют его фактическому stack.
- Примеры сторонних библиотек, frameworks и инфраструктурных технологий во внешних skills не являются разрешением добавлять новые зависимости в проект. Такие изменения допускаются только при явном требовании задачи и с соблюдением архитектурных границ репозитория.
- Если внешний skill нужно изменить специально для проекта, не редактируй upstream-копию:
  создай отдельный project-owned skill с другим именем.
- При обновлении external skill заменяй его целиком после review upstream diff.
- `openclaw/**` не входит в этот каталог и управляется отдельно.

## Происхождение skills

### Aaronontheweb/dotnet-skills

Pinned commit: `13e26d39ed01d97ea592235d041304d289f4ba07`

| Skill | Upstream path |
| --- | --- |
| `api-design` | `skills/csharp-api-design/` |
| `modern-csharp-coding-standards` | `skills/csharp-coding-standards/` |
| `type-design-performance` | `skills/csharp-type-design-performance/` |
| `dotnet-project-structure` | `skills/project-structure/` |
| `csharp-concurrency-patterns` | `skills/csharp-concurrency-patterns/` |
| `testcontainers-integration-tests` | `skills/testcontainers/` |
| `opentelemetry-net-instrumentation` | `skills/opentelementry-dotnet-instrumentation/` |

### dotnet/skills

Pinned commit: `f8184593b2f605cedaf48142e428a3c9a9e12dcd`

| Skill | Upstream path |
| --- | --- |
| `dotnet-webapi` | `plugins/dotnet-aspnetcore/skills/dotnet-webapi/` |
| `optimizing-ef-core-queries` | `plugins/dotnet-data/skills/optimizing-ef-core-queries/` |
| `run-tests` | `plugins/dotnet-test/skills/run-tests/` |
| `platform-detection` | `plugins/dotnet-test/skills/platform-detection/` |
| `filter-syntax` | `plugins/dotnet-test/skills/filter-syntax/` |

### vercel-labs/agent-skills

Pinned commit: `063bee94c3f4df8453406c830b0a7df0f2860278`

Upstream subtree копируется целиком, включая `rules/**`; frontmatter names upstream сохраняются.

| Skill | Upstream path | Upstream name |
| --- | --- | --- |
| `react-best-practices` | `skills/react-best-practices/` | `vercel-react-best-practices` |
| `composition-patterns` | `skills/composition-patterns/` | `vercel-composition-patterns` |

## Vendored sources

### vercel-labs/web-interface-guidelines

Pinned commit: `434b7f91364665f2f733b310ec54809bf8f37937`

| Upstream file | Локальный файл |
| --- | --- |
| `command.md` | `trade-system-web-design-review/guidelines.md` |
| `LICENSE` | `trade-system-web-design-review/LICENSE` |

- Файлы — неизменённый snapshot upstream (MIT); `LICENSE` хранится рядом с guidelines.
- `trade-system-web-design-review` использует только этот snapshot и не загружает guidelines во время review.
- Обновление выполняется заменой обоих файлов из нового pinned commit после review upstream diff и изменением pinned commit в этом разделе.
