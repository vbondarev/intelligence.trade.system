# Market Facts Endpoint — `market-facts/v1`

## Назначение

`GET /api/market-analysis/{symbol}/market-facts` — canonical facts layer для downstream-агентов.

Endpoint возвращает нормализованные факты и deterministic labels напрямую из market snapshot.  
Он **не является** текстовым LLM-анализом и **не содержит** готового торгового решения.

Целевая архитектура downstream-пайплайна:

```
market-facts endpoint
→ technical-analysis-agent
→ technical-agent-report.json
→ chief-decision.json
→ publisher
→ Telegram
```

---

## HTTP-контракт

```http
GET /api/market-analysis/{symbol}/market-facts?exchange=Bybit&category=Linear&mode=Intraday
```

### Query-параметры

| Параметр   | Обязательный | Описание |
|------------|--------------|----------|
| `exchange` | ✅            | Биржа. Сейчас поддерживается только `Bybit`. |
| `category` | ✅            | Категория рынка. Например: `Linear`. |
| `mode`     | ❌            | Режим анализа: `Intraday` (по умолчанию), `Swing`, `Portfolio`. |

### Ответы

| Статус | Описание |
|--------|----------|
| `200 OK` | Успешный ответ с `MarketFactsPayload`. |
| `400 Bad Request` | Ошибка валидации (отсутствует `exchange`, `category` или некорректный `mode`). |
| `503 Service Unavailable` | Сервис временно недоступен. |
| `500 Internal Server Error` | Неожиданная ошибка. |

---

## Примеры запросов

```bash
# Intraday (по умолчанию)
curl "http://localhost:8080/api/market-analysis/BTCUSDT/market-facts?exchange=Bybit&category=Linear&mode=Intraday"

# Swing
curl "http://localhost:8080/api/market-analysis/BTCUSDT/market-facts?exchange=Bybit&category=Linear&mode=Swing"

# Через Docker/internal hostname
curl -i "http://intelligence-trade-api:8080/api/market-analysis/BTCUSDT/market-facts?exchange=Bybit&category=Linear&mode=Intraday"
```

---

## Структура payload

Схема: `market-facts/v1`

```json
{
  "schemaVersion": "market-facts/v1",
  "source": {
    "payloadSchemaVersion": "1.0",
    "exchange": "Bybit",
    "symbol": "BTCUSDT",
    "category": "linear",
    "capturedAtUtc": "2026-06-24T13:09:26Z"
  },
  "analysisContext": {
    "analysisMode": "Intraday",
    "primaryTimeframes": ["15m", "1h", "4h"]
  },
  "dataQuality": {
    "status": "ok",
    "isFresh": true,
    "isPartial": false,
    "warnings": [],
    "missingSections": [],
    "sectionAgesMs": {},
    "indicatorDiagnostics": []
  },
  "price": {
    "lastPrice": 65000.0,
    "markPrice": 64990.0,
    "indexPrice": 64980.0
  },
  "derivatives": {
    "fundingRate": 0.0001,
    "openInterest": 100000.0
  },
  "orderBook": {
    "bestBidPrice": 64995.0,
    "bestAskPrice": 65005.0,
    "pressureLabel": "Balanced",
    "liquiditySkewLabel": "Balanced"
  },
  "tradeFlow": {
    "direction": "sell_dominant",
    "label": "aggressive_selling",
    "buyVolume": 3120000.0,
    "sellVolume": 5010000.0,
    "deltaPct": -37.7,
    "hasAggressiveBuyPressure": false,
    "hasAggressiveSellPressure": true
  },
  "timeframes": {
    "15m": { "timeframe": "15m", "trend": {}, "indicators": {}, "levels": {}, "derivedFlags": {}, "backendSummary": {} },
    "1h":  { "timeframe": "1h",  "trend": {}, "indicators": {}, "levels": {}, "derivedFlags": {}, "backendSummary": {} },
    "4h":  { "timeframe": "4h",  "trend": {}, "indicators": {}, "levels": {}, "derivedFlags": {}, "backendSummary": {} },
    "1d":  { "timeframe": "1d",  "trend": {}, "indicators": {}, "levels": {}, "derivedFlags": {}, "backendSummary": {} }
  },
  "levels": {
    "supports": [],
    "resistances": []
  },
  "marketInternalSentiment": {
    "longShortBiasScore": 0.1,
    "fundingBiasScore": -0.02,
    "orderBookPressureScore": 0.05,
    "tradeFlowPressureScore": 0.04,
    "marketRegime": "Trending"
  },
  "tags": ["trending", "aggressive-selling"]
}
```

---

## Детерминированные вычисления

### `dataQuality.status`

| Условие | Значение |
|---------|---------|
| `isPartial = true` (независимо от `isFresh`) | `partial` |
| `isPartial = false`, `isFresh = false` | `stale` |
| `isPartial = false`, `isFresh = true` | `ok` |

### `tradeFlow.direction`

| Условие | Значение |
|---------|---------|
| `buyVolume > sellVolume` | `buy_dominant` |
| `sellVolume > buyVolume` | `sell_dominant` |
| `buyVolume == sellVolume` | `neutral` |

### `tradeFlow.label`

Флаги `hasAggressiveBuyPressure` и `hasAggressiveSellPressure` имеют приоритет над дельтой.

| Условие | Значение |
|---------|---------|
| `hasAggressiveBuyPressure && hasAggressiveSellPressure` | `mixed_aggressive_pressure` |
| `hasAggressiveBuyPressure` | `aggressive_buying` |
| `hasAggressiveSellPressure` | `aggressive_selling` |
| Ни одного флага | `neutral` |

---

## `/llm-payload` vs `/market-facts`

| | `/llm-payload` | `/market-facts` |
|--|---------------|----------------|
| **Статус** | Legacy / transitional | Canonical (рекомендуется) |
| **Схема** | `1.0` | `market-facts/v1` |
| **Назначение** | Прямой вход для LLM | Факты для downstream-агентов |
| **Таймфреймы** | Поля `m15`, `h1`, `h4`, `d1` | Словарь `timeframes["15m"]` и т.д. |
| **dataQuality.status** | Отсутствует | `ok` / `partial` / `stale` |
| **tradeFlow.direction** | Отсутствует | `buy_dominant` / `sell_dominant` / `neutral` |
| **Aggregated levels** | Отсутствует | `levels.supports` / `levels.resistances` |

> `/llm-payload` сохраняется для обратной совместимости. Новые downstream-агенты должны использовать `/market-facts`.

---

## OpenClaw migration status

```
Current migration status:
- /market-facts is available as canonical facts endpoint (market-facts/v1).
- OpenClaw workflow switch from /llm-payload to /market-facts is a separate task.
- /llm-payload remains available during transition.

Future target:
- tech-analysis-agent → /market-facts (instead of /llm-payload)
```

---

## XRPUSDT regression baseline

При тестировании агрессивных продаж проверить:

| Поле | Ожидаемое значение |
|------|-------------------|
| `tradeFlow.deltaPct` | Отрицательное значение (сохраняется из snapshot) |
| `tradeFlow.direction` | `sell_dominant` |
| `tradeFlow.label` | `aggressive_selling` |
| `dataQuality.status` | `ok` (при fresh + non-partial snapshot) |
| `dataQuality.isPartial` | `false` |

Важно: `aggressive_selling` не должен превращаться в `aggressive_buying`. Флаги давления имеют приоритет.

