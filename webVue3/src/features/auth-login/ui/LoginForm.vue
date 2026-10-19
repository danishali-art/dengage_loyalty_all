<script setup lang="ts">
import { AppButton, AppFormField, AppInput } from '@/shared/ui';

/** Presentational: values in via v-model, errors and pending in, `submit` out. */
const email = defineModel<string | undefined>('email', { required: true });
const password = defineModel<string | undefined>('password', { required: true });
const props = defineProps<{
  errors: Partial<Record<'email' | 'password', string>>;
  formError: string | null;
  pending: boolean;
}>();
const emit = defineEmits<{ submit: [] }>();
</script>

<template>
  <form class="login-form" novalidate @submit.prevent="emit('submit')">
    <AppFormField :label="$t('auth.login.email')" for="email" :error="props.errors.email">
      <AppInput
        id="email"
        v-model="email"
        type="email"
        autocomplete="username"
        :invalid="!!props.errors.email"
        :described-by="props.errors.email ? 'email-error' : undefined"
      />
    </AppFormField>
    <AppFormField :label="$t('auth.login.password')" for="password" :error="props.errors.password">
      <AppInput
        id="password"
        v-model="password"
        type="password"
        autocomplete="current-password"
        :invalid="!!props.errors.password"
        :described-by="props.errors.password ? 'password-error' : undefined"
      />
    </AppFormField>
    <p v-if="props.formError" class="login-form__error" role="alert">{{ props.formError }}</p>
    <AppButton type="submit" block :pending="props.pending">{{ $t('auth.login.submit') }}</AppButton>
  </form>
</template>

<style scoped>
.login-form {
  display: flex;
  flex-direction: column;
  gap: 1rem;
}
.login-form__error {
  margin: 0;
  font-size: 0.8125rem;
  color: var(--app-danger-fg);
}
</style>
