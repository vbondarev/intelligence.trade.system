# ADR-0003: Выбор OAuth/OIDC Authorization Server

Статус: Accepted

Дополняет: [ADR-0002: Аутентификация универсального API через OAuth 2.0 / OpenID Connect](0002-universal-api-authentication-strategy.md)

Дата: 5 сентября 2026 года

## Контекст

ADR-0002 зафиксировал протокол и универсальный authentication contract проекта:

- `Intelligence.TradeSystem.Api` — client-agnostic OAuth/OIDC resource server;
- защищённые `/api/v1/*` предъявляют Bearer access token;
- целевой формат access token первого MVP — signed JWT;
- React использует схему React → BFF → Bearer → API;
- user-delegated и machine principals различаются;
- публичные market endpoints остаются anonymous.

ADR-0002 сознательно не выбирал конкретный Authorization Server. Этот ADR принимает такое решение до начала C-05A. Он не заменяет ADR-0002: ADR-0002 остаётся источником правил протокола, resource-server contract, BFF, CSRF и различия user/machine principal.

Это documentation-only решение. В этом PR не добавляются runtime, пакеты, проекты, миграции, endpoints или middleware.

## Решение

Для первого MVP выбирается self-hosted Authorization Server на основе:

```text
ASP.NET Core Identity
        +
OpenIddict
```

Целевой deployable/application boundary называется:

```text
Intelligence.TradeSystem.Identity
```

При реализации допустимо уточнить имя проекта в соответствии с naming conventions solution, но граница должна остаться отдельной от `Intelligence.TradeSystem.Api`.

Целевая архитектура:

```text
              Intelligence.TradeSystem.Identity
                 Authorization Server
            ┌──────────────┴──────────────┐
            │                             │
 ASP.NET Core Identity                OpenIddict
   users/security                    OAuth 2.0/OIDC
            │                             │
            └──────────────┬──────────────┘
                           │
                    signed access token
                           │
                           ▼
              Intelligence.TradeSystem.Api
                    Resource Server
                           │
                    Application / Domain
```

Authorization Server является отдельным deployable/application boundary от `Intelligence.TradeSystem.Api`. Он не должен быть скрытой частью business API.

### Границы ответственности

ASP.NET Core Identity отвечает за локальную user и credential модель:

- `ApplicationUser` и user lifecycle;
- credentials и password hashing;
- password policies;
- lockout;
- security stamp;
- claims и операции управления пользователем;
- будущие login, registration, password reset, email confirmation и MFA UX.

ASP.NET Core Identity не является OAuth/OIDC Authorization Server.

OpenIddict отвечает за protocol boundary:

- OAuth 2.0 и OpenID Connect;
- authorization и token endpoints;
- token issuance;
- OAuth application/client model;
- scopes и grants;
- refresh-token protocol;
- protocol validation;
- signed JWT access tokens;
- discovery metadata и публикацию ключей через стандартный механизм.

OpenIddict не является user/password database и не заменяет ASP.NET Core Identity. Login UI, user-management UX и интеграция с Identity принадлежат приложению `Intelligence.TradeSystem.Identity`.

### Почему Authorization Server не встраивается в API

`Intelligence.TradeSystem.Api` отвечает за бизнес-возможности:

```text
accounts
positions
portfolio
assessments
recommendations
market/user business API
```

Authorization Server отвечает за другую область:

```text
users
authentication
OAuth/OIDC protocol
token issuance
OAuth clients
sessions/authorization grants
```

Это разные operational и security responsibilities. Целевая модель не должна выглядеть так:

```text
Intelligence.TradeSystem.Api
    ├── business API
    ├── Identity
    ├── OAuth server
    ├── token issuance
    └── OpenIddict server
```

Разделение позволяет отдельно разворачивать, защищать, масштабировать и обновлять identity boundary. API не выпускает пользовательские токены и не становится владельцем user-management или OAuth grants.

### Resource Server остаётся независимым от OpenIddict Server

`Intelligence.TradeSystem.Api` не зависит от:

- ASP.NET Core Identity user store;
- `UserManager` и `SignInManager`;
- OpenIddict server endpoints;
- OpenIddict server database;
- password hashes;
- security stamps.

На resource-server boundary нужны только:

```text
issuer
audience
JWT Bearer validation
claims
scopes
ClaimsPrincipal
```

Целевой путь:

```text
Identity Server
        ↓
signed JWT
        ↓
API JWT validation
        ↓
ClaimsPrincipal
        ↓
Application / Domain boundary
```

Взаимодействие между Identity и API является protocol boundary, а не прямой C# dependency. `Api` не получает project reference на Identity host только ради проверки токена. Domain, Application, MarketIntelligence, Exchanges.Bybit и business Infrastructure не зависят от ASP.NET Core Identity или OpenIddict.

Такой контракт сохраняет возможность заменить OpenIddict другим standards-based OIDC provider без изменения бизнес-контрактов.

## Persistence boundary

Identity/OpenIddict persistence не использует существующий `TradeSystemDbContext`.

При реализации C-05A будет создан отдельный контекст с рабочим именем `IdentityDbContext` или `AuthorizationDbContext`. Точное имя — implementation detail C-05A. Этот контекст владеет:

- ASP.NET Core Identity tables;
- OpenIddict applications;
- OpenIddict authorizations;
- OpenIddict scopes;
- OpenIddict tokens.

`TradeSystemDbContext` продолжает владеть business persistence:

- `ExchangeAccount`;
- `Position`;
- `PositionChange`;
- `PortfolioState`;
- `PositionAssessment`;
- `Recommendation`.

Identity entity и Domain `UserId` — разные модели. Будущий `ApplicationUser` использует `Guid` primary key, например `ApplicationUser : IdentityUser<Guid>` или эквивалент актуального ASP.NET Core Identity. Domain не получает EF Identity entity.

Identity/Authorization persistence имеет собственный EF migration stream и собственную migration history. Auth migrations не добавляются в migrations `TradeSystemDbContext`; один migration history для business и authorization persistence не используется.

Для первого MVP допускается тот же PostgreSQL server/cluster, что и для business persistence, но logical ownership разделяется. Предпочтительная topology:

```text
PostgreSQL server
    │
    ├── TradeSystem
    │      business persistence
    │
    └── TradeSystemIdentity
           Identity/OpenIddict persistence
```

Предпочтительно использовать отдельную database. Отдельная PostgreSQL schema в той же database допустима только после явного технического обоснования при реализации Aspire/Docker. Общие `DbContext` и migration stream не допускаются.

## Subject, UserId и principals

ADR-0002 сохраняет Domain:

```text
UserId = Guid
```

Domain `UserId` не зависит от `IdentityUser`, OpenIddict, JWT, `ClaimsPrincipal`, email, username или конкретного provider.

Для self-hosted Authorization Server первого MVP принимается стабильное прямое отображение:

```text
ApplicationUser.Id
        =
OIDC sub
        =
JWT sub
        =
authenticated subject
        =
Domain UserId.Value
```

Для user-delegated flow `sub` представляет пользователя и может быть преобразован в Domain `UserId`. Mapping не использует email:

```text
sub → Domain UserId(Guid)
```

Запрещено:

```text
sub = email
Domain UserId = email
```

Email относится к login/contact identity, может измениться и не является стабильным business identifier. Email claim не обязателен для API authorization.

Для Client Credentials subject представляет machine/service principal. Он не является Domain user и не преобразуется автоматически в `UserId`. Machine token не получает автоматически user-owned scopes или доступ к user-owned resources. C-06 определяет отдельную authorization policy для user и machine principals.

## Клиенты и OAuth/OIDC flows

### React Web

Целевая схема:

```text
React
  ↓
BFF
  ↓
Authorization Code
  ↓
Authorization Server
  ↓
BFF server-side tokens
  ↓
Bearer JWT
  ↓
TradeSystem.Api
```

Browser JavaScript не получает access token или refresh token. BFF использует secure HttpOnly session cookie; автоматическая cookie boundary сохраняет CSRF requirement ADR-0002. BFF не содержит business logic и не заменяет API.

### Mobile

Mobile — public client:

```text
Authorization Code + PKCE
```

Client secret в mobile application не встраивается. Безопасное хранение native tokens определяется отдельной platform-security реализацией.

### Desktop

Desktop — public client:

```text
Authorization Code + PKCE
```

Client secret не хранится в executable или конфигурации desktop приложения.

### CLI

Предпочтительный flow:

```text
Device Authorization Flow
```

Он используется, если выбранная версия OpenIddict и фактическая configuration поддерживают требуемый сценарий. Допустимый fallback для интерактивного CLI — Authorization Code + PKCE.

### Machine clients

Для будущих service-to-service клиентов используется:

```text
Client Credentials
```

Реальные service clients не обязательны для C-05A. Client Credentials principal остаётся machine identity и не получает автоматически пользовательские scopes.

### Grants, которые не используются

Для первого MVP запрещены:

- Resource Owner Password Credentials / password grant;
- Implicit flow;
- самодельный `username/password → JWT` endpoint;
- custom token endpoint;
- custom `JwtTokenService`;
- custom refresh-token protocol;
- custom signing или token-rotation protocol.

Не включаются legacy flows только для упрощения тестирования. Refresh-token protocol принадлежит OpenIddict. Для browser/BFF refresh token хранится только server-side; требования к native storage определяются позже по платформам. Точные access/refresh token lifetimes не фиксируются этим ADR без product requirement.

## Access tokens и discovery

В соответствии с ADR-0002 первый MVP использует signed JWT access tokens. `Intelligence.TradeSystem.Api` в C-05A обязан проверять:

- signature;
- issuer;
- audience;
- expiration;
- `not-before`, если claim используется;
- required claims;
- required scopes.

Unsigned JWT не допускаются. Encrypted JWT не является обязательным требованием первого MVP без отдельной причины. Bearer access token передаётся только по TLS; в токен не помещаются password, API keys, Bybit secret, sensitive personal data или security stamp.

Issuer — стабильная публичная identity Authorization Server и является deployment configuration. Production issuer не должен быть `http://localhost:5000`; конкретный production URL сейчас не фиксируется.

Для API существует явный audience/resource identifier. Конкретная строка, например концептуальное `intelligence-trade-api`, будет выбрана в C-05A. API проверяет audience и не принимает любой валидный JWT только потому, что он выдан тем же issuer.

Для user login используется стандартный OIDC scope `openid`. `profile` и `email` подключаются только при необходимости. API scope сначала ограничивается минимальным scope, необходимым для проверки authentication boundary, например `trade.api`; полный каталог granular scopes относится к C-06/F.

Authorization Server публикует стандартные OIDC discovery metadata и JWKS. При обычном production deployment API получает public verification material через Authority/issuer → discovery → JWKS. API не хранит private signing key и не зависит от вручную захардкоженного public key как от нормального production механизма.

## Signing keys

Development signing credentials не равны production signing credentials.

В development и тестах допустимы ephemeral/development certificates. В production требуется persistent asymmetric signing key/certificate, принадлежащий только `Intelligence.TradeSystem.Identity`.

Production implementation должна поддерживать:

```text
old signing key
        +
new signing key
        +
transition period
```

Переходный период должен быть достаточным для проверки ещё действующих токенов. Собственный key-rotation protocol не разрабатывается. Конкретный certificate store, secret manager, KMS или mounted secret определяется в deployment/security implementation.

API получает только public verification material через discovery/JWKS. Private signing key не копируется в API, AppHost или другие business services.

## Client registration и consent

OAuth clients регистрируются в Authorization Server/OpenIddict и не хранятся в Domain, `TradeSystemDbContext` или business Application layer.

Будущие logical clients:

```text
trade-web-bff
trade-mobile
trade-desktop
trade-cli
```

В этом PR они не создаются runtime. Для first-party клиентов первого MVP (React/BFF, official mobile, official desktop и CLI) сложный consent UX не является обязательным до появления product requirement. Архитектурно OAuth authorization grants не отключаются; third-party clients могут потребовать consent позже.

## User lifecycle и расширение Identity

OpenIddict не предоставляет полноценный user-management/login product. Identity host владеет:

- login UI;
- registration UI/API;
- password reset;
- email confirmation;
- MFA UX;
- user lifecycle и credential policies.

Для первого runtime этапа допустим минимальный безопасный bootstrap:

```text
create/test user
        ↓
authenticate
        ↓
issue OAuth/OIDC tokens
        ↓
access protected API
```

Self-registration, email verification, password reset и MFA не становятся автоматически окончательным public signup contract. Архитектура должна позволять добавить TOTP, passkeys и external providers без изменения OAuth/OIDC contract API. Identity host в будущем может использовать Google, Microsoft, Apple или другой external login как upstream authentication provider; resource-server Bearer contract не меняется.

## Scope C-05A

C-05A больше не выбирает Authorization Server: выбор принят этим ADR. C-05A должен реализовать:

- отдельный `Intelligence.TradeSystem.Identity` host или эквивалентную отдельную application boundary;
- ASP.NET Core Identity + OpenIddict;
- отдельную Identity/Authorization persistence и отдельный EF migration stream;
- JWT Bearer resource-server configuration в `Intelligence.TradeSystem.Api`;
- issuer, audience, signature, lifetime и required-claims validation;
- минимальный scope contract;
- stable user-delegated `sub`, совпадающий с Domain `UserId` Guid;
- Authorization Code + PKCE для соответствующего public-client proof;
- отсутствие password grant и custom token protocol;
- persistent production signing credentials, discovery/JWKS и rotation-ready key configuration;
- anonymous public market endpoints;
- integration tests для discovery, issuance, validation и API boundary.

C-05A не обязан реализовывать полный identity UX, реальные service clients, сложный consent, MFA, external providers или окончательный scope catalog. C-06 остаётся отдельным этапом authorization, ownership и user isolation.

## Обязательные integration tests для C-05A

Implementation step обязан покрыть минимум:

- OIDC discovery;
- authorization/token flow;
- JWT issuance;
- issuer;
- audience;
- signature;
- expiry;
- `sub`;
- scope;
- valid JWT → protected API success;
- missing JWT → `401`;
- invalid issuer → `401`;
- invalid audience → `401`;
- expired token → `401`;
- invalid signature → `401`;
- user `sub` → Domain `UserId`;
- machine principal != user principal;
- anonymous success для public market endpoint.

Runtime не должен писать tokens или passwords в logs. HTTPS/TLS обязателен вне local development. Browser JavaScript не должен видеть refresh token.

## Почему выбран OpenIddict

OpenIddict выбран потому что:

1. это native .NET/ASP.NET Core стек;
2. он хорошо сочетается с .NET 10;
3. он интегрируется с ASP.NET Core Identity;
4. он поддерживает стандартные OAuth/OIDC flows, включая authorization code, device authorization и client credentials;
5. допускает self-hosted deployment;
6. сохраняет контроль над subject и claims;
7. поддерживает EF Core/PostgreSQL stores;
8. распространяется под Apache 2.0;
9. не требует отдельного Java/runtime стека;
10. не требует обязательной коммерческой лицензии;
11. не привязывает product identity к SaaS vendor;
12. хорошо вписывается в текущие automated tests и deployment model.

Это не означает, что OpenIddict является turnkey IAM product: host должен реализовать необходимую authorization controller, login/consent integration и operational configuration. Именно поэтому ответственность за deployment, key management и security operations явно принята проектом.

## Рассмотренные альтернативы

### A. ASP.NET Core Identity + OpenIddict — Accepted

Преимущества:

- .NET-native и self-hosted;
- open source;
- контроль над user identity, subject и claims;
- PostgreSQL/EF Core;
- стандартные OAuth/OIDC protocols;
- низкий vendor lock-in;
- один operational stack с остальным backend.

Недостатки:

- команда сама эксплуатирует Authorization Server;
- login/user UX нужно реализовывать;
- key management, monitoring, availability и incident response остаются ответственностью проекта.

### B. Keycloak — Rejected for current MVP

Keycloak — полноценный open-source IAM с Admin UI, users, MFA, federation и зрелыми OAuth/OIDC возможностями. Он не считается плохим решением.

Для текущего MVP он отклонён потому что:

- добавляет отдельный Java/Quarkus operational stack;
- увеличивает deployable/platform complexity;
- предоставляет существенно больше возможностей, чем требуется сейчас;
- для текущей .NET-команды и MVP OpenIddict проще интегрировать в существующий стек.

Keycloak остаётся возможной будущей заменой благодаря standards-based resource-server contract.

### C. Duende IdentityServer — Rejected for current project stage

Duende IdentityServer — mature .NET-native framework с сильной OAuth/OIDC поддержкой и развитой BFF ecosystem. Технически он не считается хуже OpenIddict.

Причина отклонения — commercial licensing и будущая licensing dependency. Для текущей стадии постоянная коммерческая зависимость неоправданна при наличии OpenIddict.

### D. Managed external IdP — Rejected as primary foundation for current stage

К этому классу относятся Auth0, Microsoft Entra External ID и другие managed OIDC providers.

Преимущества:

- минимум собственной эксплуатации;
- готовые user flows;
- provider-side security operations.

Недостатки:

- vendor dependency;
- pricing dependency;
- identity data и availability зависят от внешнего сервиса;
- меньше контроля над subject, claims и deployment.

Managed IdP остаётся возможной будущей миграцией благодаря стандартному OIDC contract.

### E. Homemade JWT server — Rejected

Самодельный token endpoint, `JwtTokenService`, refresh-token repository, token rotation или signing protocol не создаются. OpenIddict выбран именно для того, чтобы не реализовывать OAuth/security protocol вручную.

## PostgreSQL и эксплуатационная ответственность

Self-hosted выбор означает, что проект сам отвечает за:

- Authorization Server deployment;
- database backup и restore;
- security updates;
- availability;
- monitoring;
- signing-key protection;
- key rotation;
- user lifecycle;
- incident response.

Это осознанный trade-off выбранного решения, а не скрытая обязанность бизнес-API.

## Обратимость решения

Поскольку `TradeSystem.Api` остаётся standards-based resource server, а Domain не зависит от OpenIddict, будущая замена:

```text
OpenIddict
    →
Keycloak / Auth0 / Entra / другой OIDC provider
```

не должна требовать переписывания Domain, Application или business API contract. Могут измениться issuer, audience, claims mapping, deployment и client configuration. Не должны измениться Domain `UserId`, ownership contract и бизнес-операции только из-за замены Authorization Server.

## Границы этого PR

Не реализуются:

- OpenIddict или ASP.NET Core Identity NuGet packages;
- Identity project;
- `IdentityDbContext`/`AuthorizationDbContext`;
- `IdentityDbContext` или OpenIddict migrations;
- `ApplicationUser`;
- authorization endpoints;
- login или consent UI;
- JWT Bearer middleware;
- authentication middleware;
- BFF, React, SignalR или CORS;
- users, passwords, client registrations или signing certificates;
- actual OAuth clients;
- изменения `Program.cs`, `StartupExtensions.cs`, `Directory.Packages.props`, `*.csproj`, `TradeSystemDbContext`, `TradeSystemDbContextFactory` и существующих migrations.

## Связанные решения и очередь

- ADR-0001 остаётся историческим решением со статусом Superseded.
- ADR-0002 остаётся Accepted и задаёт universal API authentication contract.
- Этот ADR-0003 дополняет ADR-0002 и выбирает конкретный Authorization Server.
- C-05A реализует принятое решение.
- C-06 реализует authorization, ownership и user isolation.
- G-01 реализует React shell, BFF, OAuth/OIDC login и CSRF protection.
- Будущий SignalR использует ту же authenticated identity и Bearer/BFF boundary из ADR-0002.

## Источники

Официальные источники, сверенные при подготовке решения:

- [ASP.NET Core Identity — Microsoft Learn](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/identity?view=aspnetcore-10.0)
- [OpenIddict — official documentation](https://documentation.openiddict.com/)
- [OpenIddict — getting started](https://documentation.openiddict.com/guides/getting-started/)
- [OpenIddict — official GitHub repository](https://github.com/openiddict/openiddict-core)
- [OpenIddict — Apache 2.0 license](https://github.com/openiddict/openiddict-core/blob/dev/LICENSE.md)
- [Keycloak — official documentation](https://www.keycloak.org/documentation)
- [Duende IdentityServer — official product information](https://duendesoftware.com/products/identityserver)
- [Auth0 — official OpenID Connect protocol documentation](https://auth0.com/docs/authenticate/protocols/openid-connect-protocol)
- [Microsoft Entra External ID — official documentation](https://learn.microsoft.com/en-us/entra/external-id/customers/overview-customers-ciam)
- [RFC 6749: The OAuth 2.0 Authorization Framework](https://www.rfc-editor.org/rfc/rfc6749)
- [RFC 6750: The OAuth 2.0 Authorization Framework: Bearer Token Usage](https://www.rfc-editor.org/rfc/rfc6750)
- [RFC 7636: Proof Key for Code Exchange by OAuth Public Clients](https://www.rfc-editor.org/rfc/rfc7636)
- [RFC 8252: OAuth 2.0 for Native Apps](https://www.rfc-editor.org/rfc/rfc8252)
- [RFC 8628: OAuth 2.0 Device Authorization Grant](https://www.rfc-editor.org/rfc/rfc8628)
- [OpenID Connect Core 1.0](https://openid.net/specs/openid-connect-core-1_0.html)
