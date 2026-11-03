# Эксплуатация realtime-наблюдения Bybit

## Границы и поддерживаемый режим

Backend наблюдает read-only Bybit Unified Trading Account. Приватный контур
сопровождает только Linear-позиции с подтверждённым активом расчёта USDT или
USDC. Публичный контур отдельно получает mark price для поддерживаемых открытых
позиций.

Realtime уменьшает задержку, но не заменяет полную REST-сверку. Она выполняется
при запуске наблюдателя и восстановлении, планово с интервалом пять минут, а
также по явной ручной команде пользователя. Автоматическая повторная `evaluation`
или создание рекомендации после фактического изменения не запускаются.

Для MVP предполагается один активный backend observer на аккаунт. Распределённое
владение observers между несколькими одновременно работающими backend
экземплярами этим контуром не обеспечивается.

## REST-состояния

`GET /api/v1/me/exchange-accounts` возвращает `realtimeObservation` для каждого
подключения:

| `status` | Интерпретация |
|---|---|
| `unknown` | Состояние наблюдателя ещё не сохранялось |
| `initializing` | Наблюдатель запускается |
| `connected` | Транспорт подключён и heartbeat свежий |
| `reconnecting` | Транспорт восстанавливается |
| `degraded` | Транспорт или полнота наблюдения нарушены |
| `stopped` | Наблюдатель остановлен |

`reasonCode` — короткий машиночитаемый код; он не содержит текст исключения,
payload биржи или credentials. `connected` сам по себе не означает
`factualFreshness = fresh`.

`GET /api/v1/exchange-accounts/{id}/portfolio` возвращает независимые
`factualFreshness` и `valuationFreshness`. Старое поле `isFresh` сохраняет
прежнюю семантику. `valuationFreshness` считается по всем незакрытым позициям
аккаунта, а не по странице списка, и не суммирует денежные значения в разных
активах расчёта.

`GET /api/v1/positions` и `GET /api/v1/positions/{id}` могут содержать
`marketValuation`. `indicativePositionValue` выражается в `settlementAsset`;
это отдельная оценка из `publicTicker`, а не замена фактических полей позиции.
Для рыночной цены действует существующий порог свежести 5 секунд. Последняя
подтверждённая цена может оставаться доступной после устаревания, но статус
должен быть `stale`.

При отсутствии снимка портфеля для принадлежащего пользователю аккаунта
сохраняется `204 No Content`. Отсутствующий и чужой аккаунт по-прежнему дают
одинаковый `404`.

## Телеметрия

Основные instruments приватного контура находятся в meter
`Intelligence.TradeSystem.Infrastructure.Realtime`:

| Метрика | Назначение и допустимые tags |
|---|---|
| `exchange.realtime.providers.active` | Текущие активные приватные observers |
| `exchange.realtime.transport.status_updates` | Сохранённые переходы состояния; tag `status` |
| `exchange.realtime.observations.received` | Входящие события; tag `stream` (`position`/`wallet`) |
| `exchange.realtime.observation.signals.dropped` | Потерянные сигналы очереди |
| `exchange.realtime.recoveries.requested` | Запросы полной REST-сверки |
| `exchange.realtime.heartbeats.written` | Сохранённые heartbeat |
| `exchange.realtime.public_subscription.start_unconfirmed` | Неопределённый результат создания публичной подписки |
| `exchange.realtime.public_subscription.dispose_unconfirmed` | Неподтверждённое освобождение публичной подписки |
| `exchange.realtime.public_subscription.protected_degraded` | Число инструментов в защищённой деградации текущего процесса |

Meter `Intelligence.TradeSystem.Application.Realtime` дополняет их исходами
применения и длительностями:

- `exchange.realtime.observations.apply.outcomes` и
  `exchange.realtime.observations.apply.duration` имеют только bounded tags
  `stream` и `outcome`;
- `exchange.realtime.observations.receive_to_commit.duration` и
  `exchange.realtime.observations.creation_to_commit.duration` измеряются только
  для результата `applied`;
- `exchange.realtime.observations.creation_time_unavailable` считает применённые
  изменения, для которых нельзя достоверно использовать `MessageCreatedAt`;
- `exchange.realtime.observations.receive_time_unavailable` отмечает применённые
  изменения с отсутствующим или недостоверным временем получения;
- `exchange.realtime.reconciliation.duration` имеет bounded tags `kind` и
  `outcome`.

Метрики не содержат `UserId`, `ExchangeAccountId`, `symbol`, credentials,
сырые payload, текст исключения или произвольные идентификаторы. Нулевые/пустые
результаты при отсутствии события не трактуются как доказательство отсутствия
сбоя.

Состояния защищённой деградации публичной подписки хранятся в памяти
координатора. Число таких инструментов — состояние текущего процесса, а не
персистентный аудит. Отдельная метрика «успешное восстановление после
перезапуска» не публикуется: после завершения процесса нет сохранённого
подтверждения, связывающего новую подписку с прежним отказом. Восстановление
после перезапуска подтверждается эксплуатационной процедурой ниже, а не
предполагается по факту запуска.

## Обычный recovery

При `reconnecting` или `degraded`:

1. Проверьте `realtimeObservation.status`, `reasonCode`, свежесть heartbeat и
   метрики drops/recovery.
2. Убедитесь, что плановая REST-сверка продолжает выполняться. Она сохраняет
   watermark/CAS и не должна заменять более новое подтверждённое состояние.
3. Проверьте результат сверки и `factualFreshness`; не интерпретируйте
   `connected` как полное подтверждение фактов.
4. При необходимости пользователь может вызвать штатный account-scoped
   `POST /api/v1/exchange-accounts/{id}/sync`.
5. После смены credentials или отключения аккаунта события старого поколения
   не должны применяться.

## Защищённая деградация публичной подписки

Если результат первоначального `SubscribeAsync` неопределён (`start_unconfirmed`)
или освобождение существующей подписки не подтверждено (`dispose_unconfirmed`),
координатор блокирует новую подписку того же инструмента в текущем процессе.
Ошибка, отмена или timeout после начала SDK-операции не доказывают, что ресурс не
был создан. Повторный `Dispose` тоже не считается доказательством успешного
освобождения.

В этих случаях:

- не перезапускайте только отдельную подписку и не создавайте конкурирующий
  backend instance;
- проверьте `start_unconfirmed`, `dispose_unconfirmed` и
  `protected_degraded`, а также сообщения координатора по типу отказа;
- последняя подтверждённая mark price может сохраняться, но после порога
  свежести не должна выдаваться как свежая;
- восстановление MVP выполняется только после полного завершения прежнего
  процесса-владельца и проверки отсутствия конкурирующего процесса;
- после запуска проверьте создание новой подписки и обновление mark price, затем
  подтвердите `valuationFreshness` и `marketValuation` через REST.

Это исключение касается только публичного market WebSocket. Приватные observers,
REST reconciliation и штатное восстановление безопасных транспортных сбоев
сохраняют обычную retry-политику.

## Методика p95 и полноты обработки

Целевой показатель — p95 не более пяти секунд от биржевого `creationTime`
поддерживаемого фактического изменения до первого успешного REST-ответа с
соответствующим сохранённым состоянием:

`L = T_rest_visible − T_exchange_creationTime`

Для каждого измерения зафиксируйте среду, версию, период, синхронизацию часов,
объём выборки и критерий исправного соединения. Сопоставьте изменение с
`MessageCreatedAt`, фактически применённым состоянием и первым REST-ответом.
Не используйте время получения backend как замену биржевому времени и не
исключайте медленные применимые события постфактум. p95 вычисляйте nearest-rank:
элемент с индексом `ceil(0.95 × N)` в отсортированной выборке.

Отдельно фиксируйте долю `applied`/`duplicate`/`stale`/`ignored`/`recovery_required`,
потерянные сообщения, отсутствие достоверного `creationTime` и REST-only
recovery. Commit не является моментом REST-доступности.

Синтетический прогон подтверждает методику и backend pipeline, но не
характеристики транспорта реального Bybit. Реальное измерение допустимо только
в read-only режиме без торговых операций. При недостаточных доказательствах
запишите `AC-12: NOT_VERIFIED`; не объявляйте SLO достигнутым.

### Проверка PR-6

Автоматический сценарий
[RealtimeRestVisibleLatencyPostgreSqlTests.cs](../backend/src/Intelligence.TradeSystem.Infrastructure.IntegrationTests/RealtimeRestVisibleLatencyPostgreSqlTests.cs)
применяет контролируемые приватные observations к PostgreSQL и получает
первое подтверждение через HTTP `GET /api/v1/positions` в `WebApplicationFactory`.
Прогон от 2 ноября 2026 года использовал 20 synthetic observations:
nearest-rank p95 — 236,14 мс, максимум — 1114,39 мс. Он подтверждает методику и
серверный REST-visible pipeline, но не использует сообщения реального Bybit;
поэтому `AC-12` остаётся `NOT_VERIFIED`.
