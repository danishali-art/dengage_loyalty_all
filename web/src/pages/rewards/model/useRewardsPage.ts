import { computed, ref, toRef } from 'vue';
import { useI18n } from 'vue-i18n';
import { notify, type TableColumn } from '@/shared/ui';
import { useProgramCrumbs } from '@/entities/program';
import { useAccountTypesQuery } from '@/entities/account-type';
import { useTiersQuery } from '@/entities/tier';
import { isRetired, useRewardsPageQuery, type Reward } from '@/entities/reward';
import { useRewardForm } from '@/features/reward-edit';
import { useApproveReward } from '@/features/reward-approve';

const PAGE_SIZE = 12;

/** Rewards page: list, approve, create / edit / view. */
export function useRewardsPage(props: { programId: string }) {
  const { t } = useI18n();
  const programId = toRef(props, 'programId');
  const { crumbs, program } = useProgramCrumbs(programId, 'programs.overview.cards.rewards.label');
  const page = ref(1);
  const list = useRewardsPageQuery(programId, page, PAGE_SIZE);
  const accountTypes = useAccountTypesQuery(programId);
  const tiers = useTiersQuery(programId);

  const form = useRewardForm(programId, (saved, created) => {
    notify.success(created ? t('rewards.created', { name: saved.displayName }) : t('rewards.saved'));
    if (created) page.value = 1;
  });
  const { approve, approvingId } = useApproveReward(programId);

  function open(existing: Reward | null): void {
    form.open(existing, {
      programSlug: program.value?.slug ?? '',
      accountTypes: accountTypes.data.value ?? [],
      tiers: tiers.data.value ?? [],
    });
  }

  const columns = computed<TableColumn[]>(() => [
    { key: 'name', header: t('rewards.col.name') },
    { key: 'type', header: t('rewards.col.type') },
    { key: 'acquisition', header: t('rewards.col.acquisition') },
    { key: 'status', header: t('rewards.col.status') },
    { key: 'actions', header: '', align: 'right' },
  ]);

  return {
    crumbs,
    columns,
    page,
    pageSize: PAGE_SIZE,
    rows: computed(() =>
      (list.data.value?.data ?? []).map((reward) => ({ id: reward.id, reward, retired: isRetired(reward) })),
    ),
    total: computed(() => list.data.value?.total ?? 0),
    loading: list.isFetching,
    firstLoad: list.isLoading,
    /** The form needs the program's slug, wallets and tiers before it can open. */
    ready: computed(() => !!program.value && !!accountTypes.data.value && !!tiers.data.value),
    create: () => open(null),
    edit: (reward: Reward) => open(reward),
    approve,
    approvingId,
    form,
  };
}
