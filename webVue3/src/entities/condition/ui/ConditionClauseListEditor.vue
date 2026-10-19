<script setup lang="ts">
import { AppButton, AppCard, AppIcon, AppInput, AppSelect } from '@/shared/ui';
import {
  CONDITION_OPS,
  defaultValueForOp,
  type ConditionClause,
  type ConditionOp,
  type OccurredWithinValue,
} from '@/shared/lib/dsl';
import { asCsv, fromCsv } from '../model/fields';

/**
 * The flat condition-clause editor (port of conditions-editor.ts): all clauses must match. Streak
 * campaigns still use this DSL (their own table, outside CR-05's grouped tree). Presentational:
 * `v-model` the clauses.
 */
const clauses = defineModel<ConditionClause[]>({ required: true });

const opOptions = CONDITION_OPS.map((op) => ({ value: op, label: op }));
const boolOptions = [
  { value: 'true', label: 'true' },
  { value: 'false', label: 'false' },
];

function patch(index: number, change: Partial<ConditionClause>): void {
  clauses.value = clauses.value.map((c, i) => (i === index ? { ...c, ...change } : c));
}

function occurred(clause: ConditionClause): OccurredWithinValue {
  const v = clause.value;
  return typeof v === 'object' && !Array.isArray(v) && 'after_event' in v
    ? v
    : { after_event: '', hours: 24 };
}

function patchOccurred(index: number, key: 'after_event' | 'hours', raw: string): void {
  const clause = clauses.value[index];
  if (!clause) return;
  const base = occurred(clause);
  patch(index, { value: key === 'hours' ? { ...base, hours: Number(raw) } : { ...base, after_event: raw } });
}

const scalar = (clause: ConditionClause): string =>
  typeof clause.value === 'object' ? '' : String(clause.value);
</script>

<template>
  <AppCard :heading="$t('conditions.clauses.heading')">
    <div class="flex flex-col gap-3">
      <div
        v-for="(clause, i) in clauses"
        :key="i"
        class="grid grid-cols-[1fr_auto_1fr_auto] items-start gap-2"
      >
        <AppInput
          :model-value="clause.field"
          :aria-label="$t('conditions.field')"
          :placeholder="$t('conditions.clauses.fieldPlaceholder')"
          @update:model-value="patch(i, { field: $event })"
        />
        <div class="w-40">
          <AppSelect
            :model-value="clause.op"
            :options="opOptions"
            searchable
            :aria-label="$t('conditions.operator')"
            @update:model-value="
              $event &&
              patch(i, { op: $event as ConditionOp, value: defaultValueForOp($event as ConditionOp) })
            "
          />
        </div>
        <AppSelect
          v-if="clause.op === 'exists'"
          :model-value="clause.value === false ? 'false' : 'true'"
          :options="boolOptions"
          :aria-label="$t('conditions.value')"
          @update:model-value="patch(i, { value: $event === 'true' })"
        />
        <AppInput
          v-else-if="clause.op === 'in'"
          :model-value="asCsv(clause.value)"
          :aria-label="$t('conditions.value')"
          :placeholder="$t('conditions.csv')"
          @update:model-value="patch(i, { value: fromCsv($event) })"
        />
        <div v-else-if="clause.op === 'occurred_within'" class="flex gap-2">
          <AppInput
            :model-value="occurred(clause).after_event"
            :aria-label="$t('conditions.clauses.afterEvent')"
            :placeholder="$t('conditions.clauses.afterEvent')"
            @update:model-value="patchOccurred(i, 'after_event', $event)"
          />
          <AppInput
            :model-value="String(occurred(clause).hours)"
            inputmode="decimal"
            :aria-label="$t('conditions.clauses.hours')"
            :placeholder="$t('conditions.clauses.hours')"
            @update:model-value="patchOccurred(i, 'hours', $event)"
          />
        </div>
        <AppInput
          v-else
          :model-value="scalar(clause)"
          :aria-label="$t('conditions.value')"
          @update:model-value="patch(i, { value: $event })"
        />
        <AppButton
          variant="text"
          size="sm"
          :aria-label="$t('conditions.remove')"
          @click="clauses = clauses.filter((_, j) => j !== i)"
        >
          <AppIcon name="trash" />
        </AppButton>
      </div>
      <div>
        <AppButton
          size="sm"
          variant="secondary"
          @click="clauses = [...clauses, { field: '', op: 'eq', value: defaultValueForOp('eq') }]"
        >
          {{ $t('conditions.add') }}
        </AppButton>
      </div>
    </div>
  </AppCard>
</template>
