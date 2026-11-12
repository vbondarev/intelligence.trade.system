import type { RealtimeInvalidation, RealtimeListener } from '../realtime/realtimeTypes';

const listeners = new Set<RealtimeListener>();
export function emitRealtime(event: RealtimeInvalidation) { listeners.forEach((listener) => listener(event)); }
export function addRealtimeListener(listener: RealtimeListener) { listeners.add(listener); }
export function removeRealtimeListener(listener: RealtimeListener) { listeners.delete(listener); }
