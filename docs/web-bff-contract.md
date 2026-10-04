# Web BFF contract

## Граница

`Intelligence.TradeSystem.Bff` — ASP.NET Core Backend-for-Frontend (BFF) для browser и host собранного React-клиента. Он не содержит business logic, не обращается к PostgreSQL и Bybit и не ссылается на Domain, Application, Infrastructure, MarketIntelligence, Exchanges, Api или Identity.

Поток запросов:

```text
React (browser) → same-origin /bff/** → BFF → Authorization: Bearer → Api
```

- Browser общается только с BFF того же origin.
- BFF выполняет OAuth 2.0 / OpenID Connect Authorization Code + PKCE как confidential client `trade-web-bff` отдельного `Identity`.
- `Api` остаётся client-agnostic resource server и принимает только Bearer access token; cookie authentication в `Api` не используется.
- Generic reverse proxy к `Api` отсутствует: BFF вызывает только явно реализованные операции.

## Source layout и runtime

- Frontend source — `frontend/intelligence-trade-web` (React + TypeScript + Vite + npm). Frontend собирает собственный artifact `dist/` и не знает расположения BFF.
- BFF source — `backend/src/Intelligence.TradeSystem.Bff`. `dotnet build` по умолчанию собирает frontend и копирует `dist` в `wwwroot` BFF; CI и Docker собирают frontend отдельным job/stage и передают `-p:BuildClientAssets=false`. BFF Docker image собирается из корня репозитория и размещает frontend `dist` в `wwwroot`.
- Same-origin runtime — BFF обслуживает React static assets, SPA fallback и `/bff/**` с одного origin (`http://localhost:8082` в Compose и Aspire). Отдельный frontend origin, CORS и CDN не используются.
- Configuration BFF — секция `Bff` (`Bff:Oidc:*`, `Bff:Api:BaseAddress`, `Bff:Session:Lifetime`; environment `Bff__*`). Логическое имя OIDC client в Identity остаётся `trade-web-bff`.

## Server-side browser session

- После успешного OIDC callback BFF создаёт server-side authentication ticket.
- Browser получает только opaque session cookie `TradeSystem.Bff.Session`: `HttpOnly`, `SameSite=Lax`, `Path=/`, `Secure` вне локальных окружений.
- Access token, refresh token и id token хранятся только в server-side ticket и не возвращаются browser.
- Session store находится в памяти процесса (`IMemoryCache`): restart BFF завершает все browser sessions, а несколько replicas без sticky sessions не поддерживаются. Durable/distributed session store не входит в текущий контракт.
- Lifetime session задаётся `Bff:Session:Lifetime` (по умолчанию 8 часов, не больше 24 часов) и продлевается скользяще.
- Session создаётся только при успешном OIDC sign-in. Продление ticket (`RenewAsync`), запись logout intent и сохранение обновлённых tokens изменяют только существующую session: запрос, прочитавший ticket до logout или истечения session, не может её восстановить.
- Удаление session (`RemoveAsync`: logout, завершение session при ошибке refresh, истечение lifetime) терминально. Создание, продление и удаление выполняются под одной блокировкой store, поэтому продление, начатое до удаления, не воскрешает session.
## Browser-facing endpoints

Все ответы `/bff/auth/**` содержат `Cache-Control: no-store`. Неизвестные пути `/bff/**` возвращают `404` и не попадают в SPA fallback.

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

- Access token считается пригодным, если до его истечения остаётся больше 60 секунд; иначе BFF выполняет `refresh_token` grant к token endpoint из discovery Identity.
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
