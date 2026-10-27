<script setup lang="ts">
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';
import { AppFormErrors, AppFormField, AppInput, AppSelect, type SelectOption } from '@/shared/ui';
import type { FieldSetter } from '@/shared/lib/forms';
import { SUPPORTED_CURRENCIES } from '@/entities/account-type';
import { CREATABLE_ACQUISITIONS, type CreatableRewardType, type RewardFormValues } from '@/entities/reward';

/** Presentational reward fields. Everything is read-only when `retired`. */
const props = defineProps<{
  values: RewardFormValues;
  errors: Partial<Record<string, string>>;
  formErrors: readonly string[];
  isEdit: boolean;
  retired: boolean;
  pendingApproval: boolean;
  currencyLocked: boolean;
  nameGrandfathered: boolean;
  existingName: string;
  namePrefix: string;
  options: {
    pointsAccounts: readonly SelectOption<string>[];
    cashWallets: readonly SelectOption<string>[];
    tiers: readonly SelectOption<string>[];
    rewardTypes: readonly CreatableRewardType[];
  };
}>();
const emit = defineEmits<{ set: Parameters<FieldSetter<RewardFormValues>>; submit: [] }>();
const set: FieldSetter<RewardFormValues> = (field, value) => emit('set', field, value);
const { t } = useI18n();

const acquisitionOptions = computed(() =>
  CREATABLE_ACQUISITIONS.map((a) => ({ value: a, label: t(`rewards.acquisition.${a}`) })),
);
const rewardTypeOptions = computed(() =>
  props.options.rewardTypes.map((r) => ({ value: r, label: t(`rewards.type.${r}`) })),
);
const currencyOptions = SUPPORTED_CURRENCIES.map((c) => ({ value: c, label: c }));
const locked = computed(() => props.retired);
</script>

<template>
  <form id="reward-form" class="reward-form" novalidate @submit.prevent="emit('submit')">
    <AppFormErrors :messages="props.formErrors" />
    <p v-if="props.retired" class="reward-form__notice" role="status">
      {{ $t('rewards.form.retiredNotice') }}
    </p>
    <p v-else-if="props.pendingApproval" class="reward-form__notice" role="status">
      {{ $t('rewards.form.pendingNotice') }}
    </p>

    <div class="reward-form__row">
      <AppFormField
        :label="$t('rewards.form.name')"
        for="rewardName"
        :hint="props.nameGrandfathered ? null : $t('rewards.form.nameHint', { prefix: props.namePrefix })"
        :error="props.errors.nameSuffix"
      >
        <AppInput
          v-if="props.nameGrandfathered"
          id="rewardName"
          monospace
          readonly
          :model-value="props.existingName"
        />
        <div v-else class="reward-form__prefixed">
          <span class="reward-form__prefix" aria-hidden="true">{{ props.namePrefix }}</span>
          <AppInput
            id="rewardName"
            monospace
            :readonly="locked"
            :model-value="props.values.nameSuffix"
            :invalid="!!props.errors.nameSuffix"
            :described-by="props.errors.nameSuffix ? 'rewardName-error' : 'rewardName-hint'"
            @update:model-value="set('nameSuffix', $event)"
          />
        </div>
      </AppFormField>
      <AppFormField
        :label="$t('rewards.form.displayName')"
        for="displayName"
        :error="props.errors.displayName"
      >
        <AppInput
          id="displayName"
          :readonly="locked"
          :model-value="props.values.displayName"
          :invalid="!!props.errors.displayName"
          @update:model-value="set('displayName', $event)"
        />
      </AppFormField>
    </div>

    <!-- CR 2026-09-30 item 1: how it's earned comes first; the reward types follow from it. -->
    <div class="reward-form__row">
      <AppFormField
        :label="$t('rewards.form.acquisition')"
        for="acquisition"
        :hint="props.isEdit ? $t('rewards.form.acquisitionLocked') : null"
      >
        <AppSelect
          id="acquisition"
          :model-value="props.values.acquisition"
          :options="acquisitionOptions"
          :disabled="props.isEdit"
          @update:model-value="$event && set('acquisition', $event)"
        />
      </AppFormField>
      <AppFormField
        :label="$t('rewards.form.rewardType')"
        for="rewardType"
        :hint="
          props.isEdit
            ? $t('rewards.form.rewardTypeLocked')
            : props.values.acquisition === 'points_purchase'
              ? $t('rewards.form.tierNeedsStreak')
              : null
        "
        :error="props.errors.rewardType"
      >
        <AppSelect
          id="rewardType"
          :model-value="props.values.rewardType"
          :options="rewardTypeOptions"
          :disabled="props.isEdit"
          @update:model-value="$event && set('rewardType', $event)"
        />
      </AppFormField>
    </div>

    <div v-if="props.values.acquisition === 'points_purchase'" class="reward-form__row">
      <AppFormField
        :label="$t('rewards.form.pointsPrice')"
        for="pointsPrice"
        :error="props.errors.pointsPrice"
      >
        <AppInput
          id="pointsPrice"
          inputmode="decimal"
          :readonly="locked"
          :model-value="props.values.pointsPrice"
          :invalid="!!props.errors.pointsPrice"
          @update:model-value="set('pointsPrice', $event)"
        />
      </AppFormField>
      <AppFormField
        :label="$t('rewards.form.pointsAccount')"
        for="pointsAccountTypeId"
        :error="props.errors.pointsAccountTypeId"
      >
        <AppSelect
          id="pointsAccountTypeId"
          searchable
          :model-value="props.values.pointsAccountTypeId"
          :options="props.options.pointsAccounts"
          :placeholder="$t('rewards.form.selectAccount')"
          :disabled="locked"
          :invalid="!!props.errors.pointsAccountTypeId"
          @update:model-value="set('pointsAccountTypeId', $event ?? null)"
        />
      </AppFormField>
    </div>
    <p v-else class="reward-form__hint">{{ $t('rewards.form.streakHint') }}</p>

    <section v-if="props.values.rewardType === 'cashback'" class="reward-form__section">
      <h3 class="reward-form__section-title">{{ $t('rewards.form.cashbackHeading') }}</h3>
      <div class="reward-form__row reward-form__row--3">
        <AppFormField
          :label="$t('rewards.form.amount')"
          for="cashbackAmount"
          :error="props.errors.cashbackAmount"
        >
          <AppInput
            id="cashbackAmount"
            inputmode="decimal"
            :readonly="locked"
            :model-value="props.values.cashbackAmount"
            :invalid="!!props.errors.cashbackAmount"
            @update:model-value="set('cashbackAmount', $event)"
          />
        </AppFormField>
        <!-- Same fixed list as the CASH account type's currency (SupportedCurrencies.cs). -->
        <AppFormField
          :label="$t('accountTypes.currency.label')"
          for="cashbackCurrency"
          :hint="props.currencyLocked ? $t('rewards.form.currencyLocked') : null"
        >
          <AppSelect
            id="cashbackCurrency"
            :model-value="props.values.cashbackCurrency"
            :options="currencyOptions"
            :disabled="locked || props.currencyLocked"
            @update:model-value="$event && set('cashbackCurrency', $event)"
          />
        </AppFormField>
        <AppFormField
          :label="$t('rewards.form.cashWallet')"
          for="cashAccountTypeId"
          :error="
            props.errors.cashAccountTypeId ??
            (props.options.cashWallets.length === 0 ? $t('rewards.form.noWalletInCurrency') : null)
          "
        >
          <AppSelect
            id="cashAccountTypeId"
            searchable
            :model-value="props.values.cashAccountTypeId"
            :options="props.options.cashWallets"
            :placeholder="$t('rewards.form.selectAccount')"
            :disabled="locked"
            :invalid="!!props.errors.cashAccountTypeId"
            @update:model-value="set('cashAccountTypeId', $event ?? null)"
          />
        </AppFormField>
      </div>
      <p v-if="!props.isEdit" class="reward-form__hint">{{ $t('rewards.form.approvalHint') }}</p>
    </section>

    <section v-if="props.values.rewardType === 'tier_upgrade'" class="reward-form__section">
      <h3 class="reward-form__section-title">{{ $t('rewards.form.tierHeading') }}</h3>
      <div class="reward-form__row">
        <AppFormField
          :label="$t('rewards.form.targetTier')"
          for="targetTierId"
          :error="props.errors.targetTierId"
        >
          <AppSelect
            id="targetTierId"
            searchable
            :model-value="props.values.targetTierId"
            :options="props.options.tiers"
            :placeholder="$t('rewards.form.selectTier')"
            :disabled="locked"
            :invalid="!!props.errors.targetTierId"
            @update:model-value="set('targetTierId', $event ?? null)"
          />
        </AppFormField>
        <AppFormField
          :label="$t('rewards.form.durationDays')"
          for="durationDays"
          :hint="$t('rewards.form.durationHint')"
          :error="props.errors.durationDays"
        >
          <AppInput
            id="durationDays"
            inputmode="numeric"
            :readonly="locked"
            :placeholder="$t('rewards.form.untilNextReview')"
            :model-value="props.values.durationDays"
            :invalid="!!props.errors.durationDays"
            @update:model-value="set('durationDays', $event)"
          />
        </AppFormField>
      </div>
    </section>
  </form>
</template>

<style scoped>
.reward-form {
  display: flex;
  flex-direction: column;
  gap: 1rem;
}
.reward-form__row {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 0.75rem;
}
.reward-form__row--3 {
  grid-template-columns: repeat(3, minmax(0, 1fr));
}
.reward-form__notice {
  margin: 0;
  padding: 0.75rem;
  border-radius: 0.5rem;
  background: #f9fafb;
  font-size: 0.75rem;
  color: var(--app-text-soft);
}
.reward-form__prefixed {
  display: flex;
  align-items: stretch;
}
.reward-form__prefix {
  display: flex;
  align-items: center;
  padding: 0 0.625rem;
  border: 1px solid var(--app-border);
  border-right: none;
  border-radius: 0.375rem 0 0 0.375rem;
  background: #f9fafb;
  font-family: ui-monospace, SFMono-Regular, Menlo, monospace;
  font-size: 0.75rem;
  color: var(--app-muted);
}
.reward-form__hint {
  margin: 0;
  font-size: 0.75rem;
  color: var(--app-muted);
}
.reward-form__section {
  padding-top: 1rem;
  border-top: 1px solid var(--app-border);
}
.reward-form__section-title {
  margin: 0 0 0.75rem;
  font-size: 0.6875rem;
  font-weight: 700;
  letter-spacing: 0.05em;
  text-transform: uppercase;
  color: var(--app-muted);
}
</style>
