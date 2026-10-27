<script setup lang="ts">
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';
import { AppButton, AppIcon, AppInput, AppSelect } from '@/shared/ui';
import type { ConditionLeaf, ConditionLeafOp } from '@/shared/lib/dsl';
import {
  asCsv,
  betweenBound,
  fromCsv,
  isDeclared,
  kindFor,
  opsFor,
  withBetweenBound,
  withOperator,
  withValue,
  type FieldKinds,
} from '../model/fields';

/**
 * One grouped-DSL condition (field, operator, value), shared by the tree and the flat list
 * editors. Presentational: emits the edited leaf.
 */
const props = withDefaults(
  defineProps<{ leaf: ConditionLeaf; kinds: FieldKinds; removable?: boolean; idPrefix: string }>(),
  { removable: true },
);
const emit = defineEmits<{ change: [leaf: ConditionLeaf]; remove: [] }>();
const { t } = useI18n();

const operatorOptions = computed(() =>
  opsFor(props.kinds, props.leaf.field).map((op) => ({ value: op, label: op })),
);
const existsOptions = computed(() => [
  { value: 'true', label: t('conditions.present') },
  { value: 'false', label: t('conditions.absent') },
]);
const kind = computed(() => kindFor(props.kinds, props.leaf.field));
const scalar = computed(() =>
  props.leaf.value.data === null || props.leaf.value.data === undefined ? '' : String(props.leaf.value.data),
);

const change = (leaf: ConditionLeaf): void => emit('change', leaf);
</script>

<template>
  <div class="grid grid-cols-[1.7fr_0.9fr_1.6fr_auto] items-start gap-2">
    <div>
      <AppInput
        :id="`${props.idPrefix}-field`"
        :model-value="props.leaf.field"
        :aria-label="$t('conditions.field')"
        :placeholder="$t('conditions.fieldPlaceholder')"
        monospace
        :described-by="isDeclared(props.kinds, props.leaf.field) ? undefined : `${props.idPrefix}-undeclared`"
        @update:model-value="change({ ...props.leaf, field: $event })"
      />
      <p
        v-if="!isDeclared(props.kinds, props.leaf.field)"
        :id="`${props.idPrefix}-undeclared`"
        class="mt-1 text-xs text-amber-600"
      >
        {{ $t('conditions.undeclared') }}
      </p>
    </div>
    <AppSelect
      :model-value="props.leaf.operator"
      :options="operatorOptions"
      :aria-label="$t('conditions.operator')"
      @update:model-value="$event && change(withOperator(props.leaf, $event as ConditionLeafOp))"
    />
    <AppSelect
      v-if="props.leaf.operator === 'exists'"
      :model-value="props.leaf.value.data === false ? 'false' : 'true'"
      :options="existsOptions"
      :aria-label="$t('conditions.value')"
      @update:model-value="change(withValue(props.leaf, { ...props.leaf.value, data: $event === 'true' }))"
    />
    <div
      v-else-if="props.leaf.operator === 'is_null'"
      class="flex h-9 items-center rounded-md border px-3 text-xs text-muted-foreground"
    >
      {{ $t('conditions.noValue') }}
    </div>
    <AppInput
      v-else-if="props.leaf.operator === 'in' || props.leaf.operator === 'not_in'"
      :model-value="asCsv(props.leaf.value.data)"
      :aria-label="$t('conditions.value')"
      :placeholder="$t('conditions.csv')"
      @update:model-value="
        change(withValue(props.leaf, { ...props.leaf.value, type: 'string[]', data: fromCsv($event) }))
      "
    />
    <div v-else-if="props.leaf.operator === 'between'" class="flex gap-1.5">
      <AppInput
        :model-value="String(betweenBound(props.leaf.value.data, 0))"
        inputmode="decimal"
        :aria-label="$t('conditions.min')"
        :placeholder="$t('conditions.min')"
        @update:model-value="change(withBetweenBound(props.leaf, 0, $event))"
      />
      <AppInput
        :model-value="String(betweenBound(props.leaf.value.data, 1))"
        inputmode="decimal"
        :aria-label="$t('conditions.max')"
        :placeholder="$t('conditions.max')"
        @update:model-value="change(withBetweenBound(props.leaf, 1, $event))"
      />
    </div>
    <AppInput
      v-else
      :model-value="scalar"
      :inputmode="kind === 'string' ? 'text' : 'decimal'"
      :aria-label="$t('conditions.value')"
      @update:model-value="change(withValue(props.leaf, { ...props.leaf.value, type: kind, data: $event }))"
    />
    <AppButton
      variant="text"
      size="sm"
      :aria-label="$t('conditions.remove')"
      :disabled="!props.removable"
      @click="emit('remove')"
    >
      <AppIcon name="close" />
    </AppButton>
  </div>
</template>
