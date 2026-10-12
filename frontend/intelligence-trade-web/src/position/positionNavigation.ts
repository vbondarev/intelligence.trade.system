const OVERVIEW_PATH = '/app';

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
 * Адрес возврата к списку. Router state принимается только как локальный адрес обзора `/app`
 * с query: иначе (прямой URL, произвольный state) используется обзор подключения позиции.
 * Так возврат не может стать открытым перенаправлением или переходом на другой маршрут.
 */
export function resolveReturnPath(state: unknown, exchangeAccountId: string | null): string {
  if (typeof state === 'object' && state !== null && 'returnTo' in state) {
    const returnTo = state.returnTo;
    if (typeof returnTo === 'string' && (returnTo === OVERVIEW_PATH || returnTo.startsWith(`${OVERVIEW_PATH}?`))) {
      return returnTo;
    }
  }

  return exchangeAccountId === null ? OVERVIEW_PATH : accountOverviewPath(exchangeAccountId);
}
