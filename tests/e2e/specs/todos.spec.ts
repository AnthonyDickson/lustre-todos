import { expect, test } from '../support/fixtures';
import { screenshotPath } from '../support/screenshot';

test.describe('todos', () => {
  test('shows empty list on first load', async ({ page }) => {
    await page.goto('/');

    // The input is enabled once the list has loaded; before then, no items
    // show either way.
    await expect(page.getByTestId('new-todo-input')).toBeEnabled();
    await expect(page.locator('[data-testid="todo-item"]')).toHaveCount(0);
  });

  test('create a new todo', async ({ page }, testInfo) => {
    await page.goto('/');

    await page.fill('[data-testid="new-todo-input"]', 'Buy milk');
    await page.press('[data-testid="new-todo-input"]', 'Enter');

    await expect(page.locator('[data-testid="todo-item"]')).toHaveCount(1);
    await expect(page.locator('[data-testid="todo-title"]')).toHaveText(
      'Buy milk',
    );
    await page
      .getByRole('main')
      .screenshot({ path: screenshotPath(testInfo, 'todo-created') });
  });

  test('shows a not found page for an unknown path', async ({ page }) => {
    await page.goto('/no/such/page');

    await expect(page.getByTestId('not-found')).toBeVisible();
  });
});
