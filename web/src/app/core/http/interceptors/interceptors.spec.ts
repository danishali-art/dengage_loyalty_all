import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';

import { correlationIdInterceptor } from './correlation-id.interceptor';
import { authInterceptor } from './auth.interceptor';
import { errorNormalizationInterceptor } from './error-normalization.interceptor';
import { loadingInterceptor } from './loading.interceptor';
import { SessionStore } from '../../auth/session.store';
import { TokenStorage, InMemoryTokenStorage } from '../../auth/token-storage';
import { AuthService } from '../../auth/auth.service';
import { ToastService } from '../../ui/toast.service';
import { LoadingService } from '../../ui/loading.service';
import { ApiError } from '../api-error';
import { contextFrom } from '../http-context';
import { makeFakeJwt } from '../../auth/stub-auth.service';

class NoopAuth extends AuthService {
  readonly interactive = false;
  onUnauthorizedCalls = 0;
  login(): never {
    throw new Error('unused');
  }
  logout(): void {
    /* noop */
  }
  onUnauthorized(): void {
    this.onUnauthorizedCalls++;
  }
}

describe('HTTP interceptors', () => {
  let http: HttpClient;
  let httpMock: HttpTestingController;
  let session: SessionStore;
  let auth: NoopAuth;
  let toast: ToastService;
  let loading: LoadingService;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(
          withInterceptors([
            correlationIdInterceptor,
            authInterceptor,
            errorNormalizationInterceptor,
            loadingInterceptor,
          ]),
        ),
        provideHttpClientTesting(),
        { provide: TokenStorage, useClass: InMemoryTokenStorage },
        { provide: AuthService, useClass: NoopAuth },
        SessionStore,
        ToastService,
        LoadingService,
      ],
    });
    http = TestBed.inject(HttpClient);
    httpMock = TestBed.inject(HttpTestingController);
    session = TestBed.inject(SessionStore);
    auth = TestBed.inject(AuthService) as NoopAuth;
    toast = TestBed.inject(ToastService);
    loading = TestBed.inject(LoadingService);
  });

  afterEach(() => httpMock.verify());

  it('adds a correlation id and (when signed in) a bearer token', () => {
    session.setToken(makeFakeJwt({ sub: 'u', role: 'tenant_admin', tenant_id: 't' }));
    http.get('/api/v1/thing').subscribe();
    const req = httpMock.expectOne('/api/v1/thing');
    expect(req.request.headers.get('X-Correlation-Id')).toBeTruthy();
    expect(req.request.headers.get('Authorization')).toMatch(/^Bearer /);
    req.flush({});
  });

  it('does not attach a token to the login request', () => {
    session.setToken(makeFakeJwt({ sub: 'u', role: 'tenant_admin', tenant_id: 't' }));
    http.post('/api/v1/auth/login', {}).subscribe();
    const req = httpMock.expectOne('/api/v1/auth/login');
    expect(req.request.headers.has('Authorization')).toBe(false);
    req.flush({ token: 'x' });
  });

  it('normalises an error to ApiError and calls onUnauthorized for a 401', async () => {
    const promise = firstValueFrom(http.get('/api/v1/secure'));
    httpMock.expectOne('/api/v1/secure').flush(
      { title: 'Nope', code: 'unauthorized' },
      { status: 401, statusText: 'Unauthorized' },
    );
    await expect(promise).rejects.toBeInstanceOf(ApiError);
    expect(auth.onUnauthorizedCalls).toBe(1);
  });

  it('shows a toast for a server error unless suppressed', async () => {
    const p1 = firstValueFrom(http.get('/api/v1/a'));
    httpMock.expectOne('/api/v1/a').flush({ title: 'Boom' }, { status: 500, statusText: 'err' });
    await expect(p1).rejects.toBeInstanceOf(ApiError);
    expect(toast.toasts().length).toBe(1);

    toast.clear();
    const p2 = firstValueFrom(http.get('/api/v1/b', { context: contextFrom({ skipErrorToast: true }) }));
    httpMock.expectOne('/api/v1/b').flush({ title: 'Boom' }, { status: 500, statusText: 'err' });
    await expect(p2).rejects.toBeInstanceOf(ApiError);
    expect(toast.toasts().length).toBe(0);
  });

  it('ref-counts the loading indicator, and skips it when asked', () => {
    http.get('/api/v1/x').subscribe();
    expect(loading.isLoading()).toBe(true);
    httpMock.expectOne('/api/v1/x').flush({});
    expect(loading.isLoading()).toBe(false);

    http.get('/api/v1/y', { context: contextFrom({ skipLoading: true }) }).subscribe();
    expect(loading.isLoading()).toBe(false);
    httpMock.expectOne('/api/v1/y').flush({});
  });
});
