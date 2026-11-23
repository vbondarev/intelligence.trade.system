import type { ReactNode } from 'react';
import { Navigate, useLocation } from 'react-router-dom';
import { useAuth } from './authContext';
import { LoadingScreen } from '../layout/LoadingScreen';

export function ProtectedRoute({ children }: { children: ReactNode }) {
  const { loading, authenticated, sessionExpired } = useAuth();
  const { pathname, search, hash } = useLocation();

  if (loading) {
    return <LoadingScreen />;
  }

  if (!authenticated && !sessionExpired) {
    return <Navigate to="/" replace state={{ returnUrl: pathname + search + hash }} />;
  }

  return children;
}
