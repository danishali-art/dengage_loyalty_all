import { HttpContext, HttpContextToken } from '@angular/common/http';

/** Suppress the global loading bar for this request (background polls, typeahead). */
export const SKIP_LOADING = new HttpContextToken<boolean>(() => false);

/** Suppress the automatic error toast — the caller renders the failure itself (form submits). */
export const SKIP_ERROR_TOAST = new HttpContextToken<boolean>(() => false);

/** Opt this request into a single automatic retry on transient network / 5xx failures. */
export const RETRYABLE = new HttpContextToken<boolean>(() => false);

export interface RequestOptions {
  skipLoading?: boolean;
  skipErrorToast?: boolean;
  retryable?: boolean;
}

export function contextFrom(opts: RequestOptions | undefined): HttpContext {
  const ctx = new HttpContext();
  if (opts?.skipLoading) ctx.set(SKIP_LOADING, true);
  if (opts?.skipErrorToast) ctx.set(SKIP_ERROR_TOAST, true);
  if (opts?.retryable) ctx.set(RETRYABLE, true);
  return ctx;
}
