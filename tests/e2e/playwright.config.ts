import { defineConfig } from '@playwright/test';

export default defineConfig({
  testDir: './specs',
  outputDir: 'test-results',
  timeout: 30000,
  // Each worker logs in as its own E2E user (see support/fixtures.ts), so this
  // must not exceed the number of users.
  workers: 4,
  retries: 1,
  use: {
    baseURL: process.env.BASE_URL ?? 'http://localhost:5173',
    ignoreHTTPSErrors: true,
    screenshot: 'on',
    trace: 'on-first-retry',
  },
});
