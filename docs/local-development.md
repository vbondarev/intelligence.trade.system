# Локальная разработка Intelligence.TradeSystem

Этот документ описывает сборку, запуск, тестирование и диагностику проекта на Windows, Linux и macOS.

Рекомендуемый способ запуска полного локального окружения — **Docker Compose**. Он воспроизводит основную runtime-топологию проекта и поднимает PostgreSQL, Identity, API, BFF и React frontend согласованно. Прямой запуск отдельных компонентов и Aspire используются как дополнительные сценарии разработки.

## 1. Локальная топология

При запуске через Docker Compose browser обращается только к frontend:

~~~text
Browser
   ↓
http://localhost:8082
   ↓
frontend (React + nginx)
   ├── React SPA
   └── /bff/**, /signin-oidc, /signout-callback-oidc
              ↓
             BFF
              ├── OIDC → Identity
              └── Bearer → API

PostgreSQL ← Identity / API
~~~

Локальные адреса:

| Сервис | Адрес | Назначение |
|---|---|---|
| Frontend | http://localhost:8082 | Основной browser-facing origin |
| Identity | http://localhost:8081 | OAuth 2.0 / OpenID Connect Authorization Server |
| API | http://localhost:8080 | Backend API |
| Identity discovery | http://localhost:8081/.well-known/openid-configuration | Проверка OIDC discovery |
| API liveness | http://localhost:8080/alive | Проверка процесса API |

BFF не публикует отдельный host port: frontend обращается к нему по внутренней Docker network.

Compose создаёт network <code>trade-agent-network</code> с фиксированным subnet <code>172.28.0.0/24</code>. BFF доверяет forwarded headers только от этого subnet, поэтому OIDC redirect URIs строятся от public origin <code>http://localhost:8082</code>. Если network с таким именем уже существует с другим subnet, её нужно пересоздать (см. «Login возвращает redirect на внутренний адрес BFF» в разделе «Типичные проблемы»).

## 2. Требования

### Для запуска полного окружения через Docker Compose

- Git;
- Docker с Docker Compose v2;
- доступ к интернету для загрузки images/packages и публичных данных Bybit;
- свободные host ports 8080, 8081 и 8082.

Проверьте Docker Compose:

~~~bash
docker compose version
~~~

Используется команда <code>docker compose</code>; legacy-команда <code>docker-compose</code> не является основной инструкцией проекта.

### Для сборки и тестов вне контейнеров

Дополнительно нужны:

- .NET 10 SDK; точная версия зафиксирована в корневом <code>global.json</code>;
- Node.js 24 и npm; версия Node.js зафиксирована в <code>frontend/intelligence-trade-web/.nvmrc</code>;
- Docker для integration tests на Testcontainers и browser E2E.

Проверка:

~~~bash
dotnet --version
node --version
npm --version
docker --version
docker compose version
~~~

## 3. Локальные secrets

Compose ожидает следующие environment variables хоста:

- <code>TRADE_CREDENTIAL_KEY</code> — Base64-представление 32 случайных байт для защиты Bybit credentials;
- <code>TRADE_WEB_BFF_CLIENT_SECRET</code> — secret confidential OIDC client <code>trade-web-bff</code>, общий для Identity и BFF;
- <code>TRADE_WEB_DEV_PASSWORD</code> — пароль development-пользователя Identity;
- <code>TRADE_WEB_DEV_USERNAME</code> — необязательное имя development-пользователя, по умолчанию <code>trade-dev-user</code>.

Secrets не коммитятся в Git и не должны выводиться в логи.

### Важное правило повторных запусков

При существующем PostgreSQL volume используйте тот же <code>TRADE_CREDENTIAL_KEY</code>. Если заменить его случайным новым значением, ранее сохранённые encrypted Bybit credentials станут нечитаемыми.

Development-пользователь Identity также не меняет пароль автоматически. Если пользователь уже существует, <code>TRADE_WEB_DEV_PASSWORD</code> должен совпадать с паролем, использованным при его создании.

Для одного локального профиля рекомендуется сохранять все development secrets в безопасном локальном хранилище и повторно использовать их между запусками.

## 4. Быстрый запуск — Windows

Основной shell: PowerShell.

Из корня репозитория:

~~~powershell
function New-Base64Secret {
    $bytes = New-Object byte[] 32
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try {
        $rng.GetBytes($bytes)
    } finally {
        $rng.Dispose()
    }

    [Convert]::ToBase64String($bytes)
}

$env:TRADE_CREDENTIAL_KEY = New-Base64Secret
$env:TRADE_WEB_BFF_CLIENT_SECRET = New-Base64Secret
$env:TRADE_WEB_DEV_USERNAME = "trade-dev-user"
$env:TRADE_WEB_DEV_PASSWORD = "Local-dev-password-123!"

cd backend
docker compose up --build -d
docker compose ps -a
~~~

Используется <code>RandomNumberGenerator.Create().GetBytes(...)</code>, а не статический <code>RandomNumberGenerator.Fill(...)</code>, чтобы команда работала и в PowerShell-хостах, где статический метод недоступен.

Environment variables действуют только в текущей PowerShell session. Для следующих запусков восстановите сохранённые локальные значения secrets вместо генерации нового <code>TRADE_CREDENTIAL_KEY</code>.

## 5. Быстрый запуск — Linux

Основной shell в примерах: bash.

Из корня репозитория:

~~~bash
export TRADE_CREDENTIAL_KEY="$(openssl rand -base64 32)"
export TRADE_WEB_BFF_CLIENT_SECRET="$(openssl rand -base64 32)"
export TRADE_WEB_DEV_USERNAME="trade-dev-user"
export TRADE_WEB_DEV_PASSWORD="Local-dev-password-123!"

cd backend
docker compose up --build -d
docker compose ps -a
~~~

Для следующих запусков существующего PostgreSQL volume восстановите сохранённые значения development secrets, особенно <code>TRADE_CREDENTIAL_KEY</code>.

## 6. Быстрый запуск — macOS

Рекомендуемый runtime — Docker Desktop. Основной shell современных версий macOS — zsh.

Из корня репозитория:

~~~zsh
export TRADE_CREDENTIAL_KEY="$(openssl rand -base64 32)"
export TRADE_WEB_BFF_CLIENT_SECRET="$(openssl rand -base64 32)"
export TRADE_WEB_DEV_USERNAME="trade-dev-user"
export TRADE_WEB_DEV_PASSWORD="Local-dev-password-123!"

cd backend
docker compose up --build -d
docker compose ps -a
~~~

Для следующих запусков существующего PostgreSQL volume восстановите сохранённые значения development secrets, особенно <code>TRADE_CREDENTIAL_KEY</code>.

## 7. Что должно запуститься

Compose использует последовательность:

~~~text
postgres
   ↓
identity-db-init
   ↓
identity-migrations
   ↓
identity
   ↓
api
   ↓
bff
   ↓
frontend
~~~

Проверка:

~~~bash
docker compose ps -a
~~~

Нормальное состояние после успешного старта:

~~~text
identity-db-init       Exited (0)
identity-migrations    Exited (0)

postgres               Up (healthy)
identity               Up
api                    Up
bff                    Up
frontend               Up (healthy)
~~~

<code>identity-db-init</code> и <code>identity-migrations</code> — одноразовые процессы. Их состояние <code>Exited (0)</code> после успешного выполнения является нормальным.

## 8. Проверка запущенного приложения

Откройте:

~~~text
http://localhost:8082
~~~

Ожидаемый пользовательский сценарий:

1. frontend показывает стартовую страницу;
2. кнопка «Войти» переводит на Identity <code>http://localhost:8081/account/login</code>;
3. development user входит с <code>TRADE_WEB_DEV_USERNAME</code> / <code>TRADE_WEB_DEV_PASSWORD</code>;
4. после Authorization Code + PKCE browser возвращается на <code>http://localhost:8082/app</code>;
5. перезагрузка страницы сохраняет BFF session;
6. в навигации «Подключения» открывается `/app/settings/connections`; при актуальной business schema список загружается через same-origin BFF;
7. «Выйти» завершает BFF session и Identity SSO session.

> G-02 добавил migration `AddExchangeAccountDisplayName`. Если локальная business database была создана до этой migration, обновите её по разделу «Миграции PostgreSQL» ниже. Реальное подключение Bybit требует валидных read-only API credentials и не является обязательной частью базовой smoke-проверки.

### Проверка Identity discovery

Windows PowerShell:

~~~powershell
Invoke-WebRequest http://localhost:8081/.well-known/openid-configuration -UseBasicParsing
~~~

Linux/macOS:

~~~bash
curl --fail http://localhost:8081/.well-known/openid-configuration
~~~

Ожидается HTTP 200.

### Проверка API liveness

Windows PowerShell:

~~~powershell
Invoke-WebRequest http://localhost:8080/alive -UseBasicParsing
~~~

Linux/macOS:

~~~bash
curl --fail http://localhost:8080/alive
~~~

## 9. Управление Compose stack

Команды выполняются из каталога <code>backend</code>.

### Остановить окружение, сохранив данные

~~~bash
docker compose down
~~~

Named PostgreSQL volume сохраняется.

### Запустить или обновить окружение

~~~bash
docker compose up --build -d
~~~

### Посмотреть состояние всех контейнеров

~~~bash
docker compose ps -a
~~~

### Посмотреть общие логи

~~~bash
docker compose logs --tail=200
~~~

### Логи отдельного сервиса

~~~bash
docker compose logs identity --tail=200
docker compose logs api --tail=200
docker compose logs bff --tail=200
docker compose logs frontend --tail=200
~~~

## 10. Пересборка отдельных сервисов

Frontend, BFF, Identity и API являются отдельными deployment units. Для изменения одного компонента не требуется пересобирать весь stack.

Из каталога <code>backend</code>:

~~~bash
docker compose up -d --build identity
docker compose up -d --build api
docker compose up -d --build bff
docker compose up -d --build frontend
~~~

Например, после изменения Razor/CSS страницы входа достаточно:

~~~bash
docker compose up -d --build identity
~~~

Если необходимо гарантированно исключить Docker build cache:

~~~bash
docker compose build --no-cache identity
docker compose up -d --force-recreate identity
~~~

Аналогичный подход применяется к другим сервисам.

## 11. Сборка без запуска Compose

### Backend

Из корня репозитория:

~~~bash
dotnet restore backend/src/Intelligence.TradeSystem.slnx
dotnet build backend/src/Intelligence.TradeSystem.slnx --configuration Release --no-restore
~~~

Backend build не запускает npm и не собирает React frontend.

### Frontend

Из <code>frontend/intelligence-trade-web</code>:

~~~bash
npm ci
npm run typecheck
npm run lint
npm run test:unit
npm run build
~~~

Vite создаёт artifact в:

~~~text
frontend/intelligence-trade-web/dist
~~~

Production frontend image собирается независимо от backend:

~~~bash
docker build frontend/intelligence-trade-web
~~~

## 12. Тестирование

### Backend

Из корня репозитория:

~~~bash
dotnet test backend/src/Intelligence.TradeSystem.slnx --configuration Release
~~~

Integration tests используют Testcontainers, поэтому Docker должен быть доступен.

### Frontend

Из <code>frontend/intelligence-trade-web</code>:

~~~bash
npm ci
npm run typecheck
npm run lint
npm run test:unit
npm run build
~~~

### Browser E2E

Browser E2E выполняется против запущенного Compose stack.

Если Playwright browser ещё не установлен:

~~~bash
npx playwright install chromium
~~~

Windows PowerShell:

~~~powershell
cd frontend\intelligence-trade-web
$env:E2E_PASSWORD = $env:TRADE_WEB_DEV_PASSWORD
npm run test:e2e
~~~

Linux/macOS:

~~~bash
cd frontend/intelligence-trade-web
export E2E_PASSWORD="$TRADE_WEB_DEV_PASSWORD"
npm run test:e2e
~~~

Значения по умолчанию:

| Переменная | Значение |
|---|---|
| <code>WEB_BASE_URL</code> | http://localhost:8082 |
| <code>IDENTITY_BASE_URL</code> | http://localhost:8081 |
| <code>E2E_USERNAME</code> | trade-dev-user |
| <code>E2E_CLIENT_ID</code> | trade-web-bff |

<code>E2E_PASSWORD</code> обязательно должен совпадать с паролем development-пользователя Identity.

## 13. Запуск через Aspire

Альтернативный orchestration-сценарий:

~~~bash
cd backend/src
dotnet run --project Intelligence.TradeSystem.AppHost
~~~

AppHost использует Aspire CLI bundle. Secret parameters <code>tradeCredentialKey</code>, <code>tradeWebBffClientSecret</code> и <code>tradeWebDevelopmentPassword</code> задаются через Aspire parameters, например через user secrets AppHost, и не коммитятся.

AppHost запускает frontend как отдельный Dockerfile resource, BFF как внутренний project resource и Identity с public endpoint <code>http://localhost:8081</code>. Для frontend resource нужен Docker.

## 14. Прямой запуск API

Прямой <code>dotnet run</code> удобен для диагностики API, но требует доступного PostgreSQL и явной application configuration.

Linux/macOS:

~~~bash
export ConnectionStrings__TradeSystem='Host=localhost;Port=5432;Database=tradesystem;Username=tradesystem;Password=<password>'
export CredentialProtection__ActiveKeyId=local_v1
export CredentialProtection__Keys__local_v1='<base64-32-byte-key>'

cd backend/src
dotnet run --project Intelligence.TradeSystem.Api
~~~

Windows PowerShell:

~~~powershell
$env:ConnectionStrings__TradeSystem = 'Host=localhost;Port=5432;Database=tradesystem;Username=tradesystem;Password=<password>'
$env:CredentialProtection__ActiveKeyId = 'local_v1'
$env:CredentialProtection__Keys__local_v1 = $env:TRADE_CREDENTIAL_KEY

cd backend\src
dotnet run --project Intelligence.TradeSystem.Api
~~~

При прямом запуске API <code>TRADE_CREDENTIAL_KEY</code> не является application configuration key сам по себе: приложению передаётся <code>CredentialProtection__Keys__local_v1</code>.

## 15. Защита Bybit credentials

Пары <code>apiKey</code>/<code>apiSecret</code> хранятся как authenticated-encrypted payload в PostgreSQL. Master keys не сохраняются в TradeSystem database, логах, ответах API или репозитории.

Для прямого запуска используются:

~~~text
CredentialProtection__ActiveKeyId
CredentialProtection__Keys__<key-id>
~~~

При rollover:

1. добавьте новый key id в deployment configuration;
2. оставьте старый key доступным для чтения;
3. выполните <code>Reprotect</code> существующих credential rows;
4. только после этого удаляйте старый key.

Удаление старого ключа до перепротекции делает соответствующие строки нечитаемыми.

## 16. Фоновая синхронизация аккаунтов

API периодически синхронизирует активные Bybit-аккаунты. Настройки находятся в секции <code>ExchangeAccountBackgroundSync</code>:

~~~json
{
  "Enabled": true,
  "Interval": "00:05:00",
  "InitialDelay": "00:00:30",
  "BatchSize": 50,
  "MaxConcurrency": 4
}
~~~

<code>Enabled: false</code> отключает только фоновый цикл; ручная синхронизация продолжает работать.

## 17. Миграции PostgreSQL

Production schema развивается только через EF Core migrations. API и Identity host не применяют production migrations автоматически при обычном старте.

Business и Identity используют отдельные migration streams.

Команды `dotnet ef` ниже выполняются на host и поэтому требуют PostgreSQL, доступный с host по указанной connection string. Стандартный Compose service `postgres` не публикует порт `5432` наружу; для обычного Compose lifecycle используйте предусмотренные container/orchestration-процессы, а host `dotnet ef` — с отдельно доступным PostgreSQL или явно опубликованным development port.

### Business persistence

Linux/macOS:

~~~bash
cd backend/src
export ConnectionStrings__TradeSystem='Host=localhost;Port=5432;Database=tradesystem;Username=tradesystem;Password=<password>'

dotnet ef migrations list --project Intelligence.TradeSystem.Infrastructure
dotnet ef database update --project Intelligence.TradeSystem.Infrastructure
~~~

Windows PowerShell:

~~~powershell
cd backend\src
$env:ConnectionStrings__TradeSystem = 'Host=localhost;Port=5432;Database=tradesystem;Username=tradesystem;Password=<password>'

dotnet ef migrations list --project Intelligence.TradeSystem.Infrastructure
dotnet ef database update --project Intelligence.TradeSystem.Infrastructure
~~~

Создание новой migration:

~~~bash
dotnet ef migrations add <MigrationName> --project Intelligence.TradeSystem.Infrastructure
~~~

### Identity persistence

Linux/macOS:

~~~bash
cd backend/src
export ConnectionStrings__TradeSystemIdentity='Host=localhost;Port=5432;Database=tradesystem_identity;Username=tradesystem;Password=<password>'

dotnet ef migrations list --project Intelligence.TradeSystem.Identity --startup-project Intelligence.TradeSystem.Identity --context Intelligence.TradeSystem.Identity.Persistence.IdentityDbContext
dotnet ef database update --project Intelligence.TradeSystem.Identity --startup-project Intelligence.TradeSystem.Identity --context Intelligence.TradeSystem.Identity.Persistence.IdentityDbContext
dotnet run --project Intelligence.TradeSystem.Identity.Migrations
~~~

Windows PowerShell:

~~~powershell
cd backend\src
$env:ConnectionStrings__TradeSystemIdentity = 'Host=localhost;Port=5432;Database=tradesystem_identity;Username=tradesystem;Password=<password>'

dotnet ef migrations list --project Intelligence.TradeSystem.Identity --startup-project Intelligence.TradeSystem.Identity --context Intelligence.TradeSystem.Identity.Persistence.IdentityDbContext
dotnet ef database update --project Intelligence.TradeSystem.Identity --startup-project Intelligence.TradeSystem.Identity --context Intelligence.TradeSystem.Identity.Persistence.IdentityDbContext
dotnet run --project Intelligence.TradeSystem.Identity.Migrations
~~~

Для deployment/local orchestration предпочтителен одноразовый <code>Identity.Migrations</code> runner: он применяет migrations и завершается с ненулевым кодом при ошибке.

## 18. Типичные проблемы

### Identity завершается сразу после запуска

Сначала проверьте:

~~~bash
docker compose ps -a
docker compose logs identity --tail=200
~~~

Если лог содержит:

~~~text
Development user уже существует с другим паролем; пароль не изменяется автоматически.
~~~

то существующая Identity database уже содержит development user с другим паролем.

Предпочтительный вариант — использовать пароль, с которым пользователь был создан.

Windows PowerShell:

~~~powershell
$env:TRADE_WEB_DEV_PASSWORD = "<existing-password>"
docker compose up -d identity
~~~

Linux/macOS:

~~~bash
export TRADE_WEB_DEV_PASSWORD="<existing-password>"
docker compose up -d identity
~~~

### Сбросить только локальную Identity database

Если старые локальные Identity users/OIDC state не нужны, можно сбросить только <code>tradesystem_identity</code>, сохранив основную database <code>tradesystem</code>.

Из каталога <code>backend</code>:

~~~bash
docker compose stop identity bff frontend api

docker compose exec postgres psql -U tradesystem -d postgres -c 'DROP DATABASE IF EXISTS tradesystem_identity WITH (FORCE);'

docker compose run --rm identity-db-init
docker compose run --rm identity-migrations
docker compose up -d identity api bff frontend
~~~

После этого development user создаётся заново с текущим <code>TRADE_WEB_DEV_USERNAME</code> и <code>TRADE_WEB_DEV_PASSWORD</code>.

### Login возвращает redirect на внутренний адрес BFF

Если после «Войти» Identity отклоняет запрос или в <code>redirect_uri</code> указан <code>http://bff:8080/signin-oidc</code> вместо <code>http://localhost:8082/signin-oidc</code>, проверьте subnet network:

~~~bash
docker network inspect trade-agent-network
~~~

В <code>IPAM.Config</code> должен быть subnet <code>172.28.0.0/24</code>, а адреса frontend и BFF в <code>Containers</code> должны принадлежать ему. Compose не меняет subnet уже существующей network: например, network, созданная до фиксации subnet или вручную, переиспользуется со старым адресным диапазоном. Признак такой ситуации — предупреждение Compose <code>a network with name trade-agent-network exists but was not created by compose</code>.

> **Внимание:** не удаляйте network, если к ней подключены containers другого runtime/project. Проверьте список <code>Containers</code> в выводе <code>docker network inspect</code>.

Если network используется только этим stack, пересоздайте её без удаления volumes. Из каталога <code>backend</code>:

~~~bash
docker compose down
docker network rm trade-agent-network
docker compose up --build -d
~~~

Если создание network завершается ошибкой о пересечении address pool, subnet <code>172.28.0.0/24</code> уже занят другой Docker network на этой машине.

### Заняты порты

Проверьте, что 8080, 8081 и 8082 не заняты другим процессом. Compose должен иметь возможность опубликовать все три host ports.

### Browser показывает старую версию UI

После пересборки соответствующего сервиса выполните hard reload browser. Для Chrome/Edge обычно используется <code>Ctrl+F5</code> на Windows/Linux или <code>Cmd+Shift+R</code> на macOS.

Если подозревается Docker build cache:

~~~bash
docker compose build --no-cache <service>
docker compose up -d --force-recreate <service>
~~~

## 19. Полный сброс локального окружения

> **Внимание:** следующая команда удаляет named volumes и локальные PostgreSQL данные.

Из каталога <code>backend</code>:

~~~bash
docker compose down -v
~~~

Используйте её только когда нужен полный чистый development environment.

После удаления volume следующий запуск требует заново задать development secrets и создаст databases с нуля.
