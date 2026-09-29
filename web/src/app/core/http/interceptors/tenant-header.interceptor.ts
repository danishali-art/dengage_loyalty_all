import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { TenantContext } from '../../tenant/tenant-context';

/**
 * Adds `X-Tenant-Id` for observability. The URL already carries the tenant segment
 * (see `ApiClient`) — this header is purely for cross-system tracing.
 */
export const tenantHeaderInterceptor: HttpInterceptorFn = (req, next) => {
  const tenantId = inject(TenantContext).activeTenantId();
  if (!tenantId || req.headers.has('X-Tenant-Id')) return next(req);
  return next(req.clone({ setHeaders: { 'X-Tenant-Id': tenantId } }));
};
