# ADR-0001: Способ аутентификации пользователей

Статус: Accepted  
Дата: 4 сентября 2026 года

## Контекст

Проект выходит из этапа B и уже имеет постоянное хранение доменного состояния и оптимистическую конкурентность C-04. Пользовательская система, вход и разграничение данных пока не реализованы. Основной клиент первого Web MVP — React browser application; backend остаётся владельцем пользовательской сессии.

В этом ADR различаются:

- **Аутентификация (authentication)** — кто пользователь: вход, сессия, authentication cookie, построение `ClaimsPrincipal` и стабильный идентификатор пользователя.
- **Авторизация (authorization)** — что пользователь может видеть и делать. Изоляция данных по `UserId` относится к C-06 и этим ADR не реализуется.

SignalR сейчас отсутствует. Его реализация запланирована в F-04, поэтому это ADR выбирает совместимую модель для REST и будущего browser SignalR, но не создаёт Hub и не выполняет runtime-проверку SignalR.

## Решение

Для первого Web MVP принимается **ASP.NET Core Identity с cookie authentication и защищённой HttpOnly authentication cookie**.

Backend будет выдавать, проверять и завершать authentication session. React не должен хранить authentication token или authentication secret основного browser-сценария в `localStorage`, `sessionStorage` или IndexedDB. Браузер будет использовать cookie для REST-запросов и будущего SignalR handshake.

ASP.NET Core Identity выбирается как стандартная инфраструктура для:

- user storage;
- password hashing и password policies;
- login/logout и authentication lifecycle;
- security stamp;
- lockout;
- claims и последующей построенной на них авторизации.

Самодельные `CustomPasswordHasher`, `CustomUserPasswordTable` и `CustomSessionCrypto` не проектируются.

Это решение фиксирует архитектуру, а не реализацию. В C-05 не добавляются Identity packages, `AspNet*` schema, migrations, login endpoints, authentication middleware, изменения `Program.cs` или временный SignalR Hub.

## Границы решения

Решение относится к аутентификации первого browser MVP. Оно не утверждает, что cookie auth навсегда будет единственным механизмом для native clients, desktop clients, third-party API consumers или внешних интеграций.

В этот этап не входят:

- authorization policies и изоляция данных по `UserId` — C-06;
- шифрование, отзыв и ротация ключей Bybit — C-07;
- пользовательский REST API — F-03;
- SignalR и пользовательские группы — F-04;
- Identity schema, migrations и runtime authentication configuration.

## Основной Web-сценарий

Целевой поток первого Web MVP:

```text
React browser
      ↓
ASP.NET Core Identity
      ↓
secure HttpOnly authentication cookie
      ↓
REST + future browser SignalR
      ↓
ClaimsPrincipal
      ↓
stable UserId
```

Backend владеет сессией и её жизненным циклом. Cookie не является бизнес-идентичностью пользователя и не меняет независимость Domain от ASP.NET Core.

## REST

Текущие публичные market endpoints, включая `GET /api/market-analysis/{symbol}/llm-payload`, остаются anonymous, пока отдельное решение не изменит этот контракт. Необходимые health, readiness и liveness endpoints также остаются public. Swagger остаётся public только в разрешённой для этого среде и согласно отдельной operational configuration.

Будущие пользовательские endpoints:

```text
/api/v1/accounts
/api/v1/positions
/api/v1/portfolio
/api/v1/recommendations
```

должны требовать authenticated user. Для API endpoints cookie authentication должна приводить к `401 Unauthorized` и `403 Forbidden`, а не к HTML redirect на login page. Это соответствует поведению ASP.NET Core 10 для распознаваемых API endpoints.

Аутентификация подтверждает личность, но сама по себе не даёт доступа к данным другого пользователя. Проверки области `UserId` и authorization относятся к C-06.

## SignalR

SignalR не реализуется в C-05. При реализации F-04 browser contract должен быть таким:

```text
React
   ↓
authentication cookie
   ↓
SignalR handshake
   ↓
HubConnectionContext.User
```

Browser SignalR должен использовать тот же ASP.NET Core authenticated principal и ту же Identity model, что и REST. Нельзя создавать отдельную identity model для SignalR.

Если authentication cookie решает задачу browser MVP, `access_token` в query string не используется. В частности, не вводится временная token-схема только для подключения Hub. F-04 должен отдельно учесть, что SignalR кэширует principal на время соединения: инвалидизация пользователя или существенное изменение его claims должно приводить к закрытию/переподключению соединения либо к явно предусмотренной серверной проверке.

## UserId и claims

Business identity пользователя уже представлена Domain `UserId`. В будущем Identity persistence рекомендуется строить со стабильным `Guid` как primary user identifier, совместимым по значению с Domain `UserId`.

Authentication principal должен содержать стабильный user identifier claim, предпочтительно `ClaimTypes.NameIdentifier` или эквивалентный стандартный subject identifier. `NameIdentifier` должен соответствовать тому же `Guid`, который используется как business-level `UserId`.

Email, username и display name могут изменяться и не могут использоваться как системный `UserId`. Domain не должен зависеть от `IdentityUser`, `ClaimsPrincipal`, `Claim`, cookie, authentication scheme или password hash. На границе Application/API будущий `ICurrentUserContext` или эквивалент может преобразовывать principal в typed `UserId`; создавать этот abstraction в C-05 не требуется.

## CSRF

Cookie автоматически отправляется браузером, поэтому для state-changing запросов `POST`, `PUT`, `PATCH` и `DELETE` будущий Web API обязан использовать ASP.NET Core anti-forgery protection либо другой явно обоснованный механизм защиты от CSRF.

Принято направление:

```text
authentication cookie
      +
anti-forgery token/header
```

React должен получать anti-forgery token предусмотренным backend способом и передавать его отдельным HTTP header. Anti-forgery token не является authentication secret. Authentication cookie не должна становиться JavaScript-readable cookie ради CSRF-защиты. GET endpoints не должны изменять состояние.

## Cookie security

Минимальные production requirements:

- `HttpOnly = true`;
- `Secure = true`;
- cookie отправляется только по HTTPS;
- `SameSite` выбирается осознанно с учётом deployment topology, а не случайно наследуется из development settings.

Предпочтительная deployment architecture — React и API в одном site/origin boundary либо за контролируемым reverse proxy. Это уменьшает сложность cookie и CORS модели. Если в development React и API находятся на разных origins, разрешается только явно заданный trusted frontend origin; credentials разрешаются только ему, и `AllowAnyOrigin` нельзя сочетать с credentials. CORS в C-05 не реализуется.

Конкретные значения expiration, sliding expiration, cookie name и окончательная SameSite policy являются configuration/deployment decisions, а не частью Domain. Не следует фиксировать localhost workaround как production architecture.

## Session lifecycle

Будущая реализация должна соблюдать следующие принципы:

- login создаёт authentication session;
- logout уничтожает её;
- истёкшая session даёт `401`;
- disabled или revoked user не должен бесконечно сохранять доступ по старой session;
- lifecycle должен допускать принудительную инвалидизацию sessions, включая изменение security stamp и закрытие/переподключение будущих SignalR connections.

Конкретные expiration значения не выбираются в ADR и должны задаваться configuration, а не Domain.

## Рассмотренные альтернативы

| Вариант | Статус | Решение |
|---|---|---|
| ASP.NET Core Identity + cookie authentication | **Accepted** | Подходит для основного React browser MVP: браузер автоматически использует cookie, token не открывается JavaScript, REST и browser SignalR могут использовать один principal. |
| Собственный JWT + refresh tokens | **Rejected** для текущего этапа | Добавляет security complexity, refresh-token lifecycle и revocation, заставляет отдельно решать хранение token во frontend и не нужен основному browser client. |
| ASP.NET Core Identity bearer tokens | **Not selected as primary browser mechanism** | Может быть полезен для простых non-browser clients, но не является причиной выбирать token auth для browser MVP. Не включается сейчас и не становится основным публичным authentication protocol без отдельного решения. |
| Внешний OIDC provider: Keycloak, Auth0, Microsoft Entra ID или другой OIDC provider | **Deferred** | На MVP нет требования, оправдывающего дополнительную инфраструктуру или vendor dependency. Конкретный provider не выбран. |
| Cookie authentication без ASP.NET Core Identity | **Rejected** как основа пользовательской системы | Cookie handler сам по себе возможен, но Identity уже предоставляет user storage, password/security lifecycle, claims, lockout и security stamp, уменьшая объём собственного security-кода. |

Самостоятельная генерация JWT и самостоятельный refresh-token lifecycle не должны появляться в C-05. В частности, не добавляются `IJwtTokenGenerator`, `JwtTokenService` или `RefreshTokenRepository`.

## Последствия

Положительные последствия:

- authentication token не доступен JavaScript;
- у browser REST и browser SignalR единая authentication model;
- REST и SignalR используют один authenticated principal;
- меньше собственного security-кода благодаря ASP.NET Core Identity;
- C-06 сможет строить user isolation поверх стабильного `UserId`.

Отрицательные и ограничивающие последствия:

- cookie authentication ориентирована прежде всего на browser clients;
- state-changing endpoints требуют CSRF protection;
- cross-origin deployment усложняет cookie/CORS configuration;
- native clients потребуют отдельного authentication decision;
- invalidation claims или user status для уже открытого SignalR connection требует явного lifecycle решения.

## Риски

- Ошибочная `SameSite`, CORS или anti-forgery configuration может сделать browser flow небезопасным или неработоспособным.
- HttpOnly снижает риск кражи cookie через JavaScript, но не заменяет защиту от XSS и не предотвращает действия уже скомпрометированного browser context.
- При нескольких backend instances будущая реализация должна согласованно настроить ASP.NET Core Data Protection key ring, иначе одна instance не сможет валидировать cookie другой.
- SignalR principal живёт в рамках connection и не обновляется автоматически после изменения claims; F-04 обязан явно обработать это ограничение.

## Отложенные решения

- Identity user type, schema, EF stores и migrations;
- login, register, logout и current-user endpoints;
- точные cookie expiration/SameSite settings и anti-forgery endpoint/header contract;
- реализация `ICurrentUserContext` и C-06 authorization/data scoping;
- SignalR Hub, groups, reconnect and invalidation behavior — F-04;
- encryption, revoke и rotation для Bybit credentials — C-07;
- OAuth 2.0 / OpenID Connect и выбор identity provider для native, desktop, third-party или external integration clients.

Если появится реальная необходимость в native mobile, desktop, third-party API consumers или внешних интеграциях, до реализации принимается отдельное ADR по OAuth 2.0 / OpenID Connect со standard bearer access tokens и подходящим identity provider / authorization server. Это не проектируется заранее в C-05.

## Связанные этапы roadmap

- **C-04** — база этого решения: persistence и optimistic concurrency уже приняты в `develop`.
- **C-05** — это ADR; runtime authentication implementation выполняется позже.
- **C-06** — authorization и изоляция данных по `UserId`.
- **C-07** — защита credentials Bybit.
- **F-03** — пользовательский REST API.
- **F-04** — SignalR и пользовательские группы с тем же authenticated principal.

## Источники

Официальная документация Microsoft Learn, проверенная 4 сентября 2026 года:

- [Use Identity to secure a Web API backend for SPAs](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/identity-api-authorization?view=aspnetcore-10.0) — рекомендует cookies для browser-based applications и отдельно описывает token option для клиентов, которые не могут использовать cookies.
- [Introduction to Identity on ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/identity?view=aspnetcore-10.0) — описывает управление users, passwords, claims, tokens и security lifecycle через Identity.
- [Use cookie authentication without ASP.NET Core Identity](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/cookie?view=aspnetcore-10.0) — описывает cookie handler, `HttpOnly`/`Secure`/`SameSite` configuration и поведение API endpoints в ASP.NET Core 10.
- [API endpoint authentication behavior in ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/api-endpoint-auth?view=aspnetcore-10.0) — фиксирует `401`/`403` вместо login redirect для известных API endpoints.
- [SignalR authentication and authorization](https://learn.microsoft.com/en-us/aspnet/core/signalr/authn-and-authz?view=aspnetcore-10.0) — описывает автоматическое использование cookie в browser SignalR и `HubConnectionContext.User`; query-string token остаётся отдельным bearer-сценарием.
- [Prevent Cross-Site Request Forgery (XSRF/CSRF) attacks in ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/security/anti-request-forgery?view=aspnetcore-10.0) — объясняет риск автоматически отправляемых cookies и anti-forgery protection.
