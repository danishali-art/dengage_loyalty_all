<script setup lang="ts">
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';
import { AppCard, AppCheckbox, AppFormField, AppInput, AppSelect } from '@/shared/ui';
import type { FieldSetter } from '@/shared/lib/forms';
import type { RuleFormValues } from '@/entities/rule';
import type { RuleFormView } from '../model/useRuleForm';

/** Rounding, posting, expiry override and flags — only those that apply (Phase 2). Presentational. */
const props = defineProps<{
  values: RuleFormValues;
  errors: Partial<Record<string, string>>;
  view: RuleFormView;
}>();
const emit = defineEmits<{ set: Parameters<FieldSetter<RuleFormValues>> }>();
const set: FieldSetter<RuleFormValues> = (field, value) => emit('set', field, value);
const { t } = useI18n();

// '' = inherit the program's default rounding; the select keys it as 'inherit'.
const roundings = computed(() => [
  {
    value: 'inherit',
    label: props.view.programRounding
      ? t('rules.config.rounding.inheritWith', {
          direction: t(`rules.config.rounding.${props.view.programRounding}`),
        })
      : t('rules.config.rounding.inherit'),
  },
  ...(['down', 'nearest', 'up'] as const).map((r) => ({ value: r, label: t(`rules.config.rounding.${r}`) })),
]);
const postings = computed(() =>
  (['Immediate', 'Delayed'] as const).map((p) => ({ value: p, label: t(`rules.config.posting.${p}`) })),
);
const expiryPlaceholder = computed(() =>
  props.view.walletExpirationDays
    ? t('rules.config.expiryOverride.placeholderDays', { days: props.view.walletExpirationDays })
    : t('rules.config.expiryOverride.placeholderNever'),
);
</script>

<template>
  <AppCard v-if="props.view.anyConfig" :heading="$t('rules.config.heading')">
    <div class="flex flex-col gap-4">
      <p class="m-0 text-xs text-muted-foreground">{{ $t('rules.config.intro') }}</p>
      <div class="grid grid-cols-2 gap-3">
        <AppFormField
          v-if="props.view.config.rounding"
          :label="$t('rules.config.rounding.label')"
          label-id="cfgRounding-label"
        >
          <AppSelect
            id="cfgRounding"
            labelled-by="cfgRounding-label"
            :options="roundings"
            :model-value="props.values.cfgRounding || 'inherit'"
            @update:model-value="
              set('cfgRounding', $event === 'inherit' || !$event ? '' : ($event as 'down' | 'nearest' | 'up'))
            "
          />
        </AppFormField>
        <template v-if="props.view.config.posting">
          <AppFormField :label="$t('rules.config.posting.label')" label-id="cfgPosting-label">
            <AppSelect
              id="cfgPosting"
              labelled-by="cfgPosting-label"
              :options="postings"
              :model-value="props.values.cfgPosting"
              @update:model-value="$event && set('cfgPosting', $event)"
            />
          </AppFormField>
          <AppFormField
            v-if="props.values.cfgPosting === 'Delayed'"
            :label="$t('rules.config.holdDays.label')"
            for="cfgHoldDays"
            :error="props.errors.cfgHoldDays"
          >
            <AppInput
              id="cfgHoldDays"
              inputmode="numeric"
              :model-value="props.values.cfgHoldDays"
              :invalid="!!props.errors.cfgHoldDays"
              @update:model-value="set('cfgHoldDays', $event)"
            />
          </AppFormField>
        </template>
        <AppFormField
          v-if="props.view.config.expiryOverrideDays"
          :label="$t('rules.config.expiryOverride.label')"
          for="cfgExpiryOverrideDays"
          :error="props.errors.cfgExpiryOverrideDays"
          :hint="$t('rules.config.expiryOverride.hint')"
        >
          <AppInput
            id="cfgExpiryOverrideDays"
            inputmode="numeric"
            :placeholder="expiryPlaceholder"
            :model-value="props.values.cfgExpiryOverrideDays"
            :invalid="!!props.errors.cfgExpiryOverrideDays"
            :described-by="
              props.errors.cfgExpiryOverrideDays
                ? 'cfgExpiryOverrideDays-error'
                : 'cfgExpiryOverrideDays-hint'
            "
            @update:model-value="set('cfgExpiryOverrideDays', $event)"
          />
        </AppFormField>
        <AppCheckbox
          v-if="props.view.config.reversible"
          id="cfgReversible"
          :model-value="props.values.cfgReversible"
          @update:model-value="set('cfgReversible', $event)"
        >
          {{ $t('rules.config.reversible') }}
        </AppCheckbox>
        <AppCheckbox
          v-if="props.view.config.notifyOnAward"
          id="cfgNotifyOnAward"
          :model-value="props.values.cfgNotifyOnAward"
          @update:model-value="set('cfgNotifyOnAward', $event)"
        >
          {{ $t('rules.config.notifyOnAward') }}
        </AppCheckbox>
      </div>
    </div>
  </AppCard>
</template>
