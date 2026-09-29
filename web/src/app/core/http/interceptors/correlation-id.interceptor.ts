import { HttpInterceptorFn } from '@angular/common/http';

/** Stamp every request with a client-generated correlation id for cross-log tracing. */
export const correlationIdInterceptor: HttpInterceptorFn = (req, next) => {
  if (req.headers.has('X-Correlation-Id')) return next(req);
  const id =
    globalThis.crypto?.randomUUID?.() ??
    `cid-${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 10)}`;
  return next(req.clone({ setHeaders: { 'X-Correlation-Id': id } }));
};
