const OVERVIEW_PATH = '/app';
export const POSITION_SEARCH_PATH = '/app/positions/search';

/** Router state перехода из списка: адрес списка вместе с выбранным подключением и фильтрами. */
export interface PositionPageState {
  returnTo: string;
}

export function positionPagePath(positionId: string): string {
  return `/app/positions/${encodeURIComponent(positionId)}`;
}

/** Обзор конкретного подключения: используется для прямого URL и как ссылка на подключение. */
export function accountOverviewPath(exchangeAccountId: string): string {
  return `${OVERVIEW_PATH}?${new URLSearchParams({ account: exchangeAccountId }).toString()}`;
}

/**
 * Router state принимает только обзор и поиск позиций. Для поиска заново собираем
 * query из единственного известного параметра.
 */
export function resolveReturnPath(state: unknown, exchangeAccountId: string | null): string {
  if (typeof state === 'object' && state !== null && 'returnTo' in state) {
    const returnTo = state.returnTo;
    if (typeof returnTo === 'string' && (returnTo === OVERVIEW_PATH || returnTo.startsWith(`${OVERVIEW_PATH}?`))) {
      const original = new URLSearchParams(returnTo.slice(OVERVIEW_PATH.length));
      const allowed = new Set(['account', 'view', 'state', 'side', 'symbol']);
      const params = new URLSearchParams([...original].filter(([name]) => allowed.has(name)));
      return params.size > 0 ? `${OVERVIEW_PATH}?${params}` : OVERVIEW_PATH;
    }
    if (typeof returnTo === 'string' && (returnTo === POSITION_SEARCH_PATH || returnTo.startsWith(`${POSITION_SEARCH_PATH}?`))) {
      const [symbol, ...duplicates] = new URLSearchParams(returnTo.slice(POSITION_SEARCH_PATH.length)).getAll('symbol');
      return symbol && duplicates.length === 0
        ? `${POSITION_SEARCH_PATH}?${new URLSearchParams({ symbol })}`
        : POSITION_SEARCH_PATH;
    }
  }

  return exchangeAccountId === null ? OVERVIEW_PATH : accountOverviewPath(exchangeAccountId);
}
