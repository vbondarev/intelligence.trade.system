import { MantineProvider } from '@mantine/core';
import { BrowserRouter } from 'react-router-dom';
import { AuthProvider } from '../auth/AuthProvider';
import { theme } from '../styles/theme';
import { AppRoutes } from './router';

export function App() {
  return (
    <MantineProvider theme={theme} forceColorScheme="dark">
      <AuthProvider>
        <BrowserRouter>
          <AppRoutes />
        </BrowserRouter>
      </AuthProvider>
    </MantineProvider>
  );
}
