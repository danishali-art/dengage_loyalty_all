import { ref, type MaybeRefOrGetter } from 'vue';
import { useI18n } from 'vue-i18n';
import { ApiError } from '@/shared/api';
import { notify } from '@/shared/ui';
import { useApproveRewardMutation, type Reward } from '@/entities/reward';

/**
 * Second-admin approval of a cashback reward (CR 2026-09-30, A4). The server enforces creator ≠
 * approver; a self-approval attempt comes back as a normal error toast.
 */
export function useApproveReward(programId: MaybeRefOrGetter<string>) {
  const { t } = useI18n();
  const mutation = useApproveRewardMutation(programId);
  const approvingId = ref<string | null>(null);

  async function approve(reward: Reward): Promise<void> {
    if (approvingId.value) return;
    approvingId.value = reward.id;
    try {
      await mutation.mutateAsync(reward.id);
      notify.success(t('rewards.approve.done', { name: reward.displayName }));
    } catch (error) {
      notify.error(error instanceof ApiError ? error.message : t('rewards.approve.failed'));
    } finally {
      approvingId.value = null;
    }
  }

  return { approve, approvingId };
}
