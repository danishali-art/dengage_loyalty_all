import { computed, toRef } from 'vue';
import { useI18n } from 'vue-i18n';
import { notify, type TableColumn } from '@/shared/ui';
import { useProgramCrumbs } from '@/entities/program';
import { useTiersQuery, type Tier } from '@/entities/tier';
import { useTierForm } from '@/features/tier-edit';
import { useTierActions } from '@/features/tier-actions';

export interface TierRow {
  id: string;
  tier: Tier;
  index: number;
  position: number;
  canMoveUp: boolean;
  canMoveDown: boolean;
  /** i18n key explaining why Delete is disabled, or null. */
  deleteBlockedKey: string | null;
}

/** Tiers page: ordered list, reorder, create / edit / delete. */
export function useTiersPage(props: { programId: string }) {
  const { t } = useI18n();
  const programId = toRef(props, 'programId');
  const { crumbs, program } = useProgramCrumbs(programId, 'programs.overview.cards.tiers.label');
  const query = useTiersQuery(programId);
  const tiers = computed<readonly Tier[]>(() => query.data.value ?? []);
  const programIsActive = computed(() => program.value?.status === 'active');

  const form = useTierForm(programId, (saved, created) => {
    notify.success(created ? t('tiers.created', { name: saved.displayName }) : t('tiers.saved'));
  });
  const actions = useTierActions(programId, tiers, programIsActive);

  const columns = computed<TableColumn[]>(() => [
    { key: 'order', header: t('tiers.col.order') },
    { key: 'name', header: t('tiers.col.name') },
    { key: 'minPoints', header: t('tiers.col.minPoints'), align: 'right' },
    { key: 'qualifying', header: t('tiers.col.qualifying') },
    { key: 'grace', header: t('tiers.col.grace') },
    { key: 'actions', header: '', align: 'right' },
  ]);

  /** Ready-to-render rows: position and move/delete availability worked out here, not in the template. */
  const rows = computed<TierRow[]>(() =>
    tiers.value.map((tier, index) => ({
      id: tier.id,
      tier,
      index,
      position: index + 1,
      canMoveUp: index > 0,
      canMoveDown: index < tiers.value.length - 1,
      deleteBlockedKey: actions.blockedReason(tier),
    })),
  );

  return {
    crumbs,
    columns,
    tiers,
    rows,
    loading: query.isFetching,
    firstLoad: query.isLoading,
    create: () => form.open(null, tiers.value.length),
    edit: (tier: Tier) => form.open(tier, tiers.value.length),
    form,
    actions,
  };
}
