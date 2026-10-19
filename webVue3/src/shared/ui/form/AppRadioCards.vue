<script setup lang="ts" generic="T extends string">
import { RadioGroup, RadioGroupItem } from '@/shared/ui/primitives/radio-group';
import type { RadioCardOption } from './types';

/** A radio group drawn as side-by-side cards with a title and a short description. */
const model = defineModel<T | undefined>();
const props = withDefaults(
  defineProps<{
    name: string;
    options: readonly RadioCardOption<T>[];
    labelledBy: string;
    disabled?: boolean;
  }>(),
  { disabled: false },
);
</script>

<template>
  <RadioGroup
    :model-value="model"
    :name="props.name"
    :disabled="props.disabled"
    :aria-labelledby="props.labelledBy"
    class="grid-cols-2 gap-2"
    @update:model-value="model = $event as T"
  >
    <label
      v-for="option in props.options"
      :key="option.value"
      class="flex cursor-pointer items-start gap-2.5 rounded-lg border bg-white p-3"
      :class="{
        'border-primary bg-(--app-brand-light)': model === option.value,
        'cursor-not-allowed opacity-60': props.disabled,
      }"
    >
      <RadioGroupItem :value="option.value" class="mt-0.5 bg-white" />
      <span>
        <span class="block font-medium">{{ option.label }}</span>
        <span v-if="option.description" class="mt-0.5 block text-xs text-muted-foreground">
          {{ option.description }}
        </span>
      </span>
    </label>
  </RadioGroup>
</template>
