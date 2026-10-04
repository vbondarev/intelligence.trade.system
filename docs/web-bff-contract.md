# Web BFF contract

## Граница

`Intelligence.TradeSystem.Bff` — ASP.NET Core Backend-for-Frontend (BFF) для browser. Он не содержит business logic, не раздаёт React assets, не обращается к PostgreSQL и Bybit и не ссылается на Domain, Application, Infrastructure, MarketIntelligence, Exchanges, Api или Identity.

Topology:

```text
Browser
   ↓
Frontend service (единый public origin)
   ├── React SPA
   └── /bff/**, /signin-oidc, /signout-callback-oidc → BFF
                                                        ├── OIDC → Identity
                                                        └── Bearer → Api
```

- Browser обращается к BFF только через frontend service того же origin, по relative URLs.
- BFF выполняет OAuth 2.0 / OpenID Connect Authorization Code + PKCE как confidential client `trade-web-bff` отдельного `Identity`.
- `Api` остаётся client-agnostic resource server и принимает только Bearer access token; cookie authentication в `Api` не используется.
- Generic reverse proxy к `Api` отсутствует: BFF вызывает только явно реализованные операции.

## Разделение frontend и BFF

Frontend и BFF — независимые units на всех уровнях; их стабильный контракт — same-origin `/bff/**` и OIDC callback paths.

- Source: frontend — `frontend/intelligence-trade-web` (React + TypeScript + Vite + npm); BFF — `backend/src/Intelligence.TradeSystem.Bff`.
- Build: Vite собирает `frontend/intelligence-trade-web/dist` и не знает расположения backend; `dotnet build` собирает BFF без Node.js/npm и без frontend assets.
- Container: frontend image собирается из context `frontend/intelligence-trade-web` (Node.js build stage → nginx runtime только с `dist` и nginx config); BFF image — из context `backend` (.NET SDK → ASP.NET Core runtime). Ни один image не содержит source или artifacts другого; изменение одного не требует rebuild другого.
- Deployment: Compose services `frontend` и `bff`, Aspire resources `frontend` (Dockerfile) и `bff` (project). Public port `8082` принадлежит frontend; BFF не публикуется как browser endpoint.
- Configuration BFF — секция `Bff` (`Bff:Oidc:*`, `Bff:Api:BaseAddress`, `Bff:Session:Lifetime`, `Bff:Token:*`, `Bff:ForwardedHeaders:KnownNetworks`; environment `Bff__*`). Configuration читается и проверяется один раз при старте; изменение применяется только restart.
- `Bff:Token:RefreshSkew` — насколько заранее до истечения access token BFF выполняет refresh (по умолчанию `00:01:00`, не может быть отрицательным); `Bff:Token:EndpointTimeout` — timeout server-to-server запроса BFF к OIDC token endpoint Identity (по умолчанию `00:00:10`, больше нуля). Значения по умолчанию заданы в `appsettings.json` BFF.
- Логическое имя OIDC client в Identity остаётся `trade-web-bff`. Client id не имеет значения по умолчанию в коде и задаётся deployment configuration одним значением для обеих сторон: `Identity:WebBffClient:ClientId` и `Bff:Oidc:ClientId`. Compose берёт его из необязательной переменной хоста `TRADE_WEB_BFF_CLIENT_ID` (по умолчанию `trade-web-bff`), Aspire AppHost — из единой константы.

## Same-origin routing

Frontend service (nginx) — единственный browser-facing origin (`http://localhost:8082` в Compose и Aspire):

| Path | Обработчик |
|---|---|
| `/bff/**`, `/bff` | BFF |
| `/signin-oidc`, `/signout-callback-oidc` (exact) | BFF |
| `/assets/**` | static assets frontend; отсутствующий файл — `404` |
| остальные paths (`/`, `/app/**`, …) | React SPA, fallback на `index.html` |

- BFF paths никогда не попадают в SPA fallback. Сам BFF не раздаёт React: `GET /app` напрямую к BFF возвращает `404`.
- Internal адрес BFF задаётся deployment setting frontend `BFF_UPSTREAM` (например, `http://bff:8080`) и не попадает в React bundle. CORS, `SameSite=None` и отдельный browser origin для BFF не используются.
- Схема public origin задаётся обязательным deployment setting frontend `PUBLIC_SCHEME` (`http` или `https`, см. «Forwarded request context»). Frontend container без `BFF_UPSTREAM` или с отсутствующим/некорректным `PUBLIC_SCHEME` не стартует.
- Frontend container не получает server secrets: client secret, dev password, tokens и credential-protection keys передаются только BFF/Identity/Api.
- Health: frontend проверяется собственным container healthcheck (nginx отдаёт `index.html`), BFF — своими `/alive` и `/healthz` во внутренней сети. Готовность одного service не означает готовность другого.

## Forwarded request context

- Frontend proxy передаёт BFF `Host` и `X-Forwarded-Host` исходного запроса, `X-Forwarded-Proto` и `X-Forwarded-For`; значения `X-Forwarded-Host`/`X-Forwarded-Proto` proxy задаёт сам, а не пересылает от client.
- `X-Forwarded-Proto` равен deployment setting `PUBLIC_SCHEME`, а не схеме соединения, которое принял nginx (`$scheme`): за внешним TLS terminator frontend container получает plain HTTP, хотя browser открывает origin по HTTPS. `X-Forwarded-Proto` от browser или внешнего client не пересылается и на OIDC redirect URIs не влияет.
  - `PUBLIC_SCHEME=http` — browser обращается к frontend напрямую по HTTP (local Compose и Aspire, `http://localhost:8082`).
  - `PUBLIC_SCHEME=https` — TLS завершается перед frontend container (внешний load balancer/TLS terminator); BFF строит `https://<public host>/signin-oidc`.
- BFF применяет forwarded headers до authentication/OIDC middleware, поэтому OIDC `redirect_uri` и `post_logout_redirect_uri` строятся от public origin (`http://localhost:8082/...`), а не от internal endpoint BFF.
- Forwarded headers принимаются только от loopback и CIDR-сетей `Bff:ForwardedHeaders:KnownNetworks`; от остальных адресов они игнорируются. Учитывается только последнее значение `X-Forwarded-For` (ForwardLimit = 1). Compose доверяет private range Docker bridge networks (`172.16.0.0/12`); production deployment указывает фактическую сеть reverse proxy.

## Server-side browser session

- После успешного OIDC callback BFF создаёт server-side authentication ticket.
- Browser получает только opaque session cookie `TradeSystem.Bff.Session`: `HttpOnly`, `SameSite=Lax`, `Path=/`, `Secure` вне локальных окружений.
- Access token, refresh token и id token хранятся только в server-side ticket и не возвращаются browser.
- Session store находится в памяти процесса (`IMemoryCache`): restart BFF завершает все browser sessions, а несколько replicas без sticky sessions не поддерживаются. Durable/distributed session store не входит в текущий контракт.
- Lifetime session задаётся `Bff:Session:Lifetime` (по умолчанию 8 часов, не больше 24 часов) и продлевается скользяще.
- Session создаётся только при успешном OIDC sign-in. Продление ticket (`RenewAsync`), запись logout intent и сохранение обновлённых tokens изменяют только существующую session: запрос, прочитавший ticket до logout или истечения session, не может её восстановить.
- Удаление session (`RemoveAsync`: logout, завершение session при ошибке refresh, истечение lifetime) терминально. Создание, продление и удаление выполняются под одной блокировкой store, поэтому продление, начатое до удаления, не воскрешает session.

## Browser-facing endpoints

Все ответы `/bff/auth/**` содержат `Cache-Control: no-store`. Неизвестные пути `/bff/**` возвращают `404` BFF и не попадают в SPA fallback frontend.

### `GET /bff/auth/session`

Возвращает состояние session без tokens и claims Identity:

```json
{ "authenticated": true, "user": { "userId": "<guid>", "subject": "<sub>" } }
```

```json
{ "authenticated": false }
```

- Для authenticated session BFF получает актуальный access token и вызывает `GET /api/v1/auth/me`; `userId` берётся только из ответа `Api`.
- `403` — `Api` отклонил principal как не допущенный к user-owned данным.
- `503` — Identity token endpoint или `Api` временно недоступны; session при этом сохраняется.
- Завершённая или отклонённая session возвращается как `authenticated: false`, а не как `401`.

### `GET /bff/auth/login?returnUrl=<local-path>`

- Без session запускает OIDC challenge к Identity; с действующей session сразу выполняет local redirect.
- `returnUrl` принимает только local relative path (до и после percent-decoding): без scheme, host, `//`, `\`, управляющих и пробельных символов, длиной не больше 2048. Пустое значение заменяется на `/app`, некорректное возвращает `400`.
- Обычный login не отправляет `prompt=login`.

### `GET /bff/auth/antiforgery`

- Требует authenticated session, иначе `401`.
- Возвращает `{ "requestToken": "<token>" }` и устанавливает antiforgery cookie `TradeSystem.Bff.Antiforgery` (`HttpOnly`, `SameSite=Strict`).

### `POST /bff/auth/logout`

Первая фаза logout:

- требует authenticated session (`401` без неё) и centralized CSRF-проверку;
- записывает в server-side ticket logout intent `logout_intent_expires_at` со сроком 2 минуты; если session уже завершена параллельным запросом, возвращает `401`;
- действующий intent не стирается параллельным продлением ticket, прочитанного до logout;
- возвращает только same-origin адрес второй фазы: `{ "redirectUrl": "/bff/auth/logout/complete" }`.

### `GET /bff/auth/logout/complete`

Вторая фаза logout:

- без authenticated session или без действующего logout intent выполняет redirect на `/` и session не завершает, поэтому внешняя ссылка или prefetch не разлогинивают пользователя;
- с действующим intent одновременно удаляет BFF session (cookie и server-side ticket) и выполняет standard OIDC sign-out, который перенаправляет browser на Identity end-session endpoint.

## CSRF

- Все state-changing запросы (`POST`, `PUT`, `PATCH`, `DELETE`) к `/bff/**` централизованно требуют antiforgery request token в header `X-CSRF-TOKEN`; при его отсутствии или недействительности возвращается `400`.
- `SameSite` cookie не считается достаточной CSRF-защитой.
- Новые unsafe `/bff/**` endpoints автоматически попадают под эту проверку и не должны её обходить.

## Access token и refresh

- Access token считается пригодным, если до его истечения остаётся больше `Bff:Token:RefreshSkew` (по умолчанию 1 минута); иначе BFF выполняет `refresh_token` grant к token endpoint из discovery Identity. Запрос к token endpoint ограничен `Bff:Token:EndpointTimeout` (по умолчанию 10 секунд).
- Refresh одного пользователя сериализуется: параллельные запросы ожидают один refresh и используют его результат, а не расходуют ротируемый refresh token повторно.
- Automatic retry для `refresh_token` grant не выполняется: повтор может израсходовать уже ротированный refresh token.
- `invalid_grant` завершает BFF session.
- Network error, timeout, недоступный discovery, `5xx` и другие ответы token endpoint возвращают `503` и сохраняют session для следующей попытки.
- Если `Api` отвечает `401` на ещё не истёкший token, BFF выполняет один принудительный refresh и один повтор запроса; повторный `401` завершает BFF session.
- Logout имеет приоритет над refresh и продлением session: если session удалена, пока refresh ожидал token endpoint, полученные tokens не сохраняются, session не восстанавливается, а запрос возвращает `authenticated: false`.

## Full SSO logout

- Logout завершает BFF session и через standard OIDC end-session (`/connect/endsession`) — собственную SSO session Identity. После него новый authorization request требует повторного ввода credentials.
- Identity принимает только `post_logout_redirect_uri`, зарегистрированный у client (`Identity:WebBffClient:PostLogoutRedirectUris`); незарегистрированный адрес отклоняется. BFF использует `/signout-callback-oidc` и затем возвращает browser на `/`.
- Identity завершает SSO session только для end-session request, прошедшего валидацию OpenIddict, с валидным `id_token_hint` и зарегистрированным `post_logout_redirect_uri`. Request без любого из них, в том числе «голый» `/connect/endsession` по внешней ссылке, отклоняется standard OAuth error `invalid_request` и SSO session не завершает. Поэтому full logout возможен только через CSRF-защищённую цепочку BFF.
- При активной SSO session subject (`sub`) из `id_token_hint` обязан совпадать со стабильным user id пользователя этой session. Валидный ID token другого пользователя (например, собственный token атакующего) отклоняется `invalid_request` и чужую SSO session не завершает. Без активной SSO session request с валидным `id_token_hint` и зарегистрированным `post_logout_redirect_uri` завершается обычным redirect: завершать нечего, а BFF logout возвращает browser на `/signout-callback-oidc`.
- Logout не означает мгновенный отзыв уже выданных JWT access tokens: они остаются валидными для `Api` до истечения срока. Token introspection, blacklist и immediate revocation не входят в контракт.

## `prompt=login`

- Identity поддерживает `prompt=login` как явную возможность повторной аутентификации: authorization request с `prompt=login` показывает login form даже при действующей SSO session.
- BFF не добавляет `prompt=login` автоматически.
- `max_age`, MFA и другие step-up механизмы не входят в контракт.

## Token isolation

- Access и refresh tokens не попадают в JavaScript, `localStorage`, `sessionStorage`, IndexedDB, URL, browser-visible JSON, Vite build output и логи.
- Client secret передаётся BFF и Identity только через environment/secret store и не попадает в checked-in configuration и client bundle.
- React-клиент не хранит auth state в browser storage: источник истины — `GET /bff/auth/session`.

## Realtime

Browser SignalR integration через BFF до этапа G-06 отсутствует. Bearer-контракт `/hubs/v1/updates` описан в [Realtime contract v1](realtime-v1-contract.md).
