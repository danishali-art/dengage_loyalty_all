<script setup lang="ts">
import { AppFormErrors, AppFormField, AppInput } from '@/shared/ui';

/** Presentational "New tenant" fields. The dialog around it is the page's. */
const id = defineModel<string | undefined>('id', { required: true });
const name = defineModel<string | undefined>('name', { required: true });
const props = defineProps<{
  errors: Partial<Record<'id' | 'name', string>>;
  formErrors: readonly string[];
}>();
const emit = defineEmits<{ submit: [] }>();
</script>

<template>
  <form class="flex flex-col gap-4" novalidate @submit.prevent="emit('submit')">
    <AppFormErrors :messages="props.formErrors" />
    <AppFormField
      :label="$t('platform.tenants.form.id')"
      for="tenant-id"
      :hint="$t('platform.tenants.form.idHint')"
      :error="props.errors.id"
    >
      <AppInput
        id="tenant-id"
        v-model="id"
        monospace
        :placeholder="$t('platform.tenants.form.idPlaceholder')"
        :invalid="!!props.errors.id"
        :described-by="props.errors.id ? 'tenant-id-error' : 'tenant-id-hint'"
      />
    </AppFormField>
    <AppFormField :label="$t('platform.tenants.form.name')" for="tenant-name" :error="props.errors.name">
      <AppInput id="tenant-name" v-model="name" :invalid="!!props.errors.name" />
    </AppFormField>
  </form>
</template>
