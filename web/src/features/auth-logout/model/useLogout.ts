import { useRouter } from 'vue-router';
import { useQueryClient } from '@tanstack/vue-query';
import { useSessionStore } from '@/shared/model';

/** Ends the session: drops the token and every cached server response, then goes to /login. */
export function useLogout(): { logout(): Promise<void> } {
  const session = useSessionStore();
  const queryClient = useQueryClient();
  const router = useRouter();
  return {
    async logout() {
      session.clear();
      queryClient.clear();
      await router.push('/login');
    },
  };
}
