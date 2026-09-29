import { HttpErrorResponse, HttpHeaders } from '@angular/common/http';
import { apiErrorFromHttp } from './api-error';

function httpError(status: number, body: unknown, headers?: Record<string, string>): HttpErrorResponse {
  return new HttpErrorResponse({
    status,
    error: body,
    headers: headers ? new HttpHeaders(headers) : undefined,
  });
}

describe('apiErrorFromHttp', () => {
  it('maps 404 with not_found code to kind "not-found"', () => {
    const err = apiErrorFromHttp(httpError(404, { title: 'Missing', code: 'not_found', traceId: 't-1' }));
    expect(err.kind).toBe('not-found');
    expect(err.code).toBe('not_found');
    expect(err.traceId).toBe('t-1');
    expect(err.message).toBe('Missing');
  });

  it('maps 400 with invalid_* to a validation error carrying field errors', () => {
    const err = apiErrorFromHttp(
      httpError(400, {
        title: 'Invalid',
        code: 'invalid_condition',
        errors: { 'conditions[0].value': ['must be numeric'] },
      }),
    );
    expect(err.kind).toBe('validation');
    expect(err.isValidation).toBe(true);
    expect(err.fieldErrors['conditions[0].value']).toEqual(['must be numeric']);
  });

  it('maps 409 to conflict', () => {
    expect(apiErrorFromHttp(httpError(409, { title: 'Dup', code: 'name_conflict' })).kind).toBe('conflict');
  });

  it('treats a domain code like insufficient_balance on a 400 as a conflict', () => {
    expect(apiErrorFromHttp(httpError(400, { code: 'insufficient_balance', title: 'nope' })).kind).toBe(
      'conflict',
    );
  });

  it('reads Retry-After on a 429', () => {
    const err = apiErrorFromHttp(httpError(429, { title: 'Slow down' }, { 'Retry-After': '12' }));
    expect(err.kind).toBe('rate-limit');
    expect(err.retryAfterSeconds).toBe(12);
  });

  it('maps status 0 to a network error with a friendly message', () => {
    const err = apiErrorFromHttp(httpError(0, null));
    expect(err.kind).toBe('network');
    expect(err.message).toContain('Cannot reach the server');
  });

  it('maps 5xx to server', () => {
    expect(apiErrorFromHttp(httpError(503, 'text')).kind).toBe('server');
  });
});
