import { HttpInterceptorFn } from '@angular/common/http';
import { correlationIdInterceptor } from './correlation-id.interceptor';
import { authInterceptor } from './auth.interceptor';
import { tenantHeaderInterceptor } from './tenant-header.interceptor';
import { retryInterceptor } from './retry.interceptor';
import { errorNormalizationInterceptor } from './error-normalization.interceptor';
import { loadingInterceptor } from './loading.interceptor';

/** Registration order matters: correlation id first, error normalisation before loading cleanup. */
export const httpInterceptors: HttpInterceptorFn[] = [
  correlationIdInterceptor,
  authInterceptor,
  tenantHeaderInterceptor,
  retryInterceptor,
  errorNormalizationInterceptor,
  loadingInterceptor,
];
