<script setup lang="ts">
import type { AppLocale } from '@/shared/i18n';
import { AppSelect, type SelectOption } from '@/shared/ui';

/** The language picker shown where there's no user menu (the login page). */
const model = defineModel<AppLocale>({ required: true });
defineProps<{ options: readonly SelectOption<AppLocale>[] }>();
</script>

<template>
  <div class="locale-switch">
    <AppSelect
      :model-value="model"
      :options="options"
      :aria-label="$t('locale.label')"
      @update:model-value="(value) => value && (model = value)"
    />
  </div>
</template>

<style scoped>
.locale-switch {
  width: 9rem;
}
</style>
