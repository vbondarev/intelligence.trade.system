import '@testing-library/jest-dom/vitest';
import { cleanup } from '@testing-library/react';
import { afterEach, vi } from 'vitest';

// jsdom не реализует matchMedia и ResizeObserver, а Mantine-компоненты используют их при рендере.
// Обычные функции вместо vi.fn: restoreMocks в конфигурации сбрасывал бы их реализацию между тестами.
Object.defineProperty(window, 'matchMedia', {
  writable: true,
  value: (query: string): MediaQueryList =>
    ({
      matches: false,
      media: query,
      onchange: null,
      addListener: () => {},
      removeListener: () => {},
      addEventListener: () => {},
      removeEventListener: () => {},
      dispatchEvent: () => false,
    }) as MediaQueryList,
});

class ResizeObserverStub {
  observe() {}
  unobserve() {}
  disconnect() {}
}

window.ResizeObserver = ResizeObserverStub;

afterEach(() => {
  cleanup();
});

// Страницы тестируют REST-состояние; transport отдельно проверяется в realtime-тестах.
vi.mock('../realtime/realtimeClient', async () => {
  const driver = await import('./realtimeTestDriver');
  return {
    createRealtimeClient: (_state: unknown, onEvent: Parameters<typeof driver.addRealtimeListener>[0]) => ({
      start: async () => { driver.addRealtimeListener(onEvent); },
      stop: async () => { driver.removeRealtimeListener(onEvent); },
    }),
  };
});
