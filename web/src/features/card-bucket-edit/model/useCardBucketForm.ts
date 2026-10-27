import { computed, ref, toValue, watch, type MaybeRefOrGetter } from 'vue';
import { useForm } from 'vee-validate';
import { toTypedSchema } from '@vee-validate/zod';
import { translate } from '@/shared/i18n';
import { mapServerErrors, type FieldSetter } from '@/shared/lib/forms';
import { useAccountTypesQuery } from '@/entities/account-type';
import {
  cardBucketFormSchema,
  cardBucketFormValues,
  toCardBucketRequest,
  useCardBucketQuery,
  useCreateCardBucketMutation,
  useUpdateCardBucketMutation,
  type CardBucket,
  type CardBucketFormValues,
} from '@/entities/card-bucket';

const FIELDS: readonly (keyof CardBucketFormValues)[] = [
  'name',
  'targetAccountTypeId',
  'rewardAmount',
  'activeFrom',
  'activeTo',
  'mccCodes',
  'amountMin',
  'amountMax',
  'countryMode',
  'countries',
  'requireCaptured',
  'hourFrom',
  'hourTo',
  'daysOfWeek',
  'perCustomerPerDay',
  'perCustomerTotal',
  'additionalConditions',
];

/**
 * Create or edit a card bucket. `bucketId` null = a new
 * bucket. The form fills in once the program's wallets (and the bucket) have loaded.
 */
export function useCardBucketForm(
  programId: MaybeRefOrGetter<string>,
  bucketId: MaybeRefOrGetter<string | null>,
  onSaved: (bucket: CardBucket, created: boolean) => void,
) {
  const accountTypes = useAccountTypesQuery(programId);
  const existing = useCardBucketQuery(programId, bucketId);
  const isEdit = computed(() => !!toValue(bucketId));
  const formErrors = ref<string[]>([]);

  const { values, errors, setFieldValue, handleSubmit, resetForm, setErrors } = useForm<CardBucketFormValues>(
    {
      validationSchema: toTypedSchema(cardBucketFormSchema()),
      initialValues: cardBucketFormValues(undefined, null),
    },
  );
  const create = useCreateCardBucketMutation(programId);
  const update = useUpdateCardBucketMutation(programId);

  const ready = computed(() => !!accountTypes.data.value && (!isEdit.value || !!existing.data.value));
  // Filled in once: a later refetch must not overwrite what the admin is typing.
  let filled = false;
  watch(
    ready,
    (isReady) => {
      if (!isReady || filled) return;
      filled = true;
      resetForm({
        values: cardBucketFormValues(
          existing.data.value ?? undefined,
          accountTypes.data.value?.[0]?.id ?? null,
        ),
      });
    },
    { immediate: true },
  );

  const submit = handleSubmit(
    async (v) => {
      formErrors.value = [];
      try {
        const id = toValue(bucketId);
        const body = toCardBucketRequest(v);
        const saved = id ? await update.mutateAsync({ id, body }) : await create.mutateAsync(body);
        onSaved(saved, !id);
      } catch (error) {
        const mapped = mapServerErrors(error, FIELDS, translate('common.genericError'));
        setErrors(mapped.fields);
        formErrors.value = mapped.form;
      }
    },
    () => {
      formErrors.value = [translate('cardBuckets.form.fixFields')];
    },
  );

  // FieldSetter already ties `value` to `field`; VeeValidate's path types can't follow a generic key.
  const set: FieldSetter<CardBucketFormValues> = (field, value) => setFieldValue(field, value as never);

  return {
    isEdit,
    ready,
    /** The bucket couldn't be loaded (deleted, or another program's). */
    loadFailed: computed(() => existing.isError.value),
    accountOptions: computed(() =>
      (accountTypes.data.value ?? []).map((a) => ({ value: a.id, label: `${a.name} (${a.type})` })),
    ),
    values: values as CardBucketFormValues,
    errors,
    set,
    formErrors,
    pending: computed(() => create.isPending.value || update.isPending.value),
    submit,
  };
}
