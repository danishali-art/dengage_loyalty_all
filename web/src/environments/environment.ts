/**
 * Build-time facts only. Anything that varies per deployment environment lives in
 * `public/app-config.json` (fetched at bootstrap), not here.
 */
export const environment = {
  production: true,
  /** When true, the MSW browser worker stubs the API instead of hitting a real backend. */
  useApiMock: false,
  logLevel: 'warn' as 'debug' | 'info' | 'warn' | 'error',
};
