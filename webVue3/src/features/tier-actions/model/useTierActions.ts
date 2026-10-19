import { type MaybeRefOrGetter, type Ref } from 'vue';
import { useI18n } from 'vue-i18n';
import { ApiError } from '@/shared/api';
import { notify, useAppConfirm } from '@/shared/ui';
import {
  moveTier,
  tierDeleteBlockedReason,
  useDeleteTierMutation,
  useReorderTiersMutation,
  type Tier,
} from '@/entities/tier';

/** Reorder and delete tiers (port of tiers-list.page.ts moveTier / remove). */
export function useTierActions(
  programId: MaybeRefOrGetter<string>,
  tiers: Ref<readonly Tier[]>,
  programIsActive: Ref<boolean>,
) {
  const { t } = useI18n();
  const confirm = useAppConfirm();
  const reorder = useReorderTiersMutation(programId);
  const remove = useDeleteTierMutation(programId);

  function fail(error: unknown): void {
    notify.error(error instanceof ApiError ? error.message : t('common.genericError'));
  }

  async function move(index: number, direction: -1 | 1): Promise<void> {
    const ids = moveTier(tiers.value, index, direction);
    if (!ids || reorder.isPending.value) return;
    try {
      await reorder.mutateAsync(ids);
    } catch (error) {
      fail(error);
    }
  }

  /** An i18n key explaining why a tier can't be deleted, or null. */
  function blockedReason(tier: Tier): string | null {
    return tierDeleteBlockedReason(tier, programIsActive.value);
  }

  async function deleteTier(tier: Tier): Promise<void> {
    if (blockedReason(tier) || remove.isPending.value) return;
    const confirmed = await confirm.ask({
      title: t('tiers.delete.confirmTitle'),
      message: t('tiers.delete.confirmMessage', { name: tier.displayName }),
      confirmLabel: t('action.delete'),
      destructive: true,
    });
    if (!confirmed) return;
    try {
      await remove.mutateAsync(tier.id);
      notify.success(t('tiers.delete.done', { name: tier.displayName }));
    } catch (error) {
      fail(error);
    }
  }

  return { move, moving: reorder.isPending, blockedReason, deleteTier };
}
