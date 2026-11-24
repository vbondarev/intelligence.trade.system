import { Link, useLocation } from 'react-router-dom';
import { POSITION_SEARCH_PATH, resolveReturnPath } from '../position/positionNavigation';
import { StatusMessage } from '../ui/StatusMessage';

export function PositionRouteLoading() {
  return (
    <section className="page position-page" aria-labelledby="position-route-loading-title">
      <h1 id="position-route-loading-title" data-route-pending>Позиция</h1>
      <StatusMessage kind="loading" text="Загружаем страницу позиции…" />
    </section>
  );
}

export function PositionRouteLoadError() {
  const { state } = useLocation();
  const returnPath = resolveReturnPath(state, null);
  const returningToSearch = returnPath.split('?')[0] === POSITION_SEARCH_PATH;

  return (
    <section className="page position-page" aria-labelledby="position-route-error-title">
      <h1 id="position-route-error-title">Не удалось загрузить страницу позиции</h1>
      <StatusMessage kind="error" text="Обновите страницу, чтобы повторить загрузку." />
      <div className="form-actions">
        <button type="button" className="button button-secondary" onClick={() => window.location.reload()}>
          Обновить страницу
        </button>
        <Link to={returnPath} className="back-link">
          {returningToSearch ? 'Вернуться к результатам поиска' : 'Вернуться к списку'}
        </Link>
      </div>
    </section>
  );
}
