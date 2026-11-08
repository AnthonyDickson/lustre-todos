import { type TestInfo } from '@playwright/test';

/// Path for a test subject's screenshot. Playwright reuses the per-test output
/// folder across retries, so the retry number is folded into the filename to
/// keep each attempt's screenshots.
export const screenshotPath = (testInfo: TestInfo, name: string) =>
  testInfo.outputPath(
    `${name}${testInfo.retry > 0 ? `.retry-${testInfo.retry}` : ''}.png`,
  );
