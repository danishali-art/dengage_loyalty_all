<script setup lang="ts">
import { Switch } from '@/shared/ui/primitives/switch';

const model = defineModel<boolean>({ default: false });

const props = withDefaults(
  defineProps<{ id?: string; disabled?: boolean; labelledBy?: string; describedBy?: string }>(),
  { id: undefined, disabled: false, labelledBy: undefined, describedBy: undefined },
);
</script>

<template>
  <Switch
    :id="props.id"
    v-model="model"
    :disabled="props.disabled"
    :aria-labelledby="props.labelledBy"
    :aria-describedby="props.describedBy"
  />
</template>
