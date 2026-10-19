import { defineConfig, devices } from '@playwright/test';

const PORT = 4201;
const baseURL = `http://localhost:${PORT}`;

/**
 * Two kinds of spec live in `e2e/`:
 * - `*.spec.ts` answer `/api` in the browser with an in-memory mock (`e2e/mock-api.ts`), so they run
 *   without the backend.
 * - `*.e2e.spec.ts` drive the real stack (Api on :5173, plus the Consumer for the customers spec) and
 *   are skipped unless `E2E_BACKEND=1`.
 * `PW_CHANNEL` picks an installed browser (e.g. `msedge`) instead of Playwright's own Chromium.
 */
export default defineConfig({
  testDir: './e2e',
  fullyParallel: true,
  forbidOnly: !!process.env['CI'],
  retries: process.env['CI'] ? 2 : 0,
  reporter: process.env['CI'] ? [['github'], ['html', { open: 'never' }]] : [['list']],
  use: {
    baseURL,
    trace: 'on-first-retry',
    screenshot: 'only-on-failure',
  },
  projects: [
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'], channel: process.env['PW_CHANNEL'] || undefined },
    },
  ],
  webServer: {
    command: `npm run dev -- --port ${PORT} --strictPort`,
    url: baseURL,
    reuseExistingServer: !process.env['CI'],
    timeout: 180_000,
    stdout: 'ignore',
    stderr: 'pipe',
  },
});
