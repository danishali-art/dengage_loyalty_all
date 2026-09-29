/**
 * Build-time facts only. Anything that varies per deployment environment lives in
 * `public/app-config.json` (fetched at bootstrap), not here.
 */
export const environment = {
  production: false,
  /**
   * When true, the MSW browser worker stubs the API instead of hitting a real backend.
   * Flip to `true` for local UI work before the API exists.
   */
  useApiMock: true,
  logLevel: 'debug' as 'debug' | 'info' | 'warn' | 'error',
};
