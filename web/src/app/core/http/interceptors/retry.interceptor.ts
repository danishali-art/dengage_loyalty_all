import { HttpInterceptorFn } from '@angular/common/http';
import { retry, timer } from 'rxjs';
import { RETRYABLE } from '../http-context';

const TRANSIENT = new Set([0, 502, 503, 504]);

/** One retry with backoff for opted-in idempotent GETs. Never retries 409 / 429. */
export const retryInterceptor: HttpInterceptorFn = (req, next) => {
  if (req.method !== 'GET' || !req.context.get(RETRYABLE)) return next(req);

  return next(req).pipe(
    retry({
      count: 1,
      delay: (error: unknown, retryCount) => {
        const status =
          typeof error === 'object' && error !== null && 'status' in error
            ? Number((error as { status: number }).status)
            : -1;
        if (!TRANSIENT.has(status)) throw error;
        return timer(300 * retryCount + Math.random() * 200);
      },
    }),
  );
};
