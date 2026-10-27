import { computed, ref, toValue, watch, type MaybeRefOrGetter } from 'vue';
import { useForm } from 'vee-validate';
import { toTypedSchema } from '@vee-validate/zod';
import { translate } from '@/shared/i18n';
import { mapServerErrors, type FieldSetter } from '@/shared/lib/forms';
import { listIanaTimezones } from '@/shared/lib/dsl';
import { useAccountTypesQuery } from '@/entities/account-type';
import { useAllRewardsQuery } from '@/entities/reward';
import { BUILT_IN_TRIGGERS, DEFAULT_TRIGGER, useEventTypesQuery } from '@/entities/event-type';
import {
  streakCampaignFormSchema,
  streakCampaignFormValues,
  streakDslErrors,
  toStreakCampaignRequest,
  useCreateStreakCampaignMutation,
  useStreakCampaignQuery,
  useUpdateStreakCampaignMutation,
  type StreakCampaign,
  type StreakCampaignFormValues,
} from '@/entities/streak-campaign';

const FIELDS: readonly (keyof StreakCampaignFormValues)[] = [
  'name',
  'trigger',
  'targetAccountTypeId',
  'activeFrom',
  'activeTo',
  'conditions',
];

/**
 * Create or edit a streak campaign. `campaignId` null = a
 * new campaign. The form fills in once the program's wallets and rewards (and the campaign)
 * have loaded; the trigger list follows the deployment's event-type catalog when it loads.
 */
export function useStreakCampaignForm(
  programId: MaybeRefOrGetter<string>,
  campaignId: MaybeRefOrGetter<string | null>,
  onSaved: (campaign: StreakCampaign, created: boolean) => void,
) {
  const accountTypes = useAccountTypesQuery(programId);
  const rewards = useAllRewardsQuery(programId);
  const eventTypes = useEventTypesQuery();
  const existing = useStreakCampaignQuery(programId, campaignId);
  const isEdit = computed(() => !!toValue(campaignId));
  const formErrors = ref<string[]>([]);

  const { values, errors, setFieldValue, handleSubmit, resetForm, setErrors } =
    useForm<StreakCampaignFormValues>({
      validationSchema: toTypedSchema(streakCampaignFormSchema()),
      initialValues: streakCampaignFormValues(undefined, {
        trigger: DEFAULT_TRIGGER,
        targetAccountTypeId: null,
      }),
    });
  const form = values as StreakCampaignFormValues;
  const create = useCreateStreakCampaignMutation(programId);
  const update = useUpdateStreakCampaignMutation(programId);

  const ready = computed(
    () => !!accountTypes.data.value && !!rewards.data.value && (!isEdit.value || !!existing.data.value),
  );
  // Filled in once: a later refetch must not overwrite what the admin is typing.
  let filled = false;
  watch(
    ready,
    (isReady) => {
      if (!isReady || filled) return;
      filled = true;
      resetForm({
        values: streakCampaignFormValues(existing.data.value ?? undefined, {
          trigger: DEFAULT_TRIGGER,
          targetAccountTypeId: accountTypes.data.value?.[0]?.id ?? null,
        }),
      });
    },
    { immediate: true },
  );

  /** The campaign's reward as loaded — kept in the list even if it's no longer eligible. */
  const loadedRewardId = computed(() => existing.data.value?.config.reward.reward_definition_id ?? null);

  const options = computed(() => {
    const catalog = eventTypes.data.value;
    const triggers = catalog ? [...catalog.builtIn, ...catalog.generic] : [...BUILT_IN_TRIGGERS];
    return {
      triggers: [...new Set(triggers)].map((t) => ({ value: t, label: t })),
      accounts: (accountTypes.data.value ?? []).map((a) => ({
        value: a.id,
        label: `${a.name} (${a.type})`,
      })),
      // CR 2026-09-30 (§3.7): mirrors the API's invalid_streak_reward check — only an active,
      // approved streak-completion reward can be paid by a streak.
      rewards: (rewards.data.value ?? [])
        .filter(
          (r) =>
            (r.isActive && r.status === 'active' && r.acquisition === 'streak_completion') ||
            r.id === loadedRewardId.value,
        )
        .map((r) => ({ value: r.id, label: r.displayName })),
    };
  });

  const submit = handleSubmit(
    async (v) => {
      // The engine's own checks (ConditionDsl, StreakConfig.Validate), run before saving.
      const dslErrors = streakDslErrors(v);
      if (dslErrors.length) {
        formErrors.value = dslErrors;
        return;
      }
      formErrors.value = [];
      try {
        const id = toValue(campaignId);
        const body = toStreakCampaignRequest(v);
        const saved = id ? await update.mutateAsync({ id, body }) : await create.mutateAsync(body);
        onSaved(saved, !id);
      } catch (error) {
        const mapped = mapServerErrors(error, FIELDS, translate('common.genericError'));
        setErrors(mapped.fields);
        formErrors.value = mapped.form;
      }
    },
    () => {
      formErrors.value = [translate('streaks.form.fixFields')];
    },
  );

  // FieldSetter already ties `value` to `field`; VeeValidate's path types can't follow a generic key.
  const set: FieldSetter<StreakCampaignFormValues> = (field, value) => setFieldValue(field, value as never);

  return {
    isEdit,
    ready,
    loadFailed: computed(() => existing.isError.value),
    options,
    /** IANA ids only — never the browser's own zone as a silent default. */
    timezones: listIanaTimezones(),
    values: form,
    errors,
    set,
    formErrors,
    pending: computed(() => create.isPending.value || update.isPending.value),
    submit,
  };
}
