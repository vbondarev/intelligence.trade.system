# AGENTS.md

## Область действия

Этот файл применяется к `Intelligence.TradeSystem.Bff` и дополняет `../AGENTS.md` правилами BFF. Правила React-клиента находятся в `frontend/intelligence-trade-web/AGENTS.md`.

## Ответственность

- `Bff` — только ASP.NET Core browser boundary: browser session, OIDC login/logout и посредничество с OAuth tokens. BFF обслуживает только `/bff/**`, OIDC callbacks (`/signin-oidc`, `/signout-callback-oidc`) и service endpoints (`/alive`, `/healthz`).
- Business logic, торговые вычисления, рекомендации и правила риска здесь не реализуются: источник бизнес-истины — `Intelligence.TradeSystem.Api`.
- `Bff` не обращается к PostgreSQL и Bybit и не ссылается на Domain, Application, Infrastructure, MarketIntelligence, Exchanges, Api и Identity.
- К `Api` обращайся только по HTTP с `Authorization: Bearer`; cookie authentication в `Api` не переносится.
- Generic reverse proxy к `Api` автоматически не добавляй: каждая browser-facing операция BFF — явное решение со своим контрактом.
- Точное поведение browser-facing endpoints описано в [Web BFF contract](../../../docs/web-bff-contract.md); протокольные решения — в ADR-0002 и ADR-0003.
- Logical OIDC client id G-01 — `trade-web-bff`. Это invariant, а не environment-specific setting; client id не переименовывай и не подменяй deployment configuration. Имя .NET-проекта на client id не влияет.

## Независимость от frontend

- BFF и React-клиент (`frontend/intelligence-trade-web`) — отдельные build, container и deployment units. BFF не является static frontend host: не добавляй React assets, `UseStaticFiles`/SPA fallback для React, npm/Vite targets в `.csproj` и Node stages или frontend файлы в Dockerfile.
- BFF Docker image собирается из context `backend/` и не должен требовать файлов из `frontend/`.
- Browser обращается к BFF только через frontend service на едином public origin; BFF не публикуется как отдельный browser endpoint.
- BFF работает за frontend reverse proxy: forwarded headers применяются до authentication, чтобы OIDC redirect URIs строились от public origin. Они принимаются только от loopback и сетей из `Bff:ForwardedHeaders:KnownNetworks`; не доверяй им безусловно.

## Tokens и session

- Access, refresh и id tokens хранятся только в server-side ticket и не попадают в browser-visible ответы, URL, логи и client bundle.
- Browser получает только opaque HttpOnly session cookie.
- Server-side session создаёт только `ITicketStore.StoreAsync` при sign-in. `RenewAsync` обновляет лишь существующую запись, `RemoveAsync` удаляет её окончательно; все mutations store выполняются под одним process-local lock.
- Изменение tokens или logout intent существующей session выполняй через `ITicketStore.RenewAsync` по текущему session key с повторной проверкой наличия session, а не через `SignInAsync`: stale request не должен воскрешать удалённую session.
- Порог refresh access token — 60 секунд и не выносится в configuration.
- Refresh token grant не повторяется автоматически: повтор может израсходовать ротируемый refresh token. Не подключай для него retry/resilience handlers.
- До отправки refresh grant отмена browser request допустима. После начала grant получение ответа и запись новых tokens в существующий ticket не следуют за `RequestAborted`: их отменяют timeout token endpoint и остановка host.
- Lock refresh одного subject не снимается, пока grant выполняется или его ожидают. Не храни его в cache с истечением по lifetime session.
- Client secret передаётся только через environment/secret store и не попадает в checked-in configuration.

## CSRF и logout

- Любой state-changing запрос (`POST`, `PUT`, `PATCH`, `DELETE`) к `/bff/**` всегда проходит centralized antiforgery-проверку; не добавляй исключений и обходов.
- Logout выполняется только через standard OIDC end-session Identity; собственный logout protocol не создавай.
- Logout имеет приоритет над параллельными refresh и продлением session.
- Redirect после login и logout допускается только на local paths и зарегистрированные post-logout URI.

## Realtime

Browser SignalR integration через BFF относится к этапу G-06 и до него не добавляется.

## Проверки

Изменения `Bff` проверяй через `Intelligence.TradeSystem.Bff.Tests`. При изменении OIDC, session, CSRF, logout или forwarded headers учитывай Playwright E2E через frontend service в CI и `Intelligence.TradeSystem.Authentication.IntegrationTests` для Identity-стороны.
