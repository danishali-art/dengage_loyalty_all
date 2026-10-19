import { watch, type Ref } from 'vue';
import { useForm } from 'vee-validate';
import { toTypedSchema } from '@vee-validate/zod';
import { translate } from '@/shared/i18n';
import { ApiError } from '@/shared/api';
import { notify } from '@/shared/ui';
import {
  programInfoFormSchema,
  toUpdateProgramRequest,
  useUpdateProgramMutation,
  type Program,
} from '@/entities/program';

/**
 * The overview's "Program information" form (port of program-overview.page.ts saveInfo). The form
 * re-seeds whenever the server copy changes (after a save, publish or refetch).
 */
export function useProgramInfoForm(program: Ref<Program | undefined>) {
  const { defineField, errors, handleSubmit, resetForm } = useForm({
    validationSchema: toTypedSchema(programInfoFormSchema()),
  });
  const [name] = defineField('name');
  const [slug] = defineField('slug');
  const [description] = defineField('description');
  const [defaultRounding] = defineField('defaultRounding');
  const mutation = useUpdateProgramMutation(() => program.value?.id ?? '');

  watch(
    program,
    (p) => {
      if (!p) return;
      resetForm({
        values: {
          name: p.name,
          slug: p.slug,
          description: p.description ?? '',
          defaultRounding: p.defaultRounding,
        },
      });
    },
    { immediate: true },
  );

  const save = handleSubmit(async (values) => {
    const current = program.value;
    if (!current) return;
    try {
      await mutation.mutateAsync(toUpdateProgramRequest(values, current));
      notify.success(translate('programs.overview.saved'));
    } catch (error) {
      notify.error(error instanceof ApiError ? error.message : translate('common.genericError'));
    }
  });

  return { name, slug, description, defaultRounding, errors, saving: mutation.isPending, save };
}
