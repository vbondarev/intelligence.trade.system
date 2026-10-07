import { useId } from 'react';
import type { Portfolio, PortfolioExposure } from './portfolioTypes';
import { formatAssetAmount, formatSignedUsd, formatUsd } from './portfolioFormatting';

function pnlClassName(value: number | null): string {
  if (value === null || value === 0) {
    return '';
  }

  return value > 0 ? 'value-positive' : 'value-negative';
}

/**
 * Сводка портфеля: account-level значения в USD и экспозиция по активам расчёта.
 * Экспозиции разных активов показываются отдельными группами и не складываются.
 */
export function PortfolioSummary({ portfolio }: { portfolio: Portfolio }) {
  const titleId = useId();

  return (
    <>
      <section className="panel portfolio-summary" aria-labelledby={titleId}>
        <h2 id={titleId} className="section-title">
          Сводка
        </h2>
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
          <div className="metric">
            <dt>Открытые позиции</dt>
            <dd>{portfolio.currentPositionCount}</dd>
          </div>
        </dl>
      </section>
      <ExposureBlock exposures={portfolio.exposures} />
    </>
  );
}

function ExposureBlock({ exposures }: { exposures: readonly PortfolioExposure[] }) {
  const titleId = useId();
  const single = exposures.length === 1 ? exposures[0] : undefined;

  return (
    <section className="panel exposure-block" aria-labelledby={titleId}>
      <h2 id={titleId} className="section-title">
        {single === undefined ? 'Экспозиция' : `Экспозиция — ${single.settlementAsset}`}
      </h2>
      {exposures.length === 0 && <p className="muted">Открытых позиций нет.</p>}
      <div className="exposure-groups">
        {exposures.map((exposure) => (
          <ExposureGroup key={exposure.settlementAsset} exposure={exposure} showTitle={single === undefined} />
        ))}
      </div>
    </section>
  );
}

function ExposureGroup({ exposure, showTitle }: { exposure: PortfolioExposure; showTitle: boolean }) {
  const asset = exposure.settlementAsset;

  return (
    <section className="exposure-group" aria-label={`Экспозиция ${asset}`}>
      {showTitle && <h3 className="exposure-title">{asset}</h3>}
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
