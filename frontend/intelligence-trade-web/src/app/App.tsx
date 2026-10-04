import { BrowserRouter } from 'react-router-dom';
import { AuthProvider } from '../auth/AuthProvider';
import { AppRoutes } from './router';

export function App() {
  return (
    <AuthProvider>
      <BrowserRouter>
        <AppRoutes />
      </BrowserRouter>
    </AuthProvider>
  );
}
