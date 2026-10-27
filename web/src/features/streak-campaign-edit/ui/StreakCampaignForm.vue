<script setup lang="ts">
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';
import { AppCard, AppFormErrors, AppFormField, AppInput, AppSelect, type SelectOption } from '@/shared/ui';
import type { FieldSetter } from '@/shared/lib/forms';
import { ConditionClauseListEditor } from '@/entities/condition';
import {
  STREAK_METRICS,
  STREAK_ON_COMPLETE,
  STREAK_PERIODS,
  type StreakCampaignFormValues,
} from '@/entities/streak-campaign';

/** Presentational streak-campaign fields. */
const props = defineProps<{
  values: StreakCampaignFormValues;
  errors: Partial<Record<string, string>>;
  formErrors: readonly string[];
  options: {
    triggers: readonly SelectOption<string>[];
    accounts: readonly SelectOption<string>[];
    rewards: readonly SelectOption<string>[];
  };
  timezones: readonly string[];
}>();
const emit = defineEmits<{ set: Parameters<FieldSetter<StreakCampaignFormValues>>; submit: [] }>();
const set: FieldSetter<StreakCampaignFormValues> = (field, value) => emit('set', field, value);
const { t } = useI18n();

const periods = computed(() => STREAK_PERIODS.map((p) => ({ value: p, label: t(`streaks.period.${p}`) })));
const metrics = computed(() => STREAK_METRICS.map((m) => ({ value: m, label: t(`streaks.metric.${m}`) })));
const onComplete = computed(() =>
  STREAK_ON_COMPLETE.map((o) => ({ value: o, label: t(`streaks.onComplete.${o}`) })),
);
const weekStarts = computed(() => [
  { value: 'monday' as const, label: t('cardBuckets.days.monday') },
  { value: 'sunday' as const, label: t('cardBuckets.days.sunday') },
]);
const rewardKinds = computed(() => [
  { value: 'fixed_bonus' as const, label: t('streaks.form.fixedBonus') },
  { value: 'reward_definition' as const, label: t('streaks.form.rewardDefinition') },
]);
// The saved zone and UTC stay choosable even where the browser's IANA list leaves them out.
const timezoneOptions = computed(() =>
  [...new Set([props.values.timezone, 'UTC', ...props.timezones])]
    .filter((tz) => tz)
    .map((tz) => ({ value: tz, label: tz })),
);
</script>

<template>
  <form class="flex flex-col gap-6" novalidate @submit.prevent="emit('submit')">
    <AppFormErrors :messages="props.formErrors" />

    <AppCard :heading="$t('streaks.form.basics')">
      <div class="flex flex-col gap-4">
        <AppFormField :label="$t('streaks.form.name')" for="name" :error="props.errors.name">
          <AppInput
            id="name"
            :model-value="props.values.name"
            :invalid="!!props.errors.name"
            :described-by="props.errors.name ? 'name-error' : undefined"
            @update:model-value="set('name', $event)"
          />
        </AppFormField>
        <div class="grid grid-cols-2 gap-3">
          <AppFormField
            :label="$t('streaks.form.trigger')"
            label-id="trigger-label"
            :error="props.errors.trigger"
          >
            <AppSelect
              id="trigger"
              searchable
              labelled-by="trigger-label"
              :options="props.options.triggers"
              :placeholder="$t('streaks.form.selectTrigger')"
              :model-value="props.values.trigger"
              :invalid="!!props.errors.trigger"
              @update:model-value="set('trigger', $event ?? '')"
            />
          </AppFormField>
          <AppFormField
            :label="$t('streaks.form.targetAccount')"
            label-id="targetAccountTypeId-label"
            :error="props.errors.targetAccountTypeId"
          >
            <AppSelect
              id="targetAccountTypeId"
              searchable
              labelled-by="targetAccountTypeId-label"
              :options="props.options.accounts"
              :placeholder="$t('streaks.form.selectAccount')"
              :model-value="props.values.targetAccountTypeId"
              :invalid="!!props.errors.targetAccountTypeId"
              @update:model-value="set('targetAccountTypeId', $event ?? null)"
            />
          </AppFormField>
        </div>
        <div class="grid grid-cols-2 gap-3">
          <AppFormField :label="$t('streaks.form.activeFrom')" for="activeFrom">
            <AppInput
              id="activeFrom"
              type="datetime-local"
              :model-value="props.values.activeFrom"
              @update:model-value="set('activeFrom', $event)"
            />
          </AppFormField>
          <AppFormField :label="$t('streaks.form.activeTo')" for="activeTo">
            <AppInput
              id="activeTo"
              type="datetime-local"
              :model-value="props.values.activeTo"
              @update:model-value="set('activeTo', $event)"
            />
          </AppFormField>
        </div>
      </div>
    </AppCard>

    <ConditionClauseListEditor
      :model-value="props.values.conditions"
      @update:model-value="set('conditions', $event)"
    />

    <AppCard :heading="$t('streaks.form.streak')">
      <div class="flex flex-col gap-4">
        <div class="grid grid-cols-3 gap-3">
          <AppFormField :label="$t('streaks.form.period')" label-id="period-label">
            <AppSelect
              id="period"
              labelled-by="period-label"
              :options="periods"
              :model-value="props.values.period"
              @update:model-value="$event && set('period', $event)"
            />
          </AppFormField>
          <AppFormField
            :label="$t('streaks.form.targetPeriods')"
            for="targetPeriods"
            :error="props.errors.targetPeriods"
          >
            <AppInput
              id="targetPeriods"
              inputmode="numeric"
              :model-value="props.values.targetPeriods"
              :invalid="!!props.errors.targetPeriods"
              @update:model-value="set('targetPeriods', $event)"
            />
          </AppFormField>
          <AppFormField :label="$t('streaks.form.weekStart')" label-id="weekStart-label">
            <AppSelect
              id="weekStart"
              labelled-by="weekStart-label"
              :options="weekStarts"
              :model-value="props.values.weekStart"
              @update:model-value="$event && set('weekStart', $event)"
            />
          </AppFormField>
        </div>
        <div class="grid grid-cols-2 gap-3">
          <AppFormField :label="$t('streaks.form.metric')" label-id="metric-label">
            <AppSelect
              id="metric"
              labelled-by="metric-label"
              :options="metrics"
              :model-value="props.values.metric"
              @update:model-value="$event && set('metric', $event)"
            />
          </AppFormField>
          <AppFormField :label="$t('streaks.form.threshold')" for="threshold" :error="props.errors.threshold">
            <AppInput
              id="threshold"
              inputmode="decimal"
              :model-value="props.values.threshold"
              :invalid="!!props.errors.threshold"
              @update:model-value="set('threshold', $event)"
            />
          </AppFormField>
        </div>
        <div class="grid grid-cols-2 gap-3">
          <AppFormField :label="$t('streaks.form.timezone')" label-id="timezone-label">
            <AppSelect
              id="timezone"
              searchable
              labelled-by="timezone-label"
              :options="timezoneOptions"
              :model-value="props.values.timezone"
              @update:model-value="$event && set('timezone', $event)"
            />
          </AppFormField>
          <AppFormField :label="$t('streaks.form.onComplete')" label-id="onComplete-label">
            <AppSelect
              id="onComplete"
              labelled-by="onComplete-label"
              :options="onComplete"
              :model-value="props.values.onComplete"
              @update:model-value="$event && set('onComplete', $event)"
            />
          </AppFormField>
        </div>
        <AppFormField :label="$t('streaks.form.reward')" label-id="rewardKind-label">
          <AppSelect
            id="rewardKind"
            labelled-by="rewardKind-label"
            :options="rewardKinds"
            :model-value="props.values.rewardKind"
            @update:model-value="$event && set('rewardKind', $event)"
          />
        </AppFormField>
        <AppFormField
          v-if="props.values.rewardKind === 'fixed_bonus'"
          :label="$t('streaks.form.bonusAmount')"
          for="rewardAmount"
          :error="props.errors.rewardAmount"
        >
          <AppInput
            id="rewardAmount"
            inputmode="decimal"
            :model-value="props.values.rewardAmount"
            :invalid="!!props.errors.rewardAmount"
            @update:model-value="set('rewardAmount', $event)"
          />
        </AppFormField>
        <AppFormField
          v-else
          :label="$t('streaks.form.rewardDefinitionLabel')"
          label-id="rewardDefinitionId-label"
          :error="props.errors.rewardDefinitionId"
          :hint="props.options.rewards.length === 0 ? $t('streaks.form.noRewards') : null"
        >
          <AppSelect
            id="rewardDefinitionId"
            searchable
            labelled-by="rewardDefinitionId-label"
            :options="props.options.rewards"
            :placeholder="$t('streaks.form.selectReward')"
            :model-value="props.values.rewardDefinitionId"
            :invalid="!!props.errors.rewardDefinitionId"
            @update:model-value="set('rewardDefinitionId', $event ?? null)"
          />
        </AppFormField>
      </div>
    </AppCard>
  </form>
</template>
