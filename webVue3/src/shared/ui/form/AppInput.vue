<script setup lang="ts">
import { Input } from '@/shared/ui/primitives/input';

const model = defineModel<string>({ default: '' });

const props = withDefaults(
  defineProps<{
    id?: string;
    /** `datetime-local` and `date` values are plain wall-clock text; callers convert to UTC (shared/lib/date). */
    type?: 'text' | 'search' | 'email' | 'password' | 'datetime-local' | 'date';
    placeholder?: string;
    invalid?: boolean;
    readonly?: boolean;
    disabled?: boolean;
    autocomplete?: string;
    monospace?: boolean;
    describedBy?: string;
    ariaLabel?: string;
    /** Numeric fields stay text inputs (values remain strings, e.g. decimal strings) with a numeric keyboard. */
    inputmode?: 'text' | 'numeric' | 'decimal';
  }>(),
  {
    id: undefined,
    type: 'text',
    placeholder: undefined,
    invalid: false,
    readonly: false,
    disabled: false,
    autocomplete: 'off',
    monospace: false,
    describedBy: undefined,
    ariaLabel: undefined,
    inputmode: 'text',
  },
);

const emit = defineEmits<{ blur: [] }>();
</script>

<template>
  <Input
    :id="props.id"
    v-model="model"
    :type="props.type"
    :placeholder="props.placeholder"
    :readonly="props.readonly"
    :disabled="props.disabled"
    :autocomplete="props.autocomplete"
    :aria-describedby="props.describedBy"
    :aria-label="props.ariaLabel"
    :inputmode="props.inputmode"
    :aria-invalid="props.invalid || undefined"
    class="bg-white"
    :class="{ 'font-mono text-[0.8125rem]': props.monospace }"
    @blur="emit('blur')"
  />
</template>
