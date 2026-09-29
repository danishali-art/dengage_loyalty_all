import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, throwError } from 'rxjs';
import { ToastService } from '../../ui/toast.service';
import { SKIP_ERROR_TOAST } from '../http-context';
import { ApiError, apiErrorFromHttp } from '../api-error';

/**
 * Converts every `HttpErrorResponse` into a typed `ApiError`, and shows a toast unless the
 * caller opted out (`SKIP_ERROR_TOAST` — form submits, which map errors onto fields instead).
 * Always rethrows the `ApiError`.
 */
export const errorNormalizationInterceptor: HttpInterceptorFn = (req, next) => {
  const toast = inject(ToastService);

  return next(req).pipe(
    catchError((err: unknown) => {
      if (!(err instanceof HttpErrorResponse)) return throwError(() => err);

      const apiError = apiErrorFromHttp(err);
      if (!req.context.get(SKIP_ERROR_TOAST) && shouldToast(apiError)) {
        toast.error(apiError.message, {
          detail: apiError.code ?? undefined,
          traceId: apiError.traceId ?? undefined,
        });
      }
      return throwError(() => apiError);
    }),
  );
};

function shouldToast(err: ApiError): boolean {
  // 401 is handled by the auth interceptor (redirect); a toast on top is noise.
  return err.kind !== 'unauthorized';
}
