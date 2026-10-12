import { act, renderHook } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { useValidityClock } from './useValidityClock';

const T0 = Date.parse('2026-10-12T10:00:00.000Z');
const at = (seconds: number) => new Date(T0 + seconds * 1000).toISOString();

async function advance(ms: number) {
  await act(async () => {
    await vi.advanceTimersByTimeAsync(ms);
  });
}

beforeEach(() => {
  vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout', 'Date'] });
  vi.setSystemTime(T0);
});

afterEach(() => {
  vi.useRealTimers();
});

describe('useValidityClock', () => {
  it('updates the time exactly when the nearest deadline arrives and then waits for the next one', async () => {
    const { result } = renderHook(() => useValidityClock([at(90), null, undefined, 'not-a-date', at(30)]));
    expect(result.current).toBe(T0);

    await advance(30_000 - 1);
    expect(result.current).toBe(T0);

    await advance(1);
    expect(result.current).toBe(T0 + 30_000);

    await advance(60_000 - 1);
    expect(result.current).toBe(T0 + 30_000);

    await advance(1);
    expect(result.current).toBe(T0 + 90_000);
    expect(vi.getTimerCount()).toBe(0);
  });

  it('does not schedule a timer without future deadlines', () => {
    renderHook(() => useValidityClock([at(-30), null, undefined]));

    expect(vi.getTimerCount()).toBe(0);
  });

  it('replaces the timer when deadlines change', async () => {
    const { result, rerender } = renderHook(({ deadlines }) => useValidityClock(deadlines), {
      initialProps: { deadlines: [at(30)] },
    });

    rerender({ deadlines: [at(120)] });
    expect(vi.getTimerCount()).toBe(1);

    await advance(30_000);
    expect(result.current).toBe(T0);

    await advance(90_000);
    expect(result.current).toBe(T0 + 120_000);
  });

  it('waits for a far deadline with a single timer instead of firing immediately', async () => {
    const { result } = renderHook(() => useValidityClock(['2999-01-01T00:00:00Z']));
    expect(vi.getTimerCount()).toBe(1);

    await advance(24 * 60 * 60 * 1000);

    expect(result.current).toBe(T0);
    expect(vi.getTimerCount()).toBe(1);
  });

  it('clears the timer on unmount', () => {
    const { unmount } = renderHook(() => useValidityClock([at(30)]));
    expect(vi.getTimerCount()).toBe(1);

    unmount();

    expect(vi.getTimerCount()).toBe(0);
  });
});
