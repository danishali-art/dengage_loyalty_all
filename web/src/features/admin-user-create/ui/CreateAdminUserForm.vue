<script setup lang="ts">
import { AppFormErrors, AppFormField, AppInput } from '@/shared/ui';

/** Presentational "New tenant admin" fields. The dialog around it is the page's. */
const email = defineModel<string | undefined>('email', { required: true });
const password = defineModel<string | undefined>('password', { required: true });
const props = defineProps<{
  tenantId: string;
  errors: Partial<Record<'email' | 'password', string>>;
  formErrors: readonly string[];
}>();
const emit = defineEmits<{ submit: [] }>();
</script>

<template>
  <form class="flex flex-col gap-4" novalidate @submit.prevent="emit('submit')">
    <AppFormErrors :messages="props.formErrors" />
    <p class="m-0 text-sm text-muted-foreground">
      {{ $t('platform.admins.form.grants', { tenant: props.tenantId }) }}
    </p>
    <AppFormField :label="$t('platform.admins.form.email')" for="admin-email" :error="props.errors.email">
      <AppInput id="admin-email" v-model="email" type="email" :invalid="!!props.errors.email" />
    </AppFormField>
    <AppFormField
      :label="$t('platform.admins.form.password')"
      for="admin-password"
      :hint="$t('platform.admins.form.passwordHint')"
      :error="props.errors.password"
    >
      <AppInput
        id="admin-password"
        v-model="password"
        :invalid="!!props.errors.password"
        :described-by="props.errors.password ? 'admin-password-error' : 'admin-password-hint'"
      />
    </AppFormField>
  </form>
</template>
