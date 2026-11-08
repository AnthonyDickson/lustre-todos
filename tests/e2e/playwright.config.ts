import { defineConfig } from '@playwright/test';

export default defineConfig({
  testDir: './specs',
  outputDir: 'test-results',
  timeout: 30000,
  // Every test is isolated (see support/fixtures.ts), so tests can run in any
  // order and in parallel. Each worker logs in as its own E2E user, so this must
  // not exceed the number of users.
  fullyParallel: true,
  workers: 4,
  // Retries capture a trace; a test that only passes on retry still fails the run.
  retries: 1,
  failOnFlakyTests: true,
  use: {
    baseURL: process.env.BASE_URL ?? 'http://localhost:5173',
    ignoreHTTPSErrors: true,
    screenshot: 'on',
    trace: 'on-first-retry',
  },
});
