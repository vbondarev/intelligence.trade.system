import { useEffect, useState } from 'react';

// Больший delay setTimeout переполняет и срабатывает сразу; дальний срок ждём частями.
const MAX_TIMER_DELAY_MS = 2_147_483_647;

function nextDeadlineAfter(deadlines: readonly (string | null | undefined)[], now: number): number | null {
  let next: number | null = null;
  for (const deadline of deadlines) {
    if (deadline == null) {
      continue;
    }

    const timestamp = new Date(deadline).getTime();
    if (!Number.isNaN(timestamp) && timestamp > now && (next === null || timestamp < next)) {
      next = timestamp;
    }
  }

  return next;
}

/**
 * Текущее время для отображения временной валидности. Компонент перерисовывается в момент
 * наступления ближайшего из ещё не наступивших сроков `deadlines`, без периодического опроса
 * и без запросов к backend. Некорректные и отсутствующие сроки игнорируются.
 */
export function useValidityClock(deadlines: readonly (string | null | undefined)[]): number {
  const [now, setNow] = useState(() => Date.now());
  const nextDeadline = nextDeadlineAfter(deadlines, now);

  useEffect(() => {
    if (nextDeadline === null) {
      return;
    }

    const delay = Math.min(Math.max(nextDeadline - Date.now(), 0), MAX_TIMER_DELAY_MS);
    const timer = setTimeout(() => setNow(Date.now()), delay);
    return () => clearTimeout(timer);
  }, [nextDeadline, now]);

  return now;
}
