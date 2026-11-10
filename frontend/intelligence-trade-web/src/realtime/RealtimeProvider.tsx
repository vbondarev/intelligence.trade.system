import { useCallback, useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import { useAuth } from '../auth/authContext';
import { createRealtimeClient } from './realtimeClient';
import { RealtimeContext } from './realtimeContext';
import type { RealtimeListener, RealtimeState } from './realtimeTypes';

export function RealtimeProvider({ children }: { children: ReactNode }) {
  const { user, revalidate } = useAuth();
  const userId = user?.userId;
  const listeners = useRef(new Set<RealtimeListener>());
  const lifecycle = useRef<Promise<void>>(Promise.resolve());
  const [state, setState] = useState<RealtimeState>('connecting');
  const [attempt, setAttempt] = useState(0);
  const retry = useCallback(() => setAttempt((current) => current + 1), []);
  const subscribe = useCallback((listener: RealtimeListener) => {
    listeners.current.add(listener);
    return () => { listeners.current.delete(listener); };
  }, []);

  useEffect(() => {
    if (!userId) return;
    let alive = true;
    const client = createRealtimeClient(
      (next) => { if (alive) setState(next); },
      (event) => { if (alive) listeners.current.forEach((listener) => listener(event)); },
      (signal) => revalidate(userId, signal),
    );
    lifecycle.current = lifecycle.current.then(() => { if (alive) void client.start(); });
    return () => {
      alive = false;
      lifecycle.current = lifecycle.current.then(() => client.stop());
    };
  }, [userId, revalidate, attempt]);

  const value = useMemo(() => ({ state, subscribe, retry }), [state, subscribe, retry]);
  return <RealtimeContext.Provider value={value}>{children}</RealtimeContext.Provider>;
}
