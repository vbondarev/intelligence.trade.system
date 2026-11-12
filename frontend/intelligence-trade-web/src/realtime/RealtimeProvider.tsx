import { useCallback, useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import { useAuth } from '../auth/authContext';
import { createRealtimeClient } from './realtimeClient';
import { RealtimeContext } from './realtimeContext';
import type { RealtimeListener, RealtimeState } from './realtimeTypes';

// Ошибка остановки сохраняет очередь отклонённой: следующий transport нельзя запускать.
let lifecycle: Promise<void> = Promise.resolve();

export function RealtimeProvider({ children }: { children: ReactNode }) {
  const { user, revalidate } = useAuth();
  const userId = user?.userId;
  const listeners = useRef(new Set<RealtimeListener>());
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
    let stopped = false;
    const stop = async () => {
      if (stopped) return;
      stopped = true;
      await client.stop();
    };
    lifecycle = lifecycle.then(async () => {
      if (!alive) return;
      try {
        await client.start();
      } catch {
        if (alive) setState('offline');
        alive = false;
        await stop();
      }
    });
    void lifecycle.catch(() => { if (alive) setState('offline'); });
    return () => {
      alive = false;
      lifecycle = lifecycle.then(stop);
      void lifecycle.catch(() => {});
    };
  }, [userId, revalidate, attempt]);

  const value = useMemo(() => ({ state, subscribe, retry }), [state, subscribe, retry]);
  return <RealtimeContext.Provider value={value}>{children}</RealtimeContext.Provider>;
}
