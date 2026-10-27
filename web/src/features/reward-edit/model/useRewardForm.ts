import { computed, ref, watch, type MaybeRefOrGetter } from 'vue';
import { useForm } from 'vee-validate';
import { toTypedSchema } from '@vee-validate/zod';
import { translate } from '@/shared/i18n';
import { mapServerErrors, type FieldSetter } from '@/shared/lib/forms';
import { cashCurrency, DEFAULT_CURRENCY, type AccountType } from '@/entities/account-type';
import type { Tier } from '@/entities/tier';
import {
  isRetired,
  rewardFormSchema,
  rewardFormValues,
  rewardTypesFor,
  toCreateRewardRequest,
  toUpdateRewardRequest,
  useCreateRewardMutation,
  useUpdateRewardMutation,
  type Reward,
  type RewardFormContext,
  type RewardFormValues,
} from '@/entities/reward';

export interface RewardFormOpenContext {
  /** CR 2026-09-30 (A5): reward names are `{programSlug}_{suffix}`. */
  programSlug: string;
  accountTypes: readonly AccountType[];
  tiers: readonly Tier[];
}

const FIELDS: readonly (keyof RewardFormValues)[] = [
  'nameSuffix',
  'displayName',
  'acquisition',
  'rewardType',
  'pointsPrice',
  'pointsAccountTypeId',
  'cashbackAmount',
  'cashbackCurrency',
  'cashAccountTypeId',
  'targetTierId',
  'durationDays',
];

/** Create / edit / view a reward. */
export function useRewardForm(
  programId: MaybeRefOrGetter<string>,
  onSaved: (reward: Reward, created: boolean) => void,
) {
  const visible = ref(false);
  const editing = ref<Reward | null>(null);
  const openContext = ref<RewardFormOpenContext>({ programSlug: '', accountTypes: [], tiers: [] });
  const formErrors = ref<string[]>([]);

  const namePrefix = computed(() => `${openContext.value.programSlug}_`);
  /** A pre-CR name without the prefix: kept as-is until renamed (§3.10 grandfathering). */
  const nameGrandfathered = computed(
    () => !!editing.value && !editing.value.name.startsWith(namePrefix.value),
  );
  const schemaContext = computed<RewardFormContext>(() => ({
    namePrefix: namePrefix.value,
    nameGrandfathered: nameGrandfathered.value,
  }));

  const { values, errors, setFieldValue, handleSubmit, resetForm, setErrors } = useForm<RewardFormValues>({
    validationSchema: computed(() => toTypedSchema(rewardFormSchema(schemaContext.value))),
    initialValues: rewardFormValues(undefined, '_', DEFAULT_CURRENCY),
  });
  const form = values as RewardFormValues;
  const create = useCreateRewardMutation(programId);
  const update = useUpdateRewardMutation(programId);

  function open(existing: Reward | null, context: RewardFormOpenContext): void {
    editing.value = existing;
    openContext.value = context;
    formErrors.value = [];
    resetForm({
      values: rewardFormValues(existing ?? undefined, `${context.programSlug}_`, DEFAULT_CURRENCY),
    });
    visible.value = true;
  }

  // Item 3: a points purchase can't carry a tier upgrade — fall back to cashback.
  watch(
    () => form.acquisition,
    (acquisition) => {
      if (acquisition && !rewardTypesFor(acquisition).includes(form.rewardType))
        setFieldValue('rewardType', 'cashback');
    },
  );
  // §3.3: a wallet in another currency is no longer a valid choice.
  watch(
    () => form.cashbackCurrency,
    (currency) => {
      const wallet = openContext.value.accountTypes.find((a) => a.id === form.cashAccountTypeId);
      if (wallet && cashCurrency(wallet) !== currency) setFieldValue('cashAccountTypeId', null);
    },
  );

  const options = computed(() => {
    const { accountTypes, tiers } = openContext.value;
    return {
      pointsAccounts: accountTypes
        .filter((a) => a.type === 'POINTS')
        .map((a) => ({ value: a.id, label: a.name })),
      // §3.3: only CASH wallets in the chosen currency can receive the cashback.
      cashWallets: accountTypes
        .filter((a) => a.type === 'CASH' && cashCurrency(a) === form.cashbackCurrency)
        .map((a) => ({ value: a.id, label: a.name })),
      tiers: tiers.map((t) => ({ value: t.id, label: t.displayName })),
      rewardTypes: rewardTypesFor(form.acquisition ?? 'points_purchase'),
    };
  });

  const submit = handleSubmit(
    async (v) => {
      formErrors.value = [];
      try {
        const existing = editing.value;
        const saved = existing
          ? await update.mutateAsync({
              id: existing.id,
              body: toUpdateRewardRequest(v, existing, schemaContext.value),
            })
          : await create.mutateAsync(toCreateRewardRequest(v, schemaContext.value));
        visible.value = false;
        onSaved(saved, !existing);
      } catch (error) {
        const mapped = mapServerErrors(error, FIELDS, translate('rewards.form.genericError'));
        setErrors(mapped.fields);
        formErrors.value = mapped.form;
      }
    },
    () => {
      formErrors.value = [translate('rewards.form.fixFields')];
    },
  );

  // FieldSetter already ties `value` to `field`; VeeValidate's path types can't follow a generic key.
  const set: FieldSetter<RewardFormValues> = (field, value) => setFieldValue(field, value as never);

  return {
    visible,
    isEdit: computed(() => editing.value !== null),
    /** A1: retired rows stay viewable but can't be changed (the API returns reward_type_retired). */
    retired: computed(() => !!editing.value && isRetired(editing.value)),
    pendingApproval: computed(() => editing.value?.status === 'pending_approval'),
    /** §3.3: an approved cashback's currency is locked (the API returns currency_locked). */
    currencyLocked: computed(() => !!editing.value?.approvedBy),
    nameGrandfathered,
    existingName: computed(() => editing.value?.name ?? ''),
    namePrefix,
    options,
    values: form,
    errors,
    set,
    formErrors,
    pending: computed(() => create.isPending.value || update.isPending.value),
    open,
    submit,
  };
}
