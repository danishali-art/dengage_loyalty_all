<script setup lang="ts">
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';
import {
  AppCard,
  AppCheckbox,
  AppFormErrors,
  AppFormField,
  AppInput,
  AppSelect,
  type SelectOption,
} from '@/shared/ui';
import type { FieldSetter } from '@/shared/lib/forms';
import { ConditionLeafListEditor } from '@/entities/condition';
import { DAYS_OF_WEEK, type CardBucketFormValues, type CountryMode } from '@/entities/card-bucket';

/** Presentational card-bucket fields (port of card-bucket-form.page.ts). */
const props = defineProps<{
  values: CardBucketFormValues;
  errors: Partial<Record<string, string>>;
  formErrors: readonly string[];
  accountOptions: readonly SelectOption<string>[];
}>();
const emit = defineEmits<{ set: Parameters<FieldSetter<CardBucketFormValues>>; submit: [] }>();
const set: FieldSetter<CardBucketFormValues> = (field, value) => emit('set', field, value);
const { t } = useI18n();

const countryModes = computed<SelectOption<CountryMode>[]>(() => [
  { value: 'in', label: t('cardBuckets.form.countryIn') },
  { value: 'not_in', label: t('cardBuckets.form.countryNotIn') },
]);

function toggleDay(day: string, on: boolean): void {
  const days = props.values.daysOfWeek.filter((d) => d !== day);
  set('daysOfWeek', on ? [...days, day] : days);
}
</script>

<template>
  <form class="flex flex-col gap-6" novalidate @submit.prevent="emit('submit')">
    <AppFormErrors :messages="props.formErrors" />

    <AppCard :heading="$t('cardBuckets.form.basics')">
      <div class="flex flex-col gap-4">
        <AppFormField :label="$t('cardBuckets.form.name')" for="name" :error="props.errors.name">
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
            :label="$t('cardBuckets.form.rewardAccount')"
            label-id="targetAccountTypeId-label"
            :error="props.errors.targetAccountTypeId"
          >
            <AppSelect
              id="targetAccountTypeId"
              searchable
              labelled-by="targetAccountTypeId-label"
              :options="props.accountOptions"
              :placeholder="$t('cardBuckets.form.selectAccount')"
              :model-value="props.values.targetAccountTypeId"
              :invalid="!!props.errors.targetAccountTypeId"
              @update:model-value="set('targetAccountTypeId', $event ?? null)"
            />
          </AppFormField>
          <AppFormField
            :label="$t('cardBuckets.form.rewardAmount')"
            for="rewardAmount"
            :error="props.errors.rewardAmount"
          >
            <AppInput
              id="rewardAmount"
              inputmode="decimal"
              :model-value="props.values.rewardAmount"
              :invalid="!!props.errors.rewardAmount"
              :described-by="props.errors.rewardAmount ? 'rewardAmount-error' : undefined"
              @update:model-value="set('rewardAmount', $event)"
            />
          </AppFormField>
        </div>
        <div class="grid grid-cols-2 gap-3">
          <AppFormField :label="$t('cardBuckets.form.activeFrom')" for="activeFrom">
            <AppInput
              id="activeFrom"
              type="datetime-local"
              :model-value="props.values.activeFrom"
              @update:model-value="set('activeFrom', $event)"
            />
          </AppFormField>
          <AppFormField :label="$t('cardBuckets.form.activeTo')" for="activeTo">
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

    <AppCard :heading="$t('cardBuckets.form.match')">
      <div class="flex flex-col gap-4">
        <AppFormField :label="$t('cardBuckets.form.mccCodes')" for="mccCodes" :error="props.errors.mccCodes">
          <AppInput
            id="mccCodes"
            monospace
            :placeholder="$t('cardBuckets.form.mccPlaceholder')"
            :model-value="props.values.mccCodes"
            @update:model-value="set('mccCodes', $event)"
          />
        </AppFormField>
        <div class="grid grid-cols-2 gap-3">
          <AppFormField
            :label="$t('cardBuckets.form.amountMin')"
            for="amountMin"
            :error="props.errors.amountMin"
          >
            <AppInput
              id="amountMin"
              inputmode="decimal"
              :model-value="props.values.amountMin"
              :invalid="!!props.errors.amountMin"
              @update:model-value="set('amountMin', $event)"
            />
          </AppFormField>
          <AppFormField
            :label="$t('cardBuckets.form.amountMax')"
            for="amountMax"
            :error="props.errors.amountMax"
          >
            <AppInput
              id="amountMax"
              inputmode="decimal"
              :model-value="props.values.amountMax"
              :invalid="!!props.errors.amountMax"
              @update:model-value="set('amountMax', $event)"
            />
          </AppFormField>
        </div>
        <div class="grid grid-cols-2 gap-3">
          <AppFormField :label="$t('cardBuckets.form.geo')" label-id="countryMode-label">
            <AppSelect
              id="countryMode"
              labelled-by="countryMode-label"
              :options="countryModes"
              :model-value="props.values.countryMode"
              @update:model-value="$event && set('countryMode', $event)"
            />
          </AppFormField>
          <AppFormField
            :label="$t('cardBuckets.form.countries')"
            for="countries"
            :error="props.errors.countries ?? props.errors.countryMode"
          >
            <AppInput
              id="countries"
              :placeholder="$t('cardBuckets.form.countriesPlaceholder')"
              :model-value="props.values.countries"
              @update:model-value="set('countries', $event)"
            />
          </AppFormField>
        </div>
        <AppCheckbox
          id="requireCaptured"
          :model-value="props.values.requireCaptured"
          @update:model-value="set('requireCaptured', $event)"
        >
          {{ $t('cardBuckets.form.requireCaptured') }}
        </AppCheckbox>
        <div class="grid grid-cols-2 gap-3">
          <AppFormField
            :label="$t('cardBuckets.form.hourFrom')"
            for="hourFrom"
            :error="props.errors.hourFrom"
          >
            <AppInput
              id="hourFrom"
              inputmode="numeric"
              :model-value="props.values.hourFrom"
              :invalid="!!props.errors.hourFrom"
              @update:model-value="set('hourFrom', $event)"
            />
          </AppFormField>
          <AppFormField :label="$t('cardBuckets.form.hourTo')" for="hourTo" :error="props.errors.hourTo">
            <AppInput
              id="hourTo"
              inputmode="numeric"
              :model-value="props.values.hourTo"
              :invalid="!!props.errors.hourTo"
              @update:model-value="set('hourTo', $event)"
            />
          </AppFormField>
        </div>
        <AppFormField :label="$t('cardBuckets.form.daysOfWeek')" label-id="daysOfWeek-label">
          <div class="flex flex-wrap gap-4" role="group" aria-labelledby="daysOfWeek-label">
            <AppCheckbox
              v-for="day in DAYS_OF_WEEK"
              :id="`day-${day}`"
              :key="day"
              :model-value="props.values.daysOfWeek.includes(day)"
              @update:model-value="toggleDay(day, $event)"
            >
              {{ $t(`cardBuckets.days.${day}`) }}
            </AppCheckbox>
          </div>
        </AppFormField>
        <div class="grid grid-cols-2 gap-3">
          <AppFormField
            :label="$t('cardBuckets.form.perCustomerPerDay')"
            for="perCustomerPerDay"
            :error="props.errors.perCustomerPerDay"
          >
            <AppInput
              id="perCustomerPerDay"
              inputmode="decimal"
              :model-value="props.values.perCustomerPerDay"
              :invalid="!!props.errors.perCustomerPerDay"
              @update:model-value="set('perCustomerPerDay', $event)"
            />
          </AppFormField>
          <AppFormField
            :label="$t('cardBuckets.form.perCustomerTotal')"
            for="perCustomerTotal"
            :error="props.errors.perCustomerTotal"
          >
            <AppInput
              id="perCustomerTotal"
              inputmode="decimal"
              :model-value="props.values.perCustomerTotal"
              :invalid="!!props.errors.perCustomerTotal"
              @update:model-value="set('perCustomerTotal', $event)"
            />
          </AppFormField>
        </div>
      </div>
    </AppCard>

    <div class="flex flex-col gap-2">
      <ConditionLeafListEditor
        :model-value="props.values.additionalConditions"
        @update:model-value="set('additionalConditions', $event)"
      />
      <p v-if="props.errors.additionalConditions" class="m-0 text-xs text-(--app-danger-fg)" role="alert">
        {{ props.errors.additionalConditions }}
      </p>
    </div>
  </form>
</template>
