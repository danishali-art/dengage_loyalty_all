<script setup lang="ts">
import { Textarea } from '@/shared/ui/primitives/textarea';

const model = defineModel<string>({ default: '' });

const props = withDefaults(
  defineProps<{ id?: string; rows?: number; invalid?: boolean; describedBy?: string }>(),
  {
    id: undefined,
    rows: 3,
    invalid: false,
    describedBy: undefined,
  },
);

const emit = defineEmits<{ blur: [] }>();
</script>

<template>
  <Textarea
    :id="props.id"
    v-model="model"
    :rows="props.rows"
    :aria-describedby="props.describedBy"
    :aria-invalid="props.invalid || undefined"
    class="field-sizing-fixed resize-none bg-white"
    @blur="emit('blur')"
  />
</template>
