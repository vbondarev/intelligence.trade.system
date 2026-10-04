# AGENTS.md

## Область действия

Этот файл применяется к `Intelligence.TradeSystem.Web` и дополняет `../AGENTS.md` правилами host React-клиента и BFF. Для `ClientApp` дополнительно действует `ClientApp/AGENTS.md`.

## Ответственность

- `Web` — только BFF и host собранного React-клиента: browser session, OIDC login/logout и посредничество с OAuth tokens.
- Business logic, торговые вычисления, рекомендации и правила риска здесь не реализуются: источник бизнес-истины — `Intelligence.TradeSystem.Api`.
- `Web` не обращается к PostgreSQL и Bybit и не ссылается на Domain, Application, Infrastructure, MarketIntelligence, Exchanges, Api и Identity.
- К `Api` обращайся только по HTTP с `Authorization: Bearer`; cookie authentication в `Api` не переносится.
- Generic reverse proxy к `Api` автоматически не добавляй: каждая browser-facing операция BFF — явное решение со своим контрактом.
- Точное поведение browser-facing endpoints описано в [Web BFF contract](../../../docs/web-bff-contract.md); протокольные решения — в ADR-0002 и ADR-0003.

## Tokens и session

- Access, refresh и id tokens хранятся только в server-side ticket и не попадают в browser-visible ответы, URL, логи и client bundle.
- Browser получает только opaque HttpOnly session cookie.
- Refresh token grant не повторяется автоматически: повтор может израсходовать ротируемый refresh token. Не подключай для него retry/resilience handlers.
- Client secret передаётся только через environment/secret store и не попадает в checked-in configuration.

## CSRF и logout

- Любой state-changing запрос (`POST`, `PUT`, `PATCH`, `DELETE`) к `/bff/**` всегда проходит centralized antiforgery-проверку; не добавляй исключений и обходов.
- Logout выполняется только через standard OIDC end-session Identity; собственный logout protocol не создавай.
- Redirect после login и logout допускается только на local paths и зарегистрированные post-logout URI.

## Realtime

Browser SignalR integration через BFF относится к этапу G-06 и до него не добавляется.

## Проверки

Изменения `Web` проверяй через `Intelligence.TradeSystem.Web.Tests`. При изменении OIDC, session, CSRF или logout учитывай Playwright E2E в CI и `Intelligence.TradeSystem.Authentication.IntegrationTests` для Identity-стороны.
