import { expect, test } from '../support/fixtures';

/// The ASP.NET Core auth cookie's default name.
const sessionCookie = '.AspNetCore.Cookies';

test.describe('auth', () => {
  test('logging out posts to /logout, which clears the session cookie', async ({ page }) => {
    // Stand in for the provider's logout page, so the worker's provider session,
    // which later tests log in again through, survives.
    await page.route('https://127.0.0.1:9091/logout', route => route.fulfill({ body: 'Signed out' }));

    await page.goto('/');
    await expect(page.locator('[data-testid="new-todo-input"]')).toBeVisible();

    const logout = page.waitForResponse(
      response => new URL(response.url()).pathname === '/logout',
    );
    await page.click('[data-testid="logout"]');
    const response = await logout;

    // The redirect goes on to the provider's logout page (Oidc:LogoutUrl in the
    // development settings), so the logout response itself shows the sign-out.
    expect(response.request().method()).toBe('POST');
    expect(response.status()).toBe(302);
    expect(response.headers()['location']).toBe('https://127.0.0.1:9091/logout');
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

  test('the signed out page makes no API request', async ({ page }) => {
    const apiRequests: string[] = [];
    page.on('request', request => {
      if (new URL(request.url()).pathname.startsWith('/api/')) apiRequests.push(request.url());
    });

    await page.goto('/signed-out');

    await expect(page.getByTestId('signed-out')).toBeVisible();
    expect(apiRequests).toEqual([]);
  });

  test('an expired session logs in again and returns to the page', async ({ page }) => {
    // Drop the app's session but keep the provider's, as when the app's cookie
    // expires first.
    await page.context().clearCookies({ name: /^\.AspNetCore\.Cookies/ });

    const login = page.waitForRequest(request => new URL(request.url()).pathname === '/login');
    await page.goto('/active');

    expect(new URL((await login).url()).searchParams.get('returnUrl')).toBe('/active');
    await expect(page).toHaveURL(/\/active$/);
    await expect(page.getByTestId('new-todo-input')).toBeEnabled();
  });
});
