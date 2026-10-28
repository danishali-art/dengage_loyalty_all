import { computed, ref } from 'vue';
import { useRoute, useRouter } from 'vue-router';
import { useForm } from 'vee-validate';
import { toTypedSchema } from '@vee-validate/zod';
import { useMutation } from '@tanstack/vue-query';
import { z } from 'zod';
import { translate } from '@/shared/i18n';
import { useSessionStore, useTenantContextStore } from '@/shared/model';
import { ApiError } from '@/shared/api';
import { login, type LoginRequest } from '../api/login';

/** Login form logic. The component only renders. */
export function useLoginForm() {
  const session = useSessionStore();
  const tenant = useTenantContextStore();
  const router = useRouter();
  const route = useRoute();
  const formError = ref<string | null>(null);

  // Computed so the messages follow a language switch on the login page itself (CR 2026-10-28).
  const schema = computed(() =>
    toTypedSchema(
      z.object({
        email: z.string().trim().email(translate('auth.login.errors.email')),
        password: z.string().min(1, translate('auth.login.errors.password')),
      }),
    ),
  );

  const { defineField, errors, handleSubmit } = useForm({
    validationSchema: schema,
    initialValues: { email: '', password: '' },
  });
  const [email] = defineField('email');
  const [password] = defineField('password');

  const mutation = useMutation({ mutationFn: (body: LoginRequest) => login(body) });

  function goHome(): Promise<unknown> {
    const returnUrl = typeof route.query['returnUrl'] === 'string' ? route.query['returnUrl'] : '/programs';
    return router.replace(returnUrl);
  }

  const submit = handleSubmit(async (values) => {
    formError.value = null;
    try {
      const result = await mutation.mutateAsync(values);
      session.setToken(result.token);
      // A tenant_admin's tenant is fixed by the JWT — adopt it so the tenant guard passes.
      tenant.adoptSessionTenant();
      await goHome();
    } catch (error) {
      formError.value = error instanceof ApiError ? error.message : translate('auth.login.errors.generic');
    }
  });

  return { email, password, errors, formError, pending: mutation.isPending, submit, goHome };
}
