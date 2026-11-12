export type RealtimeState = 'connecting' | 'ready' | 'reconnecting' | 'offline';

export type RealtimeInvalidation =
  | { type: 'recovery' }
  | { type: 'exchangeAccount.updated' | 'portfolio.updated'; exchangeAccountId: string }
  | { type: 'position.updated' | 'evaluation.updated'; positionId: string };

export type RealtimeListener = (event: RealtimeInvalidation) => void;
