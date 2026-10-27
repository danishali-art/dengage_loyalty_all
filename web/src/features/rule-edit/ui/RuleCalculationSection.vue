<script setup lang="ts">
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';
import { AppCard, AppFormField, AppInput, AppSelect } from '@/shared/ui';
import type { FieldSetter } from '@/shared/lib/forms';
import type { RuleFormValues } from '@/entities/rule';
import type { RuleFormView } from '../model/useRuleForm';

/** The calculation for the chosen rule type. */
const props = defineProps<{
  values: RuleFormValues;
  errors: Partial<Record<string, string>>;
  view: RuleFormView;
  isEdit: boolean;
}>();
const emit = defineEmits<{ set: Parameters<FieldSetter<RuleFormValues>> }>();
const set: FieldSetter<RuleFormValues> = (field, value) => emit('set', field, value);
const { t } = useI18n();

// CR 2026-10-06 D17: a redeem / transfer rule's calculation holds the customer-facing terms.
const heading = computed(() =>
  props.values.type === 'RedemptionRule'
    ? { title: t('rules.calc.headingRedeem'), intro: t('rules.calc.introRedeem') }
    : props.values.type === 'TransferRule'
      ? { title: t('rules.calc.headingTransfer'), intro: t('rules.calc.introTransfer') }
      : { title: t('rules.calc.heading'), intro: t('rules.calc.intro') },
);
const modes = computed(() => [
  { value: 'proportional' as const, label: t('rules.form.calc.modeProportional') },
  { value: 'full' as const, label: t('rules.form.calc.modeFull') },
]);
const negatives = computed(() => [
  { value: 'allow negative' as const, label: t('rules.form.calc.allowNegative') },
  { value: 'clamp to zero' as const, label: t('rules.form.calc.clampToZero') },
]);
const reasons = computed(() =>
  (['goodwill', 'correction', 'dispute', 'migration'] as const).map((r) => ({
    value: r,
    label: t(`rules.form.calc.reason.${r}`),
  })),
);
</script>

<template>
  <AppCard :heading="heading.title">
    <div class="flex flex-col gap-4">
      <p class="m-0 text-xs text-muted-foreground">{{ heading.intro }}</p>
      <div class="grid grid-cols-2 gap-3">
        <AppFormField
          v-if="props.values.type === 'SpendRule'"
          :label="$t('rules.form.calc.rate')"
          for="calcRate"
          :error="props.errors.calcRate"
          :hint="props.view.genericTrigger ? $t('rules.calc.rateNeedsAmount') : null"
        >
          <AppInput
            id="calcRate"
            inputmode="decimal"
            :model-value="props.values.calcRate"
            :invalid="!!props.errors.calcRate"
            @update:model-value="set('calcRate', $event)"
          />
        </AppFormField>

        <AppFormField
          v-else-if="props.values.type === 'FixedBonusRule'"
          :label="$t('rules.form.calc.amount')"
          for="calcAmount"
          :error="props.errors.calcAmount"
        >
          <AppInput
            id="calcAmount"
            inputmode="decimal"
            :model-value="props.values.calcAmount"
            :invalid="!!props.errors.calcAmount"
            @update:model-value="set('calcAmount', $event)"
          />
        </AppFormField>

        <!-- CR 2026-10-05: the same fields as the wallet's redemption settings; this rule's values
             are what a redeem uses. -->
        <template v-else-if="props.values.type === 'RedemptionRule'">
          <AppFormField
            :label="$t('rules.calc.redeem.cashPerPoint')"
            for="calcCashPerPoint"
            :error="props.errors.calcCashPerPoint"
          >
            <AppInput
              id="calcCashPerPoint"
              inputmode="decimal"
              :model-value="props.values.calcCashPerPoint"
              :invalid="!!props.errors.calcCashPerPoint"
              @update:model-value="set('calcCashPerPoint', $event)"
            />
          </AppFormField>
          <AppFormField
            :label="$t('rules.calc.redeem.minimum')"
            for="calcMinRedeem"
            :error="props.errors.calcMinRedeem"
          >
            <AppInput
              id="calcMinRedeem"
              inputmode="decimal"
              :placeholder="$t('rules.calc.none')"
              :model-value="props.values.calcMinRedeem"
              :invalid="!!props.errors.calcMinRedeem"
              @update:model-value="set('calcMinRedeem', $event)"
            />
          </AppFormField>
          <div class="col-span-2">
            <AppFormField
              :label="$t('rules.calc.redeem.redeemInto')"
              label-id="calcCashAccountTypeId-label"
              :error="props.errors.calcCashAccountTypeId"
              :hint="$t('rules.calc.redeem.approvalHint')"
            >
              <AppSelect
                v-if="props.view.cashWalletOptions.length > 0"
                id="calcCashAccountTypeId"
                searchable
                labelled-by="calcCashAccountTypeId-label"
                :options="props.view.cashWalletOptions"
                :placeholder="$t('rules.calc.redeem.redeemIntoPlaceholder')"
                :model-value="props.values.calcCashAccountTypeId"
                :invalid="!!props.errors.calcCashAccountTypeId"
                @update:model-value="set('calcCashAccountTypeId', $event ?? null)"
              />
              <p v-else class="m-0 text-xs text-muted-foreground">
                {{ $t('rules.calc.redeem.noCashWallets') }}
              </p>
            </AppFormField>
          </div>
          <p v-if="!props.isEdit" class="col-span-2 m-0 text-xs text-muted-foreground">
            {{ $t('rules.calc.prefillHint') }}
          </p>
        </template>

        <!-- CR 2026-10-05 (R-O2): the daily transfer limit only — ratio and fee wait for a
             fee-engine CR. -->
        <template v-else-if="props.values.type === 'TransferRule'">
          <AppFormField
            :label="$t('rules.calc.transfer.dailyLimit')"
            for="calcMaxPerDay"
            :error="props.errors.calcMaxPerDay"
          >
            <AppInput
              id="calcMaxPerDay"
              inputmode="decimal"
              :model-value="props.values.calcMaxPerDay"
              :invalid="!!props.errors.calcMaxPerDay"
              @update:model-value="set('calcMaxPerDay', $event)"
            />
          </AppFormField>
          <p v-if="!props.isEdit" class="col-span-2 m-0 text-xs text-muted-foreground">
            {{ $t('rules.calc.prefillHint') }}
          </p>
        </template>

        <template v-else-if="props.values.type === 'ReversalRule'">
          <AppFormField :label="$t('rules.form.calc.mode')" label-id="calcMode-label">
            <AppSelect
              id="calcMode"
              labelled-by="calcMode-label"
              :options="modes"
              :model-value="props.values.calcMode"
              @update:model-value="$event && set('calcMode', $event)"
            />
          </AppFormField>
          <AppFormField :label="$t('rules.form.calc.negative')" label-id="calcAllowNegative-label">
            <AppSelect
              id="calcAllowNegative"
              labelled-by="calcAllowNegative-label"
              :options="negatives"
              :model-value="props.values.calcAllowNegative"
              @update:model-value="$event && set('calcAllowNegative', $event)"
            />
          </AppFormField>
        </template>

        <template v-else-if="props.values.type === 'ManualAdjustmentRule'">
          <AppFormField :label="$t('rules.form.calc.reasonLabel')" label-id="calcReason-label">
            <AppSelect
              id="calcReason"
              labelled-by="calcReason-label"
              :options="reasons"
              :model-value="props.values.calcReason"
              @update:model-value="$event && set('calcReason', $event)"
            />
          </AppFormField>
          <AppFormField
            :label="$t('rules.form.calc.fallbackAmount')"
            for="calcAmount"
            :error="props.errors.calcAmount"
          >
            <AppInput
              id="calcAmount"
              inputmode="decimal"
              :placeholder="$t('rules.form.calc.fallbackPlaceholder')"
              :model-value="props.values.calcAmount"
              :invalid="!!props.errors.calcAmount"
              @update:model-value="set('calcAmount', $event)"
            />
          </AppFormField>
        </template>

        <p v-else class="col-span-2 m-0 text-xs text-muted-foreground">{{ $t('rules.form.calc.retired') }}</p>
      </div>
    </div>
  </AppCard>
</template>
