import { Observable } from 'rxjs';

export interface LoginCredentials {
  email: string;
  password: string;
}

export interface LoginResult {
  token: string;
}

/**
 * The seam between the app and "how a user signs in". The rest of the app depends on
 * this abstract type + `SessionStore`; the concrete implementation (stub / http) is
 * selected at bootstrap from `APP_CONFIG.authMode`.
 */
export abstract class AuthService {
  abstract login(credentials: LoginCredentials): Observable<LoginResult>;
  abstract logout(): void;

  /**
   * Called by the auth interceptor on a 401. Implementations decide whether to attempt
   * a refresh (none available today) or simply drop the session.
   */
  abstract onUnauthorized(): void;

  /** True when this implementation has a real login screen (drives router redirects). */
  abstract readonly interactive: boolean;
}
