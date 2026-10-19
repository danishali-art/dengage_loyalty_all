<script setup lang="ts">
import { Checkbox } from '@/shared/ui/primitives/checkbox';

/** A labelled checkbox. The label is the default slot. */
const model = defineModel<boolean>({ default: false });
const props = withDefaults(defineProps<{ id: string; disabled?: boolean; describedBy?: string }>(), {
  disabled: false,
  describedBy: undefined,
});
</script>

<template>
  <div class="flex items-center gap-2">
    <Checkbox
      :id="props.id"
      :model-value="model"
      :disabled="props.disabled"
      :aria-describedby="props.describedBy"
      class="bg-white"
      @update:model-value="model = $event === true"
    />
    <label :for="props.id" class="cursor-pointer text-(--app-text-soft) peer-disabled:cursor-not-allowed">
      <slot />
    </label>
  </div>
</template>
