# market-analysis Skill

## Purpose

This skill describes how an agent should retrieve and interpret backend market-analysis data.

It is intended for an intermediate technical analysis agent, for example `tech-analysis-agent`, inside a multi-agent market analysis pipeline.

The goal of this skill is to produce a structured technical analysis report based only on the backend `llm-payload`.

This skill does not generate final Telegram posts, does not use the Mr Crypto persona, and does not act as the final market synthesis layer.
---

## Role in the Agent Pipeline

Typical flow:

```text
market-analysis skill
        ↓
tech-analysis-agent
        ↓
technical_report
        ↓
chief-market-synthesizer
        ↓
final Telegram market overview
```

This skill is responsible for:

* describing how to retrieve backend market-analysis payload;
* treating the backend payload as the only source of truth;
* interpreting technical market data from the payload;
* identifying technical bias, setup quality, levels and risks;
* helping produce a structured technical report for a higher-level synthesis agent.

This skill is not responsible for:

* final Telegram post generation;
* Mr Crypto style or copywriting;
* final market synthesis across multiple sources;
* Telegram, Twitter/X, news, macro or on-chain analysis;
* publishing, scheduling or delivery.

---

## Backend

Base URL:

```text
http://intelligence-trade-api:8080
```

Primary endpoint:

```http
GET /api/market-analysis/{symbol}/llm-payload
```

Default request for Bybit USDT perpetual intraday analysis:

```http
GET /api/market-analysis/{symbol}/llm-payload?exchange=Bybit&category=Linear&mode=Intraday&includePortfolio=false&includeAggregatedContext=false
```

Default parameters:

```text
exchange=Bybit
category=Linear
mode=Intraday
includePortfolio=false
includeAggregatedContext=false
```

The `{symbol}` value must be provided by the calling agent or workflow.

Example:

```http
GET /api/market-analysis/BTCUSDT/llm-payload?exchange=Bybit&category=Linear&mode=Intraday&includePortfolio=false&includeAggregatedContext=false
```

---

## Source of Truth

The backend `llm-payload` is the only source of truth.

The agent must not invent, estimate, assume or override missing values.

If a value is missing, unavailable or unclear, the technical report must explicitly reflect this.

Allowed:

```json
{
  "volume_1h": null,
  "warnings": ["1h volume multiplier is missing"]
}
```

Not allowed:

```json
{
  "volume_1h": "1.4x"
}
```

if this value is not present in the backend payload.

---

## Data Freshness and Quality

The agent must inspect payload freshness and completeness before producing the technical report.

Relevant quality indicators may include:

* `snapshotHealth.isFresh`;
* `snapshotHealth.isPartial`;
* `snapshotHealth.missingSections`;
* `snapshotHealth.sectionAgesMs`;
* `capturedAtUtc`;
* backend validation flags;
* section staleness;
* missing or unavailable required sections.

Mapping rules:

```text
snapshotHealth.isFresh = true  → data_quality.is_stale = false
snapshotHealth.isFresh = false → data_quality.is_stale = true

snapshotHealth.isPartial = true  → data_quality.is_partial = true
snapshotHealth.isPartial = false → data_quality.is_partial = false
```

If the snapshot is stale:

* the technical report may still be produced;
* setup quality must be conservative;
* technical confidence must be reduced;
* scenarios must remain conditional;
* the report must not describe the setup as confirmed.

If the snapshot is partial:

* missing values must be listed;
* setup quality must not be overstated;
* technical confidence must be reduced;
* scenarios must remain conditional.

---

## Status Values

Allowed `status` values:

* `ok` — backend payload is usable and the technical report was produced successfully;
* `partial` — backend payload is usable but incomplete, partially stale, or missing non-critical fields;
* `no_data` — backend payload is empty or does not contain usable market data;
* `error` — backend request failed or the payload could not be processed.

Status selection rules:

* Use `ok` only when the payload is fresh, not partial, and contains enough data to produce a reliable technical report.
* Use `partial` when the report can still be produced, but some non-critical data is missing, stale, unreliable, or incomplete.
* Use `no_data` when there is no usable market data for analysis.
* Use `error` when the backend request failed, returned invalid data, or could not be processed.

If `data_quality.is_partial = true`, prefer:

```json
{
  "status": "partial"
}
```

unless the missing data is explicitly non-critical and the backend payload is still complete enough for the requested technical report.

If `data_quality.is_stale = true`, the report may still be produced, but:

* `status` should usually be `partial`;
* `setup_quality` must be conservative;
* `risk_level` should not be understated;
* scenarios must remain conditional;
* the report must not describe the setup as confirmed.

---

## Warning Classification Rules

The report must strictly separate data-quality warnings from market, technical and positioning warnings.

Use `data_quality.warnings` only for freshness, completeness, availability or reliability issues.

Examples for `data_quality.warnings`:

```text
tradeFlow is near staleness threshold
orderBook section is stale
h1 timeframe block is missing
rsi14 is unreliable on 15m
payload is partial
missing derivatives section
backend payload could not provide volume data
```

Use top-level `warnings` for market, technical, positioning, volume, trend or context observations.

Examples for top-level `warnings`:

```text
low volume on primary timeframes
timeframe conflict: 15m bullish vs 4h/1d bearish
orderBook and tradeFlow signals are conflicting
price is close to daily resistance
open interest is declining during the bounce
long positioning is crowded
no clean entry point identified
```

Backend `snapshotHealth.warnings` must not be copied verbatim.

Each backend warning must be classified, rewritten if necessary, and placed into the correct output field:

* freshness, completeness, missing-section or reliability warnings → `data_quality.warnings`;
* market, technical, positioning, volume, trend or context warnings → top-level `warnings`;
* expected disabled-context warnings → ignored completely.

Expected disabled-context warnings include:

```text
portfolio context is not included
aggregated market context is not included
```

when the request was made with:

```text
includePortfolio=false
includeAggregatedContext=false
```

These expected disabled-context warnings must not appear in:

```json
{
  "data_quality": {
    "warnings": []
  }
}
```

and must not appear in top-level:

```json
{
  "warnings": []
}
```

The agent must not place normal market observations into `data_quality.warnings`.

The agent must not place stale, missing, partial or unreliable data issues only into top-level `warnings`.

If a warning belongs to both categories, split it into two precise warnings.

Example:

Backend warning:

```text
tradeFlow is near staleness threshold and conflicts with orderBook
```

Correct output:

```json
{
  "data_quality": {
    "warnings": [
      "tradeFlow is near staleness threshold"
    ]
  },
  "warnings": [
    "orderBook and tradeFlow signals are conflicting"
  ]
}
```

---

## Handling Backend Warning: Price Far From Nearest Relevant Level

If the backend returns a warning such as:

```text
price is far from nearest relevant level
```

the agent must not blindly expand it in a way that conflicts with selected support/resistance levels.

This warning usually refers to a backend-specific notion of a relevant intraday level or entry level.

Before using it, the agent must compare it with the selected nearest support and resistance.

Rules:

1. If the selected nearest resistance/support is actually close to price, do not say generically that price is far from the nearest relevant level.
2. If the warning is still useful, preserve it as a backend-level context warning with precise wording.
3. Do not use this warning to override calculated distances to selected support/resistance.
4. Prefer exact phrasing that explains the scope of the warning.

Allowed:

```text
backend warning: price is far from nearest relevant intraday entry level
```

Allowed:

```text
nearest daily resistance is only 1.8% above, while backend also flags weak intraday entry-level proximity
```

Not allowed:

```text
price is far from nearest relevant level
```

when the report also says that selected daily resistance is only `1.8%` above.

Not allowed:

```text
1h resistance is 14.2% away, so price is far from resistance
```

if the selected nearest valid resistance is daily resistance only `1.8%` above.

The final report must avoid internal contradiction between:

* selected `technical.levels.support`;
* selected `technical.levels.resistance`;
* calculated distance fields;
* warning text.

---

## Context Inclusion Rules

The default MVP request intentionally uses:

```text
includePortfolio=false
includeAggregatedContext=false
```

Because of that, portfolio context and aggregated market context are expected to be absent.

If the backend payload contains warnings such as:

```text
portfolio context is not included
aggregated market context is not included
```

and the request was made with:

```text
includePortfolio=false
includeAggregatedContext=false
```

then these messages must not be treated as data-quality warnings.

Do not include them in:

```json
{
  "data_quality": {
    "warnings": []
  }
}
```

Do not include them in the final technical report warnings.

They may be ignored completely.

The agent must not create fields such as:

```json
{
  "portfolio_context": false,
  "aggregated_context": false
}
```

unless a future schema explicitly requires them.

For the current MVP, portfolio and aggregated context must be excluded from the technical report JSON.

---

## Allowed Backend Market Context

The skill may use the following backend payload sections if they are present:

* `derivatives`;
* `orderBook`;
* `tradeFlow`;
* backend-provided `sentiment`;
* backend-provided `tags`;
* `indicatorDiagnostics`.

These sections are allowed because they are part of the backend `llm-payload`.

They are not considered external Telegram, Twitter/X, news, macro or on-chain analysis.

Allowed examples:

* funding rate from `derivatives.fundingRate`;
* open interest change from `derivatives.openInterestChange1hPct` and `derivatives.openInterestChange4hPct`;
* long/short ratio from `derivatives.longRatio` and `derivatives.shortRatio`;
* order book pressure from `orderBook.pressureLabel`;
* liquidity skew from `orderBook.liquiditySkewLabel`;
* trade flow pressure from `tradeFlow.deltaPct`, `tradeFlow.hasAggressiveBuyPressure`, `tradeFlow.hasAggressiveSellPressure`;
* backend sentiment fields such as `sentiment.marketRegime`.

These fields may be used to enrich the technical report, risk explanation and warnings.

However:

* do not overrule price/timeframe/level analysis using these fields alone;
* do not treat backend sentiment as social sentiment;
* do not call it Twitter, Telegram, news, macro or on-chain sentiment;
* do not create values if the fields are missing.

---

## Technical Analysis Scope

The skill may interpret only technical, derivative, order book, trade flow and market-structure data present in the backend payload.

Relevant areas:

* current price;
* 24h price change;
* 24h high and low;
* trend by timeframe;
* RSI by timeframe;
* EMA/SMA context if present;
* ATR or volatility if present;
* volume and relative volume if present;
* support and resistance levels;
* nearest support below current price;
* nearest resistance above current price;
* distance to support and resistance;
* trend alignment across timeframes;
* overbought or oversold conditions;
* setup quality;
* technical risk level;
* long and short market scenarios;
* funding rate if present;
* open interest and open interest changes if present;
* long/short ratio if present;
* order book pressure if present;
* trade flow pressure if present;
* backend market regime if present.

The skill must not analyze data from external sources unless those values are explicitly present inside the backend payload.

---

## Default Timeframes

Default timeframes for intraday technical analysis:

```text
15m
1h
4h
1d
```

For a daily market overview, the most important values are usually:

```text
current price
change24h
low24h
high24h
RSI 1h
RSI 4h
volume 15m
volume 1h
volume 4h
nearest support
nearest resistance
technical bias
setup quality
risk level
```

If one or more important timeframe blocks are missing, the report must include a warning.

---

## Level Selection Rules

When the backend provides multiple levels, select:

* nearest significant support below current price;
* nearest significant resistance above current price.

Do not create levels manually if they are not present in the backend payload.

If no valid support exists below the current price:

```json
{
  "support": null
}
```

If no valid resistance exists above the current price:

```json
{
  "resistance": null
}
```

Do not force a support or resistance level just to complete the report.

When selected levels come from different timeframes, the summary may mention their source, for example:

```text
nearest support is the 15m volume-profile cluster
nearest resistance is the 1d volume-profile cluster
```

---

## Distance Calculation Rules

If current price and nearest support are available:

```text
distance_to_support_percent = ((price - support) / price) * 100
```

If current price and nearest resistance are available:

```text
distance_to_resistance_percent = ((resistance - price) / price) * 100
```

Rounding rules:

```text
prices: 0 decimals for BTC-like large prices, otherwise preserve meaningful market precision from payload
percent values: 1 decimal
RSI values: 0 decimals
volume multipliers: 1 decimal if numeric
funding rate: preserve enough precision to remain meaningful
open interest change: 1 decimal if expressed as percent
long/short ratio: convert to percent and round to 1 decimal
```

If price or level is missing, the corresponding distance must be `null`.

---

## Bias and Setup Quality

Allowed `technical_bias` values:

```text
bullish
neutral_bullish
neutral
neutral_bearish
bearish
unclear
```

Allowed `setup_quality` values:

```text
normal
risky
premature
poor
unclear
```

Guidelines:

Use `bullish` only when the majority of relevant timeframes and indicators support upward continuation.

Use `bearish` only when the majority of relevant timeframes and indicators support downward continuation.

Use `neutral_bullish` or `neutral_bearish` when the picture leans in one direction but is not fully confirmed.

Use `neutral` when signals are mixed or price is inside a range without clear confirmation.

Use `risky` when:

* price is close to resistance for long scenarios;
* price is close to support for short scenarios;
* RSI is overheated;
* volume does not confirm the move;
* timeframes conflict;
* the snapshot is partial or stale;
* derivatives/order book/trade flow data increases uncertainty.

Use `premature` when the scenario requires breakout, retest, confirmation or reaction that has not happened yet.

Use `poor` when backend timeframe summaries show poor entry quality, trend is confirmed but entry is filtered, or there is no clean technical setup.

Use `unclear` when the payload is insufficient.

---

## Scenario Language

The skill may describe long and short scenarios only as conditional market scenarios.

Allowed:

```text
Long scenario becomes more relevant if price breaks and holds above resistance.
```

```text
Short scenario becomes more relevant if price loses support and confirms weakness.
```

Not allowed:

```text
Open long now.
```

```text
Enter short.
```

```text
Final decision: long.
```

```text
Signal confirmed.
```

The skill must avoid direct trading commands.

---

## Risk Classification

Allowed `risk_level` values:

```text
low
medium
high
unclear
```

Risk must be based only on data from the backend payload.

Common risk reasons:

* price is close to resistance;
* price is close to support;
* RSI is overbought or oversold;
* volume does not confirm the move;
* timeframes conflict;
* snapshot is stale or partial;
* volatility is elevated;
* no reliable support or resistance is available;
* open interest is declining during a move;
* funding and long/short ratio show crowded positioning;
* order book or trade flow conflicts with the higher timeframe picture.

Risk explanation should include a numeric basis when available.

Example:

```text
Risk is medium because price is only 1.4% below resistance and 1h RSI is 61.
```

If numeric basis is unavailable, the report must state that the data is missing or insufficient.

---

## Summary Precision Rules

The summary must be factual and numerically precise.

Do not exaggerate numeric comparisons.

If a value is exactly equal to a threshold, do not describe it as greater than that threshold.

Examples:

Allowed:

```text
trend strength is around 0.80
```

```text
trend strength is 0.80 or higher across selected bearish timeframes
```

```text
1h and 4h trend strength are above 0.80, while 1d is exactly 0.80
```

Not allowed:

```text
all trend strength values are > 0.80
```

when one of the values is exactly `0.80`.

Use `>` only when the payload value is strictly greater than the threshold.

Use `>=` only when the statement is true for all mentioned values.

Prefer exact values or rounded values when they improve clarity.

When summarizing timeframe conditions, use `all timeframes` only if the statement is true for every included timeframe.

Allowed:

```text
15m/1h/4h show Poor entry quality, while 1d is only Fair.
```

```text
All primary timeframes show Poor entry quality.
```

```text
All analyzed timeframes show bearish trend alignment.
```

Not allowed:

```text
All timeframes show Poor entry quality except 1d Fair.
```# market-analysis Skill

## Purpose

This skill describes how an agent should retrieve and interpret backend market-analysis data.

It is intended for an intermediate technical analysis agent, for example `tech-analysis-agent`, inside a multi-agent market analysis pipeline.

The goal of this skill is to produce a structured technical analysis report based only on the backend `llm-payload`.

This skill does not generate final Telegram posts, does not use the Mr Crypto persona, and does not act as the final market synthesis layer.

---

## Role in the Agent Pipeline

Typical flow:

```text
market-analysis skill
        ↓
tech-analysis-agent
        ↓
technical_report
        ↓
chief-market-synthesizer
        ↓
final Telegram market overview
```

This skill is responsible for:

* describing how to retrieve backend market-analysis payload;
* treating the backend payload as the only source of truth;
* interpreting technical market data from the payload;
* identifying technical bias, setup quality, levels and risks;
* helping produce a structured technical report for a higher-level synthesis agent.

This skill is not responsible for:

* final Telegram post generation;
* Mr Crypto style or copywriting;
* final market synthesis across multiple sources;
* Telegram, Twitter/X, news, macro or on-chain analysis;
* publishing, scheduling or delivery.

---

## Backend

Base URL:

```text
http://intelligence-trade-api:8080
```

Primary endpoint:

```http
GET /api/market-analysis/{symbol}/llm-payload
```

Default request for Bybit USDT perpetual intraday analysis:

```http
GET /api/market-analysis/{symbol}/llm-payload?exchange=Bybit&category=Linear&mode=Intraday&includePortfolio=false&includeAggregatedContext=false
```

Default parameters:

```text
exchange=Bybit
category=Linear
mode=Intraday
includePortfolio=false
includeAggregatedContext=false
```

The `{symbol}` value must be provided by the calling agent or workflow.

Example:

```http
GET /api/market-analysis/BTCUSDT/llm-payload?exchange=Bybit&category=Linear&mode=Intraday&includePortfolio=false&includeAggregatedContext=false
```

---

## Source of Truth

The backend `llm-payload` is the only source of truth.

The agent must not invent, estimate, assume or override missing values.

If a value is missing, unavailable or unclear, the technical report must explicitly reflect this.

Allowed:

```json
{
  "volume_1h": null,
  "warnings": ["1h volume multiplier is missing"]
}
```

Not allowed:

```json
{
  "volume_1h": "1.4x"
}
```

if this value is not present in the backend payload.

---

## Market Data Mapping Rules

The agent must map backend market fields into the technical report without changing their meaning.

Backend `price.price24hChangePct` is provided as a fraction, not as a ready percentage value.

Conversion rule:

```text
change_24h_percent = price.price24hChangePct * 100
```

Rounding rule:

```text
change_24h_percent: round to 1 decimal
```

Example:

```json
{
  "price24hChangePct": 0.033056
}
```

must become:

```json
{
  "change_24h_percent": 3.3
}
```

Not allowed:

```json
{
  "change_24h_percent": 0.0
}
```

when backend `price.price24hChangePct` is `0.033056`.

The summary must use the converted percentage value consistently.

Allowed:

```text
JUPUSDT at 0.16063, +3.3% 24h.
```

Not allowed:

```text
JUPUSDT at 0.16063, near-flat 24h (0.0%).
```

if backend `price.price24hChangePct` is `0.033056`.

Market field mapping:

```text
price.lastPrice          → market.price
price.price24hChangePct  → market.change_24h_percent after multiplying by 100
price.low24h             → market.low_24h
price.high24h            → market.high_24h
capturedAtUtc            → captured_at_utc
```

The agent must not round small but meaningful 24h changes to zero unless the converted value is actually near zero after applying the conversion rule.

---

## Data Freshness and Quality

The agent must inspect payload freshness and completeness before producing the technical report.

Relevant quality indicators may include:

* `snapshotHealth.isFresh`;
* `snapshotHealth.isPartial`;
* `snapshotHealth.missingSections`;
* `snapshotHealth.sectionAgesMs`;
* `capturedAtUtc`;
* backend validation flags;
* section staleness;
* missing or unavailable required sections.

Mapping rules:

```text
snapshotHealth.isFresh = true  → data_quality.is_stale = false
snapshotHealth.isFresh = false → data_quality.is_stale = true

snapshotHealth.isPartial = true  → data_quality.is_partial = true
snapshotHealth.isPartial = false → data_quality.is_partial = false
```

If the snapshot is stale:

* the technical report may still be produced;
* setup quality must be conservative;
* technical confidence must be reduced;
* scenarios must remain conditional;
* the report must not describe the setup as confirmed.

If the snapshot is partial:

* missing values must be listed;
* setup quality must not be overstated;
* technical confidence must be reduced;
* scenarios must remain conditional.

---

## Status Values

Allowed `status` values:

* `ok` — backend payload is usable and the technical report was produced successfully;
* `partial` — backend payload is usable but incomplete, partially stale, or missing non-critical fields;
* `no_data` — backend payload is empty or does not contain usable market data;
* `error` — backend request failed or the payload could not be processed.

Status selection rules:

* Use `ok` only when the payload is fresh, not partial, and contains enough data to produce a reliable technical report.
* Use `partial` when the report can still be produced, but some non-critical data is missing, stale, unreliable, or incomplete.
* Use `no_data` when there is no usable market data for analysis.
* Use `error` when the backend request failed, returned invalid data, or could not be processed.

If `data_quality.is_partial = true`, prefer:

```json
{
  "status": "partial"
}
```

unless the missing data is explicitly non-critical and the backend payload is still complete enough for the requested technical report.

If `data_quality.is_stale = true`, the report may still be produced, but:

* `status` should usually be `partial`;
* `setup_quality` must be conservative;
* `risk_level` should not be understated;
* scenarios must remain conditional;
* the report must not describe the setup as confirmed.

---

## Warning Classification Rules

The report must strictly separate data-quality warnings from market, technical and positioning warnings.

Use `data_quality.warnings` only for freshness, completeness, availability or reliability issues.

Examples for `data_quality.warnings`:

```text
tradeFlow is near staleness threshold
orderBook section is stale
h1 timeframe block is missing
rsi14 is unreliable on 15m
payload is partial
missing derivatives section
backend payload could not provide volume data
```

Use top-level `warnings` for market, technical, positioning, volume, trend or context observations.

Examples for top-level `warnings`:

```text
low volume on primary timeframes
timeframe conflict: 15m bullish vs 4h/1d bearish
orderBook and tradeFlow signals are conflicting
price is close to daily resistance
open interest is declining during the bounce
long positioning is crowded
no clean entry point identified
```

Backend `snapshotHealth.warnings` must not be copied verbatim.

Each backend warning must be classified, rewritten if necessary, and placed into the correct output field:

* freshness, completeness, missing-section or reliability warnings → `data_quality.warnings`;
* market, technical, positioning, volume, trend or context warnings → top-level `warnings`;
* expected disabled-context warnings → ignored completely.

Expected disabled-context warnings include:

```text
portfolio context is not included
aggregated market context is not included
```

when the request was made with:

```text
includePortfolio=false
includeAggregatedContext=false
```

These expected disabled-context warnings must not appear in:

```json
{
  "data_quality": {
    "warnings": []
  }
}
```

and must not appear in top-level:

```json
{
  "warnings": []
}
```

The agent must not place normal market observations into `data_quality.warnings`.

The agent must not place stale, missing, partial or unreliable data issues only into top-level `warnings`.

If a warning belongs to both categories, split it into two precise warnings.

Example:

Backend warning:

```text
tradeFlow is near staleness threshold and conflicts with orderBook
```

Correct output:

```json
{
  "data_quality": {
    "warnings": [
      "tradeFlow is near staleness threshold"
    ]
  },
  "warnings": [
    "orderBook and tradeFlow signals are conflicting"
  ]
}
```

---

## Handling Backend Warning: Price Far From Nearest Relevant Level

If the backend returns a warning such as:

```text
price is far from nearest relevant level
```

the agent must not blindly expand it in a way that conflicts with selected support/resistance levels.

This warning usually refers to a backend-specific notion of a relevant intraday level or entry level.

Before using it, the agent must compare it with the selected nearest support and resistance.

Rules:

1. If the selected nearest resistance/support is actually close to price, do not say generically that price is far from the nearest relevant level.
2. If the warning is still useful, preserve it as a backend-level context warning with precise wording.
3. Do not use this warning to override calculated distances to selected support/resistance.
4. Prefer exact phrasing that explains the scope of the warning.

Allowed:

```text
backend warning: price is far from nearest relevant intraday entry level
```

Allowed:

```text
nearest daily resistance is only 1.8% above, while backend also flags weak intraday entry-level proximity
```

Not allowed:

```text
price is far from nearest relevant level
```

when the report also says that selected daily resistance is only `1.8%` above.

Not allowed:

```text
1h resistance is 14.2% away, so price is far from resistance
```

if the selected nearest valid resistance is daily resistance only `1.8%` above.

The final report must avoid internal contradiction between:

* selected `technical.levels.support`;
* selected `technical.levels.resistance`;
* calculated distance fields;
* warning text.

---

## Context Inclusion Rules

The default MVP request intentionally uses:

```text
includePortfolio=false
includeAggregatedContext=false
```

Because of that, portfolio context and aggregated market context are expected to be absent.

If the backend payload contains warnings such as:

```text
portfolio context is not included
aggregated market context is not included
```

and the request was made with:

```text
includePortfolio=false
includeAggregatedContext=false
```

then these messages must not be treated as data-quality warnings.

Do not include them in:

```json
{
  "data_quality": {
    "warnings": []
  }
}
```

Do not include them in the final technical report warnings.

They may be ignored completely.

The agent must not create fields such as:

```json
{
  "portfolio_context": false,
  "aggregated_context": false
}
```

unless a future schema explicitly requires them.

For the current MVP, portfolio and aggregated context must be excluded from the technical report JSON.

---

## Allowed Backend Market Context

The skill may use the following backend payload sections if they are present:

* `derivatives`;
* `orderBook`;
* `tradeFlow`;
* backend-provided `sentiment`;
* backend-provided `tags`;
* `indicatorDiagnostics`.

These sections are allowed because they are part of the backend `llm-payload`.

They are not considered external Telegram, Twitter/X, news, macro or on-chain analysis.

Allowed examples:

* funding rate from `derivatives.fundingRate`;
* open interest change from `derivatives.openInterestChange1hPct` and `derivatives.openInterestChange4hPct`;
* long/short ratio from `derivatives.longRatio` and `derivatives.shortRatio`;
* order book pressure from `orderBook.pressureLabel`;
* liquidity skew from `orderBook.liquiditySkewLabel`;
* trade flow pressure from `tradeFlow.deltaPct`, `tradeFlow.hasAggressiveBuyPressure`, `tradeFlow.hasAggressiveSellPressure`;
* backend sentiment fields such as `sentiment.marketRegime`.

These fields may be used to enrich the technical report, risk explanation and warnings.

However:

* do not overrule price/timeframe/level analysis using these fields alone;
* do not treat backend sentiment as social sentiment;
* do not call it Twitter, Telegram, news, macro or on-chain sentiment;
* do not create values if the fields are missing.

---

## Technical Analysis Scope

The skill may interpret only technical, derivative, order book, trade flow and market-structure data present in the backend payload.

Relevant areas:

* current price;
* 24h price change;
* 24h high and low;
* trend by timeframe;
* RSI by timeframe;
* EMA/SMA context if present;
* ATR or volatility if present;
* volume and relative volume if present;
* support and resistance levels;
* nearest support below current price;
* nearest resistance above current price;
* distance to support and resistance;
* trend alignment across timeframes;
* overbought or oversold conditions;
* setup quality;
* technical risk level;
* long and short market scenarios;
* funding rate if present;
* open interest and open interest changes if present;
* long/short ratio if present;
* order book pressure if present;
* trade flow pressure if present;
* backend market regime if present.

The skill must not analyze data from external sources unless those values are explicitly present inside the backend payload.

---

## Default Timeframes

Default timeframes for intraday technical analysis:

```text
15m
1h
4h
1d
```

For a daily market overview, the most important values are usually:

```text
current price
change24h
low24h
high24h
RSI 1h
RSI 4h
volume 15m
volume 1h
volume 4h
nearest support
nearest resistance
technical bias
setup quality
risk level
```

If one or more important timeframe blocks are missing, the report must include a warning.

---

## Level Selection Rules

When the backend provides multiple levels, select:

* nearest significant support below current price;
* nearest significant resistance above current price.

Do not create levels manually if they are not present in the backend payload.

If no valid support exists below the current price:

```json
{
  "support": null
}
```

If no valid resistance exists above the current price:

```json
{
  "resistance": null
}
```

Do not force a support or resistance level just to complete the report.

When selected levels come from different timeframes, the summary may mention their source, for example:

```text
nearest support is the 15m volume-profile cluster
nearest resistance is the 1d volume-profile cluster
```

When the selected level has metadata in the backend payload, include a human-readable source field in the report.

Mapping examples:

```text
m15.support1Meta.source + strengthLabel → support_source
d1.resistance1Meta.source + strengthLabel → resistance_source
```

Example output:

```json
{
  "levels": {
    "support": 0.15547,
    "support_source": "15m volume-profile cluster (Strong)",
    "resistance": 0.16401,
    "resistance_source": "1d volume-profile cluster (Strong)",
    "distance_to_support_percent": 3.2,
    "distance_to_resistance_percent": 2.1
  }
}
```

Do not invent level source metadata.

If level source or strength is missing in the backend payload, use:

```json
{
  "support_source": null,
  "resistance_source": null
}
```

---

## Distance Calculation Rules

If current price and nearest support are available:

```text
distance_to_support_percent = ((price - support) / price) * 100
```

If current price and nearest resistance are available:

```text
distance_to_resistance_percent = ((resistance - price) / price) * 100
```

Rounding rules:

```text
prices: 0 decimals for BTC-like large prices, otherwise preserve meaningful market precision from payload
percent values: 1 decimal
RSI values: 0 decimals
volume multipliers: 1 decimal if numeric
funding rate: preserve enough precision to remain meaningful
open interest change: 1 decimal if expressed as percent
long/short ratio: convert to percent and round to 1 decimal
```

If price or level is missing, the corresponding distance must be `null`.

---

## Bias and Setup Quality

Allowed `technical_bias` values:

```text
bullish
neutral_bullish
neutral
neutral_bearish
bearish
unclear
```

Allowed `setup_quality` values:

```text
normal
risky
premature
poor
unclear
```

Guidelines:

Use `bullish` only when the majority of relevant timeframes and indicators support upward continuation.

Use `bearish` only when the majority of relevant timeframes and indicators support downward continuation.

Use `neutral_bullish` or `neutral_bearish` when the picture leans in one direction but is not fully confirmed.

Use `neutral` when signals are mixed or price is inside a range without clear confirmation.

Use `risky` when:

* price is close to resistance for long scenarios;
* price is close to support for short scenarios;
* RSI is overheated;
* volume does not confirm the move;
* timeframes conflict;
* the snapshot is partial or stale;
* derivatives/order book/trade flow data increases uncertainty.

Use `premature` when the scenario requires breakout, retest, confirmation or reaction that has not happened yet.

Use `poor` when backend timeframe summaries show poor entry quality, trend is confirmed but entry is filtered, or there is no clean technical setup.

Use `unclear` when the payload is insufficient.

---

## Scenario Language

The skill may describe long and short scenarios only as conditional market scenarios.

Allowed:

```text
Long scenario becomes more relevant if price breaks and holds above resistance.
```

```text
Short scenario becomes more relevant if price loses support and confirms weakness.
```

Not allowed:

```text
Open long now.
```

```text
Enter short.
```

```text
Final decision: long.
```

```text
Signal confirmed.
```

The skill must avoid direct trading commands.

---

## Risk Classification

Allowed `risk_level` values:

```text
low
medium
high
unclear
```

Risk must be based only on data from the backend payload.

Common risk reasons:

* price is close to resistance;
* price is close to support;
* RSI is overbought or oversold;
* volume does not confirm the move;
* timeframes conflict;
* snapshot is stale or partial;
* volatility is elevated;
* no reliable support or resistance is available;
* open interest is declining during a move;
* funding and long/short ratio show crowded positioning;
* order book or trade flow conflicts with the higher timeframe picture.

Risk explanation should include a numeric basis when available.

Example:

```text
Risk is medium because price is only 1.4% below resistance and 1h RSI is 61.
```

If numeric basis is unavailable, the report must state that the data is missing or insufficient.

---

## Summary Precision Rules

The summary must be factual and numerically precise.

Do not exaggerate numeric comparisons.

If a value is exactly equal to a threshold, do not describe it as greater than that threshold.

Examples:

Allowed:

```text
trend strength is around 0.80
```

```text
trend strength is 0.80 or higher across selected bearish timeframes
```

```text
1h and 4h trend strength are above 0.80, while 1d is exactly 0.80
```

Not allowed:

```text
all trend strength values are > 0.80
```

when one of the values is exactly `0.80`.

Use `>` only when the payload value is strictly greater than the threshold.

Use `>=` only when the statement is true for all mentioned values.

Prefer exact values or rounded values when they improve clarity.

When summarizing timeframe conditions, use `all timeframes` only if the statement is true for every included timeframe.

Allowed:

```text
15m/1h/4h show Poor entry quality, while 1d is only Fair.
```

```text
All primary timeframes show Poor entry quality.
```

```text
All analyzed timeframes show bearish trend alignment.
```

Not allowed:

```text
All timeframes show Poor entry quality except 1d Fair.
```

```text
All timeframes are bearish except 15m bullish.
```

If one or more timeframes differ from the majority, explicitly list the groups instead of using `all timeframes`.

Allowed:

```text
15m is bullish, 1h is sideways, while 4h and 1d remain bearish.
```

```text
15m/1h/4h show Poor entry quality, while 1d is Fair.
```

```text
Primary intraday timeframes show weak entry quality, while the daily timeframe is only Fair.
```

---

## Expected Output Contract

The expected result of using this skill is a structured technical report that can be passed to a higher-level market synthesis agent.

Recommended output shape:

```json
{
  "agent": "tech-analysis-agent",
  "skill": "market-analysis",
  "symbol": "BTCUSDT",
  "captured_at_utc": null,
  "status": "ok",
  "data_quality": {
    "is_stale": false,
    "is_partial": false,
    "missing_fields": [],
    "warnings": []
  },
  "market": {
    "price": null,
    "change_24h_percent": null,
    "low_24h": null,
    "high_24h": null
  },
  "technical": {
    "technical_bias": "unclear",
    "setup_quality": "unclear",
    "risk_level": "unclear",
    "rsi": {
      "15m": null,
      "1h": null,
      "4h": null,
      "1d": null
    },
    "volume": {
      "15m": null,
      "1h": null,
      "4h": null
    },
    "levels": {
      "support": null,
      "support_source": null,
      "resistance": null,
      "resistance_source": null,
      "distance_to_support_percent": null,
      "distance_to_resistance_percent": null
    }
  },
  "context": {
    "derivatives": {
      "funding_rate": null,
      "funding_rate_avg_24h": null,
      "open_interest": null,
      "open_interest_value": null,
      "open_interest_change_1h_percent": null,
      "open_interest_change_4h_percent": null,
      "long_ratio_percent": null,
      "short_ratio_percent": null,
      "premium_vs_index_percent": null
    },
    "order_book": {
      "pressure_label": null,
      "liquidity_skew_label": null,
      "imbalance_top5": null,
      "imbalance_top10": null,
      "imbalance_top20": null
    },
    "trade_flow": {
      "delta_percent": null,
      "has_aggressive_buy_pressure": null,
      "has_aggressive_sell_pressure": null
    },
    "backend_sentiment": {
      "market_regime": null,
      "long_short_bias_score": null,
      "funding_bias_score": null,
      "order_book_pressure_score": null,
      "trade_flow_pressure_score": null
    },
    "tags": []
  },
  "scenarios": {
    "long": {
      "status": "conditional",
      "condition": null
    },
    "short": {
      "status": "conditional",
      "condition": null
    }
  },
  "summary": null,
  "warnings": []
}
```

`captured_at_utc` should be mapped from backend `capturedAtUtc`.

`support_source` and `resistance_source` should describe where the selected levels came from when this information is available in the payload.

Allowed examples:

```text
15m volume-profile cluster (Strong)
1d volume-profile cluster (Strong)
4h volume-profile cluster (Weak)
```

If source metadata is unavailable, use `null`.

The strict schema may later define `captured_at_utc`, `support_source` and `resistance_source` as nullable fields.

---

## Context Block Rules

The `context` block is optional at the top level.

For the current MVP, include the `context` block when at least one of the following backend sections is present:

* `derivatives`;
* `orderBook`;
* `tradeFlow`;
* `sentiment`;
* `tags`.

If `context` is included, use stable section names:

```json
{
  "context": {
    "derivatives": {},
    "order_book": {},
    "trade_flow": {},
    "backend_sentiment": {},
    "tags": []
  }
}
```

If a specific context section is absent from the backend payload, set that section to `null`.

Example:

```json
{
  "context": {
    "derivatives": null,
    "order_book": null,
    "trade_flow": null,
    "backend_sentiment": null,
    "tags": []
  }
}
```

If a context section is present, fill only values that exist in the backend payload.

Do not invent missing context values.

Use explicit percentage field names for converted ratio values:

```json
{
  "long_ratio_percent": 67.7,
  "short_ratio_percent": 32.3
}
```

Do not use ambiguous fields when values are expressed as percentages:

```json
{
  "long_ratio": 67.7,
  "short_ratio": 32.3
}
```

When backend provides ratios as fractions:

```json
{
  "longRatio": 0.6768,
  "shortRatio": 0.3232
}
```

convert them to percentages:

```json
{
  "long_ratio_percent": 67.7,
  "short_ratio_percent": 32.3
}
```

If a strict schema exists, the schema file must be treated as the final output contract.

Do not include portfolio or aggregated market context in the technical report JSON for the current MVP.

---

## Output Boundaries

The technical report must be based only on the backend market-analysis payload.

The report must not contain:

* final Telegram post text;
* Mr Crypto persona text;
* jokes, slogans or channel-specific copywriting;
* final trading commands such as `open long`, `open short`, `enter now`;
* invented, estimated or assumed market data;
* analysis from sources outside the backend payload, including Telegram, Twitter/X, news, macro or on-chain data;
* portfolio context when `includePortfolio=false`;
* aggregated market context when `includeAggregatedContext=false`;
* ambiguous ratio fields such as `long_ratio` and `short_ratio` when values are expressed as percentages.

---

## Error Handling

If the backend request fails, the agent must return a technical report with error status.

Example:

```json
{
  "agent": "tech-analysis-agent",
  "skill": "market-analysis",
  "symbol": "BTCUSDT",
  "captured_at_utc": null,
  "status": "error",
  "data_quality": {
    "is_stale": null,
    "is_partial": null,
    "missing_fields": [],
    "warnings": ["Failed to retrieve backend market-analysis payload"]
  },
  "market": {
    "price": null,
    "change_24h_percent": null,
    "low_24h": null,
    "high_24h": null
  },
  "technical": {
    "technical_bias": "unclear",
    "setup_quality": "unclear",
    "risk_level": "unclear",
    "rsi": {
      "15m": null,
      "1h": null,
      "4h": null,
      "1d": null
    },
    "volume": {
      "15m": null,
      "1h": null,
      "4h": null
    },
    "levels": {
      "support": null,
      "support_source": null,
      "resistance": null,
      "resistance_source": null,
      "distance_to_support_percent": null,
      "distance_to_resistance_percent": null
    }
  },
  "context": {
    "derivatives": null,
    "order_book": null,
    "trade_flow": null,
    "backend_sentiment": null,
    "tags": []
  },
  "scenarios": {
    "long": {
      "status": "unavailable",
      "condition": null
    },
    "short": {
      "status": "unavailable",
      "condition": null
    }
  },
  "summary": "Backend market-analysis payload is unavailable.",
  "warnings": []
}
```

If the payload is empty, use:

```json
{
  "status": "no_data",
  "summary": "Market-analysis payload is empty or unavailable.",
  "warnings": []
}
```

If required values are missing, do not fabricate them.

Instead, list missing fields in:

```json
{
  "data_quality": {
    "missing_fields": []
  }
}
```

---

## Production Safety Rules

The agent must follow these rules:

* use backend payload only;
* do not hallucinate missing values;
* do not override backend-provided data;
* do not produce final Telegram posts;
* do not use Mr Crypto persona;
* do not produce direct trading commands;
* do not analyze external sources;
* derivatives, order book, trade flow and backend sentiment are allowed only if present in the backend payload;
* do not treat disabled portfolio context as a warning;
* do not treat disabled aggregated market context as a warning;
* do not include portfolio or aggregated context in the technical report JSON for the current MVP;
* keep `data_quality.warnings` only for data freshness, completeness and reliability issues;
* put market, technical and positioning warnings into top-level `warnings`;
* use `long_ratio_percent` and `short_ratio_percent` for converted percentage values;
* do not use ambiguous `long_ratio` or `short_ratio` fields for percentage values;
* reduce confidence when data is stale, partial or contradictory;
* prefer conservative wording when signals conflict;
* explicitly list missing or unreliable data;
* keep numeric statements precise and do not exaggerate comparisons;
* avoid internal contradiction between selected levels, calculated distances and warning text.

---

## Recommended Agent Usage

This skill is intended to be used by:

```text
tech-analysis-agent
```

Recommended behavior:

```text
1. Retrieve the backend market-analysis payload.
2. Validate payload freshness and completeness.
3. Ignore expected disabled-context warnings for portfolio and aggregated context.
4. Reclassify backend warnings into:
   - data_quality.warnings for data quality issues;
   - top-level warnings for market/technical/context issues.
5. Map backend market fields correctly:
   - capturedAtUtc → captured_at_utc;
   - price.lastPrice → market.price;
   - price.price24hChangePct * 100 → market.change_24h_percent;
   - price.low24h → market.low_24h;
   - price.high24h → market.high_24h.
6. Extract relevant technical data.
7. Extract allowed backend context if present: derivatives, order book, trade flow, backend sentiment and tags.
8. Convert backend longRatio/shortRatio to long_ratio_percent/short_ratio_percent.
9. Calculate distances to nearest support and resistance if possible.
10. Select nearest valid support/resistance without contradicting warning text.
11. Include support_source and resistance_source if source metadata exists in the payload.
12. Classify technical bias, setup quality and risk level.
13. Produce structured technical_report JSON.
14. Pass technical_report to chief-market-synthesizer.
```

This skill is not intended to be used directly by the final Telegram publishing layer.

---

## Current MVP Notes

At the current MVP stage, only the `market-analysis` backend data source is available.

Therefore, the technical report must reflect only the technical market picture and allowed backend market context from the backend payload.

Allowed current MVP context:

* technical timeframe data;
* price data;
* levels;
* derivatives;
* order book;
* trade flow;
* backend sentiment;
* backend tags;
* indicator diagnostics.

Not allowed current MVP context:

* Telegram channel analysis;
* Twitter/X analysis;
* external news analysis;
* macro analysis;
* on-chain analysis;
* portfolio context;
* aggregated market context.

Future agents may add separate reports for these sources:

```text
telegram-sentiment-agent
twitter-sentiment-agent
onchain-analysis-agent
macro-risk-agent
```

The higher-level synthesis agent is responsible for combining those reports when they become available.


```text
All timeframes are bearish except 15m bullish.
```

If one or more timeframes differ from the majority, explicitly list the groups instead of using `all timeframes`.

Allowed:

```text
15m is bullish, 1h is sideways, while 4h and 1d remain bearish.
```

```text
15m/1h/4h show Poor entry quality, while 1d is Fair.
```

```text
Primary intraday timeframes show weak entry quality, while the daily timeframe is only Fair.
```

---

## Expected Output Contract

The expected result of using this skill is a structured technical report that can be passed to a higher-level market synthesis agent.

Recommended output shape:

```json
{
  "agent": "tech-analysis-agent",
  "skill": "market-analysis",
  "symbol": "BTCUSDT",
  "status": "ok",
  "data_quality": {
    "is_stale": false,
    "is_partial": false,
    "missing_fields": [],
    "warnings": []
  },
  "market": {
    "price": null,
    "change_24h_percent": null,
    "low_24h": null,
    "high_24h": null
  },
  "technical": {
    "technical_bias": "unclear",
    "setup_quality": "unclear",
    "risk_level": "unclear",
    "rsi": {
      "15m": null,
      "1h": null,
      "4h": null,
      "1d": null
    },
    "volume": {
      "15m": null,
      "1h": null,
      "4h": null
    },
    "levels": {
      "support": null,
      "resistance": null,
      "distance_to_support_percent": null,
      "distance_to_resistance_percent": null
    }
  },
  "context": {
    "derivatives": {
      "funding_rate": null,
      "funding_rate_avg_24h": null,
      "open_interest": null,
      "open_interest_value": null,
      "open_interest_change_1h_percent": null,
      "open_interest_change_4h_percent": null,
      "long_ratio_percent": null,
      "short_ratio_percent": null,
      "premium_vs_index_percent": null
    },
    "order_book": {
      "pressure_label": null,
      "liquidity_skew_label": null,
      "imbalance_top5": null,
      "imbalance_top10": null,
      "imbalance_top20": null
    },
    "trade_flow": {
      "delta_percent": null,
      "has_aggressive_buy_pressure": null,
      "has_aggressive_sell_pressure": null
    },
    "backend_sentiment": {
      "market_regime": null,
      "long_short_bias_score": null,
      "funding_bias_score": null,
      "order_book_pressure_score": null,
      "trade_flow_pressure_score": null
    },
    "tags": []
  },
  "scenarios": {
    "long": {
      "status": "conditional",
      "condition": null
    },
    "short": {
      "status": "conditional",
      "condition": null
    }
  },
  "summary": null,
  "warnings": []
}
```

---

## Context Block Rules

The `context` block is optional at the top level.

For the current MVP, include the `context` block when at least one of the following backend sections is present:

* `derivatives`;
* `orderBook`;
* `tradeFlow`;
* `sentiment`;
* `tags`.

If `context` is included, use stable section names:

```json
{
  "context": {
    "derivatives": {},
    "order_book": {},
    "trade_flow": {},
    "backend_sentiment": {},
    "tags": []
  }
}
```

If a specific context section is absent from the backend payload, set that section to `null`.

Example:

```json
{
  "context": {
    "derivatives": null,
    "order_book": null,
    "trade_flow": null,
    "backend_sentiment": null,
    "tags": []
  }
}
```

If a context section is present, fill only values that exist in the backend payload.

Do not invent missing context values.

Use explicit percentage field names for converted ratio values:

```json
{
  "long_ratio_percent": 67.7,
  "short_ratio_percent": 32.3
}
```

Do not use ambiguous fields when values are expressed as percentages:

```json
{
  "long_ratio": 67.7,
  "short_ratio": 32.3
}
```

When backend provides ratios as fractions:

```json
{
  "longRatio": 0.6768,
  "shortRatio": 0.3232
}
```

convert them to percentages:

```json
{
  "long_ratio_percent": 67.7,
  "short_ratio_percent": 32.3
}
```

If a strict schema exists, the schema file must be treated as the final output contract.

Do not include portfolio or aggregated market context in the technical report JSON for the current MVP.

---

## Output Boundaries

The technical report must be based only on the backend market-analysis payload.

The report must not contain:

* final Telegram post text;
* Mr Crypto persona text;
* jokes, slogans or channel-specific copywriting;
* final trading commands such as `open long`, `open short`, `enter now`;
* invented, estimated or assumed market data;
* analysis from sources outside the backend payload, including Telegram, Twitter/X, news, macro or on-chain data;
* portfolio context when `includePortfolio=false`;
* aggregated market context when `includeAggregatedContext=false`;
* ambiguous ratio fields such as `long_ratio` and `short_ratio` when values are expressed as percentages.

---

## Error Handling

If the backend request fails, the agent must return a technical report with error status.

Example:

```json
{
  "agent": "tech-analysis-agent",
  "skill": "market-analysis",
  "symbol": "BTCUSDT",
  "status": "error",
  "data_quality": {
    "is_stale": null,
    "is_partial": null,
    "missing_fields": [],
    "warnings": ["Failed to retrieve backend market-analysis payload"]
  },
  "market": {
    "price": null,
    "change_24h_percent": null,
    "low_24h": null,
    "high_24h": null
  },
  "technical": {
    "technical_bias": "unclear",
    "setup_quality": "unclear",
    "risk_level": "unclear",
    "rsi": {
      "15m": null,
      "1h": null,
      "4h": null,
      "1d": null
    },
    "volume": {
      "15m": null,
      "1h": null,
      "4h": null
    },
    "levels": {
      "support": null,
      "resistance": null,
      "distance_to_support_percent": null,
      "distance_to_resistance_percent": null
    }
  },
  "context": {
    "derivatives": null,
    "order_book": null,
    "trade_flow": null,
    "backend_sentiment": null,
    "tags": []
  },
  "scenarios": {
    "long": {
      "status": "unavailable",
      "condition": null
    },
    "short": {
      "status": "unavailable",
      "condition": null
    }
  },
  "summary": "Backend market-analysis payload is unavailable.",
  "warnings": []
}
```

If the payload is empty, use:

```json
{
  "status": "no_data",
  "summary": "Market-analysis payload is empty or unavailable.",
  "warnings": []
}
```

If required values are missing, do not fabricate them.

Instead, list missing fields in:

```json
{
  "data_quality": {
    "missing_fields": []
  }
}
```

---

## Production Safety Rules

The agent must follow these rules:

* use backend payload only;
* do not hallucinate missing values;
* do not override backend-provided data;
* do not produce final Telegram posts;
* do not use Mr Crypto persona;
* do not produce direct trading commands;
* do not analyze external sources;
* derivatives, order book, trade flow and backend sentiment are allowed only if present in the backend payload;
* do not treat disabled portfolio context as a warning;
* do not treat disabled aggregated market context as a warning;
* do not include portfolio or aggregated context in the technical report JSON for the current MVP;
* keep `data_quality.warnings` only for data freshness, completeness and reliability issues;
* put market, technical and positioning warnings into top-level `warnings`;
* use `long_ratio_percent` and `short_ratio_percent` for converted percentage values;
* do not use ambiguous `long_ratio` or `short_ratio` fields for percentage values;
* reduce confidence when data is stale, partial or contradictory;
* prefer conservative wording when signals conflict;
* explicitly list missing or unreliable data;
* keep numeric statements precise and do not exaggerate comparisons;
* avoid internal contradiction between selected levels, calculated distances and warning text.

---

## Recommended Agent Usage

This skill is intended to be used by:

```text
tech-analysis-agent
```

Recommended behavior:

```text
1. Retrieve the backend market-analysis payload.
2. Validate payload freshness and completeness.
3. Ignore expected disabled-context warnings for portfolio and aggregated context.
4. Reclassify backend warnings into:
   - data_quality.warnings for data quality issues;
   - top-level warnings for market/technical/context issues.
5. Extract relevant technical data.
6. Extract allowed backend context if present: derivatives, order book, trade flow, backend sentiment and tags.
7. Convert backend longRatio/shortRatio to long_ratio_percent/short_ratio_percent.
8. Calculate distances to nearest support and resistance if possible.
9. Select nearest valid support/resistance without contradicting warning text.
10. Classify technical bias, setup quality and risk level.
11. Produce structured technical_report JSON.
12. Pass technical_report to chief-market-synthesizer.
```

This skill is not intended to be used directly by the final Telegram publishing layer.

---

## Current MVP Notes

At the current MVP stage, only the `market-analysis` backend data source is available.

Therefore, the technical report must reflect only the technical market picture and allowed backend market context from the backend payload.

Allowed current MVP context:

* technical timeframe data;
* price data;
* levels;
* derivatives;
* order book;
* trade flow;
* backend sentiment;
* backend tags;
* indicator diagnostics.

Not allowed current MVP context:

* Telegram channel analysis;
* Twitter/X analysis;
* external news analysis;
* macro analysis;
* on-chain analysis;
* portfolio context;
* aggregated market context.

Future agents may add separate reports for these sources:

```text
telegram-sentiment-agent
twitter-sentiment-agent
onchain-analysis-agent
macro-risk-agent
```

The higher-level synthesis agent is responsible for combining those reports when they become available.
