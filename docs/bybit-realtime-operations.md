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

## Метаданные инструментов и settlement asset

Перед созданием приватных подписок `BybitRealtimeProvider` вызывает
`BybitInstrumentSettlementResolver.EnsureLoadedAsync`. Подписки запускаются после
этой начальной попытки загрузки; суффикс символа не используется как замена
подтверждённому settlement asset.

`BybitInstrumentSettlementResolver` зарегистрирован как общий singleton
процесса и используется providers разных аккаунтов. Его semaphore сериализует
полные обновления. У каждого работающего `BybitRealtimeProvider` есть свой
`BybitSettlementMetadataRefreshLoop`: maintenance timer подаёт сигнал раз в
минуту, но HTTP-запрос не выполняется каждую минуту. Подтверждённый snapshot
считается свежим в течение 1 часа; минимальный интервал между попытками полного
обновления — 5 минут. После `RateLimited` общий resolver не предпринимает новые
попытки в течение 10 минут.

Полная загрузка запрашивает до 1000 инструментов на страницу и ограничена 20
страницами. Snapshot заменяется только после полного подтверждённого обхода с
достоверными, непротиворечивыми метаданными. Ошибка страницы, неполный или
некорректный ответ, пустой обязательный результат либо противоречие оставляют
последний подтверждённый snapshot без изменений. Если подтверждённого
settlement asset нет, он остаётся неизвестным: его нельзя выводить из символа
или подставлять по умолчанию.

Для неизвестного символа provider отправляет неблокирующий сигнал обновления.
Ограниченный канал и флаг запроса объединяют повторные сигналы; обработчик
биржевого сообщения не ждёт загрузку metadata. Обновление по такому сигналу
также проходит через общий resolver и его сериализацию/cooldown.

`BybitRealtimeObservationMapper` классифицирует наблюдение с неподтверждённым
settlement asset как `Ambiguous / SettlementAssetUnconfirmed`, а не применяет
его как позицию или закрытие. Для восстановления используется существующий REST
reconciliation/recovery; статус наблюдения и результат сверки проверяются через
доступные REST-состояния. Не считайте появление metadata или повтор сообщения
гарантией автоматического восстановления.

При диагностике различайте:

- `RateLimited` — биржа ограничила запрос; для общего resolver действует
  10-минутный cooldown;
- `Unavailable` — временно недоступен внешний вызов; действуют общий минимальный
  интервал попыток и сохранение последнего подтверждённого snapshot;
- `InvalidResponse` — ответ неполон, пуст, противоречив или не прошёл проверку;
  он не публикуется как новый snapshot.

Диагностическая запись resolver содержит только тип сбоя, признак retryability
и безопасный provider code. Не копируйте в тикеты или логи credentials, текст
секретов или сырой exchange payload. При остановке provider metadata refresh
loop отменяется и ожидается до завершения вместе с остановкой остальных ресурсов.

Безопасная процедура: проверить `FailureKind`/`Retryable`/безопасный
`ProviderCode` и `realtimeObservation`, дождаться штатных ограниченных попыток
обновления, затем при необходимости использовать существующую account-scoped
REST-сверку и проверить её результат. Не меняйте credentials или permissions и
не создавайте, не меняйте и не закрывайте ордера или позиции ради диагностики.

## Телеметрия

Основные instruments приватного контура находятся в meter
`Intelligence.TradeSystem.Infrastructure.Realtime`:

| Метрика | Назначение и допустимые tags |
|---|---|
| `exchange.realtime.providers.active` | Текущие активные приватные observers |
| `exchange.realtime.transport.status_updates` | Успешно сохранённые обновления от transport signals в `PersistTransportStateAsync`. Tag `status` отражает нормализованный статус наблюдения |
| `exchange.realtime.transport.gap_signals` | Сигналы о возможном пропуске, принятые supervisor. Unit `{signal}`. Tags `worker=exchange-realtime-observation`, `exchange=Bybit`, `reason=connection_lost` или `reason=message_rejected` |
| `exchange.realtime.observations.received` | Входящие события; tag `stream` (`position`/`wallet`) |
| `exchange.realtime.observation.signals.dropped` | Отказы постановки наблюдений в очередь на границе supervisor |
| `exchange.realtime.recoveries.requested` | Запросы полной REST-сверки |
| `exchange.realtime.heartbeats.written` | Сохранённые heartbeat |
| `exchange.realtime.public_subscription.start_unconfirmed` | Неопределённый результат создания публичной подписки |
| `exchange.realtime.public_subscription.dispose_unconfirmed` | Неподтверждённое освобождение публичной подписки |
| `exchange.realtime.public_subscription.protected_degraded` | Число инструментов в защищённой деградации текущего процесса |

Счётчик обновлений транспорта охватывает сохранение сигналов и не служит
полным журналом жизненного цикла. Он не включает все пути `Initializing`,
`Stopped` и внутренней деградации. Сигналы могут объединяться до сохранения,
поэтому отдельный callback не всегда отражается в счётчике. Повторное
сохранение одного статуса не обязательно означает переход между состояниями.

Meter `Intelligence.TradeSystem.Application.Realtime` дополняет эти сигналы
учётом очистки очереди, исходов применения и длительности операций:

- `exchange.realtime.observations.queue.discarded` считает наблюдения, которые
  координатор принял в очередь, а затем отбросил без применения. Единственный
  tag `reason` принимает значения `recovery_required`, `generation_superseded`
  и `account_unavailable`;
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
- `exchange.realtime.reconciliation.duration` измеряет длительность REST-сверки
  и фиксирует её вид и исход в bounded tags `kind` и `outcome`. Метка
  `kind=recovery` отражает локальный запрос или сохранённый `RecoveryRequired`
  того же поколения в снимке, прочитанном перед REST-запросом.

Сигнал о возможном пропуске учитывается при получении `ConnectionLost` или
`MessageRejected` после регистрации pending state. Повтор сохранения этого
состояния и callback `Restored` счётчик не увеличивают.

В meter `Intelligence.TradeSystem.Application.Realtime` счётчик
`exchange.realtime.rest.applied_changes` учитывает изменения, сохранённые из
account-wide REST-снимка. Unit `{change}`. Допустимы только tags
`resource=position|wallet` и `kind=recovery|reconciliation`. Счётчик
публикуется после commit REST-транзакции, до отдельного подтверждения recovery
epoch. Сверка без изменений, откат и проигранная попытка записи его не
увеличивают. Если принят только один из ресурсов, учитывается только он.

Для `position` учитываются открытие, увеличение, уменьшение, изменение и
закрытие позиции. Первичное обнаружение тоже может увеличить счётчик.
Для `wallet` учитывается изменение известных значений капитала. Обновление
только времени наблюдения и первое заполнение капитала счётчик не увеличивают.
Вид `recovery` включает и стартовую сверку при установленном `RecoveryRequired`.

Эти счётчики описывают разные наблюдаемые факты. Сигнал о возможном пропуске
может сопровождать несколько изменений или ни одного. Сверка может сохранить
изменение без транспортного сигнала. Сумма счётчиков не даёт числа пропущенных
биржевых сообщений и не измеряет полноту доставки от Bybit.

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

**Отложенная приёмка AC-12 (Human Decision от 05.11.2026).**
Эксплуатационное подтверждение реального p95 перенесено из G-06 в
[Issue #222](https://github.com/vbondarev/intelligence.trade.system/issues/222).
До получения достоверных live-доказательств используйте статус
`AC-12: DEFERRED / NOT_VERIFIED`. Целевые 5 секунд, методика и безопасность
проверки не изменены. В G-06 остаются синтетический REST-visible сценарий,
телеметрия, а также обязательная проверка полноты обработки, ошибок,
пропусков и восстановления по AC-13. Перенос AC-12 не подтверждает
эксплуатационный SLO и не ослабляет корректность REST reconciliation.

### Контролируемый пропуск и восстановление через REST (AC-13)

В [PostgreSQL-сценарии supervisor](../backend/src/Intelligence.TradeSystem.Infrastructure.IntegrationTests/RealtimeReconciliationPostgreSqlTests.Supervisor.cs)
после начального commit известны размер позиции 1 и капитал 1000. Размер 2
и капитал 1100 доступны только в следующем полном REST-снимке. Сценарий
посылает один `ConnectionLost` и не доставляет position или wallet
observations для двух изменений. После commit он проверяет оба сохранённых
значения, отсутствие входящих наблюдений и WS apply, одно измерение
`gap_signals` и по одному измерению `rest.applied_changes` для `position`
и `wallet` с `kind=recovery`. Следующий `Restored` запускает сверку с теми же
значениями и не увеличивает новые счётчики.

Воспроизведение из корня репозитория с Docker/Testcontainers:

```powershell
dotnet test backend/src/Intelligence.TradeSystem.Infrastructure.IntegrationTests/Intelligence.TradeSystem.Infrastructure.IntegrationTests.csproj --configuration Release --filter "FullyQualifiedName~Supervisor_keeps_healthy_and_sdk_reconnecting_session_and_runs_rest_recovery_after_restored" --logger "console;verbosity=detailed"
```

Известный объём пропуска задаётся тестовыми данными. Эксплуатационные
метрики отражают наблюдаемый сигнал и сохранённые изменения по отдельности.

### Проверка PR-6

Автоматический сценарий
[RealtimeRestVisibleLatencyPostgreSqlTests.cs](../backend/src/Intelligence.TradeSystem.Infrastructure.IntegrationTests/RealtimeRestVisibleLatencyPostgreSqlTests.cs)
применяет контролируемые приватные observations к PostgreSQL и получает
первое подтверждение через HTTP `GET /api/v1/positions` в `WebApplicationFactory`.
Локальный прогон 3 ноября 2026 года использовал 20 synthetic position
observations: все 20 получили outcome `applied` и REST-подтверждение;
nearest-rank p95 — 332,01 мс, максимум — 1617,81 мс. Воспроизвести его можно
командой:

```powershell
dotnet test backend/src/Intelligence.TradeSystem.Infrastructure.IntegrationTests/Intelligence.TradeSystem.Infrastructure.IntegrationTests.csproj --configuration Release --no-restore --filter "FullyQualifiedName~RealtimeRestVisibleLatencyPostgreSqlTests.Private_position_observations_become_visible_through_rest_and_measure_nearest_rank_p95" --logger "console;verbosity=detailed"
```

Этот synthetic сценарий проверяет только серверный REST-visible pipeline. Он не
подключается к Bybit и не моделирует дубликаты, пропуски или REST-only recovery,
поэтому не подтверждает полноту реальной доставки или характеристики
биржевого транспорта; `AC-12` остаётся `DEFERRED / NOT_VERIFIED` до отдельной проверки по Issue #222.
