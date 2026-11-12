import React, { StrictMode } from 'react';
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { useRealtime } from './realtimeContext';
import type { RealtimeInvalidation, RealtimeState } from './realtimeTypes';
import { describe, expect, it, vi } from 'vitest';
import { AuthContext } from '../auth/authContext';
import type { AuthContextValue } from '../auth/authTypes';
import { RealtimeProvider } from './RealtimeProvider';

const mocks = vi.hoisted(() => ({ clients: [] as Array<{ start: ReturnType<typeof vi.fn>; stop: ReturnType<typeof vi.fn>; onState: (state: RealtimeState) => void; onEvent: (event: RealtimeInvalidation) => void }> }));
vi.mock('./realtimeClient', () => ({
  createRealtimeClient: (onState: (state: RealtimeState) => void, onEvent: (event: RealtimeInvalidation) => void) => {
    const client = { start: vi.fn().mockResolvedValue(undefined), stop: vi.fn().mockResolvedValue(undefined), onState, onEvent };
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
    let finish!: () => void;
    const starting = new Promise<void>((resolve) => { finish = resolve; });
    const view = render(<StrictMode><AuthContext.Provider value={auth}><RealtimeProvider><span>content</span></RealtimeProvider></AuthContext.Provider></StrictMode>);
    mocks.clients[1]!.start.mockReturnValue(starting);
    await waitFor(() => expect(mocks.clients.reduce((count, client) => count + client.start.mock.calls.length, 0)).toBe(1));
    view.unmount();
    expect(mocks.clients[1]!.stop).not.toHaveBeenCalled();
    await act(async () => finish());
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
  it('ожидает pending start и stop при смене key и игнорирует старые callbacks', async () => {
    mocks.clients.length = 0;
    let finishStart!: () => void;
    let finishStop!: () => void;
    const starting = new Promise<void>((resolve) => { finishStart = resolve; });
    const stopping = new Promise<void>((resolve) => { finishStop = resolve; });
    const listener = vi.fn();
    function Observer() {
      const realtime = useRealtime();
      React.useEffect(() => realtime?.subscribe(listener), [realtime]);
      return <span>{realtime?.state}</span>;
    }
    const view = render(<AuthContext.Provider value={auth}><RealtimeProvider key="a"><Observer /></RealtimeProvider></AuthContext.Provider>);
    mocks.clients[0]!.start.mockReturnValue(starting);
    mocks.clients[0]!.stop.mockReturnValue(stopping);
    await waitFor(() => expect(mocks.clients[0]!.start).toHaveBeenCalledOnce());
    const second = { ...auth, user: { userId: 'two', subject: 'two' } };
    view.rerender(<AuthContext.Provider value={second}><RealtimeProvider key="b"><Observer /></RealtimeProvider></AuthContext.Provider>);
    expect(mocks.clients[0]!.stop).not.toHaveBeenCalled();
    act(() => { mocks.clients[0]!.onState('ready'); mocks.clients[0]!.onEvent({ type: 'recovery' }); });
    expect(listener).not.toHaveBeenCalled();
    expect(mocks.clients[1]!.start).not.toHaveBeenCalled();
    await act(async () => finishStart());
    await waitFor(() => expect(mocks.clients[0]!.stop).toHaveBeenCalledOnce());
    expect(mocks.clients[1]!.start).not.toHaveBeenCalled();
    await act(async () => finishStop());
    await waitFor(() => expect(mocks.clients[1]!.start).toHaveBeenCalledOnce());
    view.unmount();
  });

  it('обрабатывает ошибку запуска и выполняет cleanup', async () => {
    mocks.clients.length = 0;
    let rejectStart!: (error: Error) => void;
    const starting = new Promise<void>((_, reject) => { rejectStart = reject; });
    const listener = vi.fn();
    function Observer() {
      const realtime = useRealtime();
      React.useEffect(() => realtime?.subscribe(listener), [realtime]);
      return <span>{realtime?.state}</span>;
    }
    const view = render(<AuthContext.Provider value={auth}><RealtimeProvider><Observer /></RealtimeProvider></AuthContext.Provider>);
    mocks.clients[0]!.start.mockReturnValue(starting);
    await waitFor(() => expect(mocks.clients[0]!.start).toHaveBeenCalledOnce());
    await act(async () => rejectStart(new Error('start')));
    await waitFor(() => expect(mocks.clients[0]!.stop).toHaveBeenCalledOnce());
    act(() => { mocks.clients[0]!.onState('ready'); mocks.clients[0]!.onEvent({ type: 'recovery' }); });
    expect(screen.getByText('offline')).toBeInTheDocument();
    expect(listener).not.toHaveBeenCalled();
    view.unmount();
    expect(mocks.clients[0]!.stop).toHaveBeenCalledOnce();
  });

  it('ожидает освобождения transport перед ручным повтором', async () => {
    mocks.clients.length = 0;
    let finishStop!: () => void;
    const stopping = new Promise<void>((resolve) => { finishStop = resolve; });
    function RetryControl() {
      const realtime = useRealtime();
      return <button onClick={realtime?.retry}>Повторить</button>;
    }
    const view = render(<AuthContext.Provider value={auth}><RealtimeProvider><RetryControl /></RealtimeProvider></AuthContext.Provider>);
    mocks.clients[0]!.stop.mockReturnValue(stopping);
    await waitFor(() => expect(mocks.clients[0]!.start).toHaveBeenCalledOnce());
    fireEvent.click(screen.getByRole('button', { name: 'Повторить' }));
    await waitFor(() => expect(mocks.clients[0]!.stop).toHaveBeenCalledOnce());
    expect(mocks.clients[1]!.start).not.toHaveBeenCalled();
    await act(async () => finishStop());
    await waitFor(() => expect(mocks.clients[1]!.start).toHaveBeenCalledOnce());
    view.unmount();
  });

  it('T01: unmount ожидает start и игнорирует поздние callbacks', async () => {
    mocks.clients.length = 0;
    let finish!: () => void;
    const starting = new Promise<void>((resolve) => { finish = resolve; });
    const listener = vi.fn();
    function Observer() {
      const realtime = useRealtime();
      React.useEffect(() => realtime?.subscribe(listener), [realtime]);
      return <span>{realtime?.state}</span>;
    }
    const view = render(<AuthContext.Provider value={auth}><RealtimeProvider><Observer /></RealtimeProvider></AuthContext.Provider>);
    mocks.clients[0]!.start.mockReturnValue(starting);
    await waitFor(() => expect(mocks.clients[0]!.start).toHaveBeenCalledOnce());
    view.unmount();
    act(() => { mocks.clients[0]!.onState('ready'); mocks.clients[0]!.onEvent({ type: 'recovery' }); });
    expect(listener).not.toHaveBeenCalled();
    expect(mocks.clients[0]!.stop).not.toHaveBeenCalled();
    await act(async () => finish());
    await waitFor(() => expect(mocks.clients[0]!.stop).toHaveBeenCalledOnce());
  });

  it('ошибка stop не разрешает запуск следующего transport', async () => {
    mocks.clients.length = 0;
    let failStop!: (error: Error) => void;
    const stopping = new Promise<void>((_, reject) => { failStop = reject; });
    const view = render(<AuthContext.Provider value={auth}><RealtimeProvider key="a">a</RealtimeProvider></AuthContext.Provider>);
    mocks.clients[0]!.stop.mockReturnValue(stopping);
    await waitFor(() => expect(mocks.clients[0]!.start).toHaveBeenCalledOnce());
    view.rerender(<AuthContext.Provider value={auth}><RealtimeProvider key="b">b</RealtimeProvider></AuthContext.Provider>);
    await waitFor(() => expect(mocks.clients[0]!.stop).toHaveBeenCalledOnce());
    await act(async () => failStop(new Error('stop')));
    expect(mocks.clients[1]!.start).not.toHaveBeenCalled();
    view.unmount();
  });

});
