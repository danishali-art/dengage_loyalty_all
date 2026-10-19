import { computed, ref, type MaybeRefOrGetter } from 'vue';
import { useForm } from 'vee-validate';
import { toTypedSchema } from '@vee-validate/zod';
import { translate } from '@/shared/i18n';
import { mapServerErrors, type FieldSetter } from '@/shared/lib/forms';
import {
  accountTypeFormSchema,
  accountTypeFormValues,
  toCreateAccountTypeRequest,
  toUpdateAccountTypeRequest,
  useCreateAccountTypeMutation,
  useUpdateAccountTypeMutation,
  type AccountType,
  type AccountTypeFormValues,
} from '@/entities/account-type';

const FIELDS: readonly (keyof AccountTypeFormValues)[] = [
  'name',
  'type',
  'decimals',
  'expirationDays',
  'warningDays',
  'redemptionRate',
  'redemptionMinPoints',
  'redemptionTargetAccountTypeId',
  'transferDailyLimit',
  'currency',
];

/** Create / edit an account type (port of account-type-form.dialog.ts). */
export function useAccountTypeForm(
  programId: MaybeRefOrGetter<string>,
  onSaved: (accountType: AccountType, created: boolean) => void,
) {
  const visible = ref(false);
  const editing = ref<AccountType | null>(null);
  const siblings = ref<readonly AccountType[]>([]);
  const formErrors = ref<string[]>([]);

  const { values, errors, setFieldValue, handleSubmit, resetForm, setErrors } =
    useForm<AccountTypeFormValues>({
      validationSchema: toTypedSchema(accountTypeFormSchema()),
      initialValues: accountTypeFormValues(),
    });
  const create = useCreateAccountTypeMutation(programId);
  const update = useUpdateAccountTypeMutation(programId);

  /** `existing` null = a new account type. `all` feeds the redemption-target choice. */
  function open(existing: AccountType | null, all: readonly AccountType[]): void {
    editing.value = existing;
    siblings.value = all;
    formErrors.value = [];
    resetForm({ values: accountTypeFormValues(existing ?? undefined) });
    visible.value = true;
  }

  /** CR 2026-10-05: redeemed points convert into another, non-POINTS wallet of the program. */
  const redemptionTargets = computed(() =>
    siblings.value
      .filter((t) => t.id !== editing.value?.id && t.type !== 'POINTS')
      .map((t) => ({ value: t.id, label: t.name })),
  );

  const submit = handleSubmit(async (v) => {
    formErrors.value = [];
    try {
      const existing = editing.value;
      const saved = existing
        ? await update.mutateAsync({ id: existing.id, body: toUpdateAccountTypeRequest(v, existing) })
        : await create.mutateAsync(toCreateAccountTypeRequest(v));
      visible.value = false;
      onSaved(saved, !existing);
    } catch (error) {
      const mapped = mapServerErrors(error, FIELDS, translate('common.genericError'));
      setErrors(mapped.fields);
      formErrors.value = mapped.form;
    }
  });

  // FieldSetter already ties `value` to `field`; VeeValidate's path types can't follow a generic key.
  const set: FieldSetter<AccountTypeFormValues> = (field, value) => setFieldValue(field, value as never);

  return {
    visible,
    isEdit: computed(() => editing.value !== null),
    values: values as AccountTypeFormValues,
    errors,
    set,
    formErrors,
    redemptionTargets,
    pending: computed(() => create.isPending.value || update.isPending.value),
    open,
    submit,
  };
}
