<script setup lang="ts">
/**
 * Label + control + hint + error, wired for accessibility: pass the control the same `for` id,
 * and `hintId` / `errorId` as its `describedBy`. Errors come from VeeValidate (`errors.<field>`).
 */
const props = withDefaults(
  defineProps<{
    label: string;
    for?: string;
    hint?: string | null;
    error?: string | null;
    labelId?: string;
  }>(),
  { for: undefined, hint: null, error: null, labelId: undefined },
);
</script>

<template>
  <div class="app-field">
    <label v-if="props.for" :id="props.labelId" :for="props.for" class="app-field__label">{{
      props.label
    }}</label>
    <span v-else :id="props.labelId" class="app-field__label">{{ props.label }}</span>
    <slot />
    <p
      v-if="props.error"
      :id="props.for ? `${props.for}-error` : undefined"
      class="app-field__error"
      role="alert"
    >
      {{ props.error }}
    </p>
    <p v-else-if="props.hint" :id="props.for ? `${props.for}-hint` : undefined" class="app-field__hint">
      {{ props.hint }}
    </p>
  </div>
</template>

<style scoped>
.app-field {
  display: flex;
  flex-direction: column;
  gap: 0.375rem;
}
.app-field__label {
  font-size: 0.8125rem;
  font-weight: 500;
  color: var(--app-text-soft);
}
.app-field__hint {
  margin: 0;
  font-size: 0.75rem;
  color: var(--app-muted);
}
.app-field__error {
  margin: 0;
  font-size: 0.75rem;
  color: var(--app-danger-fg);
}
</style>
