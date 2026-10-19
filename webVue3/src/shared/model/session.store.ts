import { defineStore } from 'pinia';
import { computed, ref } from 'vue';
import { decodeJwt, isJwtExpired, type UserRole } from './jwt';
import { readStored, writeStored } from './storage';

/** Same key as the Angular portal (they run on different origins, so they never share it). */
const TOKEN_KEY = 'loyalty.portal.token';

export interface Principal {
  readonly userId: string;
  readonly role: UserRole;
  /** The tenant this user is bound to; `null` for a platform_admin. */
  readonly tenantId: string | null;
}

/**
 * Client state: who is signed in, derived from the JWT only (port of web core/auth/session.store.ts).
 * Guards, the API client and role checks read this — never a login composable.
 */
export const useSessionStore = defineStore('session', () => {
  const token = ref<string | null>(readStored(TOKEN_KEY));

  const principal = computed<Principal | null>(() => {
    const claims = token.value ? decodeJwt(token.value) : null;
    if (!claims || isJwtExpired(claims)) return null;
    return { userId: claims.sub, role: claims.role, tenantId: claims.tenant_id ?? null };
  });
  const role = computed<UserRole | null>(() => principal.value?.role ?? null);
  const isAuthenticated = computed(() => principal.value !== null);
  const isPlatformAdmin = computed(() => role.value === 'platform_admin');

  function setToken(value: string): void {
    writeStored(TOKEN_KEY, value);
    token.value = value;
  }

  function clear(): void {
    writeStored(TOKEN_KEY, null);
    token.value = null;
  }

  function hasRole(r: UserRole): boolean {
    return role.value === r;
  }

  return { token, principal, role, isAuthenticated, isPlatformAdmin, setToken, clear, hasRole };
});
