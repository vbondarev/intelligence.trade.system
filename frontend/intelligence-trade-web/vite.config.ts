import react from '@vitejs/plugin-react';
import type { ProxyOptions } from 'vite';
import { defineConfig } from 'vitest/config';

// Dev/preview server повторяет nginx-образ: тот же origin для React и same-origin proxy в BFF.
// Адрес BFF берётся только из переменной окружения процесса и не попадает в bundle.
function bffProxy(command: string, mode: string): Record<string, ProxyOptions> | undefined {
  if (command !== 'serve' || mode === 'test') {
    return undefined;
  }

  const upstream = process.env.BFF_UPSTREAM;
  if (upstream === undefined || !/^https?:\/\/[^/?#]+\/?$/.test(upstream)) {
    throw new Error('Задайте BFF_UPSTREAM в формате scheme://host[:port], например http://192.168.1.16:8083.');
  }

  // Host не подменяется: BFF строит OIDC redirect URIs из Host запроса, и они должны
  // совпадать с origin, зарегистрированным в Identity (по умолчанию http://localhost:8082).
  const target: ProxyOptions = { target: upstream, changeOrigin: false };
  return {
    '^/bff(/|\\?|$)': target,
    '^/signin-oidc(\\?|$)': target,
    '^/signout-callback-oidc(\\?|$)': target,
  };
}

export default defineConfig(({ command, mode }) => {
  const proxy = bffProxy(command, mode);

  return {
    plugins: [react()],
    server: { host: 'localhost', port: 8082, strictPort: true, proxy },
    preview: { host: 'localhost', port: 8082, strictPort: true, proxy },
    build: {
      outDir: 'dist',
      emptyOutDir: true,
      sourcemap: false,
    },
    test: {
      environment: 'jsdom',
      include: ['src/**/*.test.{ts,tsx}'],
      setupFiles: ['./src/test/setup.ts'],
      restoreMocks: true,
      unstubGlobals: true,
    },
  };
});
