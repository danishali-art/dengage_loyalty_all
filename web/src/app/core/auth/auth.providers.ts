import { Provider, inject } from '@angular/core';
import { APP_CONFIG } from '../config/app-config';
import { AuthService } from './auth.service';
import { StubAuthService } from './stub-auth.service';
import { HttpAuthService } from './http-auth.service';
import { TokenStorage, LocalStorageTokenStorage } from './token-storage';

/**
 * Binds `AuthService` to the implementation named by `APP_CONFIG.authMode`.
 * Both concrete classes are registered but only the selected one is ever constructed.
 */
export function provideAuth(): Provider[] {
  return [
    { provide: TokenStorage, useClass: LocalStorageTokenStorage },
    StubAuthService,
    HttpAuthService,
    {
      provide: AuthService,
      useFactory: (): AuthService => {
        const mode = inject(APP_CONFIG).authMode;
        return mode === 'http' ? inject(HttpAuthService) : inject(StubAuthService);
      },
    },
  ];
}
