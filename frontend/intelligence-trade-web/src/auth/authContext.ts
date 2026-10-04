import { createContext, useContext } from 'react';
import type { AuthContextValue } from './authTypes';

export const AuthContext = createContext<AuthContextValue | null>(null);

export function useAuth(): AuthContextValue {
  const value = useContext(AuthContext);
  if (value === null) {
    throw new Error('useAuth must be used inside AuthProvider.');
  }

  return value;
}
