import { createContext, useContext } from 'react';
import type { RealtimeListener, RealtimeState } from './realtimeTypes';

export interface RealtimeContextValue {
  state: RealtimeState;
  subscribe: (listener: RealtimeListener) => () => void;
  retry: () => void;
}

export const RealtimeContext = createContext<RealtimeContextValue | null>(null);
export function useRealtime() {
  return useContext(RealtimeContext);
}
