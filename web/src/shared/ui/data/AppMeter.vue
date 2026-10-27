<script setup lang="ts">
import { computed } from 'vue';
import { meterPercent } from './meter';

/**
 * Determinate bar ("120 of 500"). The caller supplies the label,
 * which is always rendered — the bar is never the only way to read the value. Turns to the
 * warning tone when full.
 */
const props = withDefaults(
  defineProps<{ value: string | number; max?: string | number | null; label: string }>(),
  { max: null },
);
const percent = computed(() => meterPercent(props.value, props.max));
</script>

<template>
  <div
    role="progressbar"
    aria-valuemin="0"
    aria-valuemax="100"
    :aria-valuenow="Math.round(percent)"
    :aria-label="props.label"
  >
    <div class="h-2 w-full overflow-hidden rounded-full bg-gray-100">
      <div
        class="h-full rounded-full"
        :class="percent >= 100 ? 'bg-(--app-warn-fg)' : 'bg-primary'"
        :style="{ width: `${percent}%` }"
      />
    </div>
    <p class="mt-1 mb-0 text-xs text-muted-foreground">{{ props.label }}</p>
  </div>
</template>
