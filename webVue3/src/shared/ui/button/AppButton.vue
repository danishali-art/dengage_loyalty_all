<script setup lang="ts">
import { Button } from '@/shared/ui/primitives/button';
import AppIcon from '../icon/AppIcon.vue';

/** The app's button. Callers never see the primitive's props — only these. */
const props = withDefaults(
  defineProps<{
    variant?: 'primary' | 'secondary' | 'danger' | 'text';
    size?: 'sm' | 'md';
    type?: 'button' | 'submit';
    pending?: boolean;
    disabled?: boolean;
    block?: boolean;
    title?: string;
    /** Required for an icon-only button. */
    ariaLabel?: string;
  }>(),
  {
    variant: 'primary',
    size: 'md',
    type: 'button',
    pending: false,
    disabled: false,
    block: false,
    title: undefined,
    ariaLabel: undefined,
  },
);

const emit = defineEmits<{ click: [event: MouseEvent] }>();

const VARIANT = { primary: 'default', secondary: 'outline', danger: 'destructive', text: 'ghost' } as const;
</script>

<template>
  <Button
    :type="props.type"
    :variant="VARIANT[props.variant]"
    :size="props.size === 'sm' ? 'sm' : 'default'"
    :disabled="props.disabled || props.pending"
    :aria-busy="props.pending || undefined"
    :title="props.title"
    :aria-label="props.ariaLabel"
    :class="{ 'w-full': props.block, 'text-primary': props.variant === 'text' }"
    @click="emit('click', $event)"
  >
    <AppIcon v-if="props.pending" name="loader" spin />
    <slot />
  </Button>
</template>
