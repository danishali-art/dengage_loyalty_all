<script setup lang="ts">
import { AppFormErrors, AppFormField, AppInput, AppTextarea } from '@/shared/ui';

/** Presentational "New program" fields. The dialog around it is the page's. */
const name = defineModel<string | undefined>('name', { required: true });
const slug = defineModel<string | undefined>('slug', { required: true });
const description = defineModel<string | undefined>('description', { required: true });
const props = defineProps<{
  errors: Partial<Record<'name' | 'slug' | 'description', string>>;
  formErrors: readonly string[];
}>();
const emit = defineEmits<{ submit: [] }>();
</script>

<template>
  <form id="create-program-form" class="program-form" novalidate @submit.prevent="emit('submit')">
    <AppFormErrors :messages="props.formErrors" />
    <AppFormField :label="$t('programs.form.name')" for="program-name" :error="props.errors.name">
      <AppInput id="program-name" v-model="name" :invalid="!!props.errors.name" />
    </AppFormField>
    <!-- CR 2026-09-30 (A5): prefixes reward names; derived from the name when left empty. -->
    <AppFormField
      :label="$t('programs.slug.label')"
      for="program-slug"
      :hint="$t('programs.slug.createHint')"
      :error="props.errors.slug"
    >
      <AppInput
        id="program-slug"
        v-model="slug"
        monospace
        :invalid="!!props.errors.slug"
        :described-by="props.errors.slug ? 'program-slug-error' : 'program-slug-hint'"
      />
    </AppFormField>
    <AppFormField :label="$t('programs.form.description')" for="program-description">
      <AppTextarea id="program-description" v-model="description" />
    </AppFormField>
  </form>
</template>

<style scoped>
.program-form {
  display: flex;
  flex-direction: column;
  gap: 1rem;
}
</style>
