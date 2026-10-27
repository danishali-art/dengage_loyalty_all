<script setup lang="ts">
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';
import { AppCard, AppFormField, AppInput, AppSelect } from '@/shared/ui';
import type { FieldSetter } from '@/shared/lib/forms';
import type { LimitControl, LimitField, RuleFormValues } from '@/entities/rule';
import type { RuleFormView } from '../model/useRuleForm';

/**
 * Limits, grouped by what they cap, each value checked by its kind (rule-limits.ts). A new rule
 * shows only the limits that apply to it (CR 2026-10-06 Phase 2). Presentational.
 */
const props = defineProps<{
  values: RuleFormValues;
  errors: Partial<Record<string, string>>;
  view: RuleFormView;
}>();
const emit = defineEmits<{ set: Parameters<FieldSetter<RuleFormValues>> }>();
const set: FieldSetter<RuleFormValues> = (field, value) => emit('set', field, value);
const { t } = useI18n();

const hint = (field: LimitField): string =>
  field.apiKey === 'min_event_amount' && props.view.genericTrigger
    ? t('rules.limits.minEventAmount.genericHint')
    : t(`${field.i18n}.hint`);

const periods = computed(() =>
  (['Day', 'Week', 'Month', 'Year'] as const).map((p) => ({
    value: p,
    label: t(`rules.limits.period.${p}`),
  })),
);
const resetWindows = computed(() =>
  (['Calendar', 'Rolling'] as const).map((w) => ({ value: w, label: t(`rules.limits.resetWindow.${w}`) })),
);
const onBreach = computed(() =>
  (['Clamp', 'Skip'] as const).map((b) => ({ value: b, label: t(`rules.limits.onBreach.${b}`) })),
);
const value = (control: LimitControl): string => props.values[control];
</script>

<template>
  <AppCard v-if="props.view.limitGroups.length > 0" :heading="$t('rules.limits.heading')">
    <div class="flex flex-col gap-5">
      <p class="m-0 text-xs text-muted-foreground">{{ $t('rules.limits.intro') }}</p>
      <fieldset v-for="group in props.view.limitGroups" :key="group.id" class="rounded-lg border p-4">
        <legend class="px-1 text-sm font-medium">{{ $t(`${group.i18n}.title`) }}</legend>
        <p class="mt-0 mb-3 text-xs text-muted-foreground">{{ $t(`${group.i18n}.hint`) }}</p>
        <div class="grid grid-cols-3 gap-3">
          <AppFormField
            v-for="field in group.fields"
            :key="field.control"
            :label="$t(`${field.i18n}.label`)"
            :for="field.control"
            :error="props.errors[field.control] ?? null"
            :hint="hint(field)"
          >
            <div class="relative">
              <AppInput
                :id="field.control"
                class="pr-16"
                :inputmode="field.kind === 'count' ? 'numeric' : 'decimal'"
                :placeholder="$t('rules.limits.none')"
                :model-value="value(field.control)"
                :invalid="!!props.errors[field.control]"
                :described-by="
                  props.errors[field.control] ? `${field.control}-error` : `${field.control}-hint`
                "
                @update:model-value="set(field.control, $event)"
              />
              <span
                class="pointer-events-none absolute inset-y-0 right-3 flex items-center text-xs text-muted-foreground"
                aria-hidden="true"
                >{{ $t(`rules.limits.units.${field.kind}`) }}</span
              >
            </div>
          </AppFormField>
        </div>
      </fieldset>

      <!-- Only shown when a per-period cap is set — otherwise the engine would silently use "Day". -->
      <fieldset v-if="props.view.showPeriod" class="rounded-lg border p-4">
        <legend class="px-1 text-sm font-medium">{{ $t('rules.limits.groups.period.title') }}</legend>
        <p class="mt-0 mb-3 text-xs text-muted-foreground">{{ $t('rules.limits.groups.period.hint') }}</p>
        <div class="grid grid-cols-3 gap-3">
          <AppFormField
            :label="$t('rules.limits.period.label')"
            label-id="limitPeriod-label"
            :error="props.errors.limitPeriod"
          >
            <AppSelect
              id="limitPeriod"
              labelled-by="limitPeriod-label"
              :options="periods"
              :placeholder="$t('rules.limits.period.choose')"
              :model-value="props.values.limitPeriod || null"
              :invalid="!!props.errors.limitPeriod"
              @update:model-value="set('limitPeriod', $event ?? '')"
            />
          </AppFormField>
          <div class="col-span-2">
            <AppFormField :label="$t('rules.limits.resetWindow.label')" label-id="limitResetWindow-label">
              <AppSelect
                id="limitResetWindow"
                labelled-by="limitResetWindow-label"
                :options="resetWindows"
                :model-value="props.values.limitResetWindow || 'Calendar'"
                @update:model-value="$event && set('limitResetWindow', $event)"
              />
            </AppFormField>
          </div>
        </div>
      </fieldset>

      <!-- Only shown when a cap that can be partly paid out is set (RuleLimits.cs). -->
      <fieldset v-if="props.view.showOnBreach" class="rounded-lg border p-4">
        <legend class="px-1 text-sm font-medium">{{ $t('rules.limits.groups.breach.title') }}</legend>
        <p class="mt-0 mb-3 text-xs text-muted-foreground">{{ $t('rules.limits.groups.breach.hint') }}</p>
        <div class="max-w-md">
          <AppSelect
            id="limitOnBreach"
            :aria-label="$t('rules.limits.groups.breach.title')"
            :options="onBreach"
            :model-value="props.values.limitOnBreach"
            @update:model-value="$event && set('limitOnBreach', $event)"
          />
        </div>
      </fieldset>
    </div>
  </AppCard>
</template>
