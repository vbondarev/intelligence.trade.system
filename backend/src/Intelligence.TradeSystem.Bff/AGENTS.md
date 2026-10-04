# AGENTS.md

## Область действия

Этот файл применяется к `Intelligence.TradeSystem.Bff` и дополняет `../AGENTS.md` правилами BFF. Правила React-клиента находятся в `frontend/intelligence-trade-web/AGENTS.md`.

## Ответственность

- `Bff` — только ASP.NET Core browser boundary и host собранного React-клиента: browser session, OIDC login/logout и посредничество с OAuth tokens.
- Business logic, торговые вычисления, рекомендации и правила риска здесь не реализуются: источник бизнес-истины — `Intelligence.TradeSystem.Api`.
- `Bff` не обращается к PostgreSQL и Bybit и не ссылается на Domain, Application, Infrastructure, MarketIntelligence, Exchanges, Api и Identity.
- К `Api` обращайся только по HTTP с `Authorization: Bearer`; cookie authentication в `Api` не переносится.
- Generic reverse proxy к `Api` автоматически не добавляй: каждая browser-facing операция BFF — явное решение со своим контрактом.
- Точное поведение browser-facing endpoints описано в [Web BFF contract](../../../docs/web-bff-contract.md); протокольные решения — в ADR-0002 и ADR-0003.
- OIDC client BFF в Identity называется `trade-web-bff`; имя .NET-проекта на него не влияет, client id не переименовывай.

## Frontend artifact

- Исходники React-клиента находятся в `frontend/intelligence-trade-web`; BFF получает только готовый artifact `dist` и размещает его в runtime `wwwroot`.
- Путь к frontend задаётся единственным MSBuild property `FrontendRoot` в `Intelligence.TradeSystem.Bff.csproj`; не дублируй его в других targets и конфигурациях.
- `wwwroot` генерируется сборкой и не коммитится. Docker image собирает frontend отдельным stage из repository-root context.

## Tokens и session

- Access, refresh и id tokens хранятся только в server-side ticket и не попадают в browser-visible ответы, URL, логи и client bundle.
- Browser получает только opaque HttpOnly session cookie.
- Server-side session создаёт только `ITicketStore.StoreAsync` при sign-in. `RenewAsync` обновляет лишь существующую запись, `RemoveAsync` удаляет её окончательно; все mutations store выполняются под одним process-local lock.
- Изменение tokens или logout intent существующей session выполняй через `ITicketStore.RenewAsync` по текущему session key с повторной проверкой наличия session, а не через `SignInAsync`: stale request не должен воскрешать удалённую session.
- Refresh token grant не повторяется автоматически: повтор может израсходовать ротируемый refresh token. Не подключай для него retry/resilience handlers.
- Client secret передаётся только через environment/secret store и не попадает в checked-in configuration.

## CSRF и logout

- Любой state-changing запрос (`POST`, `PUT`, `PATCH`, `DELETE`) к `/bff/**` всегда проходит centralized antiforgery-проверку; не добавляй исключений и обходов.
- Logout выполняется только через standard OIDC end-session Identity; собственный logout protocol не создавай.
- Logout имеет приоритет над параллельными refresh и продлением session.
- Redirect после login и logout допускается только на local paths и зарегистрированные post-logout URI.

## Realtime

Browser SignalR integration через BFF относится к этапу G-06 и до него не добавляется.

## Проверки

Изменения `Bff` проверяй через `Intelligence.TradeSystem.Bff.Tests`. При изменении OIDC, session, CSRF или logout учитывай Playwright E2E в CI и `Intelligence.TradeSystem.Authentication.IntegrationTests` для Identity-стороны.
