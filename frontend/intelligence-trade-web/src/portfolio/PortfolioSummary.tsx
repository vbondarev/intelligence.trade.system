import { useId } from 'react';
import type { Portfolio, PortfolioExposure } from './portfolioTypes';
import { formatAssetAmount, formatSignedUsd, formatUsd, pnlClassName } from './portfolioFormatting';

/**
 * Сводка портфеля: account-level значения в USD и экспозиция по активам расчёта.
 * Экспозиции разных активов показываются отдельными группами и не складываются.
 */
export function PortfolioSummary({ portfolio }: { portfolio: Portfolio }) {
  const titleId = useId();

  const { exposures, currentPositionCount } = portfolio;
  const single = exposures.length === 1 ? exposures[0] : undefined;

  return (
    <section className="portfolio-summary" aria-labelledby={titleId}>
      <h2 id={titleId} className="screen-reader-only">Сводка</h2>
      <dl className="metric-grid">
        <div className="metric">
          <dt>Общий капитал</dt>
          <dd>{formatUsd(portfolio.capital.totalEquity)}</dd>
        </div>
        <div className="metric">
          <dt>Доступно</dt>
          <dd>{formatUsd(portfolio.capital.availableCapital)}</dd>
        </div>
        <div className="metric">
          <dt>Нереализованный PnL</dt>
          <dd className={pnlClassName(portfolio.totalUnrealizedPnl)}>{formatSignedUsd(portfolio.totalUnrealizedPnl)}</dd>
        </div>
        {single ? (
          <div className="metric">
            <dt>Объём позиций</dt>
            <dd>{formatAssetAmount(single.grossExposure, single.settlementAsset)}</dd>
            <dd className="metric-context">
              Long {formatAssetAmount(single.longExposure, single.settlementAsset)} · Short {formatAssetAmount(single.shortExposure, single.settlementAsset)}
            </dd>
          </div>
        ) : (
          <div className="metric">
            <dt>Открытые позиции</dt>
            <dd>{currentPositionCount}</dd>
          </div>
        )}
      </dl>
      {exposures.length > 1 && (
        <details className="overview-disclosure exposure-block">
          <summary>Экспозиция</summary>
          <div className="exposure-groups">
            {exposures.map((exposure) => <ExposureGroup key={exposure.settlementAsset} exposure={exposure} />)}
          </div>
        </details>
      )}
      {exposures.length === 0 && (
        <p className="metric-context">
          {currentPositionCount === 0 ? 'Открытых позиций нет.' : 'Экспозиция недоступна.'}
        </p>
      )}
    </section>
  );
}

function ExposureGroup({ exposure }: { exposure: PortfolioExposure }) {
  const asset = exposure.settlementAsset;

  return (
    <section className="exposure-group" aria-label={`Экспозиция ${asset}`}>
      <h3 className="exposure-title">{asset}</h3>
      <dl className="value-rows">
        <div>
          <dt>Всего</dt>
          <dd>{formatAssetAmount(exposure.grossExposure, asset)}</dd>
        </div>
        <div>
          <dt>Long</dt>
          <dd>{formatAssetAmount(exposure.longExposure, asset)}</dd>
        </div>
        <div>
          <dt>Short</dt>
          <dd>{formatAssetAmount(exposure.shortExposure, asset)}</dd>
        </div>
      </dl>
    </section>
  );
}
