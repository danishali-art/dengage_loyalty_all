import { apiClient, unwrap, withFlags, type ApiSchema } from '@/shared/api';

export type LoginRequest = ApiSchema<'LoginRequest'>;
export type LoginResponse = ApiSchema<'LoginResponse'>;

/** `POST /api/v1/auth/login` (AuthController.cs). The form shows the failure itself. */
export function login(body: LoginRequest): Promise<LoginResponse> {
  return unwrap(apiClient().POST('/api/v1/auth/login', { ...withFlags({ skipErrorToast: true }), body }));
}
