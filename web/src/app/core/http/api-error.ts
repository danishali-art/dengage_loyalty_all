import { HttpErrorResponse } from '@angular/common/http';

export type ApiErrorKind =
  | 'validation' // 400 — invalid_*, *_not_configured; carries `errors`
  | 'unauthorized' // 401
  | 'forbidden' // 403
  | 'not-found' // 404 — not_found
  | 'conflict' // 409 — *_exceeded, insufficient_*, unique-violation
  | 'rate-limit' // 429
  | 'server' // 5xx / unparseable
  | 'network' // no response at all
  | 'unknown';

/** RFC 7807-shaped error body the Nancy API returns. */
interface ProblemBody {
  type?: string;
  title?: string;
  status?: number;
  code?: string;
  traceId?: string;
  errors?: Record<string, string[]>;
}

/** Normalised, typed representation of any failed API call. Interceptor rethrows this. */
export class ApiError extends Error {
  readonly kind: ApiErrorKind;
  readonly status: number;
  /** `snake_case` machine code from the body, e.g. `insufficient_balance`. */
  readonly code: string | null;
  readonly traceId: string | null;
  /** Field-path -> messages, for mapping back onto form controls. */
  readonly fieldErrors: Readonly<Record<string, string[]>>;
  /** Seconds to wait, from `Retry-After`, when `kind === 'rate-limit'`. */
  readonly retryAfterSeconds: number | null;

  constructor(init: {
    kind: ApiErrorKind;
    status: number;
    title: string;
    code?: string | null;
    traceId?: string | null;
    fieldErrors?: Record<string, string[]>;
    retryAfterSeconds?: number | null;
  }) {
    super(init.title);
    this.name = 'ApiError';
    this.kind = init.kind;
    this.status = init.status;
    this.code = init.code ?? null;
    this.traceId = init.traceId ?? null;
    this.fieldErrors = init.fieldErrors ?? {};
    this.retryAfterSeconds = init.retryAfterSeconds ?? null;
  }

  get isValidation(): boolean {
    return this.kind === 'validation';
  }
}

function kindForStatus(status: number, code: string | null): ApiErrorKind {
  if (status === 0) return 'network';
  if (status === 401) return 'unauthorized';
  if (status === 403) return 'forbidden';
  if (status === 404) return 'not-found';
  if (status === 429) return 'rate-limit';
  if (status >= 500) return 'server';
  // Domain conflicts sometimes arrive as 400/422 with a telltale code — classify by code first.
  if (code && /(_exceeded$|^insufficient_|_conflict$|already_exists$)/.test(code)) return 'conflict';
  if (status === 409) return 'conflict';
  if (status === 400 || status === 422) return 'validation';
  return 'unknown';
}

export function apiErrorFromHttp(err: HttpErrorResponse): ApiError {
  const body: ProblemBody | null =
    err.error && typeof err.error === 'object' ? (err.error as ProblemBody) : null;

  const status = err.status ?? 0;
  const code = body?.code ?? null;
  const kind = kindForStatus(status, code);

  const retryAfterHeader = err.headers?.get?.('Retry-After');
  const retryAfterSeconds = retryAfterHeader ? Number(retryAfterHeader) || null : null;

  const title =
    body?.title ??
    (status === 0
      ? 'Cannot reach the server. Check your connection and try again.'
      : err.message || `Request failed (${status})`);

  return new ApiError({
    kind,
    status,
    title,
    code,
    traceId: body?.traceId ?? null,
    fieldErrors: body?.errors ?? {},
    retryAfterSeconds,
  });
}
