import { defineConfig, devices } from '@playwright/test';

// This suite has no browser-specific skips: both projects are mandatory gates.
export default defineConfig({
  testDir: './security-e2e',
  workers: 1,
  retries: 0,
  forbidOnly: true,
  reporter: 'list',
  use: {
    baseURL: 'https://localtest.me:18994',
    ignoreHTTPSErrors: true, // Only the isolated fixture uses a self-signed certificate.
    trace: 'off',
    video: 'off',
    screenshot: 'off',
  },
  projects: [
    { name: 'chromium', use: { ...devices['Desktop Chrome'] } },
    { name: 'mobile-webkit', use: { ...devices['iPhone 13'] } },
  ],
});
