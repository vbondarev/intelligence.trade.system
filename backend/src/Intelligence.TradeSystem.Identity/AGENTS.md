# AGENTS.md

## Область действия

Этот файл применяется только к `Intelligence.TradeSystem.Identity` и дополняет `../AGENTS.md`. Соседний `Intelligence.TradeSystem.Identity.Migrations` — отдельный project и автоматически под этот файл не подпадает.

## Ответственность и источники решений

- `Identity` — отдельный OAuth 2.0 / OpenID Connect Authorization Server на основе ASP.NET Core Identity и OpenIddict.
- `Intelligence.TradeSystem.Api` остаётся отдельным client-agnostic resource server; не переносить business API logic в Identity.
- Протокольные и архитектурные решения определены в [ADR-0002](../../../docs/adr/0002-universal-api-authentication-strategy.md) и [ADR-0003](../../../docs/adr/0003-authorization-server-selection.md). Не создавай здесь альтернативную source of truth.

## Principals и протокол

- User principal и machine principal различаются. Валидный machine token не становится Domain user и сам по себе не даёт доступа к user-owned данным.
- Стабильный user subject должен быть совместим с Domain `UserId`; email и username не являются business identity.
- Не создавай ad-hoc login, JWT или refresh-token protocol.
- Не меняй принятые OAuth/OIDC, token, grant или PKCE semantics без отдельного architecture decision.
- Logical OIDC client id G-01 — `trade-web-bff`. Это invariant, а не environment-specific setting: `Identity:WebBffClient:ClientId` не подменяется другим значением.
- Browser/BFF решения не расширяй за пределы принятых ADR и [Web BFF contract](../../../docs/web-bff-contract.md): full browser logout выполняется только через standard OIDC end-session, а `prompt=login` остаётся явной возможностью повторной аутентификации.

## Persistence и secrets

- Identity имеет собственную persistence и migration boundary. При изменении schema синхронизируй отдельный project `Intelligence.TradeSystem.Identity.Migrations`.
- Signing private material, access/refresh tokens, passwords и credentials не должны попадать в logs или checked-in configuration.
- Test certificates и secrets оставляй в test-specific configuration.

## Проверки

Изменения Identity, authentication или OAuth/OIDC проверяй через `Intelligence.TradeSystem.Authentication.IntegrationTests`, сохраняющие реальные PostgreSQL, OpenIddict и JwtBearer protocol checks, а также SignalR authentication/authorization, закрытие соединения после истечения access token и REST recovery после reconnect.
