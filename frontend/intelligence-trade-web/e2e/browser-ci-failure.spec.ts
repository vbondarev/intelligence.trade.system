import { expect, test } from '@playwright/test';

test('диагностический отказ браузера', () => {
  const attempt = process.env.GITHUB_RUN_ATTEMPT;
  const failedBrowser = attempt === '2' ? 'chromium' : attempt === '3' ? 'mobile-webkit' : undefined;
  const browser = test.info().project.name;
  expect(browser, 'Диагностический отказ ' + browser).not.toBe(failedBrowser);
});
