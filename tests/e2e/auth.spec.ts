import { test, expect } from '@playwright/test';

/// The ASP.NET Core auth cookie's default name.
const sessionCookie = '.AspNetCore.Cookies';

test.describe('auth', () => {
  test('logging out posts to /logout, which clears the session cookie', async ({ page }) => {
    await page.goto('/');
    await expect(page.locator('[data-testid="new-todo-input"]')).toBeVisible();

    const logout = page.waitForResponse(
      response => new URL(response.url()).pathname === '/logout',
    );
    await page.click('[data-testid="logout"]');
    const response = await logout;

    // The provider session outlives the app's, so the app signs straight back
    // in after the redirect; the logout response itself shows the sign-out.
    expect(response.request().method()).toBe('POST');
    expect(response.status()).toBe(302);
    const setCookie = (await response.headersArray())
      .filter(header => header.name.toLowerCase() === 'set-cookie')
      .map(header => header.value);
    expect(setCookie.some(value => value.startsWith(`${sessionCookie}=;`))).toBe(true);
  });

  test('a link to /logout does not log the user out', async ({ page }) => {
    const response = await page.request.get('/logout', { maxRedirects: 0 });

    expect(response.status()).toBe(405);
    const cookies = await page.context().cookies();
    expect(cookies.some(cookie => cookie.name.startsWith(sessionCookie))).toBe(true);
  });
});
