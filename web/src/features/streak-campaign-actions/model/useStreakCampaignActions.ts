import { ref, type MaybeRefOrGetter } from 'vue';
import { useI18n } from 'vue-i18n';
import { notify, useAppConfirm } from '@/shared/ui';
import {
  useDeleteStreakCampaignMutation,
  useSetStreakCampaignStatusMutation,
  type StreakCampaign,
} from '@/entities/streak-campaign';

/**
 * Activate / disable and delete a streak campaign.
 * Failures are toasted by the API client.
 */
export function useStreakCampaignActions(programId: MaybeRefOrGetter<string>) {
  const { t } = useI18n();
  const confirm = useAppConfirm();
  const setStatus = useSetStreakCampaignStatusMutation(programId);
  const remove = useDeleteStreakCampaignMutation(programId);
  /** The campaign an action is running on, for its row's pending state. */
  const busyId = ref<string | null>(null);

  async function run(id: string, action: () => Promise<unknown>): Promise<void> {
    busyId.value = id;
    try {
      await action();
    } catch {
      // Already toasted by the API client.
    } finally {
      busyId.value = null;
    }
  }

  function toggleStatus(campaign: StreakCampaign): Promise<void> {
    const next = campaign.status === 'active' ? 'disabled' : 'active';
    return run(campaign.id, async () => {
      await setStatus.mutateAsync({ id: campaign.id, status: next });
      notify.success(t(next === 'active' ? 'streaks.activated' : 'streaks.disabled'));
    });
  }

  async function deleteCampaign(campaign: StreakCampaign): Promise<void> {
    const confirmed = await confirm.ask({
      title: t('streaks.delete.title'),
      message: t('streaks.delete.message', { name: campaign.name }),
      confirmLabel: t('action.delete'),
      destructive: true,
    });
    if (!confirmed) return;
    await run(campaign.id, async () => {
      await remove.mutateAsync(campaign.id);
      notify.success(t('streaks.delete.done'));
    });
  }

  return { busyId, toggleStatus, deleteCampaign };
}
