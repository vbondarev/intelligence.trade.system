import { StrictMode } from 'react';
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { useRealtime } from './realtimeContext';
import type { RealtimeState } from './realtimeTypes';
import { describe, expect, it, vi } from 'vitest';
import { AuthContext } from '../auth/authContext';
import type { AuthContextValue } from '../auth/authTypes';
import { RealtimeProvider } from './RealtimeProvider';

const mocks = vi.hoisted(() => ({ clients: [] as Array<{ start: ReturnType<typeof vi.fn>; stop: ReturnType<typeof vi.fn>; onState: (state: RealtimeState) => void }> }));
vi.mock('./realtimeClient', () => ({
  createRealtimeClient: (onState: (state: RealtimeState) => void) => {
    const client = { start: vi.fn().mockResolvedValue(undefined), stop: vi.fn().mockResolvedValue(undefined), onState };
    mocks.clients.push(client);
    return client;
  },
}));

const auth: AuthContextValue = {
  loading: false, authenticated: true, user: { userId: 'one', subject: 'one' }, error: null,
  logoutAvailable: false, login: vi.fn(), logout: vi.fn(), retry: vi.fn(), revalidate: vi.fn(),
};

describe('RealtimeProvider', () => {
  it('serializes StrictMode setup and closes the active transport on unmount', async () => {
    mocks.clients.length = 0;
    const view = render(<StrictMode><AuthContext.Provider value={auth}><RealtimeProvider><span>content</span></RealtimeProvider></AuthContext.Provider></StrictMode>);
    await waitFor(() => expect(mocks.clients.reduce((count, client) => count + client.start.mock.calls.length, 0)).toBe(1));
    view.unmount();
    await waitFor(() => expect(mocks.clients.every((client) => client.stop.mock.calls.length === 1)).toBe(true));
  });

  it('stops the old transport before starting a changed user', async () => {
    mocks.clients.length = 0;
    const view = render(<AuthContext.Provider value={auth}><RealtimeProvider>content</RealtimeProvider></AuthContext.Provider>);
    await waitFor(() => expect(mocks.clients[0]?.start).toHaveBeenCalledOnce());
    const second = { ...auth, user: { userId: 'two', subject: 'two' } };
    view.rerender(<AuthContext.Provider value={second}><RealtimeProvider>content</RealtimeProvider></AuthContext.Provider>);
    await waitFor(() => expect(mocks.clients[1]?.start).toHaveBeenCalledOnce());
    expect(mocks.clients[0]?.stop).toHaveBeenCalledOnce();
    view.unmount();
  });
  it('allows a new connection attempt after going offline', async () => {
    mocks.clients.length = 0;
    function RetryControl() {
      const realtime = useRealtime();
      return <button onClick={realtime?.retry}>Повторить</button>;
    }
    const view = render(<AuthContext.Provider value={auth}><RealtimeProvider><RetryControl /></RealtimeProvider></AuthContext.Provider>);
    await waitFor(() => expect(mocks.clients[0]?.start).toHaveBeenCalledOnce());
    act(() => mocks.clients[0]?.onState('offline'));
    fireEvent.click(screen.getByRole('button', { name: 'Повторить' }));
    await waitFor(() => expect(mocks.clients[1]?.start).toHaveBeenCalledOnce());
    expect(mocks.clients[0]?.stop).toHaveBeenCalledOnce();
    view.unmount();
  });
});
