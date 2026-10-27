<script setup lang="ts">
import { Badge } from '@/shared/ui/primitives/badge';
import type { StatusTone } from './types';

/** Small labelled status chip. The text is always rendered — status is never colour-only. */
const props = withDefaults(defineProps<{ tone?: StatusTone; dot?: boolean }>(), {
  tone: 'neutral',
  dot: false,
});

const TONE: Record<StatusTone, string> = {
  success: 'border-(--app-success-border) bg-(--app-success-bg) text-(--app-success-fg)',
  neutral: 'border-gray-200 bg-gray-100 text-(--app-text-soft)',
  warning: 'border-(--app-warn-border) bg-(--app-warn-bg) text-(--app-warn-fg)',
  danger: 'border-(--app-danger-border) bg-(--app-danger-bg) text-(--app-danger-fg)',
  info: 'border-blue-100 bg-(--app-brand-light) text-primary',
  streak: 'border-(--app-streak-border) bg-(--app-streak-bg) text-(--app-streak-fg)',
};
</script>

<template>
  <Badge variant="outline" class="rounded-full font-medium" :class="TONE[props.tone]">
    <span v-if="props.dot" class="size-1.5 rounded-full bg-current" aria-hidden="true" />
    <slot />
  </Badge>
</template>
