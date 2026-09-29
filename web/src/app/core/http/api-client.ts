import { HttpClient, HttpContext, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { APP_CONFIG } from '../config/app-config';
import { TenantContext } from '../tenant/tenant-context';
import { RequestOptions, contextFrom } from './http-context';
import { CursorPage, CursorQuery, Page, PageQuery, buildHttpParams } from './pagination';

interface CallOptions extends RequestOptions {
  params?: HttpParams | Record<string, unknown>;
}

/** One REST scope (a URL prefix + verb helpers). */
class ApiScope {
  constructor(
    private readonly http: HttpClient,
    private readonly prefix: () => string,
  ) {}

  private url(path: string): string {
    const base = this.prefix().replace(/\/$/, '');
    const rel = path.replace(/^\//, '');
    return rel ? `${base}/${rel}` : base;
  }

  private opts(o: CallOptions | undefined): { context: HttpContext; params: HttpParams | undefined } {
    return {
      context: contextFrom(o),
      params: normaliseParams(o?.params),
    };
  }

  get<T>(path: string, o?: CallOptions): Observable<T> {
    return this.http.get<T>(this.url(path), this.opts(o));
  }

  getPage<T>(path: string, query: PageQuery & Record<string, unknown>, o?: CallOptions): Observable<Page<T>> {
    return this.http.get<Page<T>>(this.url(path), {
      ...this.opts(o),
      params: buildHttpParams(query),
    });
  }

  getCursor<T>(
    path: string,
    query: CursorQuery & Record<string, unknown>,
    o?: CallOptions,
  ): Observable<CursorPage<T>> {
    return this.http.get<CursorPage<T>>(this.url(path), {
      ...this.opts(o),
      params: buildHttpParams(query),
    });
  }

  post<T>(path: string, body: unknown, o?: CallOptions): Observable<T> {
    return this.http.post<T>(this.url(path), body ?? {}, this.opts(o));
  }

  patch<T>(path: string, body: unknown, o?: CallOptions): Observable<T> {
    return this.http.patch<T>(this.url(path), body ?? {}, this.opts(o));
  }

  put<T>(path: string, body: unknown, o?: CallOptions): Observable<T> {
    return this.http.put<T>(this.url(path), body ?? {}, this.opts(o));
  }

  delete<T>(path: string, o?: CallOptions): Observable<T> {
    return this.http.delete<T>(this.url(path), this.opts(o));
  }
}

function normaliseParams(p: CallOptions['params']): HttpParams | undefined {
  if (!p) return undefined;
  return p instanceof HttpParams ? p : buildHttpParams(p);
}

/**
 * Typed entry point for every API call. The tenant path segment
 * (`/tenants/{id}`) is injected here — never into the browser URL.
 */
@Injectable({ providedIn: 'root' })
export class ApiClient {
  private readonly http = inject(HttpClient);
  private readonly config = inject(APP_CONFIG);
  private readonly tenant = inject(TenantContext);

  private get base(): string {
    return this.config.apiBaseUrl.replace(/\/$/, '');
  }

  /** `/api/v1/tenants/{activeTenantId}/…` */
  readonly tenantScope = new ApiScope(this.http, () => `${this.base}/tenants/${this.tenant.require()}`);
  /** `/api/v1/platform/…` (platform_admin only) */
  readonly platform = new ApiScope(this.http, () => `${this.base}/platform`);
  /** `/api/v1/auth/…` */
  readonly auth = new ApiScope(this.http, () => `${this.base}/auth`);
}
