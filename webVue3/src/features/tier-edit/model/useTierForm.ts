import { computed, ref, type MaybeRefOrGetter } from 'vue';
import { useForm } from 'vee-validate';
import { toTypedSchema } from '@vee-validate/zod';
import { translate } from '@/shared/i18n';
import { mapServerErrors, type FieldSetter } from '@/shared/lib/forms';
import {
  tierFormSchema,
  tierFormValues,
  toCreateTierRequest,
  toUpdateTierRequest,
  useCreateTierMutation,
  useUpdateTierMutation,
  type Tier,
  type TierFormValues,
} from '@/entities/tier';

const FIELDS: readonly (keyof TierFormValues)[] = [
  'name',
  'displayName',
  'minPoints',
  'qualifyingModel',
  'qualifyingPeriodDays',
  'graceDays',
];

/** Create / edit a tier (port of tier-form.dialog.ts). */
export function useTierForm(
  programId: MaybeRefOrGetter<string>,
  onSaved: (tier: Tier, created: boolean) => void,
) {
  const visible = ref(false);
  const editing = ref<Tier | null>(null);
  const nextSortOrder = ref(0);
  const formErrors = ref<string[]>([]);

  const { values, errors, setFieldValue, handleSubmit, resetForm, setErrors } = useForm<TierFormValues>({
    validationSchema: toTypedSchema(tierFormSchema()),
    initialValues: tierFormValues(),
  });
  const create = useCreateTierMutation(programId);
  const update = useUpdateTierMutation(programId);

  /** `existing` null = a new tier, added after the current last one. */
  function open(existing: Tier | null, tierCount: number): void {
    editing.value = existing;
    nextSortOrder.value = tierCount;
    formErrors.value = [];
    resetForm({ values: tierFormValues(existing ?? undefined) });
    visible.value = true;
  }

  const submit = handleSubmit(async (v) => {
    formErrors.value = [];
    try {
      const existing = editing.value;
      const saved = existing
        ? await update.mutateAsync({ id: existing.id, body: toUpdateTierRequest(v) })
        : await create.mutateAsync(toCreateTierRequest(v, nextSortOrder.value));
      visible.value = false;
      onSaved(saved, !existing);
    } catch (error) {
      const mapped = mapServerErrors(error, FIELDS, translate('common.genericError'));
      setErrors(mapped.fields);
      formErrors.value = mapped.form;
    }
  });

  // FieldSetter already ties `value` to `field`; VeeValidate's path types can't follow a generic key.
  const set: FieldSetter<TierFormValues> = (field, value) => setFieldValue(field, value as never);

  return {
    visible,
    isEdit: computed(() => editing.value !== null),
    // Server-authoritative lock (TiersAppService rejects the change once accounts are assigned) —
    // shown here so the admin isn't surprised by a 409 on save.
    lockQualifyingModel: computed(() => !!editing.value?.hasAssignedAccounts),
    values: values as TierFormValues,
    errors,
    set,
    formErrors,
    pending: computed(() => create.isPending.value || update.isPending.value),
    open,
    submit,
  };
}
