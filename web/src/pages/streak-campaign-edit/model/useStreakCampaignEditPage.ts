import { computed, toRef } from 'vue';
import { useI18n } from 'vue-i18n';
import { useRouter } from 'vue-router';
import { notify } from '@/shared/ui';
import { useProgramCrumbs } from '@/entities/program';
import { useStreakCampaignForm } from '@/features/streak-campaign-edit';

/** New / edit streak campaign page. */
export function useStreakCampaignEditPage(props: { programId: string; campaignId?: string }) {
  const { t } = useI18n();
  const router = useRouter();
  const programId = toRef(props, 'programId');
  const campaignId = computed(() => props.campaignId ?? null);
  const listPath = computed(() => `/programs/${programId.value}/streak-campaigns`);

  const form = useStreakCampaignForm(programId, campaignId, (_saved, created) => {
    notify.success(t(created ? 'streaks.created' : 'streaks.saved'));
    void router.push(listPath.value);
  });
  const { crumbs } = useProgramCrumbs(programId, 'streaks.heading', () => ({
    section: 'streak-campaigns',
    label: t(form.isEdit.value ? 'streaks.form.editCrumb' : 'streaks.form.newCrumb'),
  }));

  return {
    crumbs,
    heading: computed(() => t(form.isEdit.value ? 'streaks.form.editHeading' : 'streaks.form.newHeading')),
    cancel: () => router.push(listPath.value),
    form,
  };
}
