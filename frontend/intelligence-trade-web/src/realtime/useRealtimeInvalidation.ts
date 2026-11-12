import { useEffect, useRef } from 'react';
import { useRealtime } from './realtimeContext';
import type { RealtimeListener } from './realtimeTypes';

export function useRealtimeInvalidation(listener: RealtimeListener) {
  const realtime = useRealtime();
  const latest = useRef(listener);
  useEffect(() => { latest.current = listener; }, [listener]);
  const subscribe = realtime?.subscribe;
  useEffect(() => subscribe?.((event) => latest.current(event)), [subscribe]);
}
