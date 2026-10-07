export const DISPLAY_NAME_MAX_LENGTH = 100;

export type ExchangeProvider = 'bybit';

export type ExchangeAccountConnectionStatus = 'unknown' | 'connected' | 'unavailable' | 'disabled';

export type ExchangeAccountCapability = 'readBalance' | 'readPositions';

export interface ExchangeAccount {
  id: string;
  displayName: string;
  exchange: ExchangeProvider;
  connectionStatus: ExchangeAccountConnectionStatus;
  capabilities: ExchangeAccountCapability[];
  lastSyncedAt: string | null;
}

export interface ExchangeAccountListResponse {
  items: ExchangeAccount[];
}

/**
 * Credentials живут только на время отправки формы: их нельзя помещать в общий state,
 * URL или browser storage.
 */
export interface ExchangeCredentials {
  apiKey: string;
  apiSecret: string;
}

/**
 * Результат `POST /bff/me/exchange-accounts`: `created` — новое подключение (`201`),
 * `reconnected` — восстановлено ранее отключённое подключение того же аккаунта Bybit (`200`).
 */
export interface ConnectionResult {
  outcome: 'created' | 'reconnected';
  account: ExchangeAccount;
}

export type ConnectionErrorCode =
  | 'validation_failed'
  | 'exchange_credentials_invalid'
  | 'exchange_permissions_rejected'
  | 'exchange_account_already_exists'
  | 'exchange_account_identity_mismatch'
  | 'exchange_account_disabled'
  | 'exchange_unavailable'
  | 'concurrency_conflict'
  | 'resource_not_found'
  | 'authentication_required'
  | 'access_forbidden';
