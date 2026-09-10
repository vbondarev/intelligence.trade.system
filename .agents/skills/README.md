# Skills проекта

Каталог `.agents/skills` содержит выбранный набор project-scoped Agent Skills для
`Intelligence.TradeSystem`.

- External skills не переводятся и не редактируются локально.
- Repository instructions и явная задача пользователя имеют приоритет над external skill.
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
