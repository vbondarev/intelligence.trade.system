# AGENTS.md

## Кратко о репозитории
- Файл решения: `Intelligence.Trade.System.slnx`.
- Текущая область — один проект worker-сервиса: `Intelligence.Trade.System.Backend.Host/`.
- Точка входа контейнерной оркестрации: `compose.yaml` (собирает только образ backend host).

## Архитектура выполнения (что запускается и почему)
- Процесс стартует в `Intelligence.Trade.System.Backend.Host/Program.cs` через `Host.CreateApplicationBuilder(args)`.
- Основная точка расширения — hosted services; сейчас зарегистрирован только `builder.Services.AddHostedService<Worker>()`.
- `Intelligence.Trade.System.Backend.Host/Worker.cs` — бесконечный фоновый цикл с поддержкой отмены.
- Текущее поведение — heartbeat-лог раз в 1 секунду (`Worker running at: {time}`), что указывает на базовый шаблон worker-а.
- HTTP endpoints/controllers отсутствуют; рассматривайте это как не-вебовый фоновый процесс.

## Конфигурация и окружения
- Конфигурация логирования есть в `appsettings.json` и `appsettings.Development.json` с одинаковыми значениями по умолчанию.
- Локальный debug-профиль в `Properties/launchSettings.json` задает `DOTNET_ENVIRONMENT=Development`.
- Секреты ожидаются через .NET user secrets (`UserSecretsId` в `.csproj`) и/или переменные окружения.
- В `.csproj` включены nullable reference types и implicit usings; новый код должен быть с ними совместим.

## Сборка, запуск и отладка
- Локальный запуск (из корня репозитория): `dotnet run --project Intelligence.Trade.System.Backend.Host`.
- Проверка сборки: `dotnet build Intelligence.Trade.System.slnx`.
- Сборка/запуск контейнера через compose: `docker compose -f compose.yaml up --build`.
- Docker-образ многостадийный (`runtime:10.0` + `sdk:10.0`) в `Intelligence.Trade.System.Backend.Host/Dockerfile`.
- `DockerDefaultTargetOS` — Linux; учитывайте Linux-ориентированные предположения при работе с контейнером.

## Наблюдаемые конвенции проекта
- Пространство имен следует идентичности папки/проекта: `Intelligence.Trade.System.Backend.Host`.
- Предпочтителен constructor injection для зависимостей (пример: `ILogger<Worker>` в `Worker`).
- Долгоживущая работа должна учитывать `CancellationToken` (`Task.Delay(..., stoppingToken)`).
- Логирование использует структурированные шаблоны (`{time}`), а не конкатенацию строк.

## Точки интеграции и рекомендации по расширению
- Для добавления бизнес-логики регистрируйте дополнительные hosted services в `Program.cs` и изолируйте ответственность по сервисам.
- При добавлении внешних систем (broker, DB, APIs) подключайте клиентов через DI и конфигурируйте через appsettings + env vars.
- Обновления в `compose.yaml` должны быть синхронизированы с новыми зависимостями сервисов.

## Заметки для AI-агентов в этом репозитории
- По запрошенному glob-скану существующие файлы AI-инструкций не обнаружены (README/AGENTS/CLAUDE/cursor/windsurf/copilot rules).
- Считайте этот файл канонической стартовой инструкцией для агента, пока не появятся новые проекты/компоненты.


