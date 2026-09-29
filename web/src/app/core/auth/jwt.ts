import { jwtDecode } from 'jwt-decode';

export type UserRole = 'platform_admin' | 'tenant_admin';

/** Claims the Nancy API puts in the access token. */
export interface JwtClaims {
  sub: string;
  role: UserRole;
  tenant_id: string | null;
  exp?: number;
  iat?: number;
  [key: string]: unknown;
}

export function decodeJwt(token: string): JwtClaims | null {
  try {
    return jwtDecode<JwtClaims>(token);
  } catch {
    return null;
  }
}

/** True when the token has no `exp`, or `exp` is within `skewSeconds` of now. */
export function isJwtExpired(claims: JwtClaims | null, skewSeconds = 30): boolean {
  if (!claims?.exp) return true;
  return claims.exp * 1000 <= Date.now() + skewSeconds * 1000;
}
