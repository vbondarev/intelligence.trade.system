import type { ReactNode } from 'react';
import { Navigate } from 'react-router-dom';
import { useAuth } from './authContext';
import { LoadingScreen } from '../layout/LoadingScreen';

export function ProtectedRoute({ children }: { children: ReactNode }) {
  const { loading, authenticated } = useAuth();

  if (loading) {
    return <LoadingScreen />;
  }

  if (!authenticated) {
    return <Navigate to="/" replace />;
  }

  return children;
}
