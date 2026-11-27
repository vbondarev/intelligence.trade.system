import { useMemo } from 'react';
import type { SessionReadContext } from '../auth/authTypes';
import type { PositionEvaluation } from './positionTypes';

interface Request {
  positionId: string;
  opening: symbol;
  accessGeneration: number;
  session: SessionReadContext;
}

type Completion = Request & ({ ok: true; value: PositionEvaluation } | { ok: false; error: unknown });

export function usePositionEvaluationRequests() {
  return useMemo(() => {
    const pending = new Set<string>();
    const listeners = new Set<() => void>();
    const completions = new Set<(completion: Completion) => void>();

    function finish(completion: Completion) {
      pending.delete(completion.positionId);
      completions.forEach((listener) => listener(completion));
      listeners.forEach((listener) => listener());
    }

    return {
      isRunning: (id: string) => pending.has(id),
      subscribe(listener: () => void) {
        listeners.add(listener);
        return () => { listeners.delete(listener); };
      },
      onCompletion(listener: (completion: Completion) => void) {
        completions.add(listener);
        return () => { completions.delete(listener); };
      },
      start(request: Request, send: () => Promise<PositionEvaluation>) {
        if (pending.has(request.positionId)) return;
        pending.add(request.positionId);
        listeners.forEach((listener) => listener());
        void send().then(
          (value) => finish({ ...request, ok: true, value }),
          (error: unknown) => finish({ ...request, ok: false, error }),
        );
      },
    };
  }, []);
}

export type PositionEvaluationRequests = ReturnType<typeof usePositionEvaluationRequests>;
