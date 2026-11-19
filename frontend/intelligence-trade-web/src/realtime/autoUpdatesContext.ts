import { createContext, useContext, useSyncExternalStore } from 'react';
import { createAutoUpdates } from './autoUpdates';

const defaultUpdates = createAutoUpdates();
export const AutoUpdatesContext = createContext(defaultUpdates);

export function useAutoUpdates() {
  return useContext(AutoUpdatesContext);
}

export function useAutoUpdateState() {
  const updates = useAutoUpdates();
  return useSyncExternalStore(updates.subscribe, updates.getSnapshot);
}
