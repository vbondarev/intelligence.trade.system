import { lazy, Suspense } from 'react';
import { Navigate, Route, Routes } from 'react-router-dom';
import { ProtectedRoute } from '../auth/ProtectedRoute';
import { AppShell } from '../layout/AppShell';
import { ConnectionsPage } from '../pages/ConnectionsPage';
import { HomePage } from '../pages/HomePage';
import { LandingPage } from '../pages/LandingPage';
import { PositionSearchPage } from '../pages/PositionSearchPage';
import { PositionRouteLoadError, PositionRouteLoading } from './PositionRouteStatus';

const LazyPositionPage = lazy(() => import('../pages/PositionPage').then(
  ({ PositionPage }) => ({ default: PositionPage }),
  () => ({ default: PositionRouteLoadError }),
));

export function AppRoutes() {
  return (
    <Routes>
      <Route path="/" element={<LandingPage />} />
      <Route
        path="/app"
        element={
          <ProtectedRoute>
            <AppShell />
          </ProtectedRoute>
        }
      >
        <Route index element={<HomePage />} />
        <Route path="positions/search" element={<PositionSearchPage />} />
        <Route
          path="positions/:id"
          element={
            <Suspense fallback={<PositionRouteLoading />}>
              <LazyPositionPage />
            </Suspense>
          }
        />
        <Route path="settings/connections" element={<ConnectionsPage />} />
      </Route>
      <Route path="*" element={<Navigate to="/" replace />} />
    </Routes>
  );
}
