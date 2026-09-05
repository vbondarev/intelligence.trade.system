# ADR-0002: Аутентификация универсального API через OAuth 2.0 / OpenID Connect

Статус: Accepted
Заменяет: [ADR-0001: Способ аутентификации пользователей](0001-authentication-strategy.md)
Дата: 5 сентября 2026 года

## Контекст

ADR-0001 выбирал cookie authentication как основную модель для первого Web MVP. После его принятия уточнено фундаментальное требование проекта: `Intelligence.TradeSystem.Api` с самого начала является универсальным, client-agnostic API. Один и тот же бизнес-API должен быть одинаково пригоден для React, mobile, desktop, CLI, native clients и будущих интеграций.

`Intelligence.TradeSystem.Api` является **client-agnostic resource server**. Основной защищённый бизнес-API имеет корень `/api/v1/*` и не должен зависеть от типа клиента. Для разных клиентов не создаются отдельная бизнес-логика или отдельные API-контракты. Backend остаётся source of truth для состояния, оценок, рекомендаций и правил доступа.

В этом ADR разделяются:

- **Authentication** — кто пользователь;
- **Authorization** — какие данные и действия ему доступны.

Создание authenticated identity относится к C-05/C-05A. `UserId` isolation, ownership, authorization и cross-user protection относятся к C-06.

Существующие публичные endpoints, включая текущий публичный market-analysis сценарий и `GET /api/market-analysis/{symbol}/llm-payload`, остаются anonymous. Новый контракт не означает, что весь backend требует token.

## Решение

### Универсальный authentication contract

Для защищённого API принимается общая архитектура:

```text
OAuth 2.0
       +
OpenID Connect
       +
Bearer access tokens
```

Защищённый API принимает access token стандартным способом:

```http
Authorization: Bearer <access_token>
```

Browser cookie не является универсальным authentication contract `/api/v1/*`.

Термины имеют разные роли:

- **OAuth 2.0** — протокол делегированной авторизации и получения access token;
- **OpenID Connect** — identity layer поверх OAuth 2.0 для аутентификации пользователя и стандартных identity claims;
- **Bearer** — способ предъявления access token resource server;
- **JWT** — формат access token.

Для первого MVP целевым форматом access token является **подписанный JWT**. Решение не формулируется как «просто используем JWT»: протокол и границы OAuth/OIDC являются обязательными частями контракта.

### Authorization Server и Resource Server

Архитектурная граница фиксируется так:

```text
Authorization Server
        │
        │ OAuth/OIDC
        │ access token
        ▼
Intelligence.TradeSystem.Api
        │
        │ resource server
        ▼
Application / Domain
```

Authorization Server отвечает за пользовательский login, consent, выпуск, обновление и отзыв токенов в рамках выбранной реализации OAuth/OIDC. `Intelligence.TradeSystem.Api` является только resource server: он принимает access token, валидирует его, строит `ClaimsPrincipal` и передаёт authenticated identity на Application boundary.

API не должен превращаться в самодельный authorization server. В частности, запрещается архитектура:

```text
POST /login
email + password
    ↓
самодельный JWT
    ↓
самодельный refresh token
```

Без OAuth/OIDC authorization server не создаются собственные `JwtTokenService`, `RefreshTokenRepository`, custom token rotation protocol или custom authorization protocol. Token issuance выполняется стандартным OAuth/OIDC authorization server.

### Валидация access token

Будущая JWT Bearer resource-server реализация должна валидировать как минимум:

- signature;
- issuer;
- audience;
- expiration;
- `not-before`, если claim используется;
- необходимые claims и scopes.

Публичные endpoints явно остаются anonymous; наличие JWT contract для защищённого API не меняет этот режим.

### Идентичность пользователя и machine/client identity

User identity и machine/client identity являются разными видами authenticated principal. Валидность access token сама по себе не означает, что его subject можно представить как Domain user.

#### User-delegated access token

Для access token, представляющего пользователя, используется основной стабильный identity claim OAuth/OIDC — `sub`:

```text
sub
  ↓
user identity
  ↓
Domain UserId
```

Предпочтительное направление первого MVP:

```text
sub == Domain UserId Guid
```

если выбранный authorization server позволяет контролировать subject identifier. Если внешний IdP владеет `sub`, на API/Application boundary должно существовать явное сопоставление:

```text
issuer + sub
        ↓
internal UserId(Guid)
```

#### Machine / Client Credentials token

Для access token, полученного через `Client Credentials`, subject представляет не пользователя, а machine/client identity:

```text
subject/client identity
          ↓
service principal / machine identity
```

Machine identity не равна user identity и не должна автоматически преобразовываться в Domain `UserId`. Такой token не получает автоматически доступ к user-owned resources. В частности, `/api/v1/accounts`, `/api/v1/positions`, `/api/v1/portfolio` и `/api/v1/recommendations` не должны считать service principal пользователем только потому, что token валиден.

Будущий service-to-service access требует отдельной authorization model/policy. C-06 должен различать user principal и machine principal; полноценная service-account модель в этом PR не проектируется.

Domain `UserId` остаётся `Guid` и не зависит от email, username, `IdentityUser`, JWT, `ClaimsPrincipal`, cookie, machine identity или конкретного identity provider. Только user-delegated identity boundary может преобразовывать внешнюю user identity в typed `UserId`, не протаскивая OAuth/OIDC-типы в Domain. Это сопоставление не реализуется в C-05.

### Клиенты

Browser является только одним из клиентов.

Для React предпочтительно направление:

```text
React
  ↓
BFF
  ↓
Bearer access token
  ↓
Intelligence.TradeSystem.Api
```

Между React и BFF может использоваться secure HttpOnly cookie. Эта cookie относится только к browser session, не является контрактом `/api/v1/*`, не используется mobile/desktop/CLI и не меняет Bearer contract API. Токены при BFF-подходе не должны попадать в browser JavaScript.

BFF — browser-specific adapter для session/token mediation. Он не является source of truth, не содержит торговую бизнес-логику, не рассчитывает assessments/recommendations, не создаёт отдельную модель пользователей и не заменяет основной API. BFF не реализуется этим PR; его реализация входит в G-01 вместе с React shell и OAuth/OIDC login flow.

#### CSRF на BFF boundary

Если BFF использует автоматически отправляемую browser cookie, BFF обязан иметь явную CSRF-защиту. Одного `HttpOnly` недостаточно: state-changing BFF requests не должны полагаться только на наличие cookie.

Конкретный механизм выбирается при реализации G-01. Допустим стандартный anti-forgery token/header либо другой явно обоснованный эквивалентный механизм. Эта CSRF-защита относится только к cookie-based browser/BFF boundary и не является частью Bearer contract `/api/v1/*`.

Для mobile и desktop public clients предпочтителен стандартный:

```text
Authorization Code + PKCE
```

Client secret не хранится внутри mobile или desktop приложения. Конкретный UX зависит от выбранного authorization server и реализуется позже.

Для CLI не фиксируется username/password → token как основной flow. После выбора authorization server допустимы стандартные варианты, например Device Authorization Flow или Authorization Code + PKCE.

Для будущих machine clients предусматривается стандартный `Client Credentials`. Service accounts и service-to-service runtime не реализуются сейчас, но архитектура API не должна им препятствовать; machine principal не получает user authorization автоматически.

### SignalR

Будущий SignalR использует ту же authentication model и ту же identity, что и REST:

- native/token clients предъявляют Bearer access token;
- browser использует browser-specific integration через BFF поверх той же identity;
- отдельная SignalR identity model не создаётся.

SignalR не реализуется этим PR.

### Публичные и защищённые endpoints

Текущие публичные market endpoints, включая `GET /api/market-analysis/{symbol}/llm-payload`, остаются anonymous. OAuth/Bearer contract относится к будущим защищённым пользовательским endpoints под `/api/v1/*`, например:

```text
/api/v1/accounts
/api/v1/positions
/api/v1/portfolio
/api/v1/recommendations
```

Аутентификация подтверждает личность, но не предоставляет доступ к данным другого пользователя. Ownership, UserId isolation и authorization реализуются отдельно в C-06.

## Конкретный Authorization Server

В этом ADR конкретная реализация Authorization Server не выбирается. Кандидаты для отдельного решения перед или в начале C-05A:

- ASP.NET Core Identity + OpenIddict;
- Keycloak;
- Duende;
- внешний OIDC provider.

Протокол и API contract решены этим ADR; выбор provider, deployment model, user store и operational ownership остаётся отдельным решением. C-05A должен начать с явной фиксации этого выбора, а не подразумевать его. Если для выбора потребуется полноценное сравнение вариантов, оно может быть оформлено отдельным **ADR-0003: Выбор OAuth/OIDC Authorization Server**; ADR-0003 в этот PR не создаётся.

ASP.NET Core Identity поэтому не запрещается как возможная часть выбранного authorization-server решения, но больше не является основным authentication contract API и не принимается автоматически как runtime-решение. В частности, `ASP.NET Core Identity + cookie authentication` из ADR-0001 не переносится в C-05A как основная модель API.

## Границы текущего PR

Этот PR является documentation/architecture-only. Он не реализует:

- authentication middleware или JWT validation;
- authorization server, OpenIddict, Keycloak, Duende или внешний IdP;
- ASP.NET Core Identity runtime;
- login/logout/token endpoints;
- migrations, Identity schema или service accounts;
- BFF, React, SignalR;
- C-06 user isolation;
- C-07 Bybit credentials security.

## Изменение C-05 и следующий шаг

C-05 считается завершённым после принятия этого ADR:

> Принят client-agnostic authentication contract: OAuth 2.0/OpenID Connect, Bearer access tokens и signed JWT как целевой формат access token для защищённого API; browser cookie допускается только на BFF boundary.

C-05A должен реализовать основу OAuth/OIDC-аутентификации универсального API и включать:

- выбрать и зафиксировать конкретный Authorization Server;
- настроить OAuth/OIDC integration;
- настроить JWT Bearer resource server;
- настроить issuer, audience и signing validation;
- обеспечить stable authenticated user `sub`;
- связать user subject с Domain `UserId` (`sub == Guid` либо явное `issuer + subject → UserId(Guid)` mapping);
- добавить integration tests;
- оставить публичные endpoints anonymous.

C-05A не должен добавлять самодельный token protocol. User isolation и authorization остаются задачей C-06.

## Последствия

Положительные последствия:

- один business API и один Bearer contract для Web, mobile, desktop, CLI и будущих integrations;
- стандартное разделение Authorization Server и Resource Server;
- API не зависит от конкретного клиента или способа browser session;
- Domain сохраняет независимость от OAuth/OIDC, JWT, ClaimsPrincipal и IdP;
- user и machine principals не смешиваются автоматически;
- будущие PKCE, Device Authorization и Client Credentials не требуют отдельной бизнес-логики API;
- BFF ограничен browser boundary и не становится вторым backend source of truth.

Ограничения и риски:

- выбор и эксплуатация Authorization Server остаются отдельным архитектурным решением;
- JWT validation требует корректной настройки signature, issuer, audience, lifetime и claims/scopes;
- BFF добавляет browser-specific deployment boundary;
- mapping внешнего `issuer + sub` к внутреннему `UserId` требует явной политики, если IdP не позволяет контролировать subject;
- machine access требует отдельной authorization policy и не может выводиться только из валидности token;
- authenticated identity ещё не означает authorization: C-06 обязателен для user isolation.

## Связанные этапы roadmap

- **C-05** — принятие этого client-agnostic authentication contract; runtime-код не реализуется.
- **C-05A** — выбор Authorization Server и реализация OAuth/OIDC + JWT Bearer resource-server foundation.
- **C-06** — authorization, ownership и изоляция данных по `UserId`.
- **C-07** — защита credentials Bybit.
- **F-03** — пользовательский REST API поверх готовой authentication foundation.
- **F-04** — SignalR с той же Bearer identity model.
- **G-01** — React shell, BFF/session integration и OAuth/OIDC login flow.

## Источники

- [RFC 6749: The OAuth 2.0 Authorization Framework](https://www.rfc-editor.org/rfc/rfc6749)
- [RFC 6750: The OAuth 2.0 Authorization Framework: Bearer Token Usage](https://www.rfc-editor.org/rfc/rfc6750)
- [OpenID Connect Core 1.0](https://openid.net/specs/openid-connect-core-1_0.html)
- [RFC 8252: OAuth 2.0 for Native Apps](https://www.rfc-editor.org/rfc/rfc8252)
- [RFC 8628: OAuth 2.0 Device Authorization Grant](https://www.rfc-editor.org/rfc/rfc8628)
