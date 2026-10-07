# ADR-0005: Расширяемая внешняя аутентификация

## Статус

Принято.

Решение дополняет [ADR-0002](0002-universal-api-authentication-strategy.md) и [ADR-0003](0003-authorization-server-selection.md) и фиксирует архитектурные решения Issue #187. ADR-0002 и ADR-0003 остаются действующими.

## Контекст

Текущая browser authentication architecture уже разделяет клиент, browser security boundary, Authorization Server и resource server:

```text
React
  ↓
BFF
  ↓
Intelligence.TradeSystem.Identity
  ↓
OpenIddict
  ↓
Intelligence.TradeSystem.Api
```

`Intelligence.TradeSystem.Identity` является self-hosted Authorization Server. ASP.NET Core Identity владеет внутренним `ApplicationUser` и credential lifecycle, а OpenIddict формирует OAuth 2.0 / OpenID Connect boundary. BFF является OIDC client Identity и не должен аутентифицироваться напрямую в Google, Microsoft, Yandex, GitHub, ЕСИА или другом внешнем поставщике аутентификации. `Api` независимо от способа входа пользователя получает тот же Bearer access token от Identity.

Существующий invariant ADR-0003 сохраняется:

```text
ApplicationUser.Id
        =
OIDC sub
        =
Domain UserId.Value
```

External authentication расширяет только upstream login внутри Identity. Внешний provider subject не становится `Domain UserId` и не требует нового `issuer + sub → Domain UserId` mapping в `Api`.

## Решение

### AD-01. Identity остаётся единственной provider-aware границей

Целевая граница:

```text
                         ┌─ Password
                         ├─ Google
                         ├─ Microsoft
React → BFF → Identity ──┼─ Yandex
                         ├─ GitHub
                         ├─ ESIA
                         └─ будущие providers
                              │
                              ▼
                       ApplicationUser
                              │
                              ▼
                       OpenIddict subject
```

Provider-specific authentication заканчивается внутри `Intelligence.TradeSystem.Identity`. BFF и `Api` не должны знать, через какой upstream provider вошёл пользователь, а Domain/Application не получают provider-specific types или claims. Смена или добавление способа входа не создаёт новую business identity.

### AD-02. External login сопоставляется со стабильным ApplicationUser

Каноническое сопоставление:

```text
(LoginProvider, ProviderKey)
        ↓
ApplicationUser.Id
        ↓
OpenIddict sub
        ↓
Domain UserId
```

Upstream provider identity и собственный OIDC subject TradeSystem имеют разный смысл:

```text
upstream provider identity
        ≠
TradeSystem OIDC sub
```

Provider identifier используется внутри Identity для нахождения связанного `ApplicationUser`. После этого собственный OpenIddict `sub` системы остаётся `ApplicationUser.Id`.

Текущая ASP.NET Core Identity persistence уже поддерживает эту модель через `AspNetUserLogins` с ключом `LoginProvider + ProviderKey`. Базовая external-login association сама по себе не требует введения отдельной таблицы этим ADR.

### AD-03. Один пользователь может иметь несколько способов входа

Один внутренний пользователь может иметь несколько credential/login methods:

```text
ApplicationUser X
├── local password
├── Google external login
├── Microsoft external login
├── Yandex external login
└── GitHub external login
```

Все они разрешаются в один `ApplicationUser.Id = X`, поэтому сохраняются один OpenIddict `sub = X` и один Domain `UserId = X`. External login не создаёт отдельного Domain user только из-за другого способа аутентификации.

### AD-04. Email не является ключом объединения identity

Email может быть атрибутом внешней учётной записи или дополнительным UX-сигналом, но не является стабильным provider account identity и не доказывает, что две local/external identity принадлежат одному человеку.

Автоматическое связывание пользователей только по совпавшему email запрещено. Текущий `RequireUniqueEmail = false` дополнительно исключает email как уникальный системный ключ.

Связывание нового external login с существующим `ApplicationUser` должно быть отдельной явной защищённой операцией. Конкретный UX, reauthentication protocol, поведение первого входа при совпавшем email и алгоритм формирования `UserName` для external-only user определяются в G-AUTH-01.

### AD-05. Расширение выполняется через ASP.NET Core authentication schemes/handlers

Новый внешний поставщик аутентификации добавляется внутри Identity как отдельная ASP.NET Core authentication scheme/handler/adapter. После успешной provider-specific authentication разрешение external identity в `ApplicationUser` использует общий workflow.

Целевая архитектура не строится вокруг централизованного набора условий `if Google / else Yandex / else GitHub`.

Первый базовый набор направления G-AUTH рассчитан на Google, Microsoft, Yandex и GitHub; ЕСИА является специализированным последующим расширением.

Конкретные NuGet packages, authorization/token/user-info endpoints, scopes, claim names, callback URLs и handler type каждого provider являются implementation details будущих G-AUTH задач и этим ADR не фиксируются.

### AD-06. Состояние provider разделяется на четыре аспекта

Для каждого external authentication provider логически различаются:

```text
Supported
Configured
Enabled
AllowNewUsers
```

- `Supported` — приложение содержит реализацию данного способа входа.
- `Configured` — deployment содержит обязательную техническую конфигурацию provider.
- `Enabled` — администратор разрешил использование provider в runtime.
- `AllowNewUsers` — через provider разрешено создавать новых внутренних пользователей.

Вычисляемая доступность provider:

```text
Available =
    Supported
    && Configured
    && Enabled
```

`AllowNewUsers` не входит в сам факт возможности входа уже связанного пользователя. Поэтому допустим режим:

```text
Enabled = true
AllowNewUsers = false
```

при котором существующие linked users продолжают входить, а создание новых пользователей через provider запрещено.

Exact C# model, persistence entity и API contract этим ADR не задаются.

### AD-07. Secrets отделены от административного runtime state

Deployment/security configuration включает, в зависимости от provider:

```text
ClientId
ClientSecret
private keys
certificates
другой provider secret material
```

Эти значения не управляются через административное product API и не должны попадать в checked-in configuration.

Административное runtime state концептуально может включать:

```text
Enabled
AllowNewUsers
DisplayOrder
UpdatedAt
UpdatedBy
```

Его будущая persistence принадлежит Identity boundary.

Этот ADR не выбирает entity/table/schema, secret manager, configuration section names или hot-reload implementation. Архитектурное требование состоит в том, что включение и выключение технически поддержанного и настроенного provider не требует изменения checked-in configuration или redeploy.

### AD-08. Отключение provider не разрушает identity

Отключение provider означает ограничение новых authentication attempts, а не разрушение существующей identity:

```text
Disable provider
≠ Delete external login
≠ Unlink user
≠ Delete ApplicationUser
≠ Terminate existing sessions
```

При отключении provider:

- новые authentication attempts через него запрещаются;
- provider не предлагается как доступный способ входа;
- существующие external-login associations сохраняются;
- существующие Identity/BFF sessions не завершаются автоматически;
- повторное включение восстанавливает возможность входа.

Отдельная session-revocation subsystem этим решением не вводится.

### AD-09. Административная авторизация строится вокруг permission

Будущее управление provider защищается permission:

```text
identity.providers.manage
```

Целевое направление authorization model:

```text
User
 ↓
Roles
 ↓
Permissions
 ↓
Authorization Policies
```

Конкретная роль с именем `Admin` не является security contract этой возможности. Текущая Identity implementation не обязана в рамках этого ADR иметь role/permission persistence, role tables или конкретную claim schema. Exact implementation определяется отдельной задачей.

### AD-10. ЕСИА является специализированным расширением той же модели

Базовое направление G-AUTH рассчитано на Google, Microsoft, Yandex и GitHub. Архитектура допускает Apple, VK ID и другие будущие OAuth 2.0 / OpenID Connect или специализированные providers.

ЕСИА сохраняет тот же внутренний invariant:

```text
external login
    ↓
ApplicationUser
    ↓
TradeSystem OpenIddict sub
    ↓
Domain UserId
```

Из-за дополнительных организационных, сертификатных и протокольных требований его реализация выделяется в G-AUTH-03. Конкретная ЕСИА integration scheme, certificates и endpoints этим ADR не задаются.

## Последствия

Положительные последствия:

- способ входа отделён от business identity;
- один пользователь сохраняет тот же портфель, позиции и историю при использовании разных способов входа;
- Bearer/OIDC contract ADR-0002 и ADR-0003 не меняется;
- BFF contract не меняется;
- новый provider добавляется внутри Identity;
- runtime enable/disable provider не требует удаления существующих user links;
- будущая roles/permissions model может развиваться независимо от provider adapters.

Ограничения и риски:

- проект самостоятельно отвечает за безопасность external-provider integration;
- account linking требует отдельного защищённого workflow;
- provider availability становится отдельным runtime concern;
- административная authorization model пока не реализована;
- protocol details каждого provider требуют отдельной проверки при реализации.

## Связь с существующими ADR

[ADR-0002](0002-universal-api-authentication-strategy.md) остаётся без изменений и продолжает определять client-agnostic OAuth/OIDC + Bearer contract защищённого API.

[ADR-0003](0003-authorization-server-selection.md) остаётся без изменений и продолжает определять ASP.NET Core Identity + OpenIddict как self-hosted Authorization Server.

ADR-0005 уточняет только upstream authentication внутри уже выбранного Identity boundary и сохраняет invariant:

```text
ApplicationUser.Id
=
OIDC sub
=
Domain UserId.Value
```

## Отложенные вопросы и границы решения

Этот ADR намеренно не определяет:

- exact first-login/JIT creation UX;
- username generation для external-only users;
- explicit account-linking UX;
- unlink flow;
- запрет unlink последнего usable login method;
- provider-specific packages, endpoints, scopes и claims;
- provider token persistence — по умолчанию она не требуется и может появиться только при отдельной необходимости обращения к provider API;
- roles/permissions persistence;
- admin API routes;
- `/bff/admin/**` — будущий BFF administrative contract, не принимаемый этим ADR;
- `/app/admin/**` — будущий browser administrative route contract, не принимаемый этим ADR;
- конкретный admin UI;
- exact Identity DB entity для provider runtime state;
- secret-store technology;
- детали реализации ЕСИА.

Эти решения принимаются в соответствующих G-AUTH задачах и не являются частью Issue #187.
