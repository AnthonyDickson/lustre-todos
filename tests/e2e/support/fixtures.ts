import fs from 'node:fs';
import path from 'node:path';

import {
  type Browser,
  expect,
  type Page,
  test as base,
} from '@playwright/test';

/// The number of Authelia users `e2e-0` … `e2e-<n-1>` in `authelia/users.yml`.
/// Each parallel worker logs in as its own user, so this must be at least the
/// `workers` setting in `playwright.config.ts`.
const userCount = 4;

/// The Authelia user the worker at `parallelIndex` logs in as.
export function workerUser(parallelIndex: number): string {
  return `e2e-${parallelIndex}`;
}

/// Log in as `username` through the SPA's OIDC redirect to Authelia, ending
/// back on the todo page that asked for it.
export async function logIn(page: Page, baseURL: string, username: string) {
  await page.goto('/');

  // SPA detects 401, redirects to /login, OIDC challenge lands on Authelia
  await page.waitForURL('**127.0.0.1:9091**');

  // Wait for MUI login form to hydrate
  await page.waitForSelector('#username-textfield', { state: 'visible' });
  await page.waitForSelector('#password-textfield', { state: 'visible' });
  await page.waitForTimeout(500);

  // Use click + pressSequentially for MUI controlled inputs
  const usernameInput = page.locator('#username-textfield');
  await usernameInput.click();
  await usernameInput.pressSequentially(username, { delay: 50 });
  const passwordInput = page.locator('#password-textfield');
  await passwordInput.click();
  await passwordInput.pressSequentially('dev-password', { delay: 50 });
  await page.click('#sign-in-button');

  await page.waitForURL(`${baseURL}/**`, { timeout: 15000 });
}

/// Log in as `username` and save the resulting session to `storagePath`.
async function saveLogIn(
  browser: Browser,
  baseURL: string,
  username: string,
  storagePath: string,
) {
  const context = await browser.newContext({
    baseURL,
    ignoreHTTPSErrors: true,
  });
  const page = await context.newPage();

  try {
    await logIn(page, baseURL, username);
    await context.storageState({ path: storagePath });
  } catch (e) {
    await page.screenshot({
      path: storagePath.replace(/\.json$/, '-login-failure.png'),
      fullPage: true,
    });
    throw e;
  } finally {
    await context.close();
  }
}

/// The suite's `test`. Specs import `test` and `expect` from here, not from
/// `@playwright/test`, so every test is isolated:
///
/// - Each parallel worker logs in as its own user, and the server scopes all
///   data by user, so workers never see each other's data.
/// - Each test starts by deleting its user's data, so tests in the same worker
///   (and retries) never see each other's data either.
export const test = base.extend<
  { resetUserData: void },
  { workerStorageState: string }
>({
  storageState: ({ workerStorageState }, use) => use(workerStorageState),

  workerStorageState: [
    async ({ browser }, use, workerInfo) => {
      // `parallelIndex` is reused by a worker that restarts after a failure,
      // unlike `workerIndex`, so it maps to the same user and saved session.
      const index = workerInfo.parallelIndex;
      if (index >= userCount) {
        throw new Error(
          `Worker ${index} has no E2E user: add users to authelia/users.yml and raise userCount, or lower workers`,
        );
      }

      const storagePath = path.resolve(
        workerInfo.project.outputDir,
        '.auth',
        `${index}.json`,
      );
      if (!fs.existsSync(storagePath)) {
        fs.mkdirSync(path.dirname(storagePath), { recursive: true });
        await saveLogIn(
          browser,
          workerInfo.project.use.baseURL!,
          workerUser(index),
          storagePath,
        );
      }
      await use(storagePath);
    },
    { scope: 'worker' },
  ],

  resetUserData: [
    async ({ page }, use) => {
      // A dev-only endpoint (registered only in Development) that deletes all
      // of the current user's data.
      const response = await page.request.delete('/api/test/user-data');
      expect(response.status()).toBe(204);
      await use();
    },
    { auto: true },
  ],
});

export { expect };
