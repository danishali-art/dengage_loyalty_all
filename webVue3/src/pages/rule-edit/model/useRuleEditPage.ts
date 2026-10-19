import { computed, toRef } from 'vue';
import { useI18n } from 'vue-i18n';
import { useRouter } from 'vue-router';
import { notify } from '@/shared/ui';
import { useProgramCrumbs } from '@/entities/program';
import { useRuleForm } from '@/features/rule-edit';

/** New / edit rule page (port of rule-form.page.ts). */
export function useRuleEditPage(props: { programId: string; ruleId?: string }) {
  const { t } = useI18n();
  const router = useRouter();
  const programId = toRef(props, 'programId');
  const ruleId = computed(() => props.ruleId ?? null);
  const listPath = computed(() => `/programs/${programId.value}/rules`);

  const form = useRuleForm(programId, ruleId, (_saved, created) => {
    notify.success(t(created ? 'rules.form.created' : 'rules.form.saved'));
    void router.push(listPath.value);
  });
  const { crumbs } = useProgramCrumbs(programId, 'rules.heading', () => ({
    section: 'rules',
    label: t(form.isEdit.value ? 'rules.form.editCrumb' : 'rules.form.newCrumb'),
  }));

  return {
    crumbs,
    heading: computed(() => t(form.isEdit.value ? 'rules.form.editHeading' : 'rules.form.newHeading')),
    cancel: () => router.push(listPath.value),
    form,
  };
}
