import { ref } from 'vue';
import { useForm } from 'vee-validate';
import { toTypedSchema } from '@vee-validate/zod';
import { translate } from '@/shared/i18n';
import { mapServerErrors } from '@/shared/lib/forms';
import {
  createProgramFormSchema,
  toCreateProgramRequest,
  useCreateProgramMutation,
  type Program,
} from '@/entities/program';

const FIELDS = ['name', 'slug', 'description'] as const;

/** The "New program" form. */
export function useCreateProgramForm(onCreated: (program: Program) => void) {
  const formErrors = ref<string[]>([]);
  const { defineField, errors, handleSubmit, resetForm, setErrors } = useForm({
    validationSchema: toTypedSchema(createProgramFormSchema()),
    initialValues: { name: '', slug: '', description: '' },
  });
  const [name] = defineField('name');
  const [slug] = defineField('slug');
  const [description] = defineField('description');
  const mutation = useCreateProgramMutation();

  const submit = handleSubmit(async (values) => {
    formErrors.value = [];
    try {
      const program = await mutation.mutateAsync(toCreateProgramRequest(values));
      resetForm();
      onCreated(program);
    } catch (error) {
      const mapped = mapServerErrors(error, FIELDS, translate('common.genericError'));
      setErrors(mapped.fields);
      formErrors.value = mapped.form;
    }
  });

  function reset(): void {
    resetForm();
    formErrors.value = [];
  }

  return { name, slug, description, errors, formErrors, pending: mutation.isPending, submit, reset };
}
