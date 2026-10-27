<script setup lang="ts">
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';
import { AppFormErrors, AppFormField, AppInput, AppRadioCards } from '@/shared/ui';
import type { FieldSetter } from '@/shared/lib/forms';
import type { QualifyingModel, TierFormValues } from '@/entities/tier';

/** Presentational tier fields. */
const props = defineProps<{
  values: TierFormValues;
  errors: Partial<Record<string, string>>;
  formErrors: readonly string[];
  lockQualifyingModel: boolean;
}>();
const emit = defineEmits<{ set: Parameters<FieldSetter<TierFormValues>>; submit: [] }>();
const set: FieldSetter<TierFormValues> = (field, value) => emit('set', field, value);
const { t } = useI18n();

const models = computed(() => [
  {
    value: 'periodic' as QualifyingModel,
    label: t('tiers.form.periodic'),
    description: t('tiers.form.periodicHint'),
  },
  {
    value: 'lifetime' as QualifyingModel,
    label: t('tiers.form.lifetime'),
    description: t('tiers.form.lifetimeHint'),
  },
]);
</script>

<template>
  <form id="tier-form" class="tier-form" novalidate @submit.prevent="emit('submit')">
    <AppFormErrors :messages="props.formErrors" />
    <div class="tier-form__row">
      <AppFormField :label="$t('tiers.form.name')" for="tierName" :error="props.errors.name">
        <AppInput
          id="tierName"
          monospace
          :placeholder="$t('tiers.form.namePlaceholder')"
          :model-value="props.values.name"
          :invalid="!!props.errors.name"
          @update:model-value="set('name', $event)"
        />
      </AppFormField>
      <AppFormField :label="$t('tiers.form.displayName')" for="displayName" :error="props.errors.displayName">
        <AppInput
          id="displayName"
          :placeholder="$t('tiers.form.displayNamePlaceholder')"
          :model-value="props.values.displayName"
          :invalid="!!props.errors.displayName"
          @update:model-value="set('displayName', $event)"
        />
      </AppFormField>
    </div>

    <AppFormField :label="$t('tiers.form.minPoints')" for="minPoints" :error="props.errors.minPoints">
      <AppInput
        id="minPoints"
        inputmode="decimal"
        :model-value="props.values.minPoints"
        :invalid="!!props.errors.minPoints"
        @update:model-value="set('minPoints', $event)"
      />
    </AppFormField>

    <AppFormField
      :label="$t('tiers.form.qualifyingModel')"
      label-id="qualifying-model-label"
      :hint="props.lockQualifyingModel ? $t('tiers.form.modelLocked') : null"
    >
      <AppRadioCards
        name="qualifyingModel"
        labelled-by="qualifying-model-label"
        :options="models"
        :model-value="props.values.qualifyingModel"
        :disabled="props.lockQualifyingModel"
        @update:model-value="$event && set('qualifyingModel', $event)"
      />
    </AppFormField>

    <AppFormField
      v-if="props.values.qualifyingModel === 'periodic'"
      :label="$t('tiers.form.periodDays')"
      for="qualifyingPeriodDays"
      :error="props.errors.qualifyingPeriodDays"
    >
      <AppInput
        id="qualifyingPeriodDays"
        inputmode="numeric"
        :model-value="props.values.qualifyingPeriodDays"
        :invalid="!!props.errors.qualifyingPeriodDays"
        @update:model-value="set('qualifyingPeriodDays', $event)"
      />
    </AppFormField>

    <AppFormField :label="$t('tiers.form.graceDays')" for="graceDays" :error="props.errors.graceDays">
      <AppInput
        id="graceDays"
        inputmode="numeric"
        :model-value="props.values.graceDays"
        :invalid="!!props.errors.graceDays"
        @update:model-value="set('graceDays', $event)"
      />
    </AppFormField>
  </form>
</template>

<style scoped>
.tier-form {
  display: flex;
  flex-direction: column;
  gap: 1rem;
}
.tier-form__row {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 0.75rem;
}
</style>
