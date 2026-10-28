<script setup lang="ts">
import { onMounted } from 'vue';
import { useSessionStore } from '@/shared/model';
import { AppCard } from '@/shared/ui';
import { LoginForm, useLoginForm } from '@/features/auth-login';
import { LocaleSwitch, useLocaleSwitch } from '@/features/locale-switch';

const session = useSessionStore();
const { email, password, errors, formError, pending, submit, goHome } = useLoginForm();
const { locale, options: localeOptions } = useLocaleSwitch();

// Already signed in (e.g. a bookmark to /login): skip the form.
onMounted(() => {
  if (session.isAuthenticated) void goHome();
});
</script>

<template>
  <main class="login-page">
    <div class="login-page__brand">
      <div class="login-page__logo" aria-hidden="true">L·</div>
      <h1 class="login-page__title">{{ $t('auth.login.title') }}</h1>
    </div>
    <AppCard>
      <LoginForm
        v-model:email="email"
        v-model:password="password"
        :errors="errors"
        :form-error="formError"
        :pending="pending"
        @submit="submit"
      />
    </AppCard>
    <div class="login-page__locale">
      <LocaleSwitch v-model="locale" :options="localeOptions" />
    </div>
  </main>
</template>

<style scoped>
.login-page {
  display: flex;
  flex-direction: column;
  justify-content: center;
  max-width: 24rem;
  min-height: 100vh;
  margin: 0 auto;
  padding: 0 1.5rem;
}
.login-page__brand {
  margin-bottom: 1.5rem;
  text-align: center;
}
.login-page__logo {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 2.75rem;
  height: 2.75rem;
  margin: 0 auto 0.75rem;
  border-radius: 0.75rem;
  background: var(--app-brand);
  color: white;
  font-size: 1.125rem;
  font-weight: 700;
}
.login-page__locale {
  display: flex;
  justify-content: center;
  margin-top: 1rem;
}
.login-page__title {
  margin: 0;
  font-size: 1.125rem;
  font-weight: 700;
}
</style>
