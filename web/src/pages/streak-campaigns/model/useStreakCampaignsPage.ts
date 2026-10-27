import { computed, ref, toRef } from 'vue';
import { useI18n } from 'vue-i18n';
import { useRouter } from 'vue-router';
import type { TableColumn } from '@/shared/ui';
import { useProgramCrumbs } from '@/entities/program';
import { useStreakCampaignsPageQuery, type StreakCampaign } from '@/entities/streak-campaign';
import { useStreakCampaignActions } from '@/features/streak-campaign-actions';

const PAGE_SIZE = 20;

/** Streak campaigns list. */
export function useStreakCampaignsPage(props: { programId: string }) {
  const { t } = useI18n();
  const router = useRouter();
  const programId = toRef(props, 'programId');
  const { crumbs } = useProgramCrumbs(programId, 'streaks.heading');
  const page = ref(1);
  const list = useStreakCampaignsPageQuery(programId, page, PAGE_SIZE);
  const actions = useStreakCampaignActions(programId);

  const columns = computed<TableColumn[]>(() => [
    { key: 'name', header: t('streaks.col.name') },
    { key: 'trigger', header: t('streaks.col.trigger') },
    { key: 'period', header: t('streaks.col.period') },
    { key: 'targetPeriods', header: t('streaks.col.targetPeriods') },
    { key: 'status', header: t('streaks.col.status') },
    { key: 'actions', header: '', align: 'right' },
  ]);

  return {
    crumbs,
    columns,
    page,
    pageSize: PAGE_SIZE,
    rows: computed(() => list.data.value?.data ?? []),
    total: computed(() => list.data.value?.total ?? 0),
    loading: list.isFetching,
    firstLoad: list.isLoading,
    create: () => router.push(`/programs/${programId.value}/streak-campaigns/new`),
    editPath: (campaign: StreakCampaign) => `/programs/${programId.value}/streak-campaigns/${campaign.id}`,
    ...actions,
  };
}
