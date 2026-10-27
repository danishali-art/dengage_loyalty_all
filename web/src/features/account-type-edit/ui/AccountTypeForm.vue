<script setup lang="ts">
import { computed } from 'vue';
import {
  AppCheckbox,
  AppFormErrors,
  AppFormField,
  AppInfoTip,
  AppInput,
  AppSelect,
  type SelectOption,
} from '@/shared/ui';
import type { FieldSetter } from '@/shared/lib/forms';
import { SUPPORTED_CURRENCIES, type AccountTypeFormValues } from '@/entities/account-type';

/** Presentational account-type fields. */
const props = defineProps<{
  values: AccountTypeFormValues;
  errors: Partial<Record<string, string>>;
  formErrors: readonly string[];
  isEdit: boolean;
  redemptionTargets: readonly SelectOption<string>[];
}>();
const emit = defineEmits<{ set: Parameters<FieldSetter<AccountTypeFormValues>>; submit: [] }>();
const set: FieldSetter<AccountTypeFormValues> = (field, value) => emit('set', field, value);

const typeOptions: SelectOption<'POINTS' | 'CASH'>[] = [
  { value: 'POINTS', label: '⭐ POINTS' },
  { value: 'CASH', label: '💳 CASH' },
];
const currencyOptions = SUPPORTED_CURRENCIES.map((c) => ({ value: c, label: c }));
const hasExpiration = computed(() => props.values.expirationDays.trim() !== '');
</script>

<template>
  <form id="account-type-form" class="at-form" novalidate @submit.prevent="emit('submit')">
    <AppFormErrors :messages="props.formErrors" />
    <AppFormField :label="$t('accountTypes.form.name')" for="accountTypeName" :error="props.errors.name">
      <AppInput
        id="accountTypeName"
        :model-value="props.values.name"
        :invalid="!!props.errors.name"
        @update:model-value="set('name', $event)"
      />
    </AppFormField>

    <AppFormField
      :label="$t('accountTypes.form.type')"
      for="accountTypeType"
      :hint="props.isEdit ? $t('accountTypes.form.typeLocked') : null"
    >
      <AppSelect
        id="accountTypeType"
        :model-value="props.values.type"
        :options="typeOptions"
        :disabled="props.isEdit"
        @update:model-value="$event && set('type', $event)"
      />
    </AppFormField>

    <template v-if="props.values.type === 'POINTS'">
      <div class="at-form__row">
        <AppFormField :label="$t('accountTypes.form.decimals')" for="decimals" :error="props.errors.decimals">
          <template #default>
            <div class="at-form__with-tip">
              <AppInput
                id="decimals"
                inputmode="numeric"
                :model-value="props.values.decimals"
                :invalid="!!props.errors.decimals"
                @update:model-value="set('decimals', $event)"
              />
              <!-- 1.3.CL item 2: the explanation reaches keyboard and screen-reader users. -->
              <AppInfoTip :text="$t('accountTypes.decimals.info')" />
            </div>
          </template>
        </AppFormField>
        <AppFormField
          :label="$t('accountTypes.form.expirationDays')"
          for="expirationDays"
          :error="props.errors.expirationDays"
        >
          <AppInput
            id="expirationDays"
            inputmode="numeric"
            :placeholder="$t('accountTypes.form.never')"
            :model-value="props.values.expirationDays"
            :invalid="!!props.errors.expirationDays"
            @update:model-value="set('expirationDays', $event)"
          />
        </AppFormField>
      </div>
      <AppFormField
        :label="$t('accountTypes.warningDays.label')"
        for="warningDays"
        :hint="
          hasExpiration ? $t('accountTypes.warningDays.hint') : $t('accountTypes.warningDays.needsExpiry')
        "
        :error="props.errors.warningDays"
      >
        <AppInput
          id="warningDays"
          inputmode="numeric"
          :placeholder="$t('accountTypes.warningDays.placeholder')"
          :model-value="props.values.warningDays"
          :invalid="!!props.errors.warningDays"
          :described-by="props.errors.warningDays ? 'warningDays-error' : 'warningDays-hint'"
          @update:model-value="set('warningDays', $event)"
        />
      </AppFormField>

      <div>
        <AppCheckbox
          id="isTierQualifying"
          :model-value="props.values.isTierQualifying"
          described-by="tierQualifying-hint"
          @update:model-value="set('isTierQualifying', $event)"
        >
          {{ $t('accountTypes.tierQualifying.label') }}
        </AppCheckbox>
        <p id="tierQualifying-hint" class="at-form__hint">{{ $t('accountTypes.tierQualifying.hint') }}</p>
      </div>

      <!-- CR 2026-10-05: defaults only — a new redeem rule for this wallet starts with these. -->
      <div>
        <AppCheckbox
          id="redemptionEnabled"
          :model-value="props.values.redemptionEnabled"
          described-by="redemption-hint"
          @update:model-value="set('redemptionEnabled', $event)"
        >
          {{ $t('accountTypes.redemption.label') }}
        </AppCheckbox>
        <p id="redemption-hint" class="at-form__hint">{{ $t('accountTypes.redemption.hint') }}</p>
      </div>
      <template v-if="props.values.redemptionEnabled">
        <div class="at-form__row">
          <AppFormField
            :label="$t('accountTypes.redemption.cashPerPoint')"
            for="redemptionRate"
            :error="props.errors.redemptionRate"
          >
            <AppInput
              id="redemptionRate"
              inputmode="decimal"
              :model-value="props.values.redemptionRate"
              :invalid="!!props.errors.redemptionRate"
              @update:model-value="set('redemptionRate', $event)"
            />
          </AppFormField>
          <AppFormField
            :label="$t('accountTypes.redemption.minimum')"
            for="redemptionMinPoints"
            :error="props.errors.redemptionMinPoints"
          >
            <AppInput
              id="redemptionMinPoints"
              inputmode="decimal"
              :model-value="props.values.redemptionMinPoints"
              :invalid="!!props.errors.redemptionMinPoints"
              @update:model-value="set('redemptionMinPoints', $event)"
            />
          </AppFormField>
        </div>
        <AppFormField
          :label="$t('accountTypes.redemption.redeemInto')"
          for="redemptionTarget"
          :error="props.errors.redemptionTargetAccountTypeId"
        >
          <AppSelect
            v-if="props.redemptionTargets.length"
            id="redemptionTarget"
            :model-value="props.values.redemptionTargetAccountTypeId || null"
            :options="props.redemptionTargets"
            :placeholder="$t('accountTypes.redemption.selectWallet')"
            :invalid="!!props.errors.redemptionTargetAccountTypeId"
            @update:model-value="set('redemptionTargetAccountTypeId', $event ?? '')"
          />
          <p v-else class="at-form__warn">{{ $t('accountTypes.redemption.noTarget') }}</p>
        </AppFormField>
      </template>

      <!-- CR 2026-09-30 addendum A: the daily cap PointsTransferHandler enforces. -->
      <div>
        <AppCheckbox
          id="transferEnabled"
          :model-value="props.values.transferEnabled"
          described-by="transfer-hint"
          @update:model-value="set('transferEnabled', $event)"
        >
          {{ $t('accountTypes.transfer.label') }}
        </AppCheckbox>
        <p id="transfer-hint" class="at-form__hint">{{ $t('accountTypes.transfer.hint') }}</p>
      </div>
      <AppFormField
        v-if="props.values.transferEnabled"
        :label="$t('accountTypes.transfer.dailyLimit')"
        for="transferDailyLimit"
        :hint="$t('accountTypes.transfer.dailyLimitHint')"
        :error="props.errors.transferDailyLimit"
      >
        <AppInput
          id="transferDailyLimit"
          inputmode="numeric"
          :model-value="props.values.transferDailyLimit"
          :invalid="!!props.errors.transferDailyLimit"
          @update:model-value="set('transferDailyLimit', $event)"
        />
      </AppFormField>
    </template>

    <template v-else>
      <div class="at-form__row">
        <!-- 1.3.CL item 4: a fixed list (SupportedCurrencies.cs), locked once created. -->
        <AppFormField
          :label="$t('accountTypes.currency.label')"
          for="currency"
          :hint="props.isEdit ? $t('accountTypes.currency.locked') : null"
        >
          <AppSelect
            id="currency"
            :model-value="props.values.currency"
            :options="currencyOptions"
            :disabled="props.isEdit"
            @update:model-value="$event && set('currency', $event)"
          />
        </AppFormField>
        <AppFormField
          :label="$t('accountTypes.form.decimals')"
          for="cashDecimals"
          :error="props.errors.decimals"
        >
          <AppInput
            id="cashDecimals"
            inputmode="numeric"
            :model-value="props.values.decimals"
            :invalid="!!props.errors.decimals"
            @update:model-value="set('decimals', $event)"
          />
        </AppFormField>
      </div>
      <!-- 1.3.CL item 3: no expiration — cash is real credit and never expires. -->
      <p class="at-form__hint">{{ $t('accountTypes.cash.noExpiry') }}</p>
    </template>
  </form>
</template>

<style scoped>
.at-form {
  display: flex;
  flex-direction: column;
  gap: 1rem;
}
.at-form__row {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 0.75rem;
}
.at-form__with-tip {
  display: flex;
  align-items: center;
  gap: 0.5rem;
}
.at-form__hint {
  margin: 0.25rem 0 0;
  font-size: 0.75rem;
  color: var(--app-muted);
}
.at-form__warn {
  margin: 0;
  font-size: 0.75rem;
  color: var(--app-warn-fg);
}
</style>
