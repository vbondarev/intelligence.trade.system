import { HubConnectionBuilder, HttpTransportType, LogLevel, type HubConnection } from '@microsoft/signalr';
import type { RealtimeInvalidation, RealtimeState } from './realtimeTypes';

const RETRY_DELAYS_MS = [1000, 2000, 4000, 8000, 15000, 30000, 30000, 30000];

type AccountMessage = { exchangeAccountId?: unknown };
type PositionMessage = { positionId?: unknown };

export function createRealtimeClient(
  onState: (state: RealtimeState) => void,
  onEvent: (event: RealtimeInvalidation) => void,
  checkSession: (signal: AbortSignal) => Promise<boolean>,
) {
  let connection: HubConnection | null = null;
  let inFlightHubStart: Promise<void> | null = null;
  let disposed = false;
  let retries = 0;
  let timer: ReturnType<typeof setTimeout> | undefined;
  const abort = new AbortController();

  function scheduleRetry() {
    if (disposed || timer !== undefined) return;
    if (retries >= RETRY_DELAYS_MS.length) {
      onState('offline');
      return;
    }
    onState('reconnecting');
    const delay = RETRY_DELAYS_MS[retries++];
    timer = setTimeout(() => {
      timer = undefined;
      void start();
    }, delay);
  }

  async function disconnected() {
    if (disposed) return;
    onState('offline');
    if (await checkSession(abort.signal)) scheduleRetry();
  }

  async function start() {
    if (disposed || connection !== null) return;
    onState(retries === 0 ? 'connecting' : 'reconnecting');
    let current: HubConnection;
    try {
      current = new HubConnectionBuilder()
        .withUrl('/bff/hubs/v1/updates', { transport: HttpTransportType.WebSockets, skipNegotiation: true })
        .configureLogging(LogLevel.None)
        .build();
    } catch {
      await disconnected();
      return;
    }
    connection = current;
    current.on('bff.realtime.ready', () => {
      if (disposed || connection !== current) return;
      retries = 0;
      onState('ready');
      onEvent({ type: 'recovery' });
    });
    for (const type of ['exchangeAccount.updated', 'portfolio.updated'] as const) {
      current.on(type, (message: AccountMessage) => {
        if (!disposed && connection === current && typeof message?.exchangeAccountId === 'string')
          onEvent({ type, exchangeAccountId: message.exchangeAccountId });
      });
    }
    for (const type of ['position.updated', 'evaluation.updated'] as const) {
      current.on(type, (message: PositionMessage) => {
        if (!disposed && connection === current && typeof message?.positionId === 'string')
          onEvent({ type, positionId: message.positionId });
      });
    }
    current.onclose(() => {
      if (connection !== current) return;
      connection = null;
      void disconnected();
    });
    let attempt: Promise<void> | null = null;
    try {
      attempt = current.start();
      inFlightHubStart = attempt;
      await attempt;
    } catch {
      if (connection === current) {
        connection = null;
        await disconnected();
      }
    } finally {
      if (inFlightHubStart === attempt) inFlightHubStart = null;
    }
  }

  async function stop() {
    disposed = true;
    abort.abort();
    if (timer !== undefined) clearTimeout(timer);
    timer = undefined;
    const pendingStart = inFlightHubStart;
    if (pendingStart !== null) {
      try { await pendingStart; } catch { /* Ошибка запуска не отменяет очистку. */ }
    }
    const current = connection;
    connection = null;
    if (current !== null) await current.stop();
  }

  return { start, stop };
}
